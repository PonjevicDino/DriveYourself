import json
import random
import argparse

def generate_agents(prefix, count):
    agents = []

    print(f"Generating {count} agents with prefix '{prefix}'...")

    for i in range(1, count + 1):
        speed = random.randint(20, 150)
        dtc_weight = round(random.uniform(0.1, 0.95), 2)
        acc_time = round(random.uniform(5.0, 20.0), 1)

        smoothness_score = random.randint(2, 10)
        smooth_threshold = round(1.0 - (smoothness_score / 10.0), 2)
        smooth_threshold = max(0.1, smooth_threshold)

        num_str = f"{i:03d}"
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
        agents.append(agent_data)

    return agents


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