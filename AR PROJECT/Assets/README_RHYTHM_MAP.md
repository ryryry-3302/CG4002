# Creating a Rhythm Map for a New Song

## 1. Add the Audio File

1. Place your `.mp3` file in the project (e.g. `Assets/` or a subfolder).
2. Unity will import it as an audio asset. Ensure **Load Type** is set appropriately (e.g. `Streaming` for long songs).

## 2. Generate the Rhythm Map JSON

Use the Python script to auto-generate cues from beat/onset detection:

```bash
cd "AR PROJECT/Assets"
python GenerateRhythmMap.py path/to/your-song.mp3

# Or specify output path:
python GenerateRhythmMap.py path/to/your-song.mp3 path/to/output_Map.json
```

**Requirements:** `pip install librosa numpy`

**Default output:** `your-song_Map.json` (same folder as input if no output specified)

**Options:** Edit `GenerateRhythmMap.py` to change `min_interval` (default 4.0s between cues) or the gesture/section rotation.

## 3. JSON Format

Each cue has:

```json
{
  "timestamp": 4.39,
  "gestureId": "UP",
  "section": "flute"
}
```

- **timestamp** – seconds from song start
- **gestureId** – `UP`, `DOWN`, `PUNCH`, `CIRCLE`, `WITHDRAW`, `LAMBDA_SHAPE`, etc. (see `GameTypes.cs`).
- **section** – `flute`, `drum`, `pipe`, `xylophone` (lowercase)

## 4. Create a SongData Asset

1. Right-click in Project → **Create → Orchestra → Song Data**
2. Set:
   - **Song Name** – display name
   - **Artist Name** – optional
   - **Audio Clip** – your MP3
   - **Rhythm Map Json** – the generated JSON file (drag as TextAsset)
   - **BPM** / **Offset** – optional, for future features

## 5. Add to Song Selection

1. Open the **Game** scene.
2. Select the **RhythmGameController** object.
3. Set **Available Songs** size and drag your SongData assets into the array.
4. When you tap "✔ START GAME" after placing characters, the song selection menu appears. Pick a song to begin.

---

## Quick Example: Fur Elise

```bash
pip install librosa numpy
cd "AR PROJECT/Assets"
python GenerateRhythmMap.py "../jeremusic70-fur-elise-and-orchestra-110149.mp3"
# Creates jeremusic70-fur-elise-and-orchestra-110149_Map.json
```

Then: Create Song Data asset → assign the MP3 and JSON → add to RhythmGameController's Available Songs.

---

## Manual Editing

You can edit the JSON to add cues, change gestures, or sections. Ensure timestamps are in ascending order.
