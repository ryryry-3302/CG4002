public enum GameMode
{
    Tutorial,
    Regular,
    Cheats
}

public static class GameSettings
{
    public static GameMode CurrentMode = GameMode.Regular;
}
