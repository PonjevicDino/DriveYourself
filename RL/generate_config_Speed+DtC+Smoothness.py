import json
import argparse


def generate_cube_agents(prefix, count):
    agents = []

    # Configurable Min/Max Bounds
    min_speed, max_speed = 20, 100
    min_dtc, max_dtc = 0, 100
    min_smooth, max_smooth = 0, 9

    # Calculate grid size (cube root of the total count)
    grid_size = int(round(count ** (1.0 / 3.0)))
    actual_count = grid_size ** 3

    if actual_count != count:
        print(
            f"Warning: Count {count} is not a perfect cube. Using a {grid_size}x{grid_size}x{grid_size} grid ({actual_count} agents) instead.")

    print(f"Generating {actual_count} agents with prefix '{prefix}' (Speed x DtC x Smoothness)...")

    agent_index = 1

    # Physics constants for the Smoothness calculation
    physics_fps = 50.0
    full_range = 2.0

    # 3D Loop: Speed -> DtC -> Smoothness
    for i in range(grid_size):
        speed = max_speed if grid_size == 1 else int(round(min_speed + i * (max_speed - min_speed) / (grid_size - 1)))

        for j in range(grid_size):
            dtc = max_dtc if grid_size == 1 else int(round(min_dtc + j * (max_dtc - min_dtc) / (grid_size - 1)))

            for k in range(grid_size):
                smooth_score = max_smooth if grid_size == 1 else int(
                    round(min_smooth + k * (max_smooth - min_smooth) / (grid_size - 1)))

                # Convert the 0-9 Score into the Unity Physics Threshold
                if smooth_score <= 0:
                    smooth_threshold = full_range
                else:
                    total_frames = smooth_score * physics_fps
                    smooth_threshold = full_range / total_frames

                smooth_threshold = round(smooth_threshold, 5)

                # Format Strings
                num_str = f"{agent_index:03d}"
                speed_str = f"S{speed:03d}"
                weight_str = f"W{dtc}"
                acc_str = "A10"
                smooth_str = f"Sm{smooth_score}"

                agent_id = f"{prefix}-Agent_{num_str}-{speed_str}-{weight_str}-{acc_str}-{smooth_str}"

                # Assemble JSON Data
                agent_data = {
                    "id": agent_id,
                    "speed": float(speed),
                    "dtc_weight": float(dtc) / 100.0,
                    "acc_time": 10.0,
                    "smooth_threshold": float(smooth_threshold)
                }
                agents.append(agent_data)
                print(
                    f" -> Created {agent_id} (Speed: {speed} km/h | DtC: {dtc}% | Smooth: {smooth_score} -> Threshold: {smooth_threshold})")

                agent_index += 1

    return agents


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Generate 3D Grid Agent Config JSON")
    parser.add_argument("--prefix", type=str, default="Run009", help="The prefix for the Agent IDs")
    parser.add_argument("--count", type=int, default=27,
                        help="Total number of agents (should be a perfect cube, e.g., 8, 27, 64, 125)")
    parser.add_argument("--out", type=str, default="AgentConfig_CubeTest.json", help="Output filename")

    args = parser.parse_args()

    data = generate_cube_agents(args.prefix, args.count)

    with open(args.out, "w") as f:
        json.dump(data, f, indent=2)

    print(f"\nSuccessfully saved {len(data)} agents to '{args.out}'")