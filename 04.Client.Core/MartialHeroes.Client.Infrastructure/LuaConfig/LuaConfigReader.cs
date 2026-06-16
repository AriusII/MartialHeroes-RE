using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MartialHeroes.Client.Infrastructure.Exceptions;

namespace MartialHeroes.Client.Infrastructure.LuaConfig;

// spec: Docs/RE/specs/lua-config.md §1, §2, §3, §4, §6, §7

/// <summary>
/// Lightweight key=value extractor for the client's Lua config scripts
/// (<c>game.lua</c>, <c>uiconfig.lua</c>, <c>display.lua</c> and siblings).
///
/// <para>
/// The original client embeds a stock Lua 5.1.2 VM and runs each script purely for
/// its side-effects on the global table; the host then reads the resulting globals
/// back into C++ fields.  This reimplementation reproduces the consumed <em>data</em>
/// using a lightweight line scanner — there is no need for a full Lua interpreter
/// since the config scripts only define globals via simple assignment statements
/// (<c>key = value</c>) and optional <c>cpp_load</c> includes.
/// spec: Docs/RE/specs/lua-config.md §7 ("Interpreter vs. direct-parse" trade-off)
/// </para>
///
/// <para>
/// <b>Encoding:</b> the shipped <c>.lua</c> source files (<c>config.lua</c>,
/// <c>display.lua</c>, <c>uiconfig.lua</c> and siblings) are <b>CP949 (code page 949,
/// EUC-KR)</b>, NOT UTF-8.  The prior UTF-8 claim ("N-B4-2") has been superseded by
/// direct byte-inspection of the real shipped files.  This reader registers the CP949
/// provider once and decodes every file as CP949.
/// spec: Docs/RE/specs/lua-config.md §0 (encoding correction, LOAD-BEARING), §7
/// </para>
///
/// <para>
/// <b>Integer semantics:</b> each integer global is read as a full signed
/// <see cref="int"/>, never clamped to 0/1.  The boot flags happen to be 0/1 in the
/// shipped scripts, but the reader is general.
/// spec: Docs/RE/specs/lua-config.md §6
/// </para>
/// </summary>
public sealed partial class LuaConfigReader
{
    // ── Regex: matches   identifier = literal   (Lua assignment, top-level only)
    // Group 1 = identifier, Group 2 = the right-hand-side literal token.
    // We deliberately ignore function calls, table constructors, and multi-line
    // strings — the config globals the host reads are all simple number/string
    // literals.  Lines that don't match are silently skipped.
    // spec: Docs/RE/specs/lua-config.md §2.1
    [GeneratedRegex(
        @"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.+?)\s*(?:--.*)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex AssignmentLine();

    // ──────────────────────────────────────────────────────────────────────────
    // Public entry points
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a raw Lua config string (the full text of one or more config scripts,
    /// already loaded from disk/VFS by the caller) and overlays the discovered
    /// globals onto a fresh <see cref="LuaConfigRecord"/> with its spec-mandated
    /// defaults.
    /// </summary>
    /// <param name="luaSource">
    /// The CP949-decoded text of the Lua config script.
    /// The caller is responsible for reading the file (from VFS or loose disk,
    /// per the <c>vfsmode</c> flag) and decoding it as CP949 (code page 949).
    /// spec: Docs/RE/specs/lua-config.md §0 (encoding correction), §2.2
    /// </param>
    /// <returns>
    /// A fully-populated, immutable <see cref="LuaConfigRecord"/>.
    /// Keys not present in the source default to the spec-mandated values
    /// (each boot flag defaults to <c>1</c>).
    /// spec: Docs/RE/specs/lua-config.md §3
    /// </returns>
    public LuaConfigRecord Parse(string luaSource)
    {
        ArgumentNullException.ThrowIfNull(luaSource);

        var globals = ExtractGlobals(luaSource);

        return new LuaConfigRecord
        {
            // spec: Docs/RE/specs/lua-config.md §3 — boot flags, default 1 each
            VfsMode = ReadInt(globals, "vfsmode", defaultValue: 1),
            Launcher = ReadInt(globals, "launcher", defaultValue: 1),
            DebugMode = ReadInt(globals, "debugmode", defaultValue: 1),
            // UNVERIFIED: the Lua global name for the game.lua addiction-warning timing value has not
            // been pinned by spec. lua-config.md §3 describes "a game-addiction-warning timing number"
            // but does NOT give its global name. The ×1000-scaled variant is DISPLAY_GAME_ADDICTION_WARNING_CHECK_TIME
            // in display.lua (§4.2), which is a separate knob.
            // Until the game.lua key name is confirmed, this read will miss against a real game.lua.
            // spec: Docs/RE/specs/lua-config.md §3, correction box lines 225-231
            AddictionWarningTiming = ReadInt(globals, "addiction_warning", defaultValue: 1), // key UNVERIFIED

            // spec: Docs/RE/specs/lua-config.md §2.3 table / §6
            NewServerIndex = ReadInt(globals, "NEW_SERVER_INDEX", defaultValue: 1),

            // spec: Docs/RE/specs/lua-config.md §4 — display.lua integer globals
            // spec: Docs/RE/specs/lua-config.md status-banner — "DISPLAY_GLOW_RANGE_X / _Y host fallback to 2 when read as 0 | CODE-CONFIRMED"
            // An explicit 0 in the file is clamped to 2 (the host does the same before using the value).
            DisplayGlowRangeX = ClampGlowRange(ReadInt(globals, "DISPLAY_GLOW_RANGE_X", defaultValue: 2)),
            DisplayGlowRangeY = ClampGlowRange(ReadInt(globals, "DISPLAY_GLOW_RANGE_Y", defaultValue: 2)),
            DisplayFramerate = ReadInt(globals, "DISPLAY_FRAMERATE", defaultValue: 60),

            // spec: Docs/RE/specs/lua-config.md §4 — display.lua float globals
            DisplayBaseBrightMulti = ReadFloat(globals, "DISPLAY_BASE_BRIGHT_MULTI", defaultValue: 1.0f),
            DisplayGlowBrightMulti = ReadFloat(globals, "DISPLAY_GLOW_BRIGHT_MULTI", defaultValue: 1.0f),
            DisplayLightRatio = ReadFloat(globals, "DISPLAY_LIGHT_RATIO", defaultValue: 1.0f),

            // spec: Docs/RE/specs/lua-config.md §4.2 — DISPLAY_POWER selects the glow shader intensity level
            DisplayPower = ReadInt(globals, "DISPLAY_POWER", defaultValue: 1),

            // spec: Docs/RE/specs/lua-config.md §4.3 — DISPLAY_POWERSHADER is COMPUTED from DISPLAY_POWER
            // by an if/elseif chain in display.lua; a config author must not pre-assign it.
            // We derive it here using the same rule: "data/shader/power<N>dx8.psh".
            // If the scanner happens to find a literal assignment, we accept it; otherwise derive.
            DisplayPowerShader = DerivePowerShader(globals),
        };
    }

    /// <summary>
    /// Async overload: reads a config file from disk, decodes it as CP949
    /// (spec: Docs/RE/specs/lua-config.md §0 — shipped .lua files are CP949, NOT UTF-8),
    /// then calls <see cref="Parse"/>.
    /// </summary>
    /// <param name="filePath">Absolute path to the <c>.lua</c> file on disk.</param>
    /// <param name="cancellationToken">Propagated to the async file read.</param>
    public async Task<LuaConfigRecord> ParseFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        try
        {
            // spec: Docs/RE/specs/lua-config.md §0 (LOAD-BEARING encoding correction) —
            // the shipped .lua source files are CP949 (code page 949, EUC-KR), NOT UTF-8.
            // RegisterProvider must be called once at app boot; we call it here defensively
            // so the reader is self-contained in tests and headless contexts.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var cp949 = Encoding.GetEncoding(949); // spec: Docs/RE/specs/lua-config.md §0
            var text = await File.ReadAllTextAsync(filePath, cp949, cancellationToken);
            return Parse(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LuaConfigException(
                $"Failed to read Lua config file '{filePath}'.", ex);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans <paramref name="luaSource"/> line by line and extracts every
    /// simple top-level assignment (<c>identifier = literal</c>).
    /// Lines that do not match (function definitions, table constructors,
    /// comments, blank lines, <c>cpp_load</c> calls) are silently ignored.
    /// spec: Docs/RE/specs/lua-config.md §2.1
    /// </summary>
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
            result[key] = rawValue; // last assignment wins (idiomatic Lua)
        }

        return result;
    }

    /// <summary>
    /// Reads a global as a full signed integer.
    /// spec: Docs/RE/specs/lua-config.md §6 — "number-as-int reader, NOT a boolean"
    /// </summary>
    private static int ReadInt(
        Dictionary<string, string> globals,
        string key,
        int defaultValue)
    {
        if (!globals.TryGetValue(key, out var raw)) return defaultValue;

        // Strip inline Lua comment that may have survived the regex trim.
        var valueToken = StripComment(raw);

        // spec: Docs/RE/specs/lua-config.md §6 — full int, never clamped
        if (int.TryParse(valueToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            return v;

        // Lua numbers can also be floats assigned to an int-typed global — truncate.
        if (float.TryParse(valueToken, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv))
            return (int)fv;

        return defaultValue;
    }

    /// <summary>
    /// Reads a global as a <see cref="float"/>.
    /// spec: Docs/RE/specs/lua-config.md §6 (float reader sibling)
    /// </summary>
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

    /// <summary>
    /// Reads a global as a string.
    /// The raw Lua string literal is unwrapped from its surrounding quotes.
    /// The text is already a decoded .NET string (the caller decoded the file as CP949;
    /// no additional re-encoding is needed here).
    /// spec: Docs/RE/specs/lua-config.md §4, §0 (CP949 file encoding)
    /// </summary>
    private static string ReadString(
        Dictionary<string, string> globals,
        string key,
        string defaultValue)
    {
        if (!globals.TryGetValue(key, out var raw)) return defaultValue;
        var valueToken = StripComment(raw).Trim();

        // Unwrap "…" or '…' string literals.
        if (valueToken.Length >= 2 && valueToken[0] == valueToken[^1]
                                   && valueToken[0] is '"' or '\'')
        {
            return valueToken[1..^1];
        }

        // Bare word (unusual but defensible) — return as-is.
        return valueToken.Length > 0 ? valueToken : defaultValue;
    }

    /// <summary>
    /// Applies the host-side fallback: if DISPLAY_GLOW_RANGE_X or _Y was read as 0,
    /// the host substitutes 2 before using the value.
    /// spec: Docs/RE/specs/lua-config.md status-banner — "DISPLAY_GLOW_RANGE_X / _Y host fallback to 2 when read as 0 | CODE-CONFIRMED"
    /// </summary>
    private static int ClampGlowRange(int value) => value == 0 ? 2 : value;

    /// <summary>
    /// Derives DISPLAY_POWERSHADER from DISPLAY_POWER using the if/elseif chain in display.lua.
    /// spec: Docs/RE/specs/lua-config.md §4.3 — "DISPLAY_POWERSHADER is computed … by an if/elseif
    /// chain over DISPLAY_POWER, mapping the power level to data/shader/power&lt;N&gt;dx8.psh"
    /// </summary>
    private static string DerivePowerShader(Dictionary<string, string> globals)
    {
        // Accept a literal assignment if the scanner found one (non-standard file or future compat).
        // spec: Docs/RE/specs/lua-config.md §4.3 — "a config author must not pre-assign it"
        var literal = ReadString(globals, "DISPLAY_POWERSHADER", defaultValue: string.Empty);
        if (literal.Length > 0)
            return literal;

        // Derive from DISPLAY_POWER (valid set: 1,2,4,8,16,32).
        // spec: Docs/RE/specs/lua-config.md §4.2, §4.3
        var power = ReadInt(globals, "DISPLAY_POWER", defaultValue: 1);
        return $"data/shader/power{power}dx8.psh";
    }

    /// <summary>
    /// Strips a trailing Lua line comment (<c>-- …</c>) from a raw value token.
    /// The regex already trims leading/trailing whitespace from the RHS, but the
    /// comment-free trim handles edge cases where the comment had no space before
    /// the double-dash.
    /// </summary>
    private static string StripComment(string raw)
    {
        var idx = raw.IndexOf("--", StringComparison.Ordinal);
        return idx >= 0 ? raw[..idx].TrimEnd() : raw.TrimEnd();
    }
}