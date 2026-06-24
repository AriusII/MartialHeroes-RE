using Godot;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class RealWorldRenderer
{
    private static int ReadRingRadiusFromConfig()
    {
        const int DefaultRingRadius = 2;
        try
        {
            var absPath = ProjectSettings.GlobalizePath("res://client_dir.cfg");
            if (!File.Exists(absPath)) return DefaultRingRadius;

            foreach (var rawLine in File.ReadLines(absPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var eq = line.IndexOf('=');
                if (eq < 0) continue;
                var k = line[..eq].Trim();
                var v = line[(eq + 1)..].Trim();
                if (k.Equals("ring_radius", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(v, out var parsed) &&
                    parsed >= 1 && parsed <= 2)
                    return parsed;
            }
        }
        catch
        {
        }

        return DefaultRingRadius;
    }


    private static string AreaTag(int areaId)
    {
        var d0 = areaId / 100;
        var d1 = areaId / 10 % 10;
        var d2 = areaId % 10;
        return $"{d0}{d1}{d2}";
    }
}