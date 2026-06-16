namespace MartialHeroes.Client.Domain.Progression;

/// <summary>
/// Raised when the per-level rank-XP divisor table holds a <c>0</c> divisor for the active level index —
/// the legacy client's "leveltable error" condition. The application layer catches this to log the
/// diagnostic without crashing the progression update. spec: Docs/RE/specs/progression.md §4.
/// </summary>
public sealed class LevelTableException : Exception
{
    /// <summary>Constructs the exception for the offending level-cache index.</summary>
    /// <param name="levelIndex">The level-cache index whose divisor was 0.</param>
    public LevelTableException(int levelIndex)
        : base($"leveltable error: rank-XP divisor is 0 for level index {levelIndex}.")
    {
        LevelIndex = levelIndex;
    }

    /// <summary>The level-cache index whose divisor was 0. spec: progression.md §4.</summary>
    public int LevelIndex { get; }
}