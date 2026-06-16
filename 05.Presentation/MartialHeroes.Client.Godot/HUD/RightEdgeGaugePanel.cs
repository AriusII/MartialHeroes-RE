using Godot;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Domain.Actors;

namespace MartialHeroes.Client.Godot.HUD;

/// <summary>
/// Right-edge stacked HP/MP gauge composite — two 140×35 strips stacked vertically.
/// Source texture: chunrihojung.dds (VFS). Rendered at screen_width − 135, Y=200 (top row)
/// and Y=250 (second row, +50).
///
/// PASSIVE: reads ActorVitalsChangedEvent / CombatStatsRecomputedEvent forwarded by GameHud
/// and maps them directly onto ProgressBar values. No stat math — values are server-authoritative.
///
/// Placement (§5.6 CONFIRMED-formula):
///   Strip A: X = screen_width − 135,  Y = 200, W = 140, H = 35
///   Strip B: X = screen_width − 135,  Y = 250, W = 140, H = 35
///
/// spec: Docs/RE/specs/ui_hud_layout.md §5.6 — "Gauge strip A (top row): screen_width−135, Y=200, W=140, H=35"
/// spec: Docs/RE/specs/ui_hud_layout.md §5.6 — "Gauge strip B (bottom row, Y+50): screen_width−135, Y=250, W=140, H=35"
/// </summary>
public sealed partial class RightEdgeGaugePanel : Control
{
    // ── Placement constants — CONFIRMED-formula ────────────────────────────────────────────────
    // spec: Docs/RE/specs/ui_hud_layout.md §5.6

    /// <summary>Distance from right viewport edge. = screen_width − 135.
    /// spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula.</summary>
    private const float GaugeOffsetFromRight = 135f; // spec: Docs/RE/specs/ui_hud_layout.md §5.6

    /// <summary>Y for HP (top) gauge strip. spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula.</summary>
    private const float GaugeStripAY = 200f; // spec: Docs/RE/specs/ui_hud_layout.md §5.6

    /// <summary>Y for MP (bottom) gauge strip (+50 from strip A). spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula.</summary>
    private const float GaugeStripBY = 250f; // spec: Docs/RE/specs/ui_hud_layout.md §5.6

    /// <summary>Width of each gauge strip. spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula.</summary>
    private const float GaugeW = 140f; // spec: Docs/RE/specs/ui_hud_layout.md §5.6

    /// <summary>Height of each gauge strip. spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula.</summary>
    private const float GaugeH = 35f; // spec: Docs/RE/specs/ui_hud_layout.md §5.6

    // ── Child node handles ─────────────────────────────────────────────────────────────────────

    private ProgressBar _hpBar = null!;
    private Label _hpLabel = null!;
    private ProgressBar _mpBar = null!;
    private Label _mpLabel = null!;

    // ── View state (display only — no domain state) ────────────────────────────────────────────

    private uint _hp;
    private uint _maxHp;
    private uint _mp;
    private uint _maxMp;
    private ActorKey _trackedPlayerKey;
    private bool _hasTrackedPlayer;

    // ── Initialisation ─────────────────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        GD.Print("[RightEdgeGaugePanel] _Ready start");
        try
        {
            BuildUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RightEdgeGaugePanel] _Ready failed: {ex.Message}");
        }
    }

    private void BuildUi()
    {
        // Root: anchored right edge; contains both gauge strips.
        // The Control root covers from Y=200 to Y=285 (strip B bottom = 250+35).
        // Anchor: AnchorLeft=AnchorRight=1 (right edge), OffsetLeft=−GaugeW, OffsetRight=0.
        // spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula.
        AnchorLeft = 1f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 0f;
        OffsetLeft =
            -GaugeOffsetFromRight; // = −135 → starts at screen_width − 135  // spec: Docs/RE/specs/ui_hud_layout.md §5.6
        OffsetRight = GaugeW - GaugeOffsetFromRight; // = +5 → right edge of gauge
        OffsetTop = GaugeStripAY; // = 200  // spec: Docs/RE/specs/ui_hud_layout.md §5.6
        OffsetBottom = GaugeStripBY + GaugeH; // = 285 (strip B bottom)
        MouseFilter = MouseFilterEnum.Ignore;

        // ── HP strip (gauge A, top) ───────────────────────────────────────────────────────────
        // spec: Docs/RE/specs/ui_hud_layout.md §5.6 — "Gauge strip A (top row) Y=200, W=140, H=35"
        var hpStrip = new Control
        {
            Name = "HpStrip",
            Position = new Vector2(0f, 0f), // relative to this control's origin (Y=200)
            Size = new Vector2(GaugeW, GaugeH),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(hpStrip);

        var hpBg = new Panel();
        hpBg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var hpBgStyle = new StyleBoxFlat();
        hpBgStyle.BgColor = new Color(0.15f, 0.05f, 0.05f, 0.85f);
        hpBgStyle.SetBorderWidthAll(1);
        hpBgStyle.BorderColor = new Color(0.6f, 0.2f, 0.2f, 0.9f);
        hpBg.AddThemeStyleboxOverride("panel", hpBgStyle);
        hpStrip.AddChild(hpBg);

        _hpBar = new ProgressBar
        {
            Name = "HpBar",
            MinValue = 0,
            MaxValue = 100,
            Value = 100,
            ShowPercentage = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _hpBar.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var hpFill = new StyleBoxFlat();
        hpFill.BgColor = new Color(0.85f, 0.15f, 0.15f, 0.9f);
        _hpBar.AddThemeStyleboxOverride("fill", hpFill);
        var hpBgBarStyle = new StyleBoxFlat();
        hpBgBarStyle.BgColor = new Color(0.1f, 0.05f, 0.05f, 0.6f);
        _hpBar.AddThemeStyleboxOverride("background", hpBgBarStyle);
        hpStrip.AddChild(_hpBar);

        _hpLabel = new Label
        {
            Name = "HpLabel",
            Text = "HP",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _hpLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _hpLabel.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.9f, 1f));
        hpStrip.AddChild(_hpLabel);

        // ── MP strip (gauge B, bottom, Y+50) ─────────────────────────────────────────────────
        // spec: Docs/RE/specs/ui_hud_layout.md §5.6 — "Gauge strip B (bottom row, Y+50): Y=250, W=140, H=35"
        float stripBRelativeY = GaugeStripBY - GaugeStripAY; // = 50 px relative to this Control's origin
        var mpStrip = new Control
        {
            Name = "MpStrip",
            Position = new Vector2(0f,
                stripBRelativeY), // +50 px below strip A  // spec: Docs/RE/specs/ui_hud_layout.md §5.6
            Size = new Vector2(GaugeW, GaugeH),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(mpStrip);

        var mpBg = new Panel();
        mpBg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var mpBgStyle = new StyleBoxFlat();
        mpBgStyle.BgColor = new Color(0.05f, 0.05f, 0.2f, 0.85f);
        mpBgStyle.SetBorderWidthAll(1);
        mpBgStyle.BorderColor = new Color(0.2f, 0.2f, 0.7f, 0.9f);
        mpBg.AddThemeStyleboxOverride("panel", mpBgStyle);
        mpStrip.AddChild(mpBg);

        _mpBar = new ProgressBar
        {
            Name = "MpBar",
            MinValue = 0,
            MaxValue = 100,
            Value = 100,
            ShowPercentage = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _mpBar.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var mpFill = new StyleBoxFlat();
        mpFill.BgColor = new Color(0.15f, 0.3f, 0.9f, 0.9f);
        _mpBar.AddThemeStyleboxOverride("fill", mpFill);
        var mpBgBarStyle = new StyleBoxFlat();
        mpBgBarStyle.BgColor = new Color(0.05f, 0.05f, 0.12f, 0.6f);
        _mpBar.AddThemeStyleboxOverride("background", mpBgBarStyle);
        mpStrip.AddChild(_mpBar);

        _mpLabel = new Label
        {
            Name = "MpLabel",
            Text = "MP",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _mpLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _mpLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.9f, 1f, 1f));
        mpStrip.AddChild(_mpLabel);

        GD.Print($"[RightEdgeGaugePanel] UI built. " +
                 $"StripA at (screen_width−{GaugeOffsetFromRight}, {GaugeStripAY}) W={GaugeW} H={GaugeH}. " +
                 $"StripB at Y={GaugeStripBY} (+{stripBRelativeY} relative). " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula.");
    }

    // ── Event handlers (called from GameHud on main thread) ────────────────────────────────────

    /// <summary>
    /// Tracks player spawn to identify which actor's vitals to display.
    /// No game logic — just records the player key.
    /// </summary>
    public void OnActorSpawned(ActorSpawnedEvent evt)
    {
        if (_hasTrackedPlayer || evt.Key.Sort != EntitySort.PlayerCharacter) return;
        _hasTrackedPlayer = true;
        _trackedPlayerKey = evt.Key;
        _hp = evt.CurrentHp;
        _maxHp = evt.MaxHp;
        _mp = 0;
        _maxMp = 0;
        RefreshBars();
    }

    /// <summary>
    /// Reacts to a vitals change: refreshes HP/MP gauge strips.
    /// PASSIVE: values come directly from the server-authoritative event payload.
    /// spec: Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml.
    /// </summary>
    public void OnActorVitalsChanged(ActorVitalsChangedEvent evt)
    {
        if (!_hasTrackedPlayer || evt.Key != _trackedPlayerKey) return;
        _hp = evt.CurrentHp;
        _mp = evt.CurrentMp;
        if (_maxHp == 0) _maxHp = evt.CurrentHp;
        if (_maxMp == 0) _maxMp = evt.CurrentMp;
        RefreshBars();
    }

    /// <summary>
    /// Reacts to combat-stat recompute: updates max HP/MP for correct bar fill ratios.
    /// PASSIVE: reads pre-computed stats from the event — no formula here.
    /// spec: Docs/RE/specs/combat.md §1 / §2 — CombatStats aggregate.
    /// </summary>
    public void OnCombatStatsRecomputed(CombatStatsRecomputedEvent evt)
    {
        if (!_hasTrackedPlayer || evt.Key != _trackedPlayerKey) return;
        var s = evt.Stats;
        if (s.MaxLife > 0) _maxHp = (uint)s.MaxLife;
        if (s.MaxEnergy > 0) _maxMp = (uint)s.MaxEnergy;
        RefreshBars();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    private void RefreshBars()
    {
        if (_hpBar is null || _mpBar is null) return;

        if (_maxHp > 0)
        {
            _hpBar.MaxValue = _maxHp;
            _hpBar.Value = _hp;
            _hpLabel.Text = $"HP  {_hp}/{_maxHp}";
        }
        else
        {
            _hpBar.Value = 0;
            _hpLabel.Text = "HP";
        }

        if (_maxMp > 0)
        {
            _mpBar.MaxValue = _maxMp;
            _mpBar.Value = _mp;
            _mpLabel.Text = $"MP  {_mp}/{_maxMp}";
        }
        else
        {
            _mpBar.Value = 0;
            _mpLabel.Text = "MP";
        }
    }
}