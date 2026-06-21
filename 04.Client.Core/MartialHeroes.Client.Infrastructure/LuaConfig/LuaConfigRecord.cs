namespace MartialHeroes.Client.Infrastructure.LuaConfig;

// spec: Docs/RE/specs/lua-config.md §3 (boot flags), §4 (display.lua globals), §6 (integer reader)

/// <summary>
///     Strongly-typed, immutable snapshot of the integer and string globals read from
///     the client's Lua config scripts (<c>game.lua</c>, <c>uiconfig.lua</c>,
///     <c>display.lua</c> and siblings).
///     <para>
///         Each integer field defaults to <c>1</c> when the key is absent from the
///         script — matching the original client's behaviour described in
///         spec: Docs/RE/specs/lua-config.md §3.
///     </para>
///     <para>
///         All shipped <c>.lua</c> source text is <b>CP949</b> (code page 949, EUC-KR), consistent with the
///         project-wide "all game text is CP949" rule. The earlier "string values decoded as UTF-8" claim is
///         REFUTED by direct byte-inspection of the real shipped files: the Korean comment bytes are
///         double-byte CP949 syllables, not UTF-8 three-byte sequences. The 65001-configured host routine
///         observed in the binary was one in-binary conversion path, not the file encoding.
///         spec: Docs/RE/specs/lua-config.md §0 (encoding correction, LOAD-BEARING).
///     </para>
/// </summary>
public sealed record LuaConfigRecord
{
    // ── game.lua boot flags (spec: Docs/RE/specs/lua-config.md §3) ────────────

    /// <summary>
    ///     Asset source selection.
    ///     <c>1</c> = use the mounted VFS archive (packed assets);
    ///     <c>0</c> = read loose files from disk.
    ///     Default: <c>1</c>.
    ///     spec: Docs/RE/specs/lua-config.md §3
    /// </summary>
    public int VfsMode { get; init; } = 1;

    /// <summary>
    ///     Launcher bounce gate.
    ///     <c>1</c> = re-launch through <c>dostart.exe</c> unless <c>-Start</c> was passed;
    ///     <c>0</c> = run standalone.
    ///     Default: <c>1</c>.
    ///     spec: Docs/RE/specs/lua-config.md §3
    /// </summary>
    public int Launcher { get; init; } = 1;

    /// <summary>
    ///     Window / presentation mode.
    ///     <c>1</c> (non-zero) = windowed/debug presentation;
    ///     <c>0</c> = fullscreen.
    ///     Default: <c>1</c>.
    ///     spec: Docs/RE/specs/lua-config.md §3
    /// </summary>
    public int DebugMode { get; init; } = 1;

    /// <summary>
    ///     Game-addiction warning timing value read from <c>game.lua</c> (integer, same reader as boot flags).
    ///     <para>
    ///         <b>UNVERIFIED:</b> the Lua global name for this value has NOT been pinned by spec.
    ///         <c>lua-config.md §3</c> documents "a game-addiction-warning timing number … read by the same
    ///         number reader" but does not give the key name. The current key string (<c>"addiction_warning"</c>)
    ///         is unconfirmed and will likely not match a real <c>game.lua</c>. When the name is pinned, update
    ///         <see cref="LuaConfigReader" /> (the <c>ReadInt</c> call for this field).
    ///     </para>
    ///     <para>
    ///         Note: the ×1000-scaled variant (<c>DISPLAY_GAME_ADDICTION_WARNING_CHECK_TIME</c>) lives in
    ///         <c>display.lua</c> (§4.2) and is a separate, distinct knob.
    ///     </para>
    ///     Default: <c>1</c>.
    ///     spec: Docs/RE/specs/lua-config.md §3 (timing value), correction box (disentanglement of two knobs)
    /// </summary>
    public int AddictionWarningTiming { get; init; } = 1;

    // ── uiconfig.lua (spec: Docs/RE/specs/lua-config.md §2.3 table) ──────────

    /// <summary>
    ///     Index used to pre-select the default/newest server entry in the login server list.
    ///     spec: Docs/RE/specs/lua-config.md §2.3, §6
    /// </summary>
    public int NewServerIndex { get; init; } = 1;

    // ── display.lua integer globals (spec: Docs/RE/specs/lua-config.md §4) ────

    /// <summary>Glow kernel horizontal range. spec: Docs/RE/specs/lua-config.md §4</summary>
    public int DisplayGlowRangeX { get; init; } = 2; // small default per spec note

    /// <summary>Glow kernel vertical range. spec: Docs/RE/specs/lua-config.md §4</summary>
    public int DisplayGlowRangeY { get; init; } = 2;

    /// <summary>
    ///     FPS-counter on/off toggle (<c>DISPLAY_FRAMERATE</c>): <c>0</c> = hide the FPS counter,
    ///     <c>1</c> = show it. This is NOT a frame-rate cap. The real frame cap is a hardcoded engine
    ///     constant: the engine-view object's framerate field is constructor-seeded to <c>60.0</c> and no
    ///     traced path overwrites it, so the effective software cap is ~60 FPS and <c>DISPLAY_FRAMERATE</c>
    ///     is statically inert as a cap. Default: <c>0</c>.
    ///     spec: Docs/RE/specs/lua-config.md §4.2; Docs/RE/specs/client_runtime.md §8.3.1
    /// </summary>
    public int ShowFpsCounter { get; init; }

    // ── display.lua float globals (spec: Docs/RE/specs/lua-config.md §4) ──────

    /// <summary>Base brightness multiplier. spec: Docs/RE/specs/lua-config.md §4</summary>
    public float DisplayBaseBrightMulti { get; init; } = 1.0f;

    /// <summary>Glow brightness multiplier. spec: Docs/RE/specs/lua-config.md §4</summary>
    public float DisplayGlowBrightMulti { get; init; } = 1.0f;

    /// <summary>Lighting ratio. spec: Docs/RE/specs/lua-config.md §4</summary>
    public float DisplayLightRatio { get; init; } = 1.0f;

    /// <summary>
    ///     Glow shader intensity level (valid set: 1, 2, 4, 8, 16, 32).
    ///     Selects which glow pixel-shader file is used (see DisplayPowerShader).
    ///     spec: Docs/RE/specs/lua-config.md §4.2, §4.3
    /// </summary>
    public int DisplayPower { get; init; } = 1;

    // ── display.lua string global (spec: Docs/RE/specs/lua-config.md §4) ──────

    /// <summary>
    ///     Path to the active glow pixel-shader <c>.psh</c> file, derived from
    ///     <see cref="DisplayPower" /> via the <c>if/elseif</c> chain in <c>display.lua</c>.
    ///     Form: <c>data/shader/power&lt;N&gt;dx8.psh</c>.
    ///     spec: Docs/RE/specs/lua-config.md §4.3 — computed, not a bare assignment in the file.
    /// </summary>
    public string DisplayPowerShader { get; init; } = "data/shader/power1dx8.psh";
}