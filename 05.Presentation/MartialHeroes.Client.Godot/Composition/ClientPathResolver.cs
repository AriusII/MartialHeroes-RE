using Godot;
using Environment = System.Environment;

namespace MartialHeroes.Client.Godot.Composition;

public static class ClientPathResolver
{
    private const string ClientDataResPath = "res://clientdata";

    private const string ConfigResPath = "res://client_dir.cfg";

    private static readonly string[] AutoDetectCandidates =
    [
        @"D:\MartialHeroesClient",
        @"C:\MartialHeroesClient",
        "LegacyClient"
    ];


    public static string? ResolveClientDir()
    {
        var envDir = Environment.GetEnvironmentVariable("MH_CLIENT_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            if (IsValidClientDir(envDir))
            {
                GD.Print($"[ClientPath] Resolved from MH_CLIENT_DIR env: {envDir}");
                return envDir;
            }

            GD.PrintErr($"[ClientPath] MH_CLIENT_DIR='{envDir}' invalide (data.inf / data/data.vfs introuvables).");
        }

        var cfgDir = ReadClientDirFromConfig();
        if (cfgDir is not null)
        {
            if (!Path.IsPathRooted(cfgDir))
                cfgDir = GlobalizeRes("res://" + cfgDir.Replace('\\', '/'));

            if (IsValidClientDir(cfgDir))
            {
                GD.Print($"[ClientPath] Resolved from client_dir.cfg: {cfgDir}");
                return cfgDir;
            }

            GD.PrintErr($"[ClientPath] client_dir.cfg pointe sur '{cfgDir}' mais les fichiers VFS sont absents.");
        }

        var embedded = GlobalizeRes(ClientDataResPath);
        if (IsValidClientDir(embedded))
        {
            GD.Print($"[ClientPath] Resolved from project-local clientdata/: {embedded}");
            return embedded;
        }

        foreach (var candidate in AutoDetectCandidates)
            if (IsValidClientDir(candidate))
            {
                GD.Print($"[ClientPath] Resolved via auto-détection: {candidate}");
                return candidate;
            }

        GD.Print("[ClientPath] Aucun répertoire client trouvé — mode synthétique (aucun asset réel).");
        return null;
    }

    public static bool RealAssetsEnabled(string? clientDir)
    {
        return clientDir is not null;
    }


    public static bool IsValidClientDir(string dir)
    {
        try
        {
            return File.Exists(Path.Combine(dir, "data.inf"))
                   && File.Exists(Path.Combine(dir, "data", "data.vfs"));
        }
        catch
        {
            return false;
        }
    }


    private static string? ReadClientDirFromConfig()
    {
        var cfgPath = ResolveCfgPath();
        if (cfgPath is null) return null;

        return ReadKeyFromCfg(cfgPath, "client_dir");
    }

    private static string GlobalizeRes(string resPath)
    {
        try
        {
            return ProjectSettings.GlobalizePath(resPath);
        }
        catch
        {
            return resPath.StartsWith("res://", StringComparison.Ordinal)
                ? resPath["res://".Length..]
                : resPath;
        }
    }

    private static string? ResolveCfgPath()
    {
        try
        {
            var absPath = ProjectSettings.GlobalizePath(ConfigResPath);
            return File.Exists(absPath) ? absPath : null;
        }
        catch
        {
            const string fallbackName = "client_dir.cfg";
            return File.Exists(fallbackName) ? fallbackName : null;
        }
    }

    private static string? ReadKeyFromCfg(string cfgPath, string key)
    {
        try
        {
            foreach (var rawLine in File.ReadLines(cfgPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                var eq = line.IndexOf('=');
                if (eq < 0) continue;

                var k = line[..eq].Trim();
                var v = line[(eq + 1)..].Trim();

                if (k.Equals(key, StringComparison.OrdinalIgnoreCase) && v.Length > 0)
                    return v;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ClientPath] Erreur lecture '{cfgPath}': {ex.Message}");
        }

        return null;
    }
}