
import librosa
import numpy as np
import json
import sys
import os
import argparse

def generate_rhythm_map(audio_file, output_file, min_interval=4.0, intro_no_cue_seconds=5.0, bpm_override=None):
    """
    Generates a rhythm map JSON from an audio file using beat detection.
    Cues are placed on actual detected beat positions from the audio,
    filtered to have at least min_interval seconds between them.

    Args:
        audio_file (str): Path to input audio file.
        output_file (str): Path to output JSON file.
        min_interval (float): Minimum time in seconds between cues.
        intro_no_cue_seconds (float): Prevent cues from being generated in the first N seconds.
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

    detected_tempo = tempo
    if bpm_override is not None:
        tempo = float(bpm_override)
        print(f"Using BPM override: {tempo:.2f} BPM (detected {detected_tempo:.2f} BPM)")

    print(f"Detected Tempo: {tempo:.2f} BPM")
    print(f"Beat interval: {60.0/tempo:.3f} seconds")
    print(f"Total beats detected: {len(beat_times)}")

    # Also detect onsets for more musical accuracy
    onset_frames = librosa.onset.onset_detect(y=y, sr=sr)
    onset_times = librosa.frames_to_time(onset_frames, sr=sr)
    print(f"Total onsets detected: {len(onset_times)}")

    offset_candidates = []
    if len(beat_times) > 0:
        offset_candidates.append(float(beat_times[0]))
    if len(onset_times) > 0:
        offset_candidates.append(float(onset_times[0]))
    suggested_song_offset = min(offset_candidates) if offset_candidates else 0.0

    # Merge beats and onsets, sort, and deduplicate (within 0.1s)
    all_times = np.sort(np.concatenate([beat_times, onset_times]))
    # Remove near-duplicates
    filtered_times = [all_times[0]]
    for t in all_times[1:]:
        if t - filtered_times[-1] > 0.1:
            filtered_times.append(t)

    print(f"Total unique musical events: {len(filtered_times)}")

    # Tempo section logic (30 seconds halfway through the song)
    halfway = duration / 2.0
    tempo_start = halfway - 15.0
    tempo_end = halfway + 15.0

    # No cue zone logic: 5 seconds before start and 5 seconds after end
    no_cue_start = tempo_start - 5.0
    no_cue_end = tempo_end + 5.0

    # Sections and gestures to cycle through
    sections = ["flute", "drum", "pipe", "xylophone"]
    gestures = [
        "UP",
        "DOWN",
        "PUNCH",
        "WITHDRAW",
        "W_SHAPE",
        "HOURGLASS_SHAPE",
        "LIGHTNING_BOLT_SHAPE",
        "TRIPLE_CLOCKWISE_CIRCLE"
    ]

    cues = []
    
    # We want cues to skip the tempo section zone (including 5s buffers)
    last_cue_time = 0.0 - min_interval  

    section_index = 0
    gesture_index = 0

    for t in filtered_times:
        # No cues in the first intro window
        if t < intro_no_cue_seconds:
            continue

        # Skip cues that fall into the restricted zone
        if t >= no_cue_start and t <= no_cue_end:
            continue
            
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
    data = {
        "cues": cues,
        "tempoSection": {"start": round(tempo_start, 2), "end": round(tempo_end, 2)},
        "tempoBpm": round(float(tempo), 3),
        "detectedTempoBpm": round(float(detected_tempo), 3),
        "suggestedSongOffset": round(suggested_song_offset, 3),
        "generationConfig": {
            "minInterval": min_interval,
            "introNoCueSeconds": intro_no_cue_seconds,
            "bpmOverride": bpm_override
        }
    }

    with open(output_file, 'w') as f:
        json.dump(data, f, indent=2)

    print(f"\nGenerated {len(cues)} cues to {output_file}")
    if cues:
        print(f"First cue at: {cues[0]['timestamp']}s")
        print(f"Last cue at: {cues[-1]['timestamp']}s")
    else:
        print("No cues generated. Try lowering min_interval.")

    print(f"Recommended SongData.offset: {suggested_song_offset:.3f}s")
    print("Apply this in the SongData asset 'offset' field for beat alignment.")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Generate rhythm map JSON from an audio file")
    parser.add_argument("audio_file", help="Path to input audio file")
    parser.add_argument("output_file", nargs="?", help="Path to output JSON file")
    parser.add_argument("--bpm", type=float, default=None, help="Optional BPM override")
    parser.add_argument("--min-interval", type=float, default=4.0, help="Minimum seconds between cues")
    args = parser.parse_args()

    input_audio = args.audio_file
    output_json = args.output_file
    if output_json is None:
        base, _ = os.path.splitext(input_audio)
        output_json = base + "_Map.json"

    generate_rhythm_map(
        input_audio,
        output_json,
        min_interval=args.min_interval,
        bpm_override=args.bpm
    )
