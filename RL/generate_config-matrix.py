import json
import argparse
import os


def generate_agents(prefix, matrix):
    agents = []

    # Defined Ranges
    min_speed, max_speed = 50, 112.5
    min_dtc, max_dtc = 0, 100
    min_smooth, max_smooth = 1, 9
    min_acc, max_acc = 5, 15

    # Unpack the custom matrix from the command line
    num_speeds, num_dtc, num_smooth, num_acc = matrix

    actual_count = num_speeds * num_dtc * num_smooth * num_acc

    print(
        f"Generating {actual_count} agents with prefix '{prefix}'\n"
        f"({num_speeds} Speeds x {num_dtc} DtC x {num_smooth} Smoothness x {num_acc} Accel)..."
    )

    agent_index = 1
    physics_fps = 50.0
    full_range = 2.0

    for i in range(num_speeds):
        speed = max_speed if num_speeds == 1 else int(round(min_speed + i * (max_speed - min_speed) / (num_speeds - 1)))

        for j in range(num_dtc):
            dtc = max_dtc if num_dtc == 1 else int(round(min_dtc + j * (max_dtc - min_dtc) / (num_dtc - 1)))

            for k in range(num_smooth):
                smooth_score = max_smooth if num_smooth == 1 else int(
                    round(min_smooth + k * (max_smooth - min_smooth) / (num_smooth - 1)))

                if smooth_score <= 0:
                    smooth_threshold = full_range
                else:
                    total_frames = smooth_score * physics_fps
                    smooth_threshold = full_range / total_frames

                smooth_threshold = round(smooth_threshold, 5)

                for l in range(num_acc):
                    acc_time = max_acc if num_acc == 1 else min_acc + l * (max_acc - min_acc) / (num_acc - 1)
                    acc_time = round(acc_time, 1)

                    # String Formatting
                    num_str = f"{agent_index:04d}"
                    speed_str = f"S{speed:03d}"
                    weight_str = f"W{dtc}"

                    acc_suffix = str(int(acc_time)) if acc_time.is_integer() else str(acc_time).replace(".", "")
                    acc_str = f"A{acc_suffix}"
                    smooth_str = f"Sm{smooth_score}"

                    agent_id = f"{prefix}-Agent_{num_str}-{speed_str}-{weight_str}-{acc_str}-{smooth_str}"

                    # Construct Data
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
    parser = argparse.ArgumentParser(description="Generate Custom Grid Agent Config JSON")

    parser.add_argument(
        "--prefix",
        type=str,
        default="TestRun",
        help="The prefix for the Agent IDs",
    )
    parser.add_argument(
        "--out",
        type=str,
        default="AgentConfig_CustomGrid.json",
        help="Output filepath (e.g., ./output/config.json)",
    )
    parser.add_argument(
        "--matrix",
        type=int,
        nargs=4,
        metavar=('SPEEDS', 'DTC', 'SMOOTH', 'ACC'),
        default=[6, 5, 3, 3],
        help="Provide 4 numbers separated by spaces for the matrix (Speed, DtC, Smoothness, Accel)",
    )

    args = parser.parse_args()

    # Create output directory if it doesn't exist
    out_dir = os.path.dirname(args.out)
    if out_dir and not os.path.exists(out_dir):
        os.makedirs(out_dir)

    data = generate_agents(args.prefix, args.matrix)

    with open(args.out, "w") as f:
        json.dump(data, f, indent=2)

    print(f"\nSuccessfully saved {len(data)} agents to '{args.out}'")