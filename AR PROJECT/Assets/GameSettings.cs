public enum GameMode
{
    Tutorial,
    Regular,
    Cheats
}

public enum Difficulty
{
    Easy,
    Medium,
    Hard
}

public static class GameSettings
{
    public static GameMode CurrentMode = GameMode.Regular;
    public static bool TestMode = false;
    public static bool AutoPlace = true;
    public static Difficulty DifficultyLevel = Difficulty.Medium;
}
