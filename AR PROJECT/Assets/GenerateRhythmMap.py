
import librosa
import numpy as np
import json
import sys
import os

def generate_rhythm_map(audio_file, output_file, min_interval=4.0):
    """
    Generates a rhythm map JSON from an audio file using beat detection.
    Cues are placed on actual detected beat positions from the audio,
    filtered to have at least min_interval seconds between them.

    Args:
        audio_file (str): Path to input audio file.
        output_file (str): Path to output JSON file.
        min_interval (float): Minimum time in seconds between cues.
    """
    print(f"Loading {audio_file}...")
    try:
        y, sr = librosa.load(audio_file)
    except Exception as e:
        print(f"Error loading audio file: {e}")
        return

    duration = librosa.get_duration(y=y, sr=sr)
    print(f"Audio duration: {duration:.2f} seconds")

    # Detect tempo and beat positions
    print("Analyzing tempo and beats...")
    tempo, beat_frames = librosa.beat.beat_track(y=y, sr=sr)
    beat_times = librosa.frames_to_time(beat_frames, sr=sr)

    # Handle tempo being returned as an array (newer librosa versions)
    if hasattr(tempo, '__len__'):
        tempo = float(tempo[0])
    else:
        tempo = float(tempo)

    print(f"Detected Tempo: {tempo:.2f} BPM")
    print(f"Beat interval: {60.0/tempo:.3f} seconds")
    print(f"Total beats detected: {len(beat_times)}")

    # Also detect onsets for more musical accuracy
    onset_frames = librosa.onset.onset_detect(y=y, sr=sr)
    onset_times = librosa.frames_to_time(onset_frames, sr=sr)
    print(f"Total onsets detected: {len(onset_times)}")

    # Merge beats and onsets, sort, and deduplicate (within 0.1s)
    all_times = np.sort(np.concatenate([beat_times, onset_times]))
    # Remove near-duplicates
    filtered_times = [all_times[0]]
    for t in all_times[1:]:
        if t - filtered_times[-1] > 0.1:
            filtered_times.append(t)

    print(f"Total unique musical events: {len(filtered_times)}")

    # Sections and gestures to cycle through
    sections = ["flute", "drum", "pipe", "xylophone"]
    gestures = ["UP", "DOWN", "PUNCH"]

    cues = []
    last_cue_time = -min_interval  # Ensure first event can be picked

    section_index = 0
    gesture_index = 0

    for t in filtered_times:
        if t - last_cue_time >= min_interval:
            cue = {
                "timestamp": round(float(t), 2),
                "gestureId": gestures[gesture_index % len(gestures)],
                "section": sections[section_index % len(sections)]
            }
            cues.append(cue)

            last_cue_time = t
            section_index += 1
            gesture_index += 1

    # Wrap in root object
    data = {"cues": cues}

    with open(output_file, 'w') as f:
        json.dump(data, f, indent=2)

    print(f"\nGenerated {len(cues)} cues to {output_file}")
    print(f"First cue at: {cues[0]['timestamp']}s")
    print(f"Last cue at: {cues[-1]['timestamp']}s")

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python GenerateRhythmMap.py <audio_file> [output_file]")
        sys.exit(1)

    input_audio = sys.argv[1]
    if len(sys.argv) >= 3:
        output_json = sys.argv[2]
    else:
        base, _ = os.path.splitext(input_audio)
        output_json = base + "_Map.json"

    generate_rhythm_map(input_audio, output_json, min_interval=4.0)

