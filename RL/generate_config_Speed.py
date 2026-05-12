import json
import argparse


def generate_speed_agents(prefix, count):
    agents = []
    min_speed = 20
    max_speed = 100

    print(f"Generating {count} agents with prefix '{prefix}' focused only on Speed...")

    for i in range(count):
        # Calculate evenly distributed speeds
        if count > 1:
            speed = int(round(min_speed + i * (max_speed - min_speed) / (count - 1)))
        else:
            speed = max_speed

        num_str = f"{(i + 1):02d}"
        speed_str = f"S{speed:03d}"
        weight_str = "W0"
        acc_str = "A10"
        smooth_str = "Sm0"

        # ID example: Run007-Agent_01-S020
        agent_id = f"{prefix}-Agent_{num_str}-{speed_str}-{weight_str}-{acc_str}-{smooth_str}"

        # We pass 0.0 for the other parameters since we are testing ONLY speed right now
        agent_data = {
            "id": agent_id,
            "speed": float(speed),
            "dtc_weight": 0.0,
            "acc_time": 10.0,
            "smooth_threshold": 0.0
        }
        agents.append(agent_data)
        print(f" -> Created {agent_id} (Target Speed: {speed} km/h)")

    return agents


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Generate Speed-Focused Agent Config JSON")
    parser.add_argument("--prefix", type=str, default="Run007", help="The prefix for the Agent IDs")
    parser.add_argument("--count", type=int, default=10, help="How many agents to generate")
    parser.add_argument("--out", type=str, default="AgentConfig_SpeedTest.json", help="Output filename")

    args = parser.parse_args()

    data = generate_speed_agents(args.prefix, args.count)

    with open(args.out, "w") as f:
        json.dump(data, f, indent=2)

    print(f"\nSuccessfully saved {len(data)} agents to '{args.out}'")