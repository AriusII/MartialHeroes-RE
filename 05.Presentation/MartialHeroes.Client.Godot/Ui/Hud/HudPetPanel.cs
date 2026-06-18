// Ui/Hud/HudPetPanel.cs
//
// In-game PetPanel — player-couple / pair-relation companion window (slot 194, CODE-CONFIRMED).
//
// IMPORTANT: "PetPanel" is the RETAINED IN-ENGINE NAME. This is the player-COUPLE / PAIR-RELATION
// window — NOT a tamed-creature / companion-pet window. There is no creature-pet feature in this build.
//   spec: Docs/RE/specs/ui_system.md §8.26 CODE-CONFIRMED.
//   spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 194.
//
// Geometry (CODE-CONFIRMED — absolute, not screen-relative):
//   Window root: (80, 200), 228×337 on 1024×768 (absolute fixed coords, no screen-relative origin).
//   Object size: 1488 bytes (0x5D0).
//   spec: Docs/RE/specs/ui_system.md §8.26.1 CODE-CONFIRMED.
//
// Title bar (228×16 at (0,0), src (692,0)):
//   Three 11×11 corner buttons at (5,3)/(116,3)/(211,3) — actions 0/2/8 (minimize/help/close).
//   spec: Docs/RE/specs/ui_system.md §8.26.1 CODE-CONFIRMED.
//
// Partner info area:
//   Partner name label: (0, 16, 228, 15).
//   Level/stat label: (10, 59, 78, 15), (90, 59, 78, 15).
//   Two relationship gauges: (15, 77, 196, 11) and (15, 107, 196, 11).
//   spec: Docs/RE/specs/ui_system.md §8.26.1 CODE-CONFIRMED.
//
// Command buttons (4 buttons, actions 3..6):
//   Command A: (15, 157, 91, 25), src (921,0),  action 3.
//   Command B: (124,157, 91, 25), src (921,26), action 4.
//   Command C: (15, 215, 91, 25), src (921,52), action 5.
//   Command D: (124,215, 91, 25), src (921,78), action 6.
//   Info/details button: (110, 253, 110, 39), src (849,337), action 7.
//   spec: Docs/RE/specs/ui_system.md §8.26.1 CODE-CONFIRMED.
//
// Atlas binding (CODE-CONFIRMED uitex ids; DDS = VFS uitex.txt pending):
//   uitex 1 = shared HUD button / chrome atlas (three 11×11 title buttons).
//   uitex 3 = couple/pair-relation gauge / content atlas.
//   spec: Docs/RE/specs/ui_system.md §8.26.2 CODE-CONFIRMED.
//
// Action-id map (CODE-CONFIRMED):
//   0 = minimize (MED), 2 = help button (msg.xdb 16002), 3..6 = command buttons A..D,
//   7 = info/details, 8 = title-close.
//   spec: Docs/RE/specs/ui_system.md §8.26.3 CODE-CONFIRMED.
//
// Open/populate/close (CODE-CONFIRMED):
//   Auto-shown by SmsgActorPairRelation (S2C 5/53) on relation-set path.
//   Populate from partnered actor (name, level, gauges).
//   Hidden on 5/53 clear, confirm-click, or partner death (SmsgCharDeath).
//   NOT a hotkey window.
//   spec: Docs/RE/specs/ui_system.md §8.26.4 CODE-CONFIRMED.
//   TODO(world-campaign): wire SmsgActorPairRelation 5/53.
//
// Captions from msg.xdb (CODE-CONFIRMED):
//   16002 = help-button caption (action 2).
//   10025..10032 = couple/pair system-notice strings (to chat log / notice, not panel widgets).
//   3014/3015 = pair-interaction-gate notices.
//   spec: Docs/RE/specs/ui_system.md §8.26.5 CODE-CONFIRMED.
//
// PASSIVE: zero game logic. ShowPartner/ClearPartner are the caller API.
// Command buttons: use-case calls TODO(world-campaign) (generic select/send helper, no dedicated C2S).

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
/// In-game player-couple / pair-relation window (PetPanel, slot 194).
///
/// <para>IMPORTANT: "PetPanel" is the retained legacy name. This is the COUPLE/PAIR window,
/// not a creature-pet feature. Auto-shown by S2C 5/53 SmsgActorPairRelation.</para>
///
/// <para>PASSIVE: zero game logic; no domain mutation; net sends = TODO stubs.</para>
///
/// spec: Docs/RE/specs/ui_system.md §8.26 CODE-CONFIRMED.
/// spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 194.
/// </summary>
public sealed partial class HudPetPanel : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited geometry constants
    // spec: Docs/RE/specs/ui_system.md §8.26.1 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    // Absolute fixed position on 1024×768 canvas.
    // spec: §8.26.1 — "fixed (80, 200), 228×337 (absolute; no screen-relative origin)"
    private const float WinX = 80f; // spec: §8.26.1
    private const float WinY = 200f; // spec: §8.26.1
    private const float WinW = 228f; // spec: §8.26.1
    private const float WinH = 337f; // spec: §8.26.1

    // Title bar
    // spec: §8.26.1 — "228×16 caption strip at (0,0), src (692,0)"
    private const float TitleH = 16f; // spec: §8.26.1

    // Corner title-buttons (11×11)
    // spec: §8.26.1 — "three 11×11 corner buttons at (5,3)/(116,3)/(211,3), actions 0/2/8"
    private const float CornerBtnSize = 11f; // spec: §8.26.1

    // Partner name label
    // spec: §8.26.1 — "(0, 16, 228, 15)"
    private const float NameLblY = 16f; // spec: §8.26.1
    private const float NameLblH = 15f; // spec: §8.26.1

    // Relationship gauge positions
    // spec: §8.26.1 — "(15, 77, 196, 11) and (15, 107, 196, 11)"
    private const float GaugeX = 15f; // spec: §8.26.1
    private const float Gauge0Y = 77f; // spec: §8.26.1
    private const float Gauge1Y = 107f; // spec: §8.26.1
    private const float GaugeW = 196f; // spec: §8.26.1
    private const float GaugeH = 11f; // spec: §8.26.1

    // Command button layout (CODE-CONFIRMED)
    // spec: §8.26.1 — actions 3..6
    private const float CmdBtnW = 91f; // spec: §8.26.1
    private const float CmdBtnH = 25f; // spec: §8.26.1

    // msg.xdb caption ids (CODE-CONFIRMED)
    // spec: Docs/RE/specs/ui_system.md §8.26.5 CODE-CONFIRMED
    private const int MsgHelpBtn = 16002; // spec: §8.26.5 — help-button caption (action 2)

    // -------------------------------------------------------------------------
    // Child references
    // -------------------------------------------------------------------------

    private Label? _nameLabel;
    private Label? _levelLabel;
    private ProgressBar? _gauge0;
    private ProgressBar? _gauge1;

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private bool _open;

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    /// Geometry pass: builds the couple/pair-relation window at its fixed absolute position.
    ///
    /// spec: Docs/RE/specs/ui_system.md §8.26 CODE-CONFIRMED.
    /// spec: Docs/RE/specs/ui_system.md §8.26.1 CODE-CONFIRMED — geometry.
    /// spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 194.
    /// </summary>
    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudPetPanel";

        // Fixed absolute position on 1024×768 canvas.
        // spec: §8.26.1 — "fixed (80, 200), 228×337 (absolute, no screen-relative origin)"
        AnchorLeft = 0f;
        AnchorTop = 0f;
        AnchorRight = 0f;
        AnchorBottom = 0f;
        OffsetLeft = WinX; // spec: §8.26.1 — X=80
        OffsetTop = WinY; // spec: §8.26.1 — Y=200
        OffsetRight = WinX + WinW; // spec: §8.26.1 — W=228
        OffsetBottom = WinY + WinH; // spec: §8.26.1 — H=337

        Visible = false;
        _open = false;
        MouseFilter = MouseFilterEnum.Stop;

        // ── Backdrop / chrome ──
        // spec: §8.26.2 — uitex 3 = couple/pair-relation gauge/content atlas
        // TODO(assets): bind uitex 3 when atlas available.
        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.06f, 0.05f, 0.08f, 0.95f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.50f, 0.25f, 0.50f, 0.9f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        // ── Title bar (228×16, src (692,0), uitex 1) ──
        // spec: §8.26.1 — "228×16 caption strip at (0,0), src (692,0)"
        var titleBar = new Panel { Name = "TitleBar" };
        titleBar.AnchorLeft = 0f;
        titleBar.AnchorTop = 0f;
        titleBar.AnchorRight = 0f;
        titleBar.AnchorBottom = 0f;
        titleBar.OffsetLeft = 0f;
        titleBar.OffsetTop = 0f;
        titleBar.OffsetRight = WinW;
        titleBar.OffsetBottom = TitleH;
        var tbStyle = new StyleBoxFlat();
        tbStyle.BgColor = new Color(0.35f, 0.15f, 0.35f, 0.97f);
        titleBar.AddThemeStyleboxOverride("panel", tbStyle);
        AddChild(titleBar);

        // Title-bar corner buttons (11×11, actions 0/2/8)
        // spec: §8.26.1 — "at (5,3)/(116,3)/(211,3), actions 0/2/8"
        BuildCornerButton("MinimizeBtn", 5f, 3f, 0); // action 0 = minimize (MED)
        BuildCornerButton("HelpBtn", 116f, 3f, 2); // action 2 = help (msg 16002)
        BuildCornerButton("CloseBtn_Title", 211f, 3f, 8); // action 8 = close

        // ── Partner name label (0, 16, 228, 15) ──
        // spec: §8.26.1 — "(0, 16, 228, 15)" — partner display name
        _nameLabel = new Label
        {
            Name = "PartnerName",
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0f, NameLblY), // spec: §8.26.1
            Size = new Vector2(WinW, NameLblH), // spec: §8.26.1
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_nameLabel);

        // ── Level / stat labels (10,59,78,15) and (90,59,78,15) ──
        // spec: §8.26.1 — "(10,59,78,15), (90,59,78,15)"
        _levelLabel = new Label
        {
            Name = "PartnerLevel",
            Text = string.Empty,
            Position = new Vector2(10f, 59f), // spec: §8.26.1
            Size = new Vector2(78f, 15f), // spec: §8.26.1
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_levelLabel);

        // ── Relationship gauge 0 (15, 77, 196, 11) ──
        // spec: §8.26.1 — "(15, 77, 196, 11)" — gauge 0
        _gauge0 = new ProgressBar
        {
            Name = "Gauge0",
            MinValue = 0.0,
            MaxValue = 100.0,
            Value = 0.0,
            Position = new Vector2(GaugeX, Gauge0Y), // spec: §8.26.1
            Size = new Vector2(GaugeW, GaugeH), // spec: §8.26.1
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_gauge0);

        // ── Relationship gauge 1 (15, 107, 196, 11) ──
        // spec: §8.26.1 — "(15, 107, 196, 11)" — gauge 1
        _gauge1 = new ProgressBar
        {
            Name = "Gauge1",
            MinValue = 0.0,
            MaxValue = 100.0,
            Value = 0.0,
            Position = new Vector2(GaugeX, Gauge1Y), // spec: §8.26.1
            Size = new Vector2(GaugeW, GaugeH), // spec: §8.26.1
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_gauge1);

        // ── Command buttons A..D (actions 3..6) ──
        // spec: §8.26.1 — "(15,157,91,25) action 3; (124,157,91,25) action 4; ..."
        BuildCommandButton("CmdA", 15f, 157f, 3); // spec: §8.26.1 action 3, src (921,0)
        BuildCommandButton("CmdB", 124f, 157f, 4); // spec: §8.26.1 action 4, src (921,26)
        BuildCommandButton("CmdC", 15f, 215f, 5); // spec: §8.26.1 action 5, src (921,52)
        BuildCommandButton("CmdD", 124f, 215f, 6); // spec: §8.26.1 action 6, src (921,78)

        // ── Info / details button (110, 253, 110, 39), action 7 ──
        // spec: §8.26.1 — "(110,253,110,39), src (849,337), action 7"
        var infoBtn = new Button
        {
            Name = "InfoBtn",
            Text = text?.GetCaption(MsgHelpBtn, "[정보]") ?? "[정보]", // msg 16002 // spec: §8.26.5
            Position = new Vector2(110f, 253f), // spec: §8.26.1
            Size = new Vector2(110f, 39f), // spec: §8.26.1
            MouseFilter = MouseFilterEnum.Stop,
        };
        infoBtn.Pressed += () => OnAction(7); // action 7 = info/details // spec: §8.26.3
        AddChild(infoBtn);

        GD.Print("[HudPetPanel] Built — player-couple/pair-relation window slot 194 (§8.26). " +
                 "Fixed (80,200) 228×337. Partner name/level, 2 gauges, 4 cmd buttons, info button. " +
                 "Auto-shown by S2C 5/53 SmsgActorPairRelation (TODO world-campaign). " +
                 "NOT a hotkey window (§8.26.4). Command sends = TODO(world-campaign). " +
                 "spec: Docs/RE/specs/ui_system.md §8.26 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Helper factories
    // -------------------------------------------------------------------------

    private void BuildCornerButton(string nodeName, float x, float y, int action)
    {
        // spec: §8.26.1 — corner buttons 11×11, uitex 1 (shared HUD chrome)
        var btn = new Button
        {
            Name = nodeName,
            Text = action == 8 ? "×" : (action == 0 ? "−" : "?"),
            Position = new Vector2(x, y),
            Size = new Vector2(CornerBtnSize, CornerBtnSize), // spec: §8.26.1 — 11×11
            MouseFilter = MouseFilterEnum.Stop,
        };
        btn.AddThemeFontSizeOverride("font_size", 8);
        int captured = action;
        btn.Pressed += () => OnAction(captured);
        AddChild(btn);
    }

    private void BuildCommandButton(string nodeName, float x, float y, int action)
    {
        // spec: §8.26.1 — command buttons 91×25, uitex 3
        var btn = new Button
        {
            Name = nodeName,
            Text = $"[{action}]",
            Position = new Vector2(x, y),
            Size = new Vector2(CmdBtnW, CmdBtnH), // spec: §8.26.1 — 91×25
            MouseFilter = MouseFilterEnum.Stop,
        };
        int captured = action;
        btn.Pressed += () => OnAction(captured);
        AddChild(btn);
    }

    // -------------------------------------------------------------------------
    // Public API — called by the world NPC / S2C handlers
    // spec: Docs/RE/specs/ui_system.md §8.26.4 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    /// <summary>
    /// Populates and shows the panel from a partner actor push.
    /// Called when S2C SmsgActorPairRelation (5/53) fires the relation-set path.
    ///
    /// spec: Docs/RE/specs/ui_system.md §8.26.4 CODE-CONFIRMED —
    /// "auto-shown by S2C 5/53 on relation-set; populate from partnered actor record".
    /// TODO(world-campaign): call from the 5/53 packet handler.
    /// </summary>
    public void ShowPartner(string partnerName, int partnerLevel, float gauge0Value, float gauge1Value)
    {
        if (_nameLabel != null) _nameLabel.Text = partnerName;
        if (_levelLabel != null) _levelLabel.Text = $"Lv. {partnerLevel}";
        if (_gauge0 != null) _gauge0.Value = gauge0Value;
        if (_gauge1 != null) _gauge1.Value = gauge1Value;

        _open = true;
        Visible = true;

        GD.Print($"[HudPetPanel] ShowPartner: name=\"{partnerName}\" lv={partnerLevel} " +
                 $"g0={gauge0Value} g1={gauge1Value}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.26.4 CODE-CONFIRMED.");
    }

    /// <summary>
    /// Clears the panel and hides it (relation clear or partner death).
    ///
    /// spec: Docs/RE/specs/ui_system.md §8.26.4 CODE-CONFIRMED —
    /// "hidden on relation clear (5/53 clear path), confirm-click, partner death (SmsgCharDeath)".
    /// </summary>
    public void ClearPartner()
    {
        if (_nameLabel != null) _nameLabel.Text = string.Empty;
        if (_levelLabel != null) _levelLabel.Text = string.Empty;
        if (_gauge0 != null) _gauge0.Value = 0.0;
        if (_gauge1 != null) _gauge1.Value = 0.0;

        _open = false;
        Visible = false;

        GD.Print("[HudPetPanel] ClearPartner — hidden. " +
                 "spec: Docs/RE/specs/ui_system.md §8.26.4 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Action dispatcher
    // spec: Docs/RE/specs/ui_system.md §8.26.3 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private void OnAction(int actionId)
    {
        switch (actionId)
        {
            case 0:
                // Minimize (MED role). spec: §8.26.3 — "action 0 = minimize-family (no-op toggle path) — role MED"
                GD.Print("[HudPetPanel] action 0 — minimize (role MED). " +
                         "spec: Docs/RE/specs/ui_system.md §8.26.3 CODE-CONFIRMED.");
                break;
            case 2:
                // Help button (msg.xdb 16002). spec: §8.26.5 — "16002 = help-button caption (action 2)"
                GD.Print("[HudPetPanel] action 2 — help (msg 16002). " +
                         "spec: Docs/RE/specs/ui_system.md §8.26.3 CODE-CONFIRMED.");
                break;
            case 3:
            case 4:
            case 5:
            case 6:
                // Command buttons A..D — generic select/send helper (no dedicated pet C2S).
                // spec: §8.26.3 — "command buttons route through generic select/send helper (~100 panels)"
                // TODO(world-campaign): emit the generic select/send C2S (no dedicated packet).
                GD.Print($"[HudPetPanel] action {actionId} — command button {actionId - 2}. " +
                         "TODO(world-campaign): generic select/send helper. " +
                         "spec: Docs/RE/specs/ui_system.md §8.26.3 CODE-CONFIRMED.");
                break;
            case 7:
                // Info/details button. spec: §8.26.3 — "action 7 = info/details"
                GD.Print("[HudPetPanel] action 7 — info/details button. " +
                         "spec: Docs/RE/specs/ui_system.md §8.26.3 CODE-CONFIRMED.");
                break;
            case 8:
                // Close (title-bar close). spec: §8.26.3 — "action 8 = close"
                ClearPartner();
                GD.Print("[HudPetPanel] action 8 — close. " +
                         "spec: Docs/RE/specs/ui_system.md §8.26.3 CODE-CONFIRMED.");
                break;
            default:
                GD.Print($"[HudPetPanel] action {actionId} — unhandled. " +
                         "spec: Docs/RE/specs/ui_system.md §8.26.3 CODE-CONFIRMED.");
                break;
        }
    }
}