import json
import subprocess
import os
import time
import yaml
import ctypes
import sys
import threading
import shutil
import tkinter as tk
from tkinter import ttk, filedialog, messagebox
import zipfile
import stat
import uvicorn
from fastapi import FastAPI, UploadFile, File, HTTPException
from fastapi.responses import FileResponse
from pydantic import BaseModel

# --- FASTAPI SERVER SETUP ---
app = FastAPI()
training_queue = {}
active_nodes = {}
ZIP_NAME = "ml_env_package.zip"
SETTINGS_FILE = "master_settings.json"


def remove_readonly(func, path, excinfo):
    """Error handler for shutil.rmtree to handle read-only files on Windows."""
    os.chmod(path, stat.S_IWRITE)
    func(path)


class TaskRequest(BaseModel):
    node_id: str
    concurrency_capacity: int


@app.post("/register")
def register_node(request: TaskRequest):
    active_nodes[request.node_id] = {"last_heartbeat": time.time(), "capacity": request.concurrency_capacity,
                                     "assigned_runs": []}
    return {"message": "Registered", "status": "OK"}


@app.get("/download/environment")
def download_environment():
    if not os.path.exists(ZIP_NAME):
        raise HTTPException(status_code=404, detail="Environment packaging in progress.")
    return FileResponse(ZIP_NAME, media_type='application/zip', filename=ZIP_NAME)


@app.get("/get_task/{node_id}")
def get_task(node_id: str):
    if node_id not in active_nodes:
        raise HTTPException(status_code=401, detail="Node not registered")

    active_nodes[node_id]["last_heartbeat"] = time.time()
    for run_id, data in training_queue.items():
        if data["status"] == "PENDING":
            training_queue[run_id]["status"] = "IN_PROGRESS"
            training_queue[run_id]["assigned_to"] = node_id
            active_nodes[node_id]["assigned_runs"].append(run_id)
            return {"message": "Task assigned", "run_id": run_id, "config": data["config"],
                    "exe_name": app.state.exe_name}
    return {"message": "No pending tasks"}


@app.post("/heartbeat/{node_id}")
def heartbeat(node_id: str):
    if node_id in active_nodes:
        active_nodes[node_id]["last_heartbeat"] = time.time()
        return {"status": "ok"}
    return {"status": "unregistered"}


@app.post("/sync_monitoring/{node_id}")
async def sync_monitoring(node_id: str, file: UploadFile = File(...)):
    """Receives 15-minute intermediate updates to host via central TensorBoard."""
    monitor_dir = "monitoring_results"
    if not os.path.exists(monitor_dir):
        os.makedirs(monitor_dir)

    temp_zip = f"temp_sync_{node_id}.zip"
    with open(temp_zip, "wb+") as f:
        f.write(await file.read())

    try:
        with zipfile.ZipFile(temp_zip, 'r') as zip_ref:
            # Identify which run folders are inside this zip
            top_level_dirs = set()
            for name in zip_ref.namelist():
                parts = name.replace('\\', '/').split('/')
                if parts[0]:
                    top_level_dirs.add(parts[0])

            # Delete the old folders for these specific runs to prevent corrupted merges
            for run_dir in top_level_dirs:
                target_dir = os.path.join(monitor_dir, run_dir)
                if os.path.exists(target_dir):
                    try:
                        shutil.rmtree(target_dir, onerror=remove_readonly)
                    except Exception as e:
                        print(f"[SERVER] File lock issue removing {target_dir}: {e}")

            # Extract the fresh files
            zip_ref.extractall(monitor_dir)
    except Exception as e:
        print(f"[SERVER] Error extracting sync zip from {node_id}: {e}")
    finally:
        if os.path.exists(temp_zip):
            os.remove(temp_zip)

    return {"status": "ok"}


@app.post("/submit_result/{run_id}")
async def submit_result(run_id: str, file: UploadFile = File(...)):
    """Receives the final, completed runs for permanent storage."""
    if not os.path.exists("central_results"): os.makedirs("central_results")
    file_location = os.path.join("central_results", f"{run_id}.zip")
    with open(file_location, "wb+") as file_object:
        file_object.write(await file.read())

    if run_id in training_queue:
        training_queue[run_id]["status"] = "COMPLETED"
        node_id = training_queue[run_id]["assigned_to"]
        if node_id in active_nodes and run_id in active_nodes[node_id]["assigned_runs"]:
            active_nodes[node_id]["assigned_runs"].remove(run_id)
    return {"info": f"file '{file.filename}' saved"}


# --- UI AND MANAGEMENT ---
class MasterApp:
    def __init__(self):
        self.root = tk.Tk()
        self.root.title("ML-Agents Master Dispatcher")
        self.root.geometry("750x650")
        style = ttk.Style(self.root)
        style.theme_use('clam')

        self.paths = {"config": "", "env": "", "json": "", "conda": ""}
        self.steps_val = 100000000

        self.load_settings()
        self.setup_launcher_ui()

    def load_settings(self):
        if os.path.exists(SETTINGS_FILE):
            try:
                with open(SETTINGS_FILE, "r") as f:
                    data = json.load(f)
                    self.paths.update(data.get("paths", {}))
                    self.steps_val = data.get("steps", 100000000)
            except Exception as e:
                print(f"Warning: Could not load settings - {e}")

    def save_settings(self):
        try:
            with open(SETTINGS_FILE, "w") as f:
                json.dump({
                    "paths": self.paths,
                    "steps": self.steps_var.get()
                }, f, indent=4)
        except Exception as e:
            print(f"Warning: Could not save settings - {e}")

    def setup_launcher_ui(self):
        self.launcher_frame = ttk.Frame(self.root)
        self.launcher_frame.pack(fill="both", expand=True, padx=20, pady=20)

        ttk.Label(self.launcher_frame, text="Server Configuration", font=("Arial", 14, "bold")).pack(pady=10)

        def create_picker(label_text, key, is_dir=False, filetypes=None):
            frame = ttk.Frame(self.launcher_frame)
            frame.pack(fill="x", pady=5)
            ttk.Label(frame, text=label_text, width=20).pack(side="left")
            entry = ttk.Entry(frame)

            if self.paths[key]:
                entry.insert(0, self.paths[key])

            entry.configure(state="readonly")
            entry.pack(side="left", fill="x", expand=True, padx=5)

            def browse():
                if is_dir:
                    path = filedialog.askdirectory()
                else:
                    path = filedialog.askopenfilename(filetypes=filetypes)
                if path:
                    entry.configure(state="normal")
                    entry.delete(0, tk.END)
                    entry.insert(0, path)
                    entry.configure(state="readonly")
                    self.paths[key] = path

            ttk.Button(frame, text="Browse", command=browse).pack(side="right")

        create_picker("Base YAML Config:", "config", filetypes=[("YAML Files", "*.yaml")])
        create_picker("Unity Env (.exe):", "env", filetypes=[("Executables", "*.exe")])
        create_picker("Agents JSON:", "json", filetypes=[("JSON Files", "*.json")])
        create_picker("Conda Env Folder:", "conda", is_dir=True)

        ttk.Label(self.launcher_frame, text="Global Training Steps:").pack(anchor="w", pady=(15, 0))
        self.steps_var = tk.IntVar(value=self.steps_val)
        ttk.Entry(self.launcher_frame, textvariable=self.steps_var, width=15).pack(anchor="w", pady=5)

        ttk.Button(self.launcher_frame, text="Launch Distributed Server", command=self.start_server).pack(pady=30)

    def start_server(self):
        if not all(self.paths.values()):
            messagebox.showerror("Error", "Please select all required files and directories.")
            return

        self.save_settings()
        self.launcher_frame.destroy()
        self.init_data_and_dashboard()

    def launch_tensorboard(self):
        monitor_dir = "monitoring_results"
        if not os.path.exists(monitor_dir):
            os.makedirs(monitor_dir)

        # Locate Python dynamically within the selected conda environment
        python_exe = os.path.join(self.paths["conda"], "python.exe")
        if not os.path.exists(python_exe):
            python_exe = os.path.join(self.paths["conda"], "Scripts", "python.exe")

        if not os.path.exists(python_exe):
            print(f"[SERVER] Error: Cannot find python executable in {self.paths['conda']} to launch TensorBoard.")
            return

        print("[SERVER] Launching global TensorBoard on port 6006...")
        tb_cmd = [
            python_exe, "-m", "tensorboard.main",
            "--logdir", monitor_dir,
            "--port", "6006",
            "--bind_all"
        ]

        # Fire and forget
        subprocess.Popen(tb_cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    def init_data_and_dashboard(self):
        app.state.exe_name = os.path.basename(self.paths["env"])
        steps = self.steps_var.get()

        with open(self.paths["json"], 'r') as f:
            agents_list = json.load(f)
        with open(self.paths["config"], 'r') as f:
            base_yaml_config = yaml.safe_load(f)

        agents_per_scene = 64
        num_envs = 4
        total_global = agents_per_scene * num_envs

        for agent in agents_list:
            run_id = agent["id"]
            local_config = base_yaml_config.copy()
            local_config["environment_parameters"] = {
                "difficulty_ratio": 1.0,
                "target_speed": float(agent.get("speed", 50)),
                "dtc_weight": float(agent.get("dtc_weight", 0.5)),
                "acc_time": float(agent.get("acc_time", 10.0)),
                "smooth_threshold": float(agent.get("smooth_threshold", 1.0)),
                "max_python_steps": float(steps),
                "total_global_agents": float(total_global)
            }
            if 'behaviors' in local_config:
                for b_name in local_config['behaviors']:
                    local_config['behaviors'][b_name]['max_steps'] = steps

            training_queue[run_id] = {"config": local_config, "status": "PENDING", "assigned_to": None}

        self.total_agents = len(agents_list)
        self.setup_dashboard_ui()

        # Boot Background Services
        self.launch_tensorboard()
        threading.Thread(target=lambda: uvicorn.run(app, host="0.0.0.0", port=8000, log_level="warning"),
                         daemon=True).start()
        threading.Thread(target=self.package_environment, daemon=True).start()

    def setup_dashboard_ui(self):
        self.packaging_var = tk.StringVar(value="Status: Packaging Environment...")
        ttk.Label(self.root, textvariable=self.packaging_var, font=("Arial", 10, "italic")).pack(pady=5)

        self.main_lbl = ttk.Label(self.root, text=f"Overall Progress: 0/{self.total_agents}",
                                  font=("Arial", 12, "bold"))
        self.main_lbl.pack(pady=10)

        self.main_progress = ttk.Progressbar(self.root, maximum=self.total_agents, length=600)
        self.main_progress.pack(pady=5)

        ttk.Label(self.root, text="Active Client Nodes:", font=("Arial", 10, "bold")).pack(anchor="w", padx=20, pady=5)
        self.nodes_text = tk.Text(self.root, height=5, width=85, state="disabled")
        self.nodes_text.pack(pady=5)

        ttk.Label(self.root, text="Task Queue Status (Top 50 PENDING/IN_PROGRESS):", font=("Arial", 10, "bold")).pack(
            anchor="w", padx=20, pady=5)
        self.canvas = tk.Canvas(self.root, borderwidth=0, highlightthickness=0)
        self.scrollbar = ttk.Scrollbar(self.root, orient="vertical", command=self.canvas.yview)
        self.scrollable_frame = ttk.Frame(self.canvas)

        self.scrollable_frame.bind("<Configure>", lambda e: self.canvas.configure(scrollregion=self.canvas.bbox("all")))
        self.canvas.create_window((0, 0), window=self.scrollable_frame, anchor="nw")
        self.canvas.configure(yscrollcommand=self.scrollbar.set)
        self.canvas.pack(side="left", fill="both", expand=True, padx=20, pady=10)
        self.scrollbar.pack(side="right", fill="y")

        self._refresh()
        self._fault_tolerance_loop()

    def package_environment(self):
        print("[SERVER] Packaging environment (Store Only)...")
        conda_dir = self.paths["conda"]
        unity_dir = os.path.dirname(self.paths["env"])

        with zipfile.ZipFile("ml_env_package.zip", 'w', compression=zipfile.ZIP_STORED) as zip_ref:
            print("[SERVER] Adding Conda Environment to ZIP...")
            for root, _, files in os.walk(conda_dir):
                for file in files:
                    file_path = os.path.join(root, file)
                    rel_path = os.path.relpath(file_path, conda_dir)
                    arc_name = os.path.join("venv", rel_path)
                    zip_ref.write(file_path, arcname=arc_name)

            print("[SERVER] Adding Unity Build to ZIP...")
            for root, _, files in os.walk(unity_dir):
                for file in files:
                    file_path = os.path.join(root, file)
                    rel_path = os.path.relpath(file_path, unity_dir)
                    arc_name = os.path.join("unity_build", rel_path)
                    zip_ref.write(file_path, arcname=arc_name)

        print("[SERVER] Packaging complete.")
        self.packaging_var.set("Status: Environment Ready! Waiting for clients... | TensorBoard: Localhost:6006")

    def _refresh(self):
        completed = sum(1 for data in training_queue.values() if data["status"] == "COMPLETED")
        self.main_progress["value"] = completed
        self.main_lbl.config(text=f"Overall Progress: {completed}/{self.total_agents}")

        self.nodes_text.config(state="normal")
        self.nodes_text.delete(1.0, tk.END)
        for node_id, data in active_nodes.items():
            last_seen = int(time.time() - data["last_heartbeat"])
            self.nodes_text.insert(tk.END,
                                   f"[{node_id}] | Cap: {data['capacity']} | Tasks: {len(data['assigned_runs'])} | Seen: {last_seen}s ago\n")
        self.nodes_text.config(state="disabled")

        for widget in self.scrollable_frame.winfo_children(): widget.destroy()

        display_count = 0
        for run_id, data in training_queue.items():
            if display_count >= 50: break
            if data["status"] in ["PENDING", "IN_PROGRESS"]:
                frame = ttk.Frame(self.scrollable_frame)
                frame.pack(fill="x", padx=5, pady=2)
                assignee = data["assigned_to"] if data["assigned_to"] else "Unassigned"
                lbl = ttk.Label(frame, text=f"{run_id:<25} | {data['status']:<12} | Node: {assignee}",
                                font=("Consolas", 9))
                lbl.pack(side="left")
                display_count += 1

        self.root.after(1000, self._refresh)

    def _fault_tolerance_loop(self):
        current_time = time.time()
        dead_nodes = [node_id for node_id, data in active_nodes.items() if current_time - data["last_heartbeat"] > 180]

        for dead_node in dead_nodes:
            for run_id in active_nodes[dead_node]["assigned_runs"]:
                if training_queue[run_id]["status"] == "IN_PROGRESS":
                    training_queue[run_id]["status"] = "PENDING"
                    training_queue[run_id]["assigned_to"] = None
            del active_nodes[dead_node]

        self.root.after(10000, self._fault_tolerance_loop)


def is_admin():
    try:
        return ctypes.windll.shell32.IsUserAnAdmin()
    except:
        return False


if __name__ == "__main__":
    if not is_admin():
        import ctypes

        params = " ".join([f'"{arg}"' for arg in sys.argv[1:]])
        ctypes.windll.shell32.ShellExecuteW(None, "runas", sys.executable, f'"{sys.argv[0]}" {params}', None, 1)
        sys.exit()

    gui = MasterApp()
    gui.root.mainloop()