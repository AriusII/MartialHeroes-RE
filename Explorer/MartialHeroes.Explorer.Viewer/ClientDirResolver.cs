using Godot;
using Environment = System.Environment;

namespace MartialHeroes.Explorer.Viewer;

public static class ClientDirResolver
{
    private static readonly string[] Candidates =
    [
        @"D:\MartialHeroesClient",
        @"C:\MartialHeroesClient"
    ];

    public static string? Resolve()
    {
        var env = Environment.GetEnvironmentVariable("MH_CLIENT_DIR");
        if (!string.IsNullOrWhiteSpace(env) && IsValid(env))
        {
            GD.Print($"[Viewer] ClientDir from MH_CLIENT_DIR: {env}");
            return env;
        }

        var cfgDir = ReadFromCfg();
        if (cfgDir is not null && IsValid(cfgDir))
        {
            GD.Print($"[Viewer] ClientDir from client_dir.cfg: {cfgDir}");
            return cfgDir;
        }

        try
        {
            var projDir = ProjectSettings.GlobalizePath("res://");
            var sibling = Path.GetFullPath(
                Path.Combine(projDir, "..", "..", "05.Presentation", "MartialHeroes.Client.Godot", "clientdata"));
            if (IsValid(sibling))
            {
                GD.Print($"[Viewer] ClientDir from sibling clientdata: {sibling}");
                return sibling;
            }
        }
        catch
        {
        }

        foreach (var candidate in Candidates)
            if (IsValid(candidate))
            {
                GD.Print($"[Viewer] ClientDir auto-detected: {candidate}");
                return candidate;
            }

        return null;
    }

    public static bool IsValid(string dir)
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

    public static (string InfPath, string VfsPath) GetVfsPaths(string clientDir)
    {
        return (
            Path.Combine(clientDir, "data.inf"),
            Path.Combine(clientDir, "data", "data.vfs")
        );
    }

    private static string? ReadFromCfg()
    {
        try
        {
            string cfgPath;
            try
            {
                cfgPath = ProjectSettings.GlobalizePath("res://client_dir.cfg");
            }
            catch
            {
                cfgPath = "client_dir.cfg";
            }

            if (!File.Exists(cfgPath)) return null;

            foreach (var rawLine in File.ReadLines(cfgPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var eq = line.IndexOf('=');
                if (eq < 0) continue;
                var k = line[..eq].Trim();
                var v = line[(eq + 1)..].Trim();
                if (k.Equals("client_dir", StringComparison.OrdinalIgnoreCase) && v.Length > 0)
                    return v;
            }
        }
        catch
        {
        }

        return null;
    }
}