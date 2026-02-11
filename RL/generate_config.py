import json
import random
import argparse
import itertools

def generate_agents(prefix, count):
    agents = []
    vals_speed = [20, 60, 100]
    vals_dtc = [0.10, 0.53, 0.95]
    vals_acc = [5.0, 12.5, 20.0]
    vals_smooth = [0, 5, 10]

    combinations = list(itertools.product(vals_speed, vals_dtc, vals_acc, vals_smooth))
    num_structured = len(combinations)

    print(f"Generating {count} agents with prefix '{prefix}'...")

    print(f"Generating agents with prefix '{prefix}'...")

    if count < num_structured:
        print(f" [WARNING] Requesting {count} agents, but structured generation requires {num_structured} slots.")
        print(f" [WARNING] Skipping structured generation. All agents will be RANDOM.")
        for i in range(1, count + 1):
            create_random_agent(agents, prefix, i)

    else:
        print(f" [System] Generating {num_structured} structured agents (Min/Mid/Max grid)...")
        for i, combo in enumerate(combinations, 1):
            s, w, a, sm = combo
            create_agent_from_values(agents, prefix, i, s, w, a, sm)

        remaining = count - num_structured
        if remaining > 0:
            print(f" [System] Generating {remaining} additional random agents...")
            for i in range(num_structured + 1, count + 1):
                create_random_agent(agents, prefix, i)

    return agents


def create_random_agent(agents_list, prefix, index):
    speed = random.randint(20, 100)
    dtc_weight = round(random.uniform(0.1, 0.95), 2)
    acc_time = round(random.uniform(5.0, 20.0), 1)
    smoothness_score = random.randint(0, 10)

    create_agent_from_values(agents_list, prefix, index, speed, dtc_weight, acc_time, smoothness_score)


def create_agent_from_values(agents_list, prefix, index, speed, dtc_weight, acc_time, smoothness_score):
    physics_fps = 50.0
    full_range = 2.0
    
    if smooth_seconds <= 0.1:
        smooth_threshold = full_range
    else:
        total_frames = smoothness_score * physics_fps
        smooth_threshold = full_range / total_frames
    smooth_threshold = round(smooth_threshold, 5)

    num_str = f"{index:03d}"
    speed_str = f"S{speed:03d}"

    weight_val = int(dtc_weight * 100)
    weight_str = f"W{weight_val:02d}"

    acc_val = int(acc_time)
    acc_str = f"A{acc_val:02d}"

    smooth_str = f"Sm{smoothness_score:02d}"

    agent_id = f"{prefix}-Agent_{num_str}-{speed_str}-{weight_str}-{acc_str}-{smooth_str}"

    agent_data = {
        "id": agent_id,
        "speed": speed,
        "dtc_weight": dtc_weight,
        "acc_time": float(acc_time),
        "smooth_threshold": float(smooth_threshold)
    }
    agents_list.append(agent_data)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Generate Agent Config JSON")
    parser.add_argument("--prefix", type=str, default="Run001", help="The prefix for the Agent IDs (e.g. Run001)")
    parser.add_argument("--count", type=int, default=1000, help="How many agents to generate")
    parser.add_argument("--out", type=str, default="AgentConfig.json", help="Output filename")

    args = parser.parse_args()

    data = generate_agents(args.prefix, args.count)

    with open(args.out, "w") as f:
        json.dump(data, f, indent=2)

    print(f"Successfully saved {len(data)} agents to '{args.out}'")