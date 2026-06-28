using System;
using System.Collections.Generic;
using System.IO;

namespace MartialHeroes.Explorer.Files.Services;

public static class ClientLocator
{
    private const string InfName = "data.inf";

    private static readonly string[] AbsoluteCandidates =
    [
        @"D:\MartialHeroesClient",
        @"C:\MartialHeroesClient"
    ];

    public static bool IsValidClientDir(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;

        try
        {
            return File.Exists(Path.Combine(dir, InfName))
                   && File.Exists(Path.Combine(dir, "data", "data.vfs"));
        }
        catch
        {
            return false;
        }
    }

    public static string? ResolveClientDir()
    {
        var env = Environment.GetEnvironmentVariable("MH_CLIENT_DIR");
        if (IsValidClientDir(env)) return env;

        var configured = ReadConfiguredDir();
        if (IsValidClientDir(configured)) return configured;

        foreach (var dir in EnumerateProjectLocalCandidates())
            if (IsValidClientDir(dir))
                return dir;

        foreach (var dir in AbsoluteCandidates)
            if (IsValidClientDir(dir))
                return dir;

        return null;
    }

    private static IEnumerable<string> EnumerateProjectLocalCandidates()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 12; depth++, dir = dir.Parent)
        {
            yield return Path.Combine(dir.FullName, "clientdata");
            yield return Path.Combine(
                dir.FullName,
                "05.Presentation",
                "MartialHeroes.Client.Godot",
                "clientdata");
        }
    }

    private static string? ReadConfiguredDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 12; depth++, dir = dir.Parent)
        {
            var cfg = Path.Combine(dir.FullName, "client_dir.cfg");
            if (!File.Exists(cfg)) continue;

            try
            {
                foreach (var raw in File.ReadLines(cfg))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith('#')) continue;

                    var eq = line.IndexOf('=');
                    if (eq < 0) continue;

                    var key = line[..eq].Trim();
                    var value = line[(eq + 1)..].Trim();
                    if (key.Equals("client_dir", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                        return Path.IsPathRooted(value)
                            ? value
                            : Path.Combine(dir.FullName, value);
                }
            }
            catch
            {
                return null;
            }
        }

        return null;
    }
}