import argparse
import json
import subprocess
import os
import sys
import yaml
import time
from concurrent.futures import ThreadPoolExecutor

BASE_PORT = 5005
PORT_GAP = 64


def train_single_agent(agent_data, base_yaml_config, args, worker_index):
    delay = 15 # * worker_index
    if delay > 0:
        print(f" [System] Worker {worker_index} waiting {delay}s to avoid CPU spike...")
        time.sleep(delay)
        
    run_id = agent_data.get("id", "Unknown_Agent")
    speed = agent_data.get("speed", 50)
    dtc_weight = agent_data.get("dtc_weight", 0.5)

    assigned_port = BASE_PORT + (worker_index * PORT_GAP)

    print(f"------------------------------------------------")
    print(f" [Worker {worker_index}] STARTING: {run_id}")
    print(f" >> Target: {speed} km/h | DtC: {dtc_weight}")
    print(f" >> Base Port: {assigned_port} (Range: {assigned_port}-{assigned_port + 16})")
    print(f"------------------------------------------------")

    temp_config_path = f"temp_config_{run_id}.yaml"

    local_config = base_yaml_config.copy()
    local_config["environment_parameters"] = {
        "target_speed": float(speed),
        "dtc_weight": float(dtc_weight)
    }
    
    if 'behaviors' in local_config:
        for behavior_name in local_config['behaviors']:
            local_config['behaviors'][behavior_name]['max_steps'] = args.steps

    with open(temp_config_path, 'w') as f:
        yaml.dump(local_config, f)

    cmd = [
        "mlagents-learn",
        temp_config_path,
        f"--run-id={run_id}",
        f"--env={args.env}",
        f"--base-port={assigned_port}",
        "--num-envs=16",
        "--no-graphics",
        "--force",
        "--width=512", "--height=512",
        "--timeout-wait=300"
    ]

    try:
        process = subprocess.Popen(
            cmd,
            creationflags=subprocess.CREATE_NEW_CONSOLE
        )

        return_code = process.wait()

        if return_code == 0:
            print(f" [Worker {worker_index}] FINISHED: {run_id} (Success)\n")
        else:
            print(f" [Worker {worker_index}] FAILED: {run_id} (Exit Code: {return_code})\n")

    except Exception as e:
        print(f" [Worker {worker_index}] CRASHED: {run_id}. Error: {e}\n")
    finally:
        if os.path.exists(temp_config_path):
            os.remove(temp_config_path)


def run_training_manager():
    parser = argparse.ArgumentParser(description='Parallel ML-Agents Training Manager')
    parser.add_argument('--config', type=str, required=True)
    parser.add_argument('--env', type=str, required=True)
    parser.add_argument('--json', type=str, required=True)
    parser.add_argument('--steps', type=int, default=5000000)
    parser.add_argument('--concurrency', type=int, default=1)

    args = parser.parse_args()

    if not os.path.exists(args.config): print("Config missing"); return
    if not os.path.exists(args.env): print("Env missing"); return
    if not os.path.exists(args.json): print("JSON missing"); return

    with open(args.json, 'r') as f:
        agents_list = json.load(f)
    with open(args.config, 'r') as f:
        base_yaml_config = yaml.safe_load(f)

    print(f"==================================================")
    print(f" PARALLEL TRAINING MANAGER STARTED")
    print(f" Total Agents: {len(agents_list)}")
    print(f" Concurrent Jobs: {args.concurrency}")
    print(f"==================================================\n")

    with ThreadPoolExecutor(max_workers=args.concurrency) as executor:
        futures = []
        for i, agent in enumerate(agents_list):
            futures.append(
                executor.submit(train_single_agent, agent, base_yaml_config, args, i)
            )


if __name__ == "__main__":
    run_training_manager()