// Ui/Hud/HudRightEdgeGauge.cs
//
// Right-edge stacked HP/MP gauge composite.
//
// Two gauge strips stacked at the right edge form one composite widget.
// Source texture: data/ui/chunrihojung.dds (loaded by path, not via uitex.txt).
//
// Placement (CONFIRMED-formula):
//   Gauge strip A (HP):  screen_width − 135, Y = 200, W = 140, H = 35
//   Gauge strip B (MP):  screen_width − 135, Y = 250, W = 140, H = 35
//
// In Godot terms: AnchorLeft = 1, AnchorRight = 1
//   OffsetLeft  = -135, OffsetRight = 5 (=−135+140)
//   Strip A: OffsetTop = 200, OffsetBottom = 235
//   Strip B: OffsetTop = 250, OffsetBottom = 285
//
// spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula.
// spec: Docs/RE/specs/ui_hud_layout.md §5.3 — "chunrihojung.dds" source texture.

using Godot;
using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
/// Right-edge stacked HP/MP gauge composite. Two 140×35 strips at screen_width−135.
///
/// <para>PASSIVE: reads <see cref="IHudEventHub"/> ExpLevels for vitals; renders ProgressBar fills.
/// Zero game logic. Degrades gracefully when VFS is offline.</para>
///
/// spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula.
/// </summary>
public sealed partial class HudRightEdgeGauge : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited placement constants
    // spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula
    // -------------------------------------------------------------------------

    private const float OffsetFromRight = 135f; // spec: ui_hud_layout.md §5.6
    private const float GaugeW = 140f;          // spec: ui_hud_layout.md §5.6
    private const float GaugeH = 35f;           // spec: ui_hud_layout.md §5.6
    private const float HpY = 200f;             // spec: ui_hud_layout.md §5.6 strip A
    private const float MpY = 250f;             // spec: ui_hud_layout.md §5.6 strip B (+50)

    // Atlas: chunrihojung.dds (the right-edge gauge chrome).
    // spec: Docs/RE/specs/ui_hud_layout.md §5.3 / §5.6 — source texture "chunrihojung.dds".
    private const string GaugeDdsPath = "data/ui/chunrihojung.dds"; // spec: ui_hud_layout.md §5.3

    // -------------------------------------------------------------------------
    // Child controls
    // -------------------------------------------------------------------------

    private ProgressBar _hpBar = null!;
    private ProgressBar _mpBar = null!;
    private Label _hpLabel = null!;
    private Label _mpLabel = null!;

    // -------------------------------------------------------------------------
    // Hub drain state
    // -------------------------------------------------------------------------

    // NOTE: IHudEventHub does not yet expose a HP/MP vitals channel (only ExpLevels = XP/level).
    // HP/MP arrives on IClientEventBus (ActorVitalsChangedEvent), not the HUD hub.
    // TODO(world-campaign): when a VitalsChanged hub channel is added, drain it here.
    // For now, the gauge renders at fixed 100% until live data wiring is established.

    // -------------------------------------------------------------------------
    // Build (geometry pass)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Geometry pass: positions the two gauge strips at screen_width−135, Y=200 and Y=250.
    /// spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula.
    /// </summary>
    public void Build(HudAtlasLibrary atlas)
    {
        Name = "HudRightEdgeGauge";
        MouseFilter = MouseFilterEnum.Ignore;

        // Container: right-anchored, spans Y=200..285.
        // AnchorLeft=1, AnchorRight=1 → right edge relative.
        // The two strips sit inside at their Y offsets.
        AnchorLeft = 1f;
        AnchorRight = 1f;
        AnchorTop = 0f;
        AnchorBottom = 0f;
        OffsetLeft = -OffsetFromRight;         // spec: ui_hud_layout.md §5.6
        OffsetRight = -OffsetFromRight + GaugeW; // spec: ui_hud_layout.md §5.6
        OffsetTop = HpY;                       // spec: ui_hud_layout.md §5.6 strip A Y
        OffsetBottom = MpY + GaugeH;           // spec: ui_hud_layout.md §5.6 strip B bottom

        // Try to load chunrihojung.dds as the gauge backdrop.
        // spec: ui_hud_layout.md §5.3 / §5.6 — "chunrihojung.dds" source texture.
        Texture2D? gaugeTex = atlas.GetByPath(GaugeDdsPath);
        if (gaugeTex is null)
            GD.PrintErr("[HudRightEdgeGauge] chunrihojung.dds unavailable — gauge chrome absent (VFS offline). " +
                        "spec: Docs/RE/specs/ui_hud_layout.md §5.6.");

        // HP strip (strip A)
        _hpBar = BuildGaugeStrip(gaugeTex, localY: 0f,
            fillColor: new Color(0.9f, 0.1f, 0.1f, 0.85f));

        // MP strip (strip B)  — positioned +50 from HP strip
        // spec: ui_hud_layout.md §5.6 — "Y+50 for strip B"
        _mpBar = BuildGaugeStrip(gaugeTex, localY: MpY - HpY, // = 50
            fillColor: new Color(0.1f, 0.3f, 0.9f, 0.85f));

        // HP value label
        _hpLabel = new Label
        {
            Name = "HpLabel",
            Text = "HP —/—",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _hpLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _hpLabel.OffsetTop = 0f;
        _hpLabel.OffsetBottom = GaugeH;
        AddChild(_hpLabel);

        // MP value label
        _mpLabel = new Label
        {
            Name = "MpLabel",
            Text = "MP —/—",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _mpLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _mpLabel.OffsetTop = MpY - HpY;
        _mpLabel.OffsetBottom = MpY - HpY + GaugeH;
        AddChild(_mpLabel);

        GD.Print("[HudRightEdgeGauge] Built — two 140×35 strips at screen_width−135, Y=200/250. " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula.");
    }

    private ProgressBar BuildGaugeStrip(Texture2D? chromeTex, float localY, Color fillColor)
    {
        var container = new Control
        {
            Name = localY < 1f ? "HpStrip" : "MpStrip",
            Position = new Vector2(0f, localY),
            Size = new Vector2(GaugeW, GaugeH),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(container);

        // Chrome backdrop
        if (chromeTex is not null)
        {
            // Strip A and B are two 140×35 regions; strip A at srcY=0, strip B at srcY=35.
            // Exact source coordinates are not byte-confirmed; the chrome is applied as full-strip.
            // TODO(spec): chunrihojung.dds internal source rects are debugger-pending.
            var chromeSrcY = (int)(localY / GaugeH) * (int)GaugeH; // 0 or 35
            var chrome = new TextureRect
            {
                Name = "Chrome",
                Texture = new AtlasTexture
                {
                    Atlas = chromeTex,
                    Region = new Rect2(0, chromeSrcY, (int)GaugeW, (int)GaugeH),
                    FilterClip = true,
                },
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            chrome.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            container.AddChild(chrome);
        }

        // Progress fill bar
        var bar = new ProgressBar
        {
            Name = "Bar",
            MinValue = 0,
            MaxValue = 100,
            Value = 100,
            MouseFilter = MouseFilterEnum.Ignore,
            ShowPercentage = false,
        };
        bar.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var fillStyle = new StyleBoxFlat { BgColor = fillColor };
        bar.AddThemeStyleboxOverride("fill", fillStyle);
        var bgStyle = new StyleBoxEmpty();
        bar.AddThemeStyleboxOverride("background", bgStyle);
        container.AddChild(bar);

        return bar;
    }

    // -------------------------------------------------------------------------
    // Hub binding
    // -------------------------------------------------------------------------

    /// <summary>
    /// Binds to the HUD event hub. The gauge shell is built; live HP/MP wiring is deferred.
    /// TODO(world-campaign): when IHudEventHub exposes a VitalsChanged channel, drain it here.
    /// </summary>
    public void BindHub(IHudEventHub hub)
    {
        // IHudEventHub exposes: ChatLines, BuffStates, CombatTexts, TargetChanges,
        // ExpLevels (XP/level only), StatAllocations, ZoneChanges.
        // HP/MP vitals arrive via IClientEventBus (ActorVitalsChangedEvent) — not on the hub.
        // TODO(world-campaign): add VitalsChanged channel to IHudEventHub; drain here.
        GD.Print("[HudRightEdgeGauge] BindHub: gauge shell built. " +
                 "HP/MP live wiring deferred — hub lacks VitalsChanged channel (TODO world-campaign).");
    }

    public override void _Process(double delta)
    {
        // No hub channel to drain yet. TODO(world-campaign): drain VitalsChanged when available.
    }
}
