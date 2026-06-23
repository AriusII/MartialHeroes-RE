namespace MartialHeroes.Client.Domain.Progression.Progression;

public sealed class LevelTableException : Exception
{
    public LevelTableException(int levelIndex)
        : base($"leveltable error: rank-XP divisor is 0 for level index {levelIndex}.")
    {
        LevelIndex = levelIndex;
    }

    public int LevelIndex { get; }
}