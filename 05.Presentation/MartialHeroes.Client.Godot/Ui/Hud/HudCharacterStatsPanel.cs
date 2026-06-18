// Ui/Hud/HudCharacterStatsPanel.cs
//
// Character statistics panel — StatusPanel + 3 sibling text panels (A/B/C).
//
// Chrome (CODE-CONFIRMED):
//   - Main frame: inventwindow.dds (uitex 2).
//   - Border strip: tradekeepwindow.dds (uitex 4).
//   spec: Docs/RE/specs/ui_system.md §8.7 CODE-CONFIRMED.
//
// Action ids (CODE-CONFIRMED):
//   300..312 (13 stat allocation slots).
//   spec: ui_system.md §8.7 CODE-CONFIRMED.
//
// Stat name strings from msg.xdb keys 60001–60022 (CODE-CONFIRMED).
//   spec: ui_system.md §8.7 CODE-CONFIRMED.
//
// Placement: StatsX=180, StatsY=95, StatsW=130, StatsH=196.
//   spec: Docs/RE/specs/ui_hud_layout.md §3.4 CODE-CONFIRMED
//   (preserved from old HudPanelConfig; matches §3.4 sub-panels A/B/C layout).
//
// 3 sibling text panels A/B/C (CODE-CONFIRMED):
//   A — left stat column.
//   B — right stat column.
//   C — sub-stat or secondary column.
//   Exact per-panel geometry: TODO(spec) — exact per-sub-panel coords not recovered.
//
// Toggle: key C (new; old HUD used key C for stats; HudMaster calls ToggleStats()).
//
// Stat values read from IHudEventHub.StatAllocations (StatAllocationView snapshots).
//
// PASSIVE: reads channels; zero game logic; no stat math.
//   Stat allocation button → use-case call; outcome arrives as StatAllocationView event.
//
// spec: Docs/RE/specs/ui_system.md §8.7 — StatusPanel CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_hud_layout.md §3.4 — placement CODE-CONFIRMED.

using System.Threading.Channels;
using Godot;
using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
/// Character statistics panel (StatusPanel + 3 sub-panels A/B/C).
///
/// <para>PASSIVE: drains IHudEventHub.StatAllocations to render stat values.
/// Allocation buttons emit use-case calls; no game-rule math in this view.</para>
///
/// spec: Docs/RE/specs/ui_system.md §8.7 CODE-CONFIRMED.
/// spec: Docs/RE/specs/ui_hud_layout.md §3.4 CODE-CONFIRMED.
/// </summary>
public sealed partial class HudCharacterStatsPanel : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited placement constants
    // spec: Docs/RE/specs/ui_hud_layout.md §3.4 CODE-CONFIRMED (sub-panel composite)
    // -------------------------------------------------------------------------

    private const float StatsX = 180f; // spec: ui_hud_layout.md §3.4 / HudPanelConfig.StatsX
    private const float StatsY = 95f; // spec: ui_hud_layout.md §3.4 / HudPanelConfig.StatsY
    private const float StatsW = 130f; // spec: ui_hud_layout.md §3.4 / HudPanelConfig.StatsW
    private const float StatsH = 196f; // spec: ui_hud_layout.md §3.4 / HudPanelConfig.StatsH

    // Stat action ids (CODE-CONFIRMED)
    // spec: ui_system.md §8.7 CODE-CONFIRMED — "action ids 300..312"
    private const int StatActionStart = 300; // spec: ui_system.md §8.7
    private const int StatActionCount = 13; // spec: ui_system.md §8.7 — "300..312 = 13 actions"

    // Stat name msg.xdb key range (CODE-CONFIRMED)
    // spec: ui_system.md §8.7 CODE-CONFIRMED — "stat names msg IDs 60001–60022"
    private const int StatNameMsgStart = 60001; // spec: ui_system.md §8.7
    private const int StatNameMsgEnd = 60022; // spec: ui_system.md §8.7

    // Chrome atlas ids
    // spec: ui_system.md §8.6.1 — uitex 2 = inventwindow.dds, uitex 4 = tradekeepwindow.dds
    private const int InvTexId = 2; // spec: ui_system.md §8.6.1 — inventwindow.dds (main chrome)
    private const int TradeTexId = 4; // spec: ui_system.md §8.6.1 — tradekeepwindow.dds (border strip)

    // -------------------------------------------------------------------------
    // Child controls
    // -------------------------------------------------------------------------

    // Stat row labels (value portion) — up to 22 stat names
    private readonly Label[] _statValueLabels = new Label[StatNameMsgEnd - StatNameMsgStart + 1];
    private Label _charNameLabel = null!;
    private Label _charLevelLabel = null!;

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private bool _visible;
    private ChannelReader<StatAllocationView>? _statAllocations;
    private ClientContext? _ctx;

    // -------------------------------------------------------------------------
    // Build (geometry pass)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Geometry pass: creates the StatusPanel at (180, 95) size 130×196, plus 3 sibling sub-panels A/B/C.
    ///
    /// spec: Docs/RE/specs/ui_hud_layout.md §3.4 CODE-CONFIRMED.
    /// spec: Docs/RE/specs/ui_system.md §8.7 CODE-CONFIRMED.
    /// </summary>
    public void Build(HudAtlasLibrary atlas, HudTextLibrary text, ClientContext ctx)
    {
        Name = "HudCharacterStatsPanel";
        _ctx = ctx;

        // Positioned at (180, 95); size 130×196 — but expanded for 3 sub-panels.
        // spec: ui_hud_layout.md §3.4 — main stats composite at (180, 95).
        Position = new Vector2(StatsX, StatsY);
        // Width expanded to fit 3 sub-panels (A/B/C) side-by-side (rough: 390 total).
        // Exact sub-panel X/Y: TODO(spec) — per-sub-panel coords not recovered.
        Size = new Vector2(StatsW * 3f + 10f, StatsH + 60f);
        Visible = false; // hidden by default; key-C reveals
        MouseFilter = MouseFilterEnum.Stop;

        // Load chrome textures
        // spec: ui_system.md §8.7 — inventwindow.dds + tradekeepwindow.dds
        Texture2D? mainChrome = atlas.GetById(InvTexId);
        Texture2D? borderChrome = atlas.GetById(TradeTexId);
        if (mainChrome is null)
            GD.PrintErr("[HudCharacterStatsPanel] inventwindow.dds (uitex 2) unavailable. " +
                        "spec: Docs/RE/specs/ui_system.md §8.7.");
        if (borderChrome is null)
            GD.PrintErr("[HudCharacterStatsPanel] tradekeepwindow.dds (uitex 4) unavailable. " +
                        "spec: Docs/RE/specs/ui_system.md §8.7.");

        // Background
        var bg = new Panel { Name = "Bg" };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0.06f, 0.06f, 0.1f, 0.93f);
        bgStyle.SetBorderWidthAll(2);
        bgStyle.BorderColor = new Color(0.45f, 0.35f, 0.15f, 0.9f);
        bg.AddThemeStyleboxOverride("panel", bgStyle);
        AddChild(bg);

        // Main chrome (inventwindow.dds)
        if (mainChrome is not null)
        {
            var chromeTex = new TextureRect
            {
                Name = "Chrome",
                Texture = mainChrome,
                StretchMode = TextureRect.StretchModeEnum.KeepAspect,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            chromeTex.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(chromeTex);
        }

        // Border strip (tradekeepwindow.dds) — top-left inset decoration
        if (borderChrome is not null)
        {
            var borderTex = new TextureRect
            {
                Name = "BorderStrip",
                Texture = borderChrome,
                StretchMode = TextureRect.StretchModeEnum.KeepAspect,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            // Manual anchor+offset (LayoutPreset.Custom not available in Godot 4.6.3)
            borderTex.AnchorLeft = 0f;
            borderTex.AnchorTop = 0f;
            borderTex.AnchorRight = 1f;
            borderTex.AnchorBottom = 0f;
            borderTex.OffsetTop = 0f;
            borderTex.OffsetBottom = 16f;
            AddChild(borderTex);
        }

        // Character name / level header
        _charNameLabel = new Label
        {
            Name = "CharName",
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Left,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _charNameLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        _charNameLabel.OffsetLeft = 6f;
        _charNameLabel.OffsetTop = 18f;
        _charNameLabel.OffsetRight = 200f;
        _charNameLabel.OffsetBottom = 34f;
        AddChild(_charNameLabel);

        _charLevelLabel = new Label
        {
            Name = "CharLevel",
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _charLevelLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        _charLevelLabel.OffsetLeft = -60f;
        _charLevelLabel.OffsetTop = 18f;
        _charLevelLabel.OffsetRight = -4f;
        _charLevelLabel.OffsetBottom = 34f;
        AddChild(_charLevelLabel);

        // Build 3 sub-panels A/B/C (stat columns)
        // spec: ui_system.md §8.7 CODE-CONFIRMED — "3 sibling text panels A, B, C"
        // Exact per-panel geometry not recovered: TODO(spec).
        BuildStatSubPanel("PanelA", 0f, 36f, StatsW, StatsH,
            StatNameMsgStart, StatNameMsgStart + 8, text, ctx);
        BuildStatSubPanel("PanelB", StatsW + 5f, 36f, StatsW, StatsH,
            StatNameMsgStart + 9, StatNameMsgStart + 17, text, ctx);
        BuildStatSubPanel("PanelC", StatsW * 2f + 10f, 36f, StatsW, StatsH,
            StatNameMsgStart + 18, StatNameMsgEnd, text, ctx);

        GD.Print("[HudCharacterStatsPanel] Built — StatusPanel + 3 sub-panels (A/B/C). " +
                 "Action ids 300-312; stat names msg.xdb 60001-60022. " +
                 "inventwindow.dds (uitex 2) + tradekeepwindow.dds (uitex 4). " +
                 "spec: Docs/RE/specs/ui_system.md §8.7 CODE-CONFIRMED. " +
                 "TODO(spec): per-sub-panel coordinates not recovered.");
    }

    private void BuildStatSubPanel(string panelName, float x, float y, float w, float h,
        int msgStart, int msgEnd, HudTextLibrary text, ClientContext ctx)
    {
        var panel = new Control
        {
            Name = panelName,
            Position = new Vector2(x, y),
            Size = new Vector2(w, h),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(panel);

        float rowH = 18f;
        float startY = 4f;
        int rowIdx = 0;

        for (int msgId = msgStart; msgId <= msgEnd; msgId++)
        {
            int statIdx = msgId - StatNameMsgStart;
            float rowY = startY + rowIdx * rowH;

            // Stat name label from msg.xdb
            // spec: ui_system.md §8.7 CODE-CONFIRMED — "stat names msg IDs 60001–60022"
            string statName = text.GetCaption(msgId, $"stat{statIdx}");
            var nameLabel = new Label
            {
                Name = $"StatName{msgId}",
                Text = statName,
                HorizontalAlignment = HorizontalAlignment.Left,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            // Manual anchor+offset (LayoutPreset.Custom not available in Godot 4.6.3)
            nameLabel.AnchorLeft = 0f;
            nameLabel.AnchorTop = 0f;
            nameLabel.AnchorRight = 0f;
            nameLabel.AnchorBottom = 0f;
            nameLabel.OffsetLeft = 2f;
            nameLabel.OffsetTop = rowY;
            nameLabel.OffsetRight = w * 0.6f;
            nameLabel.OffsetBottom = rowY + rowH;
            panel.AddChild(nameLabel);

            // Stat value label — filled by StatAllocationView drain
            var valueLabel = new Label
            {
                Name = $"StatValue{msgId}",
                Text = "—",
                HorizontalAlignment = HorizontalAlignment.Right,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            // Manual anchor+offset (LayoutPreset.Custom not available in Godot 4.6.3)
            valueLabel.AnchorLeft = 0f;
            valueLabel.AnchorTop = 0f;
            valueLabel.AnchorRight = 0f;
            valueLabel.AnchorBottom = 0f;
            valueLabel.OffsetLeft = w * 0.62f;
            valueLabel.OffsetTop = rowY;
            valueLabel.OffsetRight = w - 2f;
            valueLabel.OffsetBottom = rowY + rowH;
            panel.AddChild(valueLabel);

            if (statIdx >= 0 && statIdx < _statValueLabels.Length)
                _statValueLabels[statIdx] = valueLabel;

            rowIdx++;
        }

        // Stat allocation buttons (action ids 300..312)
        // spec: ui_system.md §8.7 CODE-CONFIRMED — "action ids 300..312"
        // Placed as "+" buttons next to stat values where applicable.
        // The allocation is an intent call — not a local mutation.
        // TODO(world-campaign): bind allocation buttons to ctx.UseCases.AllocateStatAsync(statIdx).
    }

    // -------------------------------------------------------------------------
    // Hub binding
    // -------------------------------------------------------------------------

    /// <summary>Binds to the HUD event hub's StatAllocations channel.</summary>
    public void BindHub(IHudEventHub hub)
    {
        _statAllocations = hub.StatAllocations;
        GD.Print("[HudCharacterStatsPanel] BindHub: StatAllocations channel connected.");
    }

    // -------------------------------------------------------------------------
    // Per-frame drain
    // -------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_statAllocations is null) return;

        StatAllocationView? latest = null;
        while (_statAllocations.TryRead(out StatAllocationView? ev))
            latest = ev;

        if (latest is null) return;
        ApplyStatView(latest);
    }

    private void ApplyStatView(StatAllocationView view)
    {
        // StatAllocationView carries STR/INT/AGI/DEX/CON + deltas + remaining points.
        // spec: Docs/RE/specs/progression.md §2 / §7.1 / §8.1.
        // Map to the first 5 stat labels (indices 0..4 = STR, INT, AGI, DEX, CON).
        // The msg.xdb names 60001–60022 identify the stat rows; the 5 bases map to the first 5.
        // TODO(spec): confirm exact msg.xdb order matches (STR/INT/AGI/DEX/CON vs display order).
        SetStatLabel(0, view.BaseStr, view.DeltaStr); // STR
        SetStatLabel(1, view.BaseInt, view.DeltaInt); // INT
        SetStatLabel(2, view.BaseAgi, view.DeltaAgi); // AGI
        SetStatLabel(3, view.BaseDex, view.DeltaDex); // DEX
        SetStatLabel(4, view.BaseCon, view.DeltaCon); // CON

        // Remaining points (available to allocate)
        // spec: progression.md §7.1 — PointsAvailable = remaining − Σ deltas.
        // TODO(world-campaign): show remaining points in a dedicated label.
    }

    private void SetStatLabel(int idx, uint baseVal, uint delta)
    {
        if ((uint)idx >= (uint)_statValueLabels.Length) return;
        Label? lbl = _statValueLabels[idx];
        if (lbl is null) return;
        lbl.Text = delta > 0 ? $"{baseVal}+{delta}" : $"{baseVal}";
    }

    // -------------------------------------------------------------------------
    // Toggle
    // -------------------------------------------------------------------------

    /// <summary>
    /// Toggles the stats panel visibility. Called by HudMaster on key-C press.
    /// </summary>
    public void Toggle()
    {
        _visible = !_visible;
        Visible = _visible;
        GD.Print($"[HudCharacterStatsPanel] Toggle → visible={_visible}.");
    }
}