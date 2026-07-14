import json
import argparse
import re
import os


def filter_agents(input_file, output_file, agents_to_keep):
    print(f"Loading '{input_file}'...")

    with open(input_file, 'r') as f:
        data = json.load(f)

    filtered_agents = []
    # Regex pattern to match "Agent_XXXX" and capture the numbers
    pattern = re.compile(r"Agent_(\d+)")

    for agent in data:
        agent_id_string = agent.get("id", "")
        match = pattern.search(agent_id_string)

        if match:
            # Convert "0142" to 142
            agent_num = int(match.group(1))

            if agent_num in agents_to_keep:
                filtered_agents.append(agent)
        else:
            print(f"Warning: Could not parse agent number from ID: {agent_id_string}")

    # Create output directory if it doesn't exist
    out_dir = os.path.dirname(output_file)
    if out_dir and not os.path.exists(out_dir):
        os.makedirs(out_dir)

    # Save the new JSON
    with open(output_file, "w") as f:
        json.dump(filtered_agents, f, indent=2)

    print(f"Successfully filtered agents!")
    print(f"Kept {len(filtered_agents)} out of {len(data)} agents.")
    print(f"Saved to '{output_file}'")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Filter an Agent Config JSON to keep specific agents for retraining.")

    parser.add_argument(
        "--input",
        type=str,
        required=True,
        help="Path to the input JSON file (e.g., AgentConfig_Run002.1-Ret1.json)",
    )
    parser.add_argument(
        "--output",
        type=str,
        default="AgentConfig_Retrain.json",
        help="Output filepath (e.g., AgentConfig_Retrain.json)",
    )
    parser.add_argument(
        "--keep",
        type=int,
        nargs='+',
        required=True,
        help="List of agent numbers to keep, separated by spaces (e.g., --keep 142 152 181 211 214)",
    )

    args = parser.parse_args()

    filter_agents(args.input, args.output, args.keep)