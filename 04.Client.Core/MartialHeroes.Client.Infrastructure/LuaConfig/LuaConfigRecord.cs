namespace MartialHeroes.Client.Infrastructure.LuaConfig;

// spec: Docs/RE/specs/lua-config.md §3 (boot flags), §4 (display.lua globals), §6 (integer reader)

/// <summary>
/// Strongly-typed, immutable snapshot of the integer and string globals read from
/// the client's Lua config scripts (<c>game.lua</c>, <c>uiconfig.lua</c>,
/// <c>display.lua</c> and siblings).
///
/// <para>
/// Each integer field defaults to <c>1</c> when the key is absent from the
/// script — matching the original client's behaviour described in
/// spec: Docs/RE/specs/lua-config.md §3.
/// </para>
/// <para>
/// String values are decoded as <b>UTF-8</b> (code page 65001), NOT CP949.
/// This is a load-bearing per-path exception to the project-wide CP949 default:
/// spec: Docs/RE/specs/lua-config.md §5.2 (N-B4-2).
/// </para>
/// </summary>
public sealed record LuaConfigRecord
{
    // ── game.lua boot flags (spec: Docs/RE/specs/lua-config.md §3) ────────────

    /// <summary>
    /// Asset source selection.
    /// <c>1</c> = use the mounted VFS archive (packed assets);
    /// <c>0</c> = read loose files from disk.
    /// Default: <c>1</c>.
    /// spec: Docs/RE/specs/lua-config.md §3
    /// </summary>
    public int VfsMode { get; init; } = 1;

    /// <summary>
    /// Launcher bounce gate.
    /// <c>1</c> = re-launch through <c>dostart.exe</c> unless <c>-Start</c> was passed;
    /// <c>0</c> = run standalone.
    /// Default: <c>1</c>.
    /// spec: Docs/RE/specs/lua-config.md §3
    /// </summary>
    public int Launcher { get; init; } = 1;

    /// <summary>
    /// Window / presentation mode.
    /// <c>1</c> (non-zero) = windowed/debug presentation;
    /// <c>0</c> = fullscreen.
    /// Default: <c>1</c>.
    /// spec: Docs/RE/specs/lua-config.md §3
    /// </summary>
    public int DebugMode { get; init; } = 1;

    /// <summary>
    /// Game-addiction warning timing value (integer seconds × 1000 or similar unit).
    /// Read by the same integer reader as the boot flags.
    /// Default: <c>1</c>.
    /// spec: Docs/RE/specs/lua-config.md §3
    /// </summary>
    public int AddictionWarningTiming { get; init; } = 1;

    // ── uiconfig.lua (spec: Docs/RE/specs/lua-config.md §2.3 table) ──────────

    /// <summary>
    /// Index used to pre-select the default/newest server entry in the login server list.
    /// spec: Docs/RE/specs/lua-config.md §2.3, §6
    /// </summary>
    public int NewServerIndex { get; init; } = 1;

    // ── display.lua integer globals (spec: Docs/RE/specs/lua-config.md §4) ────

    /// <summary>Glow kernel horizontal range. spec: Docs/RE/specs/lua-config.md §4</summary>
    public int DisplayGlowRangeX { get; init; } = 2; // small default per spec note

    /// <summary>Glow kernel vertical range. spec: Docs/RE/specs/lua-config.md §4</summary>
    public int DisplayGlowRangeY { get; init; } = 2;

    /// <summary>Target frame-rate cap. spec: Docs/RE/specs/lua-config.md §4</summary>
    public int DisplayFramerate { get; init; } = 60;

    // ── display.lua float globals (spec: Docs/RE/specs/lua-config.md §4) ──────

    /// <summary>Base brightness multiplier. spec: Docs/RE/specs/lua-config.md §4</summary>
    public float DisplayBaseBrightMulti { get; init; } = 1.0f;

    /// <summary>Glow brightness multiplier. spec: Docs/RE/specs/lua-config.md §4</summary>
    public float DisplayGlowBrightMulti { get; init; } = 1.0f;

    /// <summary>Lighting ratio. spec: Docs/RE/specs/lua-config.md §4</summary>
    public float DisplayLightRatio { get; init; } = 1.0f;

    // ── display.lua string global (spec: Docs/RE/specs/lua-config.md §4) ──────

    /// <summary>
    /// Shader filename string.
    /// Decoded as UTF-8 (code page 65001), NOT CP949.
    /// spec: Docs/RE/specs/lua-config.md §4, §5.2 (N-B4-2)
    /// </summary>
    public string DisplayPowerShader { get; init; } = string.Empty;
}