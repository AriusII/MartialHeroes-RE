// Ui/Hud/HudKeepNpcDialog.cs
//
// In-game NPC storage/keep dialog menu — `KeepNpcPanel` (master service slot 152).
//
// Role: the 5-option vertical dialog menu shown when the player clicks a KIND-9 storage/keep NPC.
// This is the NPC hub that routes to open-storage, keep-service options, NPC dialog, quest offer,
// or close. It has NO item grid and emits NOTHING on the wire itself.
//
// Geometry (CODE-CONFIRMED, panel-local origin):
//   Backdrop image      (0,   0, 167, 176)  uitex 4  src (0, 695)
//   Header strip image  (0, 176, 167,  63)  uitex 4  src (0, 871)
//   Top button (open-storage)  (37, 37, 90, 25)   uitex 4  action 1  sel 1
//   Service button (keep-service) (25, 69, 106, 40)   uitex 4  action 2  sel 2
//   Service button (NPC dialog)   (25,109, 106, 40)   uitex 2  action 0  sel 0
//   Service button (quest offer)  (25,149, 106, 40)   uitex 2  action 3  sel 3
//   Bottom button (close)         (25,189, 106, 40)   uitex 2  action 4  sel 4
//   spec: Docs/RE/specs/ui_system.md §8.27.1 CODE-CONFIRMED
//
// Atlas: uitex 4 = backdrop+top-button; uitex 2 = dialog/quest/close buttons.
//   spec: Docs/RE/specs/ui_system.md §8.27.2 CODE-CONFIRMED
//
// Selector mechanism (§8.27.3 CODE-CONFIRMED):
//   sel 0 → NPC dialog sub-view (TODO world-campaign)
//   sel 1 → KeepPanel (HudStorageWindow, slot 191) — WIRED
//   sel 2 → keep-service option list (slot 182) — TODO(world-campaign)
//   sel 3 → quest-offer list (slot 215) — TODO(world-campaign)
//   sel 4 → close
//   ESC (key 27) → same as close
//
// Outbound: nothing from this panel itself; open-storage → C2S 2/142 via HudStorageWindow.
//   spec: Docs/RE/specs/ui_system.md §8.27.3 — "Emits nothing on the wire from this panel".
//
// PASSIVE: zero game logic; intents → delegate calls or use-case stubs.

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
/// In-game NPC storage/keep dialog menu (KeepNpcPanel, master service slot 152).
///
/// <para>This is the 5-option vertical NPC interaction menu shown for KIND-9 storage NPCs. It
/// routes the player's selection to the appropriate target window via wired delegates.
/// It emits NO packets itself; it merely opens other windows.</para>
///
/// <para>PASSIVE: zero game logic. Call <see cref="Open"/> from the world NPC-click dispatcher
/// when the clicked NPC is KIND 9.</para>
///
/// spec: Docs/RE/specs/ui_system.md §8.27 CODE-CONFIRMED.
/// </summary>
public sealed partial class HudKeepNpcDialog : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // spec: Docs/RE/specs/ui_system.md §8.27.1 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    // Panel nominal size (backdrop covers 167×(176+63)=239)
    // spec: ui_system.md §8.27.1 — "167×176 backdrop at (0,0); 167×63 header strip at (0,176)"
    private const float PanelW = 167f; // spec: ui_system.md §8.27.1 CODE-CONFIRMED
    private const float PanelH = 239f; // spec: ui_system.md §8.27.1 CODE-CONFIRMED (176+63)

    // Top button (open-storage) geometry
    // spec: ui_system.md §8.27.1 — "(37, 37, 90, 25) uitex 4 action 1"
    private const float TopBtnX = 37f;
    private const float TopBtnY = 37f;
    private const float TopBtnW = 90f;
    private const float TopBtnH = 25f;

    // Service buttons geometry: x=25, w=106, h=40
    // spec: ui_system.md §8.27.1 — "four 106×40 service buttons at x=25, y = {69, 109, 149, 189}"
    private const float SvcBtnX = 25f;
    private const float SvcBtnW = 106f;
    private const float SvcBtnH = 40f;
    private static readonly float[] SvcBtnY = { 69f, 109f, 149f, 189f };

    // uitex integer ids
    // spec: ui_system.md §8.27.2 CODE-CONFIRMED
    private const int BackdropTexId = 4; // uitex 4: backdrop + top-button + keep-service btn
    private const int DialogTexId = 2; // uitex 2: dialog/quest/close buttons

    // -------------------------------------------------------------------------
    // Routing delegates (wired by HudMaster)
    // spec: ui_system.md §8.27.3 — selector 1 → KeepPanel (§8.32)
    // -------------------------------------------------------------------------

    private Action? _onOpenStorage; // sel 1 → HudStorageWindow.Open()
    private Action? _onVendor; // sel 0 → NPC dialog (TODO world-campaign)

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private bool _open;
    private uint _activeNpcId; // stored by the NPC dispatcher on open (debugger-pending exact field)

    // -------------------------------------------------------------------------
    // Wiring (called by HudMaster before Build)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Wires the selector routing delegates.
    ///
    /// spec: Docs/RE/specs/ui_system.md §8.27.3 — selector mechanism.
    /// </summary>
    /// <param name="onOpenStorage">Invoked on sel 1 (KIND-9 open storage → KeepPanel slot 191).</param>
    /// <param name="onVendor">Invoked on sel 0 (NPC dialog); may be null → TODO(world-campaign).</param>
    public void Wire(Action? onOpenStorage, Action? onVendor = null)
    {
        _onOpenStorage = onOpenStorage;
        _onVendor = onVendor;
    }

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    /// Geometry pass: builds the 5-button NPC dialog menu.
    ///
    /// spec: Docs/RE/specs/ui_system.md §8.27.1 CODE-CONFIRMED.
    /// </summary>
    public void Build(HudAtlasLibrary atlas)
    {
        Name = "HudKeepNpcDialog";

        // Port choice: centred around the NPC position; exact abs origin debugger-pending.
        // Using a fixed left-margin position as a safe default.
        // spec: ui_system.md §8.27 — "master-window-placed HUD child; absolute origin debugger-pending"
        Size = new Vector2(PanelW, PanelH);
        Position = new Vector2(420f, 240f); // port choice: centred ~screen mid-left
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        // Backdrop panel (uitex 4, src 0,695 — 167×176)
        // spec: ui_system.md §8.27.1 — "(0,0,167,176) uitex 4 src (0,695)"
        var bd = new Panel { Name = "Backdrop" };
        bd.Position = Vector2.Zero;
        bd.Size = new Vector2(PanelW, PanelH);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.08f, 0.07f, 0.05f, 0.92f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.5f, 0.4f, 0.15f, 0.9f);
        bd.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(bd);

        // Top button — open storage (sel 1)
        // spec: ui_system.md §8.27.1 — "(37,37,90,25) uitex 4 src (642,836) action 1 sel 1"
        var topBtn = new Button
        {
            Name = "OpenStorageBtn",
            Text = "Open Storage",
            Position = new Vector2(TopBtnX, TopBtnY),
            Size = new Vector2(TopBtnW, TopBtnH),
            MouseFilter = MouseFilterEnum.Stop,
        };
        topBtn.Pressed += () => OnSelector(1); // action 1, sel 1 → open storage
        AddChild(topBtn);

        // Service button 0: NPC dialog (action 0, sel 0) — uitex 2
        // spec: ui_system.md §8.27.1 — "(25,109,106,40) uitex 2 action 0 sel 0"
        var btnDialog = new Button
        {
            Name = "BtnDialog",
            Text = "Talk",
            Position = new Vector2(SvcBtnX, SvcBtnY[1]), // y=109
            Size = new Vector2(SvcBtnW, SvcBtnH),
            MouseFilter = MouseFilterEnum.Stop,
        };
        btnDialog.Pressed += () => OnSelector(0);
        AddChild(btnDialog);

        // Service button 1: keep-service list (action 2, sel 2) — uitex 4
        // spec: ui_system.md §8.27.1 — "(25,69,106,40) uitex 4 action 2 sel 2"
        var btnKeep = new Button
        {
            Name = "BtnKeepService",
            Text = "Service",
            Position = new Vector2(SvcBtnX, SvcBtnY[0]), // y=69
            Size = new Vector2(SvcBtnW, SvcBtnH),
            MouseFilter = MouseFilterEnum.Stop,
        };
        btnKeep.Pressed += () => OnSelector(2);
        AddChild(btnKeep);

        // Service button 2: quest offer (action 3, sel 3) — uitex 2
        // spec: ui_system.md §8.27.1 — "(25,149,106,40) uitex 2 action 3 sel 3"
        var btnQuest = new Button
        {
            Name = "BtnQuest",
            Text = "Quest",
            Position = new Vector2(SvcBtnX, SvcBtnY[2]), // y=149
            Size = new Vector2(SvcBtnW, SvcBtnH),
            MouseFilter = MouseFilterEnum.Stop,
        };
        btnQuest.Pressed += () => OnSelector(3);
        AddChild(btnQuest);

        // Bottom button: close (action 4, sel 4) — uitex 2
        // spec: ui_system.md §8.27.1 — "(25,189,106,40) uitex 2 action 4 sel 4"
        var btnClose = new Button
        {
            Name = "BtnClose",
            Text = "Close",
            Position = new Vector2(SvcBtnX, SvcBtnY[3]), // y=189
            Size = new Vector2(SvcBtnW, SvcBtnH),
            MouseFilter = MouseFilterEnum.Stop,
        };
        btnClose.Pressed += () => OnSelector(4);
        AddChild(btnClose);

        GD.Print("[HudKeepNpcDialog] Built — KeepNpcPanel slot 152. " +
                 "5-option NPC dialog menu: sel0=dialog sel1=open-storage(→slot 191) " +
                 "sel2=keep-service(TODO world-campaign) sel3=quest(TODO world-campaign) sel4=close. " +
                 "Atlas: uitex 4 (backdrop/top-btn) + uitex 2 (dialog/quest/close) VFS-pending. " +
                 "Emits NO wire packet from this panel. spec: Docs/RE/specs/ui_system.md §8.27 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Open / close
    // -------------------------------------------------------------------------

    /// <summary>
    /// Opens the KeepNpcPanel for the given NPC id.
    /// Called by the world NPC-click dispatcher when NPC is KIND 9.
    /// spec: Docs/RE/specs/ui_system.md §8.27 — "KIND 9 → open KeepNpcPanel".
    /// </summary>
    public void Open(uint npcId = 0)
    {
        _activeNpcId = npcId;
        _open = true;
        Visible = true;
        GD.Print($"[HudKeepNpcDialog] Opened for NPC {npcId}. spec: Docs/RE/specs/ui_system.md §8.27.");
    }

    private void Close()
    {
        _open = false;
        Visible = false;
        GD.Print("[HudKeepNpcDialog] Closed. spec: Docs/RE/specs/ui_system.md §8.27.3 sel 4.");
    }

    // -------------------------------------------------------------------------
    // Selector dispatch
    // -------------------------------------------------------------------------

    private void OnSelector(int sel)
    {
        // spec: ui_system.md §8.27.3 CODE-CONFIRMED — latches selector field (panel+180),
        // then dispatches per the selector value.
        switch (sel)
        {
            case 0:
                // NPC dialog-text path (KIND-9 sub-view)
                // spec: ui_system.md §8.27.3 — "sel 0: NPC dialog only if KIND==9"
                if (_onVendor is not null)
                    _onVendor();
                else
                    GD.Print("[HudKeepNpcDialog] sel 0 = NPC dialog: TODO(world-campaign). " +
                             "spec: Docs/RE/specs/ui_system.md §8.27.3 sel 0.");
                Close();
                break;

            case 1:
                // Open storage — bag-count-gated (KIND-9 limit 50), then → KeepPanel slot 191
                // spec: ui_system.md §8.27.3 — "sel 1: OPEN STORAGE → KeepPanel (§8.32)"
                // spec: ui_system.md §8.27.3 — "bag-count gate (KIND-9 limit 50)"
                if (_onOpenStorage is not null)
                    _onOpenStorage();
                else
                    GD.Print("[HudKeepNpcDialog] sel 1 = open storage: no delegate wired. " +
                             "spec: Docs/RE/specs/ui_system.md §8.27.3 sel 1.");
                Close();
                break;

            case 2:
                // Keep-service option list (slot 182) — 9-entry service menu
                // spec: ui_system.md §8.27.3 — "sel 2: keep-service option list (slot 182)"
                GD.Print("[HudKeepNpcDialog] sel 2 = keep-service list (slot 182): TODO(world-campaign). " +
                         "spec: Docs/RE/specs/ui_system.md §8.27.3 sel 2.");
                Close();
                break;

            case 3:
                // Quest-offer list (slot 215)
                // spec: ui_system.md §8.27.3 — "sel 3: quest-offer list (slot 215)"
                GD.Print("[HudKeepNpcDialog] sel 3 = quest offer (slot 215): TODO(world-campaign). " +
                         "spec: Docs/RE/specs/ui_system.md §8.27.3 sel 3.");
                Close();
                break;

            case 4:
                // Close
                // spec: ui_system.md §8.27.3 — "sel 4: CLOSE"
                Close();
                break;

            default:
                // Selector ≥ 5 = no-op per spec
                // spec: ui_system.md §8.27.3 — "≥ 5: ignored (no-op)"
                GD.Print($"[HudKeepNpcDialog] sel {sel} ≥ 5: no-op. " +
                         "spec: Docs/RE/specs/ui_system.md §8.27.3.");
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Input (ESC closes)
    // -------------------------------------------------------------------------

    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            // spec: ui_system.md §8.27.3 — "ESC (key 27) when shown: same CLOSE path"
            Close();
            GetViewport().SetInputAsHandled();
        }
    }
}