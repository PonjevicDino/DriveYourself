import os
import argparse
import tensorflow as tf


def process_tensorboard_file(filepath, new_frequency, keep_backup):
    print(f"Processing: {filepath}")
    temp_filepath = filepath + ".temp"

    events_total = 0
    events_kept = 0

    try:
        # Initialize a writer for the new shrunken file
        writer = tf.io.TFRecordWriter(temp_filepath)

        # Iterate over all events in the original file
        for event in tf.compat.v1.train.summary_iterator(filepath):
            events_total += 1

            # Keep the event if its step falls on our new frequency boundaries.
            # We also explicitly preserve graph definitions or initialization metadata (usually at step 0).
            if event.step % new_frequency == 0 or not event.HasField('summary'):
                writer.write(event.SerializeToString())
                events_kept += 1

        writer.close()
        print(f"  -> Original events: {events_total} | Kept events: {events_kept}")

        # Handle backups and replacements
        if keep_backup:
            backup_filepath = filepath + ".bak"
            os.replace(filepath, backup_filepath)
            print(f"  -> Backup saved as: {backup_filepath}")
        else:
            os.remove(filepath)

        # Replace the original file with the new shrunken file
        os.rename(temp_filepath, filepath)
        print("  -> File successfully shrunk.\n")

    except Exception as e:
        print(f"  -> [ERROR] Failed to process {filepath}: {e}")
        # Clean up the temporary file if something went wrong
        if os.path.exists(temp_filepath):
            os.remove(temp_filepath)


def main():
    parser = argparse.ArgumentParser(description="Shrink TensorBoard event files by extending the summary frequency.")
    parser.add_argument("directory", type=str, help="Target directory to search for TensorBoard files recursively.")
    parser.add_argument("--freq", type=int, default=100000, help="New summary frequency threshold (default: 100000).")
    parser.add_argument("--backup", action="store_true", help="Keep a backup of the original files ending in .bak")

    args = parser.parse_args()
    target_dir = args.directory

    if not os.path.isdir(target_dir):
        print(f"Error: The directory '{target_dir}' does not exist.")
        return

    # Recursively find all TensorBoard files
    for root, _, files in os.walk(target_dir):
        for file in files:
            # Match TensorBoard files and exclude any temporary/backup files we might have made
            if "events.out.tfevents" in file and not file.endswith(".bak") and not file.endswith(".temp"):
                filepath = os.path.join(root, file)
                process_tensorboard_file(filepath, args.freq, args.backup)


if __name__ == "__main__":
    # Hide TensorFlow C++ compiler warnings on startup
    os.environ['TF_CPP_MIN_LOG_LEVEL'] = '2'
    main()