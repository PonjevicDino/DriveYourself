import argparse
import json
import subprocess
import os
import time
import yaml
import ctypes
import sys
import re
import threading
import tkinter as tk
from tkinter import ttk
from concurrent.futures import ThreadPoolExecutor, as_completed

BASE_PORT = 5005
PORT_GAP = 64
MAX_RETRIES = 1

# Regex to catch the step count from ML-Agents terminal output
STEP_PATTERN = re.compile(r"Step:\s*(\d+)")


# ==========================================
# GUI DASHBOARD CLASS (Runs on Main Thread)
# ==========================================
class TrainingDashboard:
    def __init__(self, total_agents):
        self.root = tk.Tk()
        self.root.title("ML-Agents Matrix Training Dashboard")
        self.root.geometry("650x800")

        # Style
        style = ttk.Style(self.root)
        style.theme_use('clam')

        # Main Progress Header
        self.main_lbl = ttk.Label(self.root, text=f"Overall Progress: 0/{total_agents}", font=("Arial", 12, "bold"))
        self.main_lbl.pack(pady=10)
        self.main_progress = ttk.Progressbar(self.root, maximum=total_agents, length=600)
        self.main_progress.pack(pady=5)

        # Scrollable Canvas Setup (For 256 Agents)
        self.canvas = tk.Canvas(self.root, borderwidth=0, highlightthickness=0)
        self.scrollbar = ttk.Scrollbar(self.root, orient="vertical", command=self.canvas.yview)
        self.scrollable_frame = ttk.Frame(self.canvas)

        self.scrollable_frame.bind(
            "<Configure>",
            lambda e: self.canvas.configure(scrollregion=self.canvas.bbox("all"))
        )
        self.canvas.create_window((0, 0), window=self.scrollable_frame, anchor="nw")
        self.canvas.configure(yscrollcommand=self.scrollbar.set)

        self.canvas.pack(side="left", fill="both", expand=True, padx=10, pady=10)
        self.scrollbar.pack(side="right", fill="y")

        # Enable Mousewheel scrolling
        self.canvas.bind_all("<MouseWheel>", self._on_mousewheel)

        # Thread-safe data storage
        self.agent_widgets = {}
        self.data_lock = threading.Lock()
        self.agent_data = {}
        self.main_completed = 0
        self.total_agents = total_agents

        # Start the UI refresh loop
        self._refresh()

    def _on_mousewheel(self, event):
        self.canvas.yview_scroll(int(-1 * (event.delta / 120)), "units")

    def init_agents_ui(self, current_queue, steps):
        """Builds the UI rows (Called safely on the main thread)"""
        # Clear old widgets if this is a retry batch
        for widget in self.scrollable_frame.winfo_children():
            widget.destroy()

        with self.data_lock:
            self.agent_data.clear()
            self.agent_widgets.clear()

            for agent in current_queue:
                run_id = agent["id"]
                self.agent_data[run_id] = {"step": 0, "status": "Waiting...", "max_steps": steps}

                # Create Row Frame
                frame = ttk.Frame(self.scrollable_frame)
                frame.pack(fill="x", padx=5, pady=4)

                lbl = ttk.Label(frame, text=run_id, width=32, font=("Consolas", 9))
                lbl.pack(side="left")

                prog = ttk.Progressbar(frame, maximum=steps, length=200)
                prog.pack(side="left", padx=10)

                stat = ttk.Label(frame, text="Waiting...", width=20, font=("Consolas", 9))
                stat.pack(side="left")

                self.agent_widgets[run_id] = {"prog": prog, "stat": stat}

    def update_agent(self, run_id, step, status):
        """Called by background workers to update data"""
        with self.data_lock:
            if run_id in self.agent_data:
                self.agent_data[run_id]["step"] = step
                self.agent_data[run_id]["status"] = status

    def increment_main(self):
        with self.data_lock:
            self.main_completed += 1

    def set_title(self, title):
        with self.data_lock:
            self.main_lbl.config(text=title)

    def _refresh(self):
        """Loops every 200ms to update the GUI from the data dictionary"""
        with self.data_lock:
            self.main_progress["value"] = self.main_completed

            for run_id, data in self.agent_data.items():
                if run_id in self.agent_widgets:
                    w = self.agent_widgets[run_id]
                    w["prog"]["value"] = data["step"]

                    pct = int((data["step"] / data["max_steps"]) * 100)
                    status_text = data["status"]

                    if status_text == "Training":
                        w["stat"].config(text=f"{pct}% (Training)")
                    else:
                        w["stat"].config(text=status_text)

        self.root.after(200, self._refresh)


# ==========================================
# SYSTEM/TRAINING LOGIC (Background Thread)
# ==========================================

def is_admin():
    try:
        return ctypes.windll.shell32.IsUserAnAdmin()
    except:
        return False


def log_crash(run_id, exit_code, port, log_file_path):
    with open("crash_log.txt", "a") as f:
        timestamp = time.strftime('%Y-%m-%d %H:%M:%S')
        f.write(
            f"[{timestamp}] Agent {run_id} failed on port {port} with exit code {exit_code}. See {log_file_path} for details.\n")


def train_single_agent(agent_data, base_yaml_config, args, worker_index, dashboard):
    try:
        run_id = agent_data.get("id", "Unknown_Agent")
        speed = agent_data.get("speed", 50)
        dtc_weight = agent_data.get("dtc_weight", 0.5)
        assigned_port = BASE_PORT + (worker_index * PORT_GAP)

        if worker_index < args.concurrency:
            delay = worker_index * 5
            if delay > 0:
                dashboard.update_agent(run_id, 0, f"Waiting {delay}s...")
                time.sleep(delay)

        dashboard.update_agent(run_id, 0, "Training")

        # Terminal Output
        print(
            f" [Worker {worker_index}] STARTING: {run_id} | Target: {speed}km/h | DtC: {dtc_weight} | Port: {assigned_port}")

        temp_config_path = f"temp_config_{run_id}.yaml"
        log_file_path = f"terminal_log_{run_id}.txt"
        local_config = base_yaml_config.copy()

        local_config["environment_parameters"] = {
            "difficulty_ratio": 1.0,
            "target_speed": float(speed),
            "dtc_weight": float(dtc_weight),
            "acc_time": float(agent_data.get("acc_time", 10.0)),
            "smooth_threshold": float(agent_data.get("smooth_threshold", 1.0))
        }

        if 'behaviors' in local_config:
            for behavior_name in local_config['behaviors']:
                local_config['behaviors'][behavior_name]['max_steps'] = args.steps

        with open(temp_config_path, 'w') as f:
            yaml.dump(local_config, f)

        cmd = [
            "mlagents-learn", temp_config_path,
            f"--run-id={run_id}",
            f"--env={args.env}",
            f"--base-port={str(assigned_port)}",
            "--num-envs=4",
            "--force",
            "--width=768", "--height=512",
            "--timeout-wait=300",
            "--no-graphics"
        ]

        # Read output line-by-line
        with open(log_file_path, "w") as log_file:
            process = subprocess.Popen(
                cmd,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                bufsize=1
            )
            pid = process.pid

            for line in process.stdout:
                log_file.write(line)
                log_file.flush()

                match = STEP_PATTERN.search(line)
                if match:
                    current_step = int(match.group(1))
                    dashboard.update_agent(run_id, current_step, "Training")

            return_code = process.wait()

        if return_code == 0:
            dashboard.update_agent(run_id, args.steps, "Done")
            dashboard.increment_main()
            dashboard.set_title(f"Overall Progress: {dashboard.main_completed}/{dashboard.total_agents}")
            print(f" [Worker {worker_index}] FINISHED: {run_id} (Success)")
            if os.path.exists(log_file_path):
                os.remove(log_file_path)
            return True
        else:
            dashboard.update_agent(run_id, 0, "FAILED")
            print(f" [Worker {worker_index}] FAILED:   {run_id} (Exit Code: {return_code})")
            log_crash(run_id, return_code, assigned_port, log_file_path)
            subprocess.run(f"taskkill /F /T /PID {pid}", shell=True, capture_output=True)
            return False

    except Exception as e:
        dashboard.update_agent(run_id, 0, "CRASHED")
        print(f" [Worker {worker_index}] CRASHED:  {run_id} (Error: {e})")
        if 'pid' in locals():
            subprocess.run(f"taskkill /F /T /PID {pid}", shell=True, capture_output=True)
        return False
    finally:
        if os.path.exists(temp_config_path):
            os.remove(temp_config_path)


def background_execution_thread(args, agents_list, base_yaml_config, dashboard):
    """Manages the queue and retry logic outside the GUI loop"""
    current_queue = agents_list

    for attempt in range(1, MAX_RETRIES + 2):
        if not current_queue:
            break

        print(f"\n==================================================")
        print(f" STARTING BATCH ATTEMPT {attempt} ({len(current_queue)} agents remaining)")
        print(f"==================================================\n")

        # Tell the GUI on the main thread to build the rows
        dashboard.root.after(0, dashboard.init_agents_ui, current_queue, args.steps)
        time.sleep(1)  # Brief pause to let UI render

        next_queue = []

        with ThreadPoolExecutor(max_workers=args.concurrency) as executor:
            futures = {
                executor.submit(train_single_agent, agent, base_yaml_config, args, i, dashboard): agent
                for i, agent in enumerate(current_queue)
            }

            for future in as_completed(futures):
                agent = futures[future]
                try:
                    if not future.result():
                        next_queue.append(agent)
                except:
                    next_queue.append(agent)

        if next_queue:
            with open("failed_agents_queue.json", "w") as f:
                json.dump(next_queue, f, indent=2)

        current_queue = next_queue

    print(f"\n==================================================")
    if current_queue:
        dashboard.set_title(f"FINISHED WITH ERRORS ({len(current_queue)} Failed)")
        print(f" [END] {len(current_queue)} agents completely failed. Check crash_log.txt.")
    else:
        dashboard.set_title(f"ALL AGENTS COMPLETED SUCCESSFULLY!")
        print(f" [END] All agents completed successfully!")
    print(f"==================================================")


# ==========================================
# BOOTSTRAP
# ==========================================

if __name__ == "__main__":
    if not is_admin():
        print("Administrator rights missing! Requesting elevation...")
        params = " ".join([f'"{arg}"' for arg in sys.argv[1:]])
        ctypes.windll.shell32.ShellExecuteW(None, "runas", sys.executable, f'"{sys.argv[0]}" {params}', None, 1)
        sys.exit()

    parser = argparse.ArgumentParser(description='Parallel ML-Agents Training Manager')
    parser.add_argument('--config', type=str, required=True)
    parser.add_argument('--env', type=str, required=True)
    parser.add_argument('--json', type=str, required=True)
    parser.add_argument('--steps', type=int, default=5000000)
    parser.add_argument('--concurrency', type=int, default=1)
    args = parser.parse_args()

    if not os.path.exists(args.config) or not os.path.exists(args.env) or not os.path.exists(args.json):
        print("Missing required files (Config, Env, or JSON).")
        sys.exit()

    with open(args.json, 'r') as f:
        agents_list = json.load(f)
    with open(args.config, 'r') as f:
        base_yaml_config = yaml.safe_load(f)

    print(f"==================================================")
    print(f" PARALLEL TRAINING MANAGER STARTED (GUI MODE)")
    print(f" Total Agents: {len(agents_list)}")
    print(f" Concurrent Jobs: {args.concurrency}")
    print(f"==================================================\n")

    # Initialize the GUI Dashboard on the Main Thread
    dashboard = TrainingDashboard(len(agents_list))

    # Kick off the training manager in a Background Thread
    bg_thread = threading.Thread(
        target=background_execution_thread,
        args=(args, agents_list, base_yaml_config, dashboard),
        daemon=True  # Ensures the background thread dies if you close the GUI window
    )
    bg_thread.start()

    # Hand control over to the Tkinter GUI Loop
    dashboard.root.mainloop()