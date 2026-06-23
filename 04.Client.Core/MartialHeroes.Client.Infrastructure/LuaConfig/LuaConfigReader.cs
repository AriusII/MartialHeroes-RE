using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MartialHeroes.Client.Infrastructure.Exceptions;

namespace MartialHeroes.Client.Infrastructure.LuaConfig;


public sealed partial class LuaConfigReader
{
    [GeneratedRegex(
        @"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.+?)\s*(?:--.*)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex AssignmentLine();


    public LuaConfigRecord Parse(string luaSource)
    {
        ArgumentNullException.ThrowIfNull(luaSource);

        var globals = ExtractGlobals(luaSource);

        var displayPower = ReadInt(globals, "DISPLAY_POWER", 1);

        return new LuaConfigRecord
        {
            VfsMode = ReadInt(globals, "vfsmode", 1),
            Launcher = ReadInt(globals, "launcher", 1),
            DebugMode = ReadInt(globals, "debugmode", 1),
            AddictionWarningTiming = ReadInt(globals, "addiction_warning", 1),

            NewServerIndex = ReadInt(globals, "NEW_SERVER_INDEX", 1),

            DisplayGlowRangeX = ClampGlowRange(ReadInt(globals, "DISPLAY_GLOW_RANGE_X", 2)),
            DisplayGlowRangeY = ClampGlowRange(ReadInt(globals, "DISPLAY_GLOW_RANGE_Y", 2)),
            ShowFpsCounter = ReadInt(globals, "DISPLAY_FRAMERATE", 0),

            DisplayBaseBrightMulti = ReadFloat(globals, "DISPLAY_BASE_BRIGHT_MULTI", 1.0f),
            DisplayGlowBrightMulti = ReadFloat(globals, "DISPLAY_GLOW_BRIGHT_MULTI", 1.0f),
            DisplayLightRatio = ReadFloat(globals, "DISPLAY_LIGHT_RATIO", 1.0f),

            DisplayPower = displayPower,

            DisplayPowerShader = DerivePowerShader(globals, displayPower)
        };
    }

    public async Task<LuaConfigRecord> ParseFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var cp949 = Encoding.GetEncoding(949);
            var text = await File.ReadAllTextAsync(filePath, cp949, cancellationToken);
            return Parse(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LuaConfigException(
                $"Failed to read Lua config file '{filePath}'.", ex);
        }
    }


    private static Dictionary<string, string> ExtractGlobals(string luaSource)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var regex = AssignmentLine();

        foreach (var line in luaSource.AsSpan().EnumerateLines())
        {
            var lineStr = line.ToString();
            var match = regex.Match(lineStr);
            if (!match.Success) continue;

            var key = match.Groups[1].Value;
            var rawValue = match.Groups[2].Value;
            result[key] = rawValue;
        }

        return result;
    }

    private static int ReadInt(
        Dictionary<string, string> globals,
        string key,
        int defaultValue)
    {
        if (!globals.TryGetValue(key, out var raw)) return defaultValue;

        var valueToken = StripComment(raw);

        if (int.TryParse(valueToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            return v;

        if (float.TryParse(valueToken, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv))
            return (int)fv;

        return defaultValue;
    }

    private static float ReadFloat(
        Dictionary<string, string> globals,
        string key,
        float defaultValue)
    {
        if (!globals.TryGetValue(key, out var raw)) return defaultValue;
        var valueToken = StripComment(raw);
        return float.TryParse(valueToken, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : defaultValue;
    }

    private static string ReadString(
        Dictionary<string, string> globals,
        string key,
        string defaultValue)
    {
        if (!globals.TryGetValue(key, out var raw)) return defaultValue;
        var valueToken = StripComment(raw).Trim();

        if (valueToken.Length >= 2 && valueToken[0] == valueToken[^1]
                                   && valueToken[0] is '"' or '\'')
            return valueToken[1..^1];

        return valueToken.Length > 0 ? valueToken : defaultValue;
    }

    private static int ClampGlowRange(int value)
    {
        return value == 0 ? 2 : value;
    }

    private static string DerivePowerShader(Dictionary<string, string> globals, int displayPower)
    {
        var literal = ReadString(globals, "DISPLAY_POWERSHADER", string.Empty);
        if (literal.Length > 0)
            return literal;

        var level = IsValidDisplayPower(displayPower) ? displayPower : 1;
        return $"data/shader/power{level}dx8.psh";
    }

    private static bool IsValidDisplayPower(int power)
    {
        return power is 1 or 2 or 4 or 8 or 16 or 32;
    }

    private static string StripComment(string raw)
    {
        var idx = raw.IndexOf("--", StringComparison.Ordinal);
        return idx >= 0 ? raw[..idx].TrimEnd() : raw.TrimEnd();
    }
}