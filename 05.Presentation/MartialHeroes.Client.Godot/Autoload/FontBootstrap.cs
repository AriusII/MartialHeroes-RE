// Autoload/FontBootstrap.cs
//
// Very-first autoload: installs a Korean-capable SystemFont as the project-wide
// ThemeDB.FallbackFont so that ALL CP949 strings (login captions, char-select names,
// World HUD chat/stats) render as real glyphs instead of □ tofu boxes.
//
// MECHANISM:
//   Godot's ThemeDB.FallbackFont is the font-of-last-resort consulted when a Control's
//   Theme (or its parent chain) supplies no explicit font.  Setting it here — in the
//   earliest autoload _Ready — guarantees the fallback is active before Boot.tscn loads
//   any screen.  No individual Control or Theme asset needs to be changed.
//
// FONT CHOICE:
//   Godot's SystemFont queries the OS font registry by name list in priority order,
//   resolving the first installed face.  Priority list below mirrors the legacy client's
//   Korean faces (spec §6.2) with Malgun Gothic added as the reliable Windows-10/11
//   universal fallback — it ships on every modern Windows and covers the full Hangul
//   Syllables block (U+AC00–U+D7A3) required to display CP949-decoded strings.
//
//   spec: Docs/RE/specs/ui_system.md §6.2 — legacy faces: DotumChe, Dotum, BatangChe.
//   spec: Docs/RE/specs/ui_system.md §13.4 — Godot font mapping guidance.
//
// NOTE: This autoload has ZERO game-rule authority.  It only touches the Godot theme
//       subsystem (ThemeDB) — a purely presentational concern.  No Application layer code
//       is called here.
//
// THREADING: _Ready runs on the Godot main thread before any other scene node.
//            ThemeDB is main-thread-only; no threading concerns.

using Godot;

namespace MartialHeroes.Client.Godot.Autoload;

/// <summary>
/// Presentation-layer font bootstrap autoload.
///
/// Registers a Korean-capable <see cref="SystemFont"/> as <c>ThemeDB.FallbackFont</c>
/// so every CP949 UI caption renders as real Hangul glyphs on any Windows 10/11 machine.
///
/// Must be listed <b>first</b> in the <c>[autoload]</c> section of <c>project.godot</c>
/// so it runs before <c>ClientContext</c> or any screen autoload.
///
/// spec: Docs/RE/specs/ui_system.md §6.2 — legacy Korean faces (DotumChe / Dotum / BatangChe).
/// spec: Docs/RE/specs/ui_system.md §13.4 — Godot font mapping guidance.
/// </summary>
public sealed partial class FontBootstrap : Node
{
    // -------------------------------------------------------------------------
    // Korean face priority list
    // -------------------------------------------------------------------------
    // Mirrors the legacy client face table (spec §6.2) with Malgun Gothic as the
    // guaranteed-present Windows-10/11 fallback.  Godot's SystemFont resolves the
    // first installed face and falls back automatically through the list.
    //
    // spec: Docs/RE/specs/ui_system.md §6.2 — DotumChe (fixed-pitch sans), Dotum
    //       (proportional sans), BatangChe (fixed-pitch serif). CODE-CONFIRMED.
    // Malgun Gothic: standard Unicode Korean sans-serif, ships with Windows 7+.
    // Gulim / Gulimche: older KR system faces present on many KR Windows installs.
    private static readonly string[] KoreanFaceNames =
    [
        "Dotum", // spec: §6.2 face "Dotum" (proportional sans) CODE-CONFIRMED
        "DotumChe", // spec: §6.2 face "DotumChe" (fixed-pitch sans) CODE-CONFIRMED
        "Gulim", // common KR alternative proportional sans
        "GulimChe", // common KR alternative fixed-pitch sans
        "Malgun Gothic", // ships on all Windows 10/11 — guaranteed CJK coverage
        "Batang", // spec: §6.2 face "BatangChe" proportional variant CODE-CONFIRMED
        "BatangChe", // spec: §6.2 face "BatangChe" (fixed-pitch serif) CODE-CONFIRMED
    ];

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        try
        {
            InstallKoreanFallbackFont();
        }
        catch (Exception ex)
        {
            // Non-fatal — the engine continues with the built-in Latin font.
            GD.PrintErr($"[FontBootstrap] Failed to install Korean fallback font: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Font installation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a <see cref="SystemFont"/> that resolves Korean faces in priority order
    /// and installs it as <c>ThemeDB.FallbackFont</c>.
    ///
    /// <para>
    /// <see cref="SystemFont"/> resolves the first installed face from the
    /// <see cref="SystemFont.FontNames"/> list.  Every subsequent entry is a fallback.
    /// Godot also performs its own per-glyph fallback within the font subsystem, so
    /// even if only Malgun Gothic is installed, all Hangul code points will resolve.
    /// </para>
    ///
    /// spec: Docs/RE/specs/ui_system.md §6.2 — face priority: DotumChe, Dotum, BatangChe.
    /// spec: Docs/RE/specs/ui_system.md §13.4 — Godot font mapping guidance.
    /// </summary>
    private static void InstallKoreanFallbackFont()
    {
        var sysFont = new SystemFont();

        // Set face priority list.
        // spec: Docs/RE/specs/ui_system.md §6.2 CODE-CONFIRMED (DotumChe/Dotum/BatangChe).
        // Malgun Gothic added as guaranteed Windows 10/11 presence for universal coverage.
        sysFont.FontNames = KoreanFaceNames;

        // Use sub-pixel anti-aliasing for small pixel sizes (12–16 px slots per spec §6.2).
        // Godot SystemFont defaults to grayscale AA; subpixel gives crisper CJK at small sizes.
        sysFont.Antialiasing = TextServer.FontAntialiasing.Lcd;

        // Disable hinting for Korean faces — KR fonts embed their own hinting tables
        // and auto-hinting distorts the pixel-precise glyph metrics at small sizes.
        sysFont.Hinting = TextServer.Hinting.None;

        // ThemeDB.FallbackFont is consulted when no explicit font is in a Control's theme.
        // Setting it here affects ALL Controls that do not override font in their own theme.
        // spec: Docs/RE/specs/ui_system.md §13.4 — "global default font for all Controls".
        ThemeDB.Singleton.FallbackFont = sysFont;

        // FallbackFontSize: default slot 0 height is 12 px (DotumChe, spec §6.2 slot 0).
        // Individual widgets override via AddThemeFontSizeOverride when needed.
        // spec: Docs/RE/specs/ui_system.md §6.2 — slot 0 rowHeight = 12 px. CODE-CONFIRMED.
        ThemeDB.Singleton.FallbackFontSize = 12;

        GD.Print("[FontBootstrap] Korean SystemFont installed as ThemeDB.FallbackFont. " +
                 $"Faces: [{string.Join(", ", KoreanFaceNames)}]. " +
                 "spec: Docs/RE/specs/ui_system.md §6.2");
    }
}