import json
import argparse

def generate_agents(prefix, count):
    agents = []

    min_speed, max_speed = 50, 100
    min_dtc, max_dtc = 0, 100
    min_smooth, max_smooth = 1, 9
    min_acc, max_acc = 5, 15

    grid_size = int(round(count ** (1.0 / 4.0)))
    actual_count = grid_size**4

    if actual_count != count:
        print(
            f"Warning: Count {count} is not a perfect 4D power.\n"
            f"Using a {grid_size}x{grid_size}x{grid_size}x{grid_size} grid ({actual_count} agents) instead."
        )

    print(
        f"Generating {actual_count} agents with prefix '{prefix}' (Speed x DtC x Smoothness x Accel)..."
    )

    agent_index = 1

    physics_fps = 50.0
    full_range = 2.0

    for i in range(grid_size):
        speed = (
            max_speed
            if grid_size == 1
            else int(
                round(min_speed + i * (max_speed - min_speed) / (grid_size - 1))
            )
        )

        for j in range(grid_size):
            dtc = (
                max_dtc
                if grid_size == 1
                else int(
                    round(min_dtc + j * (max_dtc - min_dtc) / (grid_size - 1))
                )
            )

            for k in range(grid_size):
                smooth_score = (
                    max_smooth
                    if grid_size == 1
                    else int(
                        round(
                            min_smooth
                            + k * (max_smooth - min_smooth) / (grid_size - 1)
                        )
                    )
                )

                if smooth_score <= 0:
                    smooth_threshold = full_range
                else:
                    total_frames = smooth_score * physics_fps
                    smooth_threshold = full_range / total_frames

                smooth_threshold = round(smooth_threshold, 5)

                for l in range(grid_size):
                    if grid_size == 1:
                        acc_time = max_acc
                    else:
                        acc_time = min_acc + l * (max_acc - min_acc) / (
                            grid_size - 1
                        )

                    acc_time = round(acc_time, 1)

                    num_str = f"{agent_index:04d}"
                    speed_str = f"S{speed:03d}"
                    weight_str = f"W{dtc}"

                    acc_suffix = (
                        str(int(acc_time))
                        if acc_time.is_integer()
                        else str(acc_time).replace(".", "")
                    )
                    acc_str = f"A{acc_suffix}"
                    smooth_str = f"Sm{smooth_score}"

                    agent_id = f"{prefix}-Agent_{num_str}-{speed_str}-{weight_str}-{acc_str}-{smooth_str}"

                    agent_data = {
                        "id": agent_id,
                        "speed": float(speed),
                        "dtc_weight": float(dtc) / 100.0,
                        "acc_time": float(acc_time),
                        "smooth_threshold": float(smooth_threshold),
                    }
                    agents.append(agent_data)

                    agent_index += 1

    return agents


if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="Generate 4D Grid Agent Config JSON"
    )
    parser.add_argument(
        "--prefix",
        type=str,
        default="SpeedDtCSmoothnessTestShort",
        help="The prefix for the Agent IDs",
    )
    parser.add_argument(
        "--count",
        type=int,
        default=1296,
        help="Total agents (Perfect 4th powers: 16, 81, 256, 625, 1296)",
    )
    parser.add_argument(
        "--out",
        type=str,
        default="AgentConfig_Hypercube.json",
        help="Output filename",
    )

    args = parser.parse_args()

    data = generate_agents(args.prefix, args.count)

    with open(args.out, "w") as f:
        json.dump(data, f, indent=2)

    print(f"\nSuccessfully saved {len(data)} agents to '{args.out}'")