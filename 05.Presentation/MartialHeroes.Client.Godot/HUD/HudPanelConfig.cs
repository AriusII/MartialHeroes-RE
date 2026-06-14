// HUD/HudPanelConfig.cs
//
// Recovered panel placement constants for the five in-game HUD panels from the
// single in-game HUD-build routine (CODE-CONFIRMED-static).
//
// All values decoded from the HUD-assembly call site immediates — the plain literal
// arguments forwarded to the sized/rect-taking constructor variant of each panel class.
//
// Reference canvas: 1024 × 768 (top-left origin, +X right, +Y down).
// spec: Docs/RE/specs/ui_hud_layout.md §3 — recovered panel placements (CODE-CONFIRMED-static).
// spec: Docs/RE/specs/ui_hud_layout.md §3.1 — rect-slot argument convention.
// spec: Docs/RE/specs/ui_hud_layout.md §3.2 — anchor conventions.

using Godot;

namespace MartialHeroes.Client.Godot.HUD;

/// <summary>
/// Recovered HUD panel placement constants.
/// All values are CODE-CONFIRMED-static decoded from the HUD-assembly call site immediates.
/// Screen-width-relative formulas (minimap, party) are confirmed as formulas; absolute
/// pixel X depends on the runtime screen-width global.
///
/// spec: Docs/RE/specs/ui_hud_layout.md §3 — "five panels now recovered".
/// spec: Docs/RE/specs/ui_hud_layout.md §3.2 — anchor conventions.
/// </summary>
public static class HudPanelConfig
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Stats / ActorState panel — ABSOLUTE placement
    //  spec: Docs/RE/specs/ui_hud_layout.md §3.3 — "Stats: X=180 Y=95 W=130 H=196 Absolute"
    //        CODE-CONFIRMED-static (plain literal immediates).
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Primary ActorState panel X. spec: Docs/RE/specs/ui_hud_layout.md §3.3 CODE-CONFIRMED-static.</summary>
    public const float StatsX = 180f; // spec: Docs/RE/specs/ui_hud_layout.md §3.3

    /// <summary>Primary ActorState panel Y. spec: Docs/RE/specs/ui_hud_layout.md §3.3 CODE-CONFIRMED-static.</summary>
    public const float StatsY = 95f; // spec: Docs/RE/specs/ui_hud_layout.md §3.3

    /// <summary>Primary ActorState panel width. spec: Docs/RE/specs/ui_hud_layout.md §3.3 CODE-CONFIRMED-static.</summary>
    public const float StatsW = 130f; // spec: Docs/RE/specs/ui_hud_layout.md §3.3

    /// <summary>Primary ActorState panel height. spec: Docs/RE/specs/ui_hud_layout.md §3.3 CODE-CONFIRMED-static.</summary>
    public const float StatsH = 196f; // spec: Docs/RE/specs/ui_hud_layout.md §3.3

    // ─────────────────────────────────────────────────────────────────────────
    //  Stats sub-panels — four absolute sibling panels
    //  spec: Docs/RE/specs/ui_hud_layout.md §3.4 — "three sibling stat sub-panels plus primary"
    //        CODE-CONFIRMED-static (plain literal immediates).
    //
    //  Note: the roles of sub-panels A/B/C (HP vs MP vs stamina vs status) are not yet
    //  individually labelled — see Known unknowns §4.
    // ─────────────────────────────────────────────────────────────────────────

    // Sub-panel layout mirrors §3.4 table exactly:
    // | ActorState (primary) | 180 | 95  | 130 | 196 | Absolute |
    // | Stat sub-panel A     |  50 | 95  | 130 | 196 | Absolute |
    // | Stat sub-panel B     | 287 | 14  | 130 | 231 | Absolute |
    // | Stat sub-panel C     |  50 | 147 | 130 | 196 | Absolute |

    /// <summary>
    /// Stat sub-panel A: X=50 Y=95 W=130 H=196.
    /// spec: Docs/RE/specs/ui_hud_layout.md §3.4 CODE-CONFIRMED-static.
    /// Role: not yet labelled (§4 known unknowns).
    /// </summary>
    public static readonly Rect2 StatSubPanelA = new(50f, 95f, 130f, 196f); // spec: Docs/RE/specs/ui_hud_layout.md §3.4

    /// <summary>
    /// Stat sub-panel B: X=287 Y=14 W=130 H=231.
    /// spec: Docs/RE/specs/ui_hud_layout.md §3.4 CODE-CONFIRMED-static.
    /// Role: not yet labelled (§4 known unknowns).
    /// </summary>
    public static readonly Rect2
        StatSubPanelB = new(287f, 14f, 130f, 231f); // spec: Docs/RE/specs/ui_hud_layout.md §3.4

    /// <summary>
    /// Stat sub-panel C: X=50 Y=147 W=130 H=196.
    /// spec: Docs/RE/specs/ui_hud_layout.md §3.4 CODE-CONFIRMED-static.
    /// Role: not yet labelled (§4 known unknowns).
    /// </summary>
    public static readonly Rect2
        StatSubPanelC = new(50f, 147f, 130f, 196f); // spec: Docs/RE/specs/ui_hud_layout.md §3.4

    // ─────────────────────────────────────────────────────────────────────────
    //  Minimap panel — SCREEN-WIDTH-RELATIVE placement
    //  spec: Docs/RE/specs/ui_hud_layout.md §3.3
    //        "X = screen_width − 135, Y=0, W=135, H=195, top-right corner"
    //        CONFIRMED-formula (pixel X pending a known-resolution read).
    //
    //  Godot mapping: AnchorLeft=1, AnchorRight=1, AnchorTop=0, AnchorBottom=0
    //    OffsetLeft  = -MinimapW         (= -135)
    //    OffsetRight =  0                (flush to right edge)
    //    OffsetTop   =  MinimapY         (= 0)
    //    OffsetBottom = MinimapY + MinimapH (= 195)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimap width. screen_width − 135 formula → W=135.
    /// spec: Docs/RE/specs/ui_hud_layout.md §3.3 CONFIRMED-formula.
    /// </summary>
    public const float MinimapW = 135f; // spec: Docs/RE/specs/ui_hud_layout.md §3.3

    /// <summary>
    /// Minimap Y (top-flush). spec: Docs/RE/specs/ui_hud_layout.md §3.3 CONFIRMED-formula.
    /// </summary>
    public const float MinimapY = 0f; // spec: Docs/RE/specs/ui_hud_layout.md §3.3

    /// <summary>
    /// Minimap height. spec: Docs/RE/specs/ui_hud_layout.md §3.3 CONFIRMED-formula.
    /// </summary>
    public const float MinimapH = 195f; // spec: Docs/RE/specs/ui_hud_layout.md §3.3

    // ─────────────────────────────────────────────────────────────────────────
    //  Party panel — SCREEN-WIDTH-RELATIVE placement (right column)
    //  spec: Docs/RE/specs/ui_hud_layout.md §3.3
    //        "X = screen_width + 318, Y=0, W=318, H=732"
    //        CONFIRMED-formula; same X-formula and 318×732 size as inventory; Y=0.
    //
    //  Godot mapping: AnchorLeft=1, AnchorRight=1, AnchorTop=0, AnchorBottom=0
    //    OffsetLeft  = +318    (panel starts 318 px beyond the right viewport edge — off-screen)
    //    OffsetRight = +636    (= 318 + 318)
    //    OffsetTop   =   0
    //    OffsetBottom = 732
    //
    //  Note: party panel at screen_width+318 is off-screen until a slide-in animation;
    //  same formula as inventory §1.1.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Party panel right-edge inset from screen_width. = +318.
    /// spec: Docs/RE/specs/ui_hud_layout.md §3.3 CONFIRMED-formula.
    /// </summary>
    public const float PartyOffsetFromRight = 318f; // spec: Docs/RE/specs/ui_hud_layout.md §3.3

    /// <summary>
    /// Party panel width. spec: Docs/RE/specs/ui_hud_layout.md §3.3 CONFIRMED-formula.
    /// </summary>
    public const float PartyW = 318f; // spec: Docs/RE/specs/ui_hud_layout.md §3.3

    /// <summary>
    /// Party panel height. spec: Docs/RE/specs/ui_hud_layout.md §3.3 CONFIRMED-formula.
    /// </summary>
    public const float PartyH = 732f; // spec: Docs/RE/specs/ui_hud_layout.md §3.3

    // ─────────────────────────────────────────────────────────────────────────
    //  Trade panel — PARENT-RELATIVE placement (reads inventory panel's stored X)
    //  spec: Docs/RE/specs/ui_hud_layout.md §3.3
    //        "X = inventory panel's stored X, Y=0, W=318, H=732"
    //        CODE-CONFIRMED-static (reads inventory stored X; overlays same 318×732 right column).
    //
    //  Godot mapping: anchored at the same right-column position as inventory.
    //  Because both inventory and trade occupy the same 318-px right column, Trade
    //  shares the inventory anchor and is placed above or below it (Y=0 here means
    //  trade starts at the top of the column).
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Trade panel width. Matches inventory W. spec: Docs/RE/specs/ui_hud_layout.md §3.3 CODE-CONFIRMED-static.
    /// </summary>
    public const float TradeW = 318f; // spec: Docs/RE/specs/ui_hud_layout.md §3.3

    /// <summary>
    /// Trade panel height. spec: Docs/RE/specs/ui_hud_layout.md §3.3 CODE-CONFIRMED-static.
    /// </summary>
    public const float TradeH = 732f; // spec: Docs/RE/specs/ui_hud_layout.md §3.3

    // ─────────────────────────────────────────────────────────────────────────
    //  Skill bar — ABSOLUTE container origin + data-driven 9-slot grid
    //  spec: Docs/RE/specs/ui_hud_layout.md §3.5
    //        "container origin (349, 13); thin anchor strip ~7×504; 9-slot data-driven grid"
    //        container origin CODE-CONFIRMED-static; per-slot data-driven.
    //
    //  The container is anchored absolutely at (349, 13). Each of the 9 child slots
    //  resolves its base X/Y from a runtime skill-slot registry record (per-slot data).
    //  Observed icon-cell sizes across layout branches: 146×49, 297×50, 58×58.
    //  Three-button cluster sits at base offsets 763/792/821 px from slot base.
    //  For a first render (no live registry), use 58×58 cells in a horizontal row.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Skill bar container origin X. spec: Docs/RE/specs/ui_hud_layout.md §3.5 CODE-CONFIRMED-static.
    /// </summary>
    public const float SkillBarX = 349f; // spec: Docs/RE/specs/ui_hud_layout.md §3.5

    /// <summary>
    /// Skill bar container origin Y. spec: Docs/RE/specs/ui_hud_layout.md §3.5 CODE-CONFIRMED-static.
    /// </summary>
    public const float SkillBarY = 13f; // spec: Docs/RE/specs/ui_hud_layout.md §3.5

    /// <summary>
    /// Number of skill slots in the bar. spec: Docs/RE/specs/ui_hud_layout.md §3.5 — "nine skill slots".
    /// </summary>
    public const int SkillBarSlotCount = 9; // spec: Docs/RE/specs/ui_hud_layout.md §3.5

    /// <summary>
    /// Fallback icon cell size for the skill bar when no live slot registry is available.
    /// Smallest observed icon-cell size from the layout branches is 58×58.
    /// spec: Docs/RE/specs/ui_hud_layout.md §3.5 — "observed icon-cell sizes: 146×49, 297×50, 58×58".
    /// </summary>
    public const float SkillBarSlotFallbackSize = 58f; // spec: Docs/RE/specs/ui_hud_layout.md §3.5

    // Convenience: position as Vector2 for Godot Control positioning.
    /// <summary>Skill bar container origin as Vector2. spec: Docs/RE/specs/ui_hud_layout.md §3.5 CODE-CONFIRMED-static.</summary>
    public static Vector2 SkillBarOrigin => new(SkillBarX, SkillBarY); // spec: Docs/RE/specs/ui_hud_layout.md §3.5

    // ─────────────────────────────────────────────────────────────────────────
    //  Right-edge stacked HP/MP gauge (§5.6) — SCREEN-WIDTH-RELATIVE
    //  spec: Docs/RE/specs/ui_hud_layout.md §5.6
    //        "Gauge strip A: screen_width−135, Y=200, W=140, H=35"
    //        "Gauge strip B: screen_width−135, Y=250, W=140, H=35 (+50)"
    //        CONFIRMED-formula. Source texture: chunrihojung.dds.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Distance from right edge for both gauge strips. spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula.</summary>
    public const float RightGaugeOffsetFromRight = 135f; // spec: Docs/RE/specs/ui_hud_layout.md §5.6

    /// <summary>Y of HP (top) gauge strip. spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula.</summary>
    public const float RightGaugeHpY = 200f; // spec: Docs/RE/specs/ui_hud_layout.md §5.6

    /// <summary>Y of MP (bottom) gauge strip. spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula.</summary>
    public const float RightGaugeMpY = 250f; // spec: Docs/RE/specs/ui_hud_layout.md §5.6

    /// <summary>Width of each gauge strip. spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula.</summary>
    public const float RightGaugeW = 140f; // spec: Docs/RE/specs/ui_hud_layout.md §5.6

    /// <summary>Height of each gauge strip. spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula.</summary>
    public const float RightGaugeH = 35f; // spec: Docs/RE/specs/ui_hud_layout.md §5.6

    // ─────────────────────────────────────────────────────────────────────────
    //  Bottom action bar (§5.7) — CENTRED-X + BOTTOM-ANCHORED
    //  spec: Docs/RE/specs/ui_hud_layout.md §5.7
    //        "centerX(1024), screen_height−60, W=1024, H=60, innerY=957"
    //        Mixed (centred-X + bottom). CONFIRMED-formula.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Bottom action bar width. centerX(1024) formula. spec: Docs/RE/specs/ui_hud_layout.md §5.7 CONFIRMED-formula.</summary>
    public const float ActionBarW = 1024f; // spec: Docs/RE/specs/ui_hud_layout.md §5.7

    /// <summary>Bottom action bar height. screen_height−60. spec: Docs/RE/specs/ui_hud_layout.md §5.7 CONFIRMED-formula.</summary>
    public const float ActionBarH = 60f; // spec: Docs/RE/specs/ui_hud_layout.md §5.7

    // ─────────────────────────────────────────────────────────────────────────
    //  Top full-width status bar (§5.4) — SCREEN-WIDTH-RELATIVE
    //  spec: Docs/RE/specs/ui_hud_layout.md §5.4
    //        "X=0, Y=120, W=screen_width, H=20" CONFIRMED-formula.
    //
    //  Right-edge square buttons:
    //        "screen_width−200, Y=0, W=64, H=64" CONFIRMED-formula.
    //
    //  Right-anchored panel:
    //        "screen_width−406, Y=0, W=406, H=119" CONFIRMED-formula.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Top status bar Y. spec: Docs/RE/specs/ui_hud_layout.md §5.4 CONFIRMED-formula.</summary>
    public const float TopStatusBarY = 120f; // spec: Docs/RE/specs/ui_hud_layout.md §5.4

    /// <summary>Top status bar height. spec: Docs/RE/specs/ui_hud_layout.md §5.4 CONFIRMED-formula.</summary>
    public const float TopStatusBarH = 20f; // spec: Docs/RE/specs/ui_hud_layout.md §5.4

    /// <summary>Corner button offset from right edge. spec: Docs/RE/specs/ui_hud_layout.md §5.4 CONFIRMED-formula.</summary>
    public const float CornerButtonOffsetFromRight = 200f; // spec: Docs/RE/specs/ui_hud_layout.md §5.4

    /// <summary>Corner button size. spec: Docs/RE/specs/ui_hud_layout.md §5.4 CONFIRMED-formula.</summary>
    public const float CornerButtonSize = 64f; // spec: Docs/RE/specs/ui_hud_layout.md §5.4

    // ─────────────────────────────────────────────────────────────────────────
    //  Screen-centred modal centring helper (§5.8) — CONFIRMED-formula
    //  spec: Docs/RE/specs/ui_hud_layout.md §5.1 — "centerX(W) = (screen_width − W) / 2"
    //  spec: Docs/RE/specs/ui_hud_layout.md §5.8 — "the most common dialog family"
    //
    //  Confirm/info dialog (the most common family, ~12 sites):
    //        W=340, H=190, tex idx 2. CONFIRMED-formula.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Confirm dialog width. spec: Docs/RE/specs/ui_hud_layout.md §5.8 (confirm/info family).</summary>
    public const float ConfirmDialogW = 340f; // spec: Docs/RE/specs/ui_hud_layout.md §5.8

    /// <summary>Confirm dialog height. spec: Docs/RE/specs/ui_hud_layout.md §5.8 (confirm/info family).</summary>
    public const float ConfirmDialogH = 190f; // spec: Docs/RE/specs/ui_hud_layout.md §5.8
}