namespace MartialHeroes.Client.Domain.Progression.Progression;

public sealed class LevelTableException(int levelIndex)
    : Exception($"leveltable error: rank-XP divisor is 0 for level index {levelIndex}.")
{
    public int LevelIndex { get; } = levelIndex;
}