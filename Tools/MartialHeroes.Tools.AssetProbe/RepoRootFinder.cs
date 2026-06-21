namespace MartialHeroes.Tools.AssetProbe;

/// <summary>
///     Locates the repository root (the directory containing <c>MartialHeroes.slnx</c>) by walking up
///     from the harness binary. Used by the output-path guard (refuse the repo tree).
/// </summary>
internal static class RepoRootFinder
{
    private static string? _cached;
    private static bool _resolved;

    public static string? Find()
    {
        if (_resolved) return _cached;
        _resolved = true;
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MartialHeroes.slnx")))
            {
                _cached = dir.FullName;
                return _cached;
            }

        return null;
    }
}