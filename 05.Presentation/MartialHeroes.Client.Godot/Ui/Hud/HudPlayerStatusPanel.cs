// Ui/Hud/HudPlayerStatusPanel.cs
//
// PlayerStatusPanel — HP/MP/stamina gauges + portrait + condition bar + level.
// Faithful counterpart of the recovered GagePanel (PlayerStatusPanel), HUD panel-slot array slot 15.
//
// Slot: 15 (MainWindow member +0x27C = slot 15).
// Geometry: dst (0,0); W=285, H=88; base atlas = UI-manifest key 4.
// spec: Docs/RE/scenes/ingame.md §2.1 — PlayerStatusPanel ("GagePanel") internal layout.
//
// Gauge fill formula (spec-recovered):
//   HP fill width   = 172 · CurrentHp   / MaxHp   clamped to 172.
//   MP fill width   = 172 · CurrentMp   / MaxMp   clamped to 172.
//   Stamina fill w  = 172 · CurStamina  / MaxSt   clamped to 172.
//   Condition bar h = 44  · cur         / range    (vertical bar, 44 px total).
//   spec: ingame.md §2.1 — "Gauge fill width = 172 · current / max, clamped to 172"
//   spec: ingame.md §2.1 — "Bar fill = 44 · cur / range" (condition bar)
//
// Child widget atlas src-rects (from ingame.md §2.4 / §2.1):
//   HP src     (331,694)  — spec: ingame.md §2.4
//   MP src     (504,694)  — spec: ingame.md §2.4
//   Stamina src(331,712)  — spec: ingame.md §2.4
//   Condition  (598,736)  — spec: ingame.md §2.4 (vertical bar)
//   Portrait   (933,715)  — spec: ingame.md §2.4
//
// Hub binding: IHudEventHub.Vitals — drains HudVitalsEvent each frame.
// spec: Docs/RE/scenes/ingame.md §2.1 — vtable slot 7 onDraw "updates gauges + labels + condition".
//
// PASSIVE: zero game logic. Reads IHudEventHub.Vitals; renders gauge fills. No stat math.

using System.Threading.Channels;
using Godot;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
///     Player vitals panel — HP / MP / stamina gauge fills, portrait placeholder, condition vertical bar,
///     and level label. Faithful counterpart of the recovered <c>PlayerStatusPanel</c> ("GagePanel"),
///     HUD panel-slot array <b>slot 15</b>.
///     <para>
///         PASSIVE: drains <see cref="IHudEventHub.Vitals" /> each frame; updates only the fill widths
///         and label text from the delivered <see cref="HudVitalsEvent" />. Zero game logic.
///     </para>
///     <para>
///         Geometry: top-left corner, W=285, H=88, base atlas = UI-manifest key 4.
///         Fill formula: <c>min(172, 172 · current / max)</c> px wide per gauge.
///         Condition vertical bar: <c>44 · cur / range</c> px tall.
///     </para>
///     spec: Docs/RE/scenes/ingame.md §2 — Core HUD group; slot 15 = PlayerStatusPanel (GagePanel).
///     spec: Docs/RE/scenes/ingame.md §2.1 — internal layout: gauge fills, condition bar, portrait, labels.
///     spec: Docs/RE/scenes/ingame.md §2.4 — HP src (331,694); MP src (504,694); stamina (331,712);
///     condition bar (598,736); portrait (933,715).
/// </summary>
public sealed partial class HudPlayerStatusPanel : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited geometry constants
    // spec: Docs/RE/scenes/ingame.md §2.1
    // -------------------------------------------------------------------------

    private const float PanelW = 285f; // spec: ingame.md §2.1 — W=285
    private const float PanelH = 88f; // spec: ingame.md §2.1 — H=88
    private const float GaugeMaxW = 172f; // spec: ingame.md §2.1 — "172 = bar pixel width"
    private const float GaugeH = 6f; // approximate bar height (ingame.md §2.1 fill image h driven by gauge math)
    private const float HpGaugeY = 18f; // approximate Y within panel (layout from §2.1 vtable slot 14)
    private const float MpGaugeY = 32f; // MP strip below HP
    private const float StaminaGaugeY = 46f; // stamina strip below MP
    private const float GaugeOffsetX = 10f; // left indent within panel (approximate)

    // Condition vertical bar: 44 px tall — spec: ingame.md §2.1 "Bar fill = 44 · cur / range"
    private const float CondBarMaxH = 44f; // spec: ingame.md §2.1 — condition bar pixel height
    private const float CondBarX = 3f; // approximate x (ingame.md §2.1 condition +0xC8)
    private const float CondBarY = 14f; // approximate y
    private const float CondBarW = 6f; // approximate width

    // Portrait placeholder (ingame.md §2.1 — portrait button +0xCC)
    private const float PortraitX = 3f; // spec: ingame.md §2.4 — portrait src (933,715)
    private const float PortraitY = 3f;
    private const float PortraitW = 40f; // placeholder size
    private const float PortraitH = 50f;

    // Atlas src-rects (ingame.md §2.4, uitex id resolved via UI-manifest key 4)
    private const int HpSrcX = 331; // spec: ingame.md §2.4 — "HP src 331,694"
    private const int HpSrcY = 694; // spec: ingame.md §2.4
    private const int MpSrcX = 504; // spec: ingame.md §2.4 — "MP src 504,694"
    private const int MpSrcY = 694; // spec: ingame.md §2.4
    private const int StSrcX = 331; // spec: ingame.md §2.4 — "stamina src 331,712"
    private const int StSrcY = 712; // spec: ingame.md §2.4
    private const int CondSrcX = 598; // spec: ingame.md §2.4 — "condition bar src 598,736"
    private const int CondSrcY = 736; // spec: ingame.md §2.4
    private const int PortraitSrcX = 933; // spec: ingame.md §2.4 — "portrait src 933,715"
    private const int PortraitSrcY = 715; // spec: ingame.md §2.4

    // UI-manifest key 4 — base atlas for PlayerStatusPanel.
    // spec: ingame.md §2.4 — "PlayerStatusPanel base atlas = UI-manifest key 4"
    private const int ManifestKey = 4; // spec: ingame.md §2.4
    private Control? _condBar; // condition vertical fill bar

    // -------------------------------------------------------------------------
    // Child controls (built once; updated per HudVitalsEvent)
    // -------------------------------------------------------------------------

    private Control? _hpFill; // HP gauge fill (ColorRect or TextureRect)
    private Label? _hpLabel; // "cur/max" text label for HP
    private Label? _levelLabel; // level display
    private Control? _mpFill; // MP gauge fill
    private Label? _mpLabel; // "cur/max" text label for MP
    private Control? _staminaFill; // stamina gauge fill
    private Label? _staminaLabel; // "cur/max" stamina label

    // -------------------------------------------------------------------------
    // Hub drain state
    // -------------------------------------------------------------------------

    private ChannelReader<HudVitalsEvent>? _vitals;

    // -------------------------------------------------------------------------
    // Build (geometry pass)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Geometry pass: positions the PlayerStatusPanel at the top-left corner (dst 0,0; W=285, H=88).
    ///     Builds HP/MP/stamina gauge fills, condition vertical bar, portrait placeholder, and labels.
    ///     Graceful-null when the VFS/atlas is offline (fallback <see cref="ColorRect" /> fills).
    ///     spec: Docs/RE/scenes/ingame.md §2.1 — vtable slot 14 builds all child widgets.
    ///     spec: Docs/RE/scenes/ingame.md §2.4 — base atlas UI-manifest key 4.
    /// </summary>
    public void Build(HudAtlasLibrary atlas)
    {
        Name = "HudPlayerStatusPanel";

        // Top-left corner, fixed W=285 H=88.
        // spec: ingame.md §2.1 — "Built top-left (dst 0,0; w=285, h=88)"
        AnchorLeft = 0f;
        AnchorTop = 0f;
        AnchorRight = 0f;
        AnchorBottom = 0f;
        OffsetLeft = 0f;
        OffsetTop = 0f;
        OffsetRight = PanelW;
        OffsetBottom = PanelH;
        MouseFilter = MouseFilterEnum.Ignore;

        // --- Background chrome (UI-manifest key 4, graceful-null if offline) ---
        // spec: ingame.md §2.4 — "PlayerStatusPanel base atlas = UI-manifest key 4"
        var bg = new Panel { Name = "PanelBg" };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0.05f, 0.04f, 0.08f, 0.88f);
        bgStyle.SetBorderWidthAll(1);
        bgStyle.BorderColor = new Color(0.35f, 0.30f, 0.50f, 0.80f);
        bg.AddThemeStyleboxOverride("panel", bgStyle);
        bg.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(bg);

        // --- Portrait placeholder (portrait button +0xCC, src 933,715 via manifest key 4) ---
        // spec: ingame.md §2.1 — "portrait button +0xCC"
        // spec: ingame.md §2.4 — "portrait src 933,715"
        // TODO(world-campaign): bind uitex atlas (manifest key 4) when VFS is available
        var portrait = new ColorRect
        {
            Name = "PortraitPlaceholder",
            Color = new Color(0.15f, 0.12f, 0.18f, 0.9f),
            Position = new Vector2(PortraitX, PortraitY),
            Size = new Vector2(PortraitW, PortraitH),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(portrait);

        // --- Condition vertical bar (condition bar +0xC8, src 598,736, fill = 44·cur/range) ---
        // spec: ingame.md §2.1 — "condition vertical bar +0xC8; Bar fill = 44·cur/range"
        // spec: ingame.md §2.4 — "condition bar src 598,736"
        var condBg = new ColorRect
        {
            Name = "CondBarBg",
            Color = new Color(0.1f, 0.08f, 0.15f, 0.8f),
            Position = new Vector2(CondBarX, CondBarY),
            Size = new Vector2(CondBarW, CondBarMaxH),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(condBg);
        var condFill = new ColorRect
        {
            Name = "CondBarFill",
            Color = new Color(0.55f, 0.20f, 0.80f, 0.9f), // purple-ish condition colour (placeholder)
            Position = new Vector2(CondBarX, CondBarY + CondBarMaxH), // starts at bottom, grows up
            Size = new Vector2(CondBarW, 0f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(condFill);
        _condBar = condFill;

        // --- HP gauge fill (src 331,694; fill width = 172·HP/MaxHP) ---
        // spec: ingame.md §2.1 — "HP cur +0xF0 / max +0x108; fill width = 172·current/max clamped to 172"
        // spec: ingame.md §2.4 — "HP src 331,694"
        _hpFill = BuildGaugeFill("HpFill",
            GaugeOffsetX, HpGaugeY,
            new Color(0.80f, 0.15f, 0.15f, 0.9f)); // red HP bar (fallback color)

        // --- MP gauge fill (src 504,694; fill width = 172·MP/MaxMP) ---
        // spec: ingame.md §2.1 — "MP cur +0xF8 / max +0x110; fill width = 172·current/max clamped to 172"
        // spec: ingame.md §2.4 — "MP src 504,694"
        _mpFill = BuildGaugeFill("MpFill",
            GaugeOffsetX, MpGaugeY,
            new Color(0.15f, 0.40f, 0.80f, 0.9f)); // blue MP bar (fallback color)

        // --- Stamina gauge fill (src 331,712; fill width = 172·St/MaxSt) ---
        // spec: ingame.md §2.1 — "stamina cur +0x100 / max +0x118; fill width = 172·current/max clamped to 172"
        // spec: ingame.md §2.4 — "stamina src 331,712"
        _staminaFill = BuildGaugeFill("StaminaFill",
            GaugeOffsetX, StaminaGaugeY,
            new Color(0.15f, 0.70f, 0.30f, 0.9f)); // green stamina bar (fallback color)

        // --- HP label ("cur/max" — spec: ingame.md §2.1 "Label text: six 30-byte CP949 char buffers") ---
        // spec: ingame.md §14.4 — plain "%d/%d" template; no comma grouping
        _hpLabel = BuildGaugeLabel("HpLabel", GaugeOffsetX + GaugeMaxW + 4f, HpGaugeY);

        // --- MP label ---
        _mpLabel = BuildGaugeLabel("MpLabel", GaugeOffsetX + GaugeMaxW + 4f, MpGaugeY);

        // --- Stamina label ---
        _staminaLabel = BuildGaugeLabel("StaminaLabel", GaugeOffsetX + GaugeMaxW + 4f, StaminaGaugeY);

        // --- Level label (level value +0x174, driven by flag byte +0x154) ---
        // spec: ingame.md §2.1 — "level value +0x174; flag byte +0x154 gates numeric level text"
        _levelLabel = new Label
        {
            Name = "LevelLabel",
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(GaugeOffsetX, 60f),
            Size = new Vector2(PanelW - GaugeOffsetX - 4f, 14f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        // Default HUD face: slot 0 = DotumChe 12 px. spec: ingame.md §14.2 — slot 0 default.
        HudFont.ApplyToLabel(_levelLabel, 0); // spec: ingame.md §14.2 — slot 0 DotumChe 12 px
        AddChild(_levelLabel);

        GD.Print("[HudPlayerStatusPanel] Built — PlayerStatusPanel slot 15 (W=285, H=88, top-left). " +
                 "HP/MP/Stamina fills (172px max) + condition bar (44px max) + portrait placeholder + level label. " +
                 "spec: Docs/RE/scenes/ingame.md §2.1 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Hub binding
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Binds the panel to <see cref="IHudEventHub.Vitals" /> so <see cref="_Process" /> can drain it.
    ///     spec: Docs/RE/scenes/ingame.md §2.1 — vtable slot 7 onDraw "updates gauges + labels + condition".
    /// </summary>
    public void BindHub(IHudEventHub hub)
    {
        _vitals = hub.Vitals;
        GD.Print("[HudPlayerStatusPanel] BindHub: Vitals channel connected.");
    }

    // -------------------------------------------------------------------------
    // Per-frame drain
    // -------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_vitals is null) return;

        while (_vitals.TryRead(out var ev))
        {
            if (ev is null) continue;
            ApplyVitals(ev);
        }
    }

    // -------------------------------------------------------------------------
    // Apply event
    // -------------------------------------------------------------------------

    private void ApplyVitals(HudVitalsEvent ev)
    {
        // HP fill — spec: ingame.md §2.1 "fill width = 172·current/max clamped to 172"
        SetFillWidth(_hpFill, ev.HpRatio);

        // MP fill — spec: ingame.md §2.1 same formula for MP
        SetFillWidth(_mpFill, ev.MpRatio);

        // Stamina fill — spec: ingame.md §2.1 same formula for stamina
        SetFillWidth(_staminaFill, ev.StaminaRatio);

        // HP label — spec: ingame.md §14.4 plain "%d/%d" (no comma grouping)
        if (_hpLabel is not null)
            _hpLabel.Text = $"{ev.CurrentHp}/{ev.MaxHp}";

        // MP label
        if (_mpLabel is not null)
            _mpLabel.Text = $"{ev.CurrentMp}/{ev.MaxMp}";

        // Stamina label
        if (_staminaLabel is not null)
            _staminaLabel.Text = $"{ev.CurrentStamina}/{ev.MaxStamina}";

        // Condition bar — placeholder (no condition field in HudVitalsEvent; zero until spec surface)
        // spec: ingame.md §2.1 — "scale +0x168, current +0x16C, range-hi +0x170"
        // TODO(world-campaign): wire condition scale when Application exposes it
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void SetFillWidth(Control? fill, float ratio)
    {
        var w = Math.Min(GaugeMaxW, GaugeMaxW * ratio);
        switch (fill)
        {
            case ColorRect cr: cr.Size = new Vector2(w, GaugeH); break;
            case TextureRect tr: tr.Size = new Vector2(w, GaugeH); break;
        }
    }

    private ColorRect BuildGaugeFill(string nodeName, float x, float y, Color fallbackColor)
    {
        var rect = new ColorRect
        {
            Name = nodeName,
            Color = fallbackColor,
            Position = new Vector2(x, y),
            Size = new Vector2(0f, GaugeH), // starts at 0; updated by ApplyVitals
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(rect);
        return rect;
    }

    private Label BuildGaugeLabel(string nodeName, float x, float y)
    {
        var lbl = new Label
        {
            Name = nodeName,
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(x, y),
            Size = new Vector2(PanelW - x - 2f, GaugeH + 4f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        // Default HUD face: slot 0 = DotumChe 12 px. spec: ingame.md §14.2.
        HudFont.ApplyToLabel(lbl, 0); // spec: ingame.md §14.2 — slot 0 DotumChe 12 px
        AddChild(lbl);
        return lbl;
    }
}