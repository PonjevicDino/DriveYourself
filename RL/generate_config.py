import json
import random
import argparse

def generate_agents(prefix, count):
    agents = []

    print(f"Generating {count} agents with prefix '{prefix}'...")

    for i in range(1, count):
        speed = random.randint(20, 150)

        dtc_weight = round(random.uniform(0.1, 0.95), 2)

        num_str = f"{i:03d}"

        speed_str = f"S{speed:03d}"

        weight_val = int(dtc_weight * 100)
        weight_str = f"W{weight_val:02d}"

        agent_id = f"{prefix}-Agent_{num_str}-{speed_str}-{weight_str}"

        agent_data = {
            "id": agent_id,
            "speed": speed,
            "dtc_weight": dtc_weight
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