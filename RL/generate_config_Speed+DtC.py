import json
import argparse
import math


def generate_matrix_agents(prefix, count):
    agents = []

    min_speed = 20
    max_speed = 100

    min_dtc = 0
    max_dtc = 100

    # Calculate grid dimensions (assuming a square matrix, e.g., 25 agents = 5x5 grid)
    grid_size = int(math.isqrt(count))

    if grid_size * grid_size != count:
        print(
            f"Warning: Count {count} is not a perfect square. Using a {grid_size}x{grid_size} grid ({grid_size ** 2} agents) instead.")

    print(f"Generating {grid_size ** 2} agents with prefix '{prefix}' (Speed x DtC Matrix)...")

    agent_index = 1

    for i in range(grid_size):
        # Calculate evenly distributed speed for this row
        if grid_size > 1:
            speed = int(round(min_speed + i * (max_speed - min_speed) / (grid_size - 1)))
        else:
            speed = max_speed

        for j in range(grid_size):
            # Calculate evenly distributed DtC for this column
            if grid_size > 1:
                dtc = int(round(min_dtc + j * (max_dtc - min_dtc) / (grid_size - 1)))
            else:
                dtc = max_dtc

            num_str = f"{agent_index:03d}"
            speed_str = f"S{speed:03d}"
            weight_str = f"W{dtc}"
            acc_str = "A10"
            smooth_str = "Sm0"

            # ID example: Run008-Agent_01-S020-W0-A10-Sm0
            agent_id = f"{prefix}-Agent_{num_str}-{speed_str}-{weight_str}-{acc_str}-{smooth_str}"

            # Normalize dtc_weight from 0-100 to 0.0-1.0 for Unity's EnvironmentParameters
            agent_data = {
                "id": agent_id,
                "speed": float(speed),
                "dtc_weight": float(dtc) / 100.0,
                "acc_time": 10.0,
                "smooth_threshold": 0.0
            }
            agents.append(agent_data)
            print(f" -> Created {agent_id} (Target Speed: {speed} km/h | DtC Weight: {dtc}%)")

            agent_index += 1

    return agents


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Generate Speed x DtC Agent Config JSON")
    parser.add_argument("--prefix", type=str, default="Run008", help="The prefix for the Agent IDs")
    parser.add_argument("--count", type=int, default=25,
                        help="Total number of agents (should be a perfect square, e.g., 9, 16, 25)")
    parser.add_argument("--out", type=str, default="AgentConfig_MatrixTest.json", help="Output filename")

    args = parser.parse_args()

    data = generate_matrix_agents(args.prefix, args.count)

    with open(args.out, "w") as f:
        json.dump(data, f, indent=2)

    print(f"\nSuccessfully saved {len(data)} agents to '{args.out}'")