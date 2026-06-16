import tkinter as tk
from tkinter import ttk, scrolledtext
import requests
import subprocess
import os
import sys
import shutil
import time
import zipfile
import threading
import json
import stat
from concurrent.futures import ThreadPoolExecutor

BASE_PORT = 5005
PORT_GAP = 64


def is_admin():
    import ctypes
    try:
        return ctypes.windll.shell32.IsUserAnAdmin()
    except:
        return False


def remove_readonly(func, path, excinfo):
    """Error handler for shutil.rmtree to handle read-only files on Windows."""
    os.chmod(path, stat.S_IWRITE)
    func(path)


def clean_previous_environment():
    """Removes old deployment artifacts before fetching the fresh environment."""
    folders_to_delete = ["venv", "unity_build"]
    for folder in folders_to_delete:
        if os.path.exists(folder):
            print(f"[INIT] Removing old '{folder}' directory...")
            try:
                shutil.rmtree(folder, onerror=remove_readonly)
                print(f"[INIT] Successfully removed '{folder}'.")
            except Exception as e:
                print(f"[ERROR] Failed to remove '{folder}'. Is an old Unity process still running? Error: {e}")


class PrintRedirector:
    def __init__(self, text_widget):
        self.text_widget = text_widget

    def write(self, string):
        self.text_widget.configure(state='normal')
        self.text_widget.insert('end', string)
        self.text_widget.see('end')
        self.text_widget.configure(state='disabled')

    def flush(self):
        pass


class WorkerClient:
    def __init__(self, server_ip, concurrency, node_id):
        self.server_url = f"http://{server_ip}:8000"
        self.node_id = node_id
        self.concurrency = concurrency
        self.local_results_dir = "results"
        self.env_vars = os.environ.copy()

    def setup_environment(self):
        print(f"\n[INIT] Registering node '{self.node_id}' with Master Server at {self.server_url}...")
        while True:
            try:
                response = requests.post(f"{self.server_url}/register", json={
                    "node_id": self.node_id,
                    "concurrency_capacity": self.concurrency
                })
                if response.status_code == 200:
                    # Start heartbeat immediately to survive the long extraction process
                    threading.Thread(target=self.heartbeat_loop, daemon=True).start()
                    break
            except Exception as e:
                print(f"Waiting for Master Server... ({e})")
                time.sleep(5)

        print("[INIT] Forcing cleanup of previous environment artifacts...")
        clean_previous_environment()

        if not os.path.exists("venv") or not os.path.exists("unity_build"):
            print("[INIT] Need environment files. Waiting for Server packaging to finish...")
            while True:
                try:
                    response = requests.get(f"{self.server_url}/download/environment", stream=True)
                    if response.status_code == 200:
                        break
                except:
                    pass
                print("Server not ready or still packaging... Retrying in 10s.")
                time.sleep(10)

            print("[INIT] Downloading ml_env_package.zip...")
            with open("ml_env_package.zip", "wb") as f:
                for chunk in response.iter_content(chunk_size=8192):
                    f.write(chunk)

            print("[INIT] Extracting package (this may take a moment)...")
            with zipfile.ZipFile("ml_env_package.zip", 'r') as zip_ref:
                zip_ref.extractall(".")
            os.remove("ml_env_package.zip")
            print("[INIT] Environment extracted and ready.")

        venv_root = os.path.abspath("venv")
        scripts_path = os.path.abspath(os.path.join("venv", "Scripts"))
        lib_bin_path = os.path.abspath(os.path.join("venv", "Library", "bin"))

        self.env_vars["PATH"] = f"{venv_root};{scripts_path};{lib_bin_path};" + self.env_vars["PATH"]

    def heartbeat_loop(self):
        while True:
            try:
                requests.post(f"{self.server_url}/heartbeat/{self.node_id}", timeout=5)
            except:
                pass
            time.sleep(60)

    def sync_results_loop(self):
        """Periodically pushes intermediate state to the server without deleting local copies."""
        while True:
            # 15 Minute Cycle (900 seconds)
            time.sleep(900)

            if not os.path.exists(self.local_results_dir) or not os.listdir(self.local_results_dir):
                continue

            print(f"\n[MONITOR] Compressing current results for Master Sync...")
            sync_zip = f"sync_{self.node_id}.zip"
            try:
                # Use ZIP_STORED to save client CPU power
                with zipfile.ZipFile(sync_zip, 'w', compression=zipfile.ZIP_STORED) as zipf:
                    for root, _, files in os.walk(self.local_results_dir):
                        for file in files:
                            # Skip PyTorch checkpoints to save massive bandwidth
                            if file.endswith('.pt'):
                                continue
                            file_path = os.path.join(root, file)
                            arcname = os.path.relpath(file_path, self.local_results_dir)
                            zipf.write(file_path, arcname)

                with open(sync_zip, 'rb') as f:
                    requests.post(f"{self.server_url}/sync_monitoring/{self.node_id}", files={"file": f})
                print(f"[MONITOR] Intermediate state pushed to Server.")
            except Exception as e:
                # File locking or network issues won't crash the client, it just tries again in 15m.
                print(f"[MONITOR] Sync failed (will retry next cycle): {e}")
            finally:
                if os.path.exists(sync_zip):
                    try:
                        os.remove(sync_zip)
                    except:
                        pass

    def worker_thread(self, worker_index):
        import yaml
        assigned_port = BASE_PORT + (worker_index * PORT_GAP)

        while True:
            try:
                response = requests.get(f"{self.server_url}/get_task/{self.node_id}", timeout=10)

                # Graceful recovery if the server dropped this node
                if response.status_code == 401:
                    print(f"[Worker {worker_index}] Node was dropped by server. Re-registering...")
                    requests.post(f"{self.server_url}/register", json={
                        "node_id": self.node_id,
                        "concurrency_capacity": self.concurrency
                    })
                    time.sleep(5)
                    continue

                task = response.json()

                if "message" in task and task["message"] == "No pending tasks":
                    time.sleep(15)
                    continue

                if "run_id" not in task:
                    print(f"[Worker {worker_index}] Unexpected server response: {task}")
                    time.sleep(15)
                    continue

                run_id = task["run_id"]
                config_dict = task["config"]
                exe_name = task["exe_name"]

                print(f"[Worker {worker_index}] Assigned Task: {run_id} | Port: {assigned_port}")

                temp_config_path = f"temp_config_{run_id}.yaml"
                with open(temp_config_path, 'w') as f:
                    yaml.dump(config_dict, f)

                env_path = os.path.abspath(os.path.join("unity_build", exe_name))

                python_exe = os.path.abspath(os.path.join("venv", "python.exe"))
                if not os.path.exists(python_exe):
                    python_exe = os.path.abspath(os.path.join("venv", "Scripts", "python.exe"))

                if not os.path.exists(python_exe):
                    print(f"[Worker {worker_index}] ERROR: Python executable missing at {python_exe}!")
                    time.sleep(15)
                    continue

                mlagents_cmd = [
                    python_exe,
                    "-c",
                    "import sys; from mlagents.trainers.learn import main; sys.argv[0] = 'mlagents-learn'; sys.exit(main())",
                    temp_config_path,
                    f"--run-id={run_id}", f"--env={env_path}", f"--base-port={assigned_port}",
                    "--num-envs=4", "--force", "--width=768", "--height=512",
                    "--timeout-wait=300", "--no-graphics"
                ]

                process = subprocess.run(
                    mlagents_cmd,
                    env=self.env_vars,
                    creationflags=subprocess.CREATE_NEW_CONSOLE
                )

                if process.returncode == 0:
                    print(f"[Worker {worker_index}] Task {run_id} complete. Delivering final results to Master...")
                    run_folder = os.path.join(self.local_results_dir, run_id)
                    final_zip = f"results_{run_id}.zip"

                    # Manually package the run folder to exclude heavy .pt checkpoints
                    with zipfile.ZipFile(final_zip, 'w', compression=zipfile.ZIP_DEFLATED) as zipf:
                        for root, _, files in os.walk(run_folder):
                            for file in files:
                                if file.endswith('.pt'):
                                    continue
                                file_path = os.path.join(root, file)
                                arcname = os.path.relpath(file_path, run_folder)
                                zipf.write(file_path, arcname)

                    with open(final_zip, 'rb') as f:
                        requests.post(f"{self.server_url}/submit_result/{run_id}", files={"file": f})

                    os.remove(final_zip)
                    print(f"[Worker {worker_index}] Delivery successful. (Local data preserved).")

                else:
                    print(f"[Worker {worker_index}] Task {run_id} FAILED. Releasing task.")

            except Exception as e:
                print(f"[Worker {worker_index}] Execution Error: {e}")
                time.sleep(15)
            finally:
                if 'temp_config_path' in locals() and os.path.exists(temp_config_path):
                    os.remove(temp_config_path)

    def start_workers(self):
        print(f"\n[START] Booting {self.concurrency} concurrent workers...")
        with ThreadPoolExecutor(max_workers=self.concurrency) as executor:
            for i in range(self.concurrency):
                executor.submit(self.worker_thread, i)


class ClientGUI:
    def __init__(self):
        self.root = tk.Tk()
        self.root.title("ML-Agents Client Node")
        self.root.geometry("600x500")
        self.config_file = "client_config.json"

        style = ttk.Style(self.root)
        style.theme_use('clam')

        frame = ttk.LabelFrame(self.root, text="Node Configuration")
        frame.pack(fill="x", padx=10, pady=10)

        ttk.Label(frame, text="Master Server IP:").grid(row=0, column=0, padx=5, pady=5, sticky="w")
        self.ip_entry = ttk.Entry(frame, width=20)
        self.ip_entry.grid(row=0, column=1, padx=5, pady=5, sticky="w")

        ttk.Label(frame, text="Concurrency:").grid(row=1, column=0, padx=5, pady=5, sticky="w")
        self.concurrency_var = tk.IntVar(value=1)
        self.concurrency_spin = ttk.Spinbox(frame, from_=1, to=16, textvariable=self.concurrency_var, width=5)
        self.concurrency_spin.grid(row=1, column=1, padx=5, pady=5, sticky="w")

        self.start_btn = ttk.Button(frame, text="Connect & Start Training", command=self.start_client)
        self.start_btn.grid(row=2, column=0, columnspan=2, pady=10)

        self.log_area = scrolledtext.ScrolledText(self.root, state='disabled', height=20, font=("Consolas", 9))
        self.log_area.pack(fill="both", expand=True, padx=10, pady=(0, 10))

        sys.stdout = PrintRedirector(self.log_area)
        sys.stderr = PrintRedirector(self.log_area)

        self.load_config()

    def load_config(self):
        if os.path.exists(self.config_file):
            try:
                with open(self.config_file, 'r') as f:
                    config = json.load(f)
                    self.ip_entry.insert(0, config.get("ip", "192.168."))
                    self.concurrency_var.set(config.get("concurrency", 1))
            except Exception:
                self.ip_entry.insert(0, "192.168.")
        else:
            self.ip_entry.insert(0, "192.168.")

    def save_config(self):
        config = {
            "ip": self.ip_entry.get().strip(),
            "concurrency": self.concurrency_var.get()
        }
        try:
            with open(self.config_file, 'w') as f:
                json.dump(config, f)
        except Exception as e:
            print(f"[WARNING] Could not save config: {e}")

    def start_client(self):
        self.save_config()

        ip = self.ip_entry.get().strip()
        concurrency = self.concurrency_var.get()
        node_id = os.environ.get('COMPUTERNAME', 'Unknown_Client')

        self.start_btn.config(state="disabled")
        self.ip_entry.config(state="disabled")
        self.concurrency_spin.config(state="disabled")

        client = WorkerClient(ip, concurrency, node_id)

        def run_background():
            client.setup_environment()

            # The heartbeat loop is now safely started during setup_environment.
            # We just need to start the sync loop and the workers here.
            threading.Thread(target=client.sync_results_loop, daemon=True).start()

            # Block and run training
            client.start_workers()

        threading.Thread(target=run_background, daemon=True).start()


if __name__ == "__main__":
    if not is_admin():
        import ctypes

        params = " ".join([f'"{arg}"' for arg in sys.argv[1:]])
        ctypes.windll.shell32.ShellExecuteW(None, "runas", sys.executable, f'"{sys.argv[0]}" {params}', None, 1)
        sys.exit()

    app = ClientGUI()
    app.root.mainloop()