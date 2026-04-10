using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrchestraMaestro
{
    [Serializable]
    public class LeaderboardEntry
    {
        public string playerName;
        public int score;
        public long savedAtTicks;
    }

    [Serializable]
    public class SongLeaderboard
    {
        public string songKey;
        public string songDisplayName;
        public List<LeaderboardEntry> entries = new List<LeaderboardEntry>();
    }

    [Serializable]
    internal class LeaderboardDatabase
    {
        public List<SongLeaderboard> boards = new List<SongLeaderboard>();
    }

    public static class LeaderboardService
    {
        public const int MaxEntries = 10;
        public const int MaxNameLength = 6;

        private const string SaveKey = "OrchestraMaestro.Leaderboard.V1";
        private static LeaderboardDatabase cache;

        public static string GetSongKey(SongData song)
        {
            if (song == null) return "unknown-song";
            return GetSongKey(song.songName);
        }

        public static string GetSongKey(string songName)
        {
            string normalized = string.IsNullOrWhiteSpace(songName) ? "Unknown Song" : songName.Trim();
            return normalized.ToLowerInvariant();
        }

        public static string GetSongDisplayName(SongData song)
        {
            if (song == null || string.IsNullOrWhiteSpace(song.songName)) return "Unknown Song";
            return song.songName.Trim();
        }

        public static string GetSongDisplayNameForKey(string songKey)
        {
            EnsureLoaded();
            SongLeaderboard board = FindBoard(songKey);
            if (board == null || string.IsNullOrWhiteSpace(board.songDisplayName))
                return "Unknown Song";
            return board.songDisplayName;
        }

        public static List<string> GetSongKeys()
        {
            EnsureLoaded();
            List<string> keys = new List<string>();
            for (int i = 0; i < cache.boards.Count; i++)
            {
                SongLeaderboard board = cache.boards[i];
                if (board == null || string.IsNullOrWhiteSpace(board.songKey)) continue;
                if (board.entries == null || board.entries.Count == 0) continue;
                keys.Add(board.songKey);
            }
            return keys;
        }

        public static List<LeaderboardEntry> GetEntries(string songKey)
        {
            EnsureLoaded();
            SongLeaderboard board = FindBoard(songKey);
            if (board == null || board.entries == null)
                return new List<LeaderboardEntry>();

            List<LeaderboardEntry> copy = new List<LeaderboardEntry>(board.entries.Count);
            for (int i = 0; i < board.entries.Count; i++)
            {
                LeaderboardEntry entry = board.entries[i];
                if (entry == null) continue;
                copy.Add(new LeaderboardEntry
                {
                    playerName = entry.playerName,
                    score = entry.score,
                    savedAtTicks = entry.savedAtTicks
                });
            }
            return copy;
        }

        public static bool QualifiesForTop(string songKey, int score)
        {
            EnsureLoaded();
            SongLeaderboard board = FindBoard(songKey);
            if (board == null || board.entries == null || board.entries.Count < MaxEntries)
                return true;

            int lowest = int.MaxValue;
            for (int i = 0; i < board.entries.Count; i++)
            {
                if (board.entries[i].score < lowest)
                    lowest = board.entries[i].score;
            }
            return score > lowest;
        }

        public static int SubmitScore(string songKey, string songDisplayName, string playerName, int score)
        {
            EnsureLoaded();

            if (!QualifiesForTop(songKey, score))
                return -1;

            string normalizedName = NormalizePlayerName(playerName);
            SongLeaderboard board = GetOrCreateBoard(songKey, songDisplayName);

            board.entries.Add(new LeaderboardEntry
            {
                playerName = normalizedName,
                score = score,
                savedAtTicks = DateTime.UtcNow.Ticks
            });

            board.entries.Sort(CompareEntries);
            if (board.entries.Count > MaxEntries)
                board.entries.RemoveRange(MaxEntries, board.entries.Count - MaxEntries);

            Save();

            for (int i = 0; i < board.entries.Count; i++)
            {
                LeaderboardEntry entry = board.entries[i];
                if (entry.score == score && string.Equals(entry.playerName, normalizedName, StringComparison.Ordinal))
                    return i + 1;
            }

            return -1;
        }

        public static string NormalizePlayerName(string rawName)
        {
            string normalized = SanitizePlayerNameInput(rawName).Trim();
            return string.IsNullOrEmpty(normalized) ? "PLAYER" : normalized;
        }

        public static string SanitizePlayerNameInput(string rawInput)
        {
            if (string.IsNullOrEmpty(rawInput)) return string.Empty;

            char[] chars = rawInput.ToUpperInvariant().ToCharArray();
            List<char> filtered = new List<char>(MaxNameLength);
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!char.IsLetterOrDigit(c) && c != ' ') continue;
                filtered.Add(c);
                if (filtered.Count >= MaxNameLength) break;
            }

            return new string(filtered.ToArray());
        }

        private static int CompareEntries(LeaderboardEntry a, LeaderboardEntry b)
        {
            int scoreCompare = b.score.CompareTo(a.score);
            if (scoreCompare != 0) return scoreCompare;
            return a.savedAtTicks.CompareTo(b.savedAtTicks);
        }

        private static SongLeaderboard GetOrCreateBoard(string songKey, string songDisplayName)
        {
            string normalizedKey = GetSongKey(songKey);
            SongLeaderboard board = FindBoard(normalizedKey);
            if (board != null)
            {
                if (!string.IsNullOrWhiteSpace(songDisplayName))
                    board.songDisplayName = songDisplayName.Trim();
                return board;
            }

            SongLeaderboard created = new SongLeaderboard
            {
                songKey = normalizedKey,
                songDisplayName = string.IsNullOrWhiteSpace(songDisplayName) ? "Unknown Song" : songDisplayName.Trim(),
                entries = new List<LeaderboardEntry>()
            };
            cache.boards.Add(created);
            return created;
        }

        private static SongLeaderboard FindBoard(string songKey)
        {
            string normalizedKey = GetSongKey(songKey);
            for (int i = 0; i < cache.boards.Count; i++)
            {
                SongLeaderboard board = cache.boards[i];
                if (board == null) continue;
                if (string.Equals(board.songKey, normalizedKey, StringComparison.Ordinal))
                    return board;
            }
            return null;
        }

        private static void EnsureLoaded()
        {
            if (cache != null) return;

            if (!PlayerPrefs.HasKey(SaveKey))
            {
                cache = new LeaderboardDatabase();
                return;
            }

            string raw = PlayerPrefs.GetString(SaveKey, string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
            {
                cache = new LeaderboardDatabase();
                return;
            }

            try
            {
                cache = JsonUtility.FromJson<LeaderboardDatabase>(raw);
            }
            catch
            {
                cache = new LeaderboardDatabase();
            }

            if (cache == null)
                cache = new LeaderboardDatabase();
            if (cache.boards == null)
                cache.boards = new List<SongLeaderboard>();
        }

        private static void Save()
        {
            string serialized = JsonUtility.ToJson(cache);
            PlayerPrefs.SetString(SaveKey, serialized);
            PlayerPrefs.Save();
        }
    }
}