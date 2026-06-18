// Ui/Hud/HudTradeWindow.cs
//
// In-game Trade escrow window — `KeepPanel` / `TradeKeepWindow`.
//
// Placement (CODE-CONFIRMED):
//   W = 318, H = 732, Y = 0; X = inventory panel X (same right-dock column).
//   spec: Docs/RE/specs/ui_system.md §8.13 CODE-CONFIRMED.
//   spec: Docs/RE/specs/ui_hud_layout.md §3.3 / §5.3 CODE-CONFIRMED.
//
// Atlas (CODE-CONFIRMED):
//   uitex 4 = tradekeepwindow.dds — main frame, side/tab buttons, money strip, buttons.
//   uitex 2 = inventwindow.dds — bottom strip, big lock/commit button (action 260).
//   spec: Docs/RE/specs/ui_system.md §8.13 CODE-CONFIRMED.
//
// Grid — ONE 60-cell grid (10 rows × 6 cols), TOGGLED between my-side and their-side:
//   Cell size = 38×38 px, stride +38, origin (45, 162).
//   Cell action ids = 200..259.
//   spec: Docs/RE/specs/ui_system.md §8.13 CODE-CONFIRMED.
//
// Toggle: action 261 (side A = my offer) / action 262 (side B = their offer).
// Add-money button (action 263, msg 2213); set-money button (action 264, msg 2214).
// Lock/commit button (action 260).
//   spec: Docs/RE/specs/ui_system.md §8.13 CODE-CONFIRMED.
//
// Open trigger: S2C 4/23 SmsgUserTradeRequestResult (phase byte = 3 "accepted").
//   No hotkey — opcode-initiated only.
// Inbound slots: S2C 4/24 SmsgUserTradeSlotUpdate, S2C 4/25 SmsgUserTradeFullResponse.
//   Stub with TODO (no hub channel yet).
// Outbound: C2S 2/23 CmsgTradeRequest, 2/24 CmsgTradeSlotAdd, 2/25 CmsgTradeConfirm.
//   Stub with TODO (use-case method pending).
//   spec: Docs/RE/specs/ui_system.md §8.13 CODE-CONFIRMED.
//
// PASSIVE: zero game logic; no local item mutation; intents → use-case calls (stubbed).

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
/// In-game Trade escrow window (KeepPanel / TradeKeepWindow). 318×732 right-docked.
///
/// <para>PASSIVE: renders the 60-cell trade grid toggled between my-side and their-side.
/// All item-move intents and commit intents fire use-case calls (stubbed pending world-campaign).
/// Inbound slot updates (S2C 4/24 / 4/25) are stubbed with TODO.</para>
///
/// spec: Docs/RE/specs/ui_system.md §8.13 CODE-CONFIRMED.
/// spec: Docs/RE/specs/ui_hud_layout.md §3.3 / §5.3 CODE-CONFIRMED.
/// </summary>
public sealed partial class HudTradeWindow : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // spec: Docs/RE/specs/ui_system.md §8.13 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private const float TradeW = 318f; // spec: ui_system.md §8.13 — W=318
    private const float TradeH = 732f; // spec: ui_system.md §8.13 — H=732

    // Grid constants (ONE 60-cell grid, 10×6)
    // spec: ui_system.md §8.13 — "10 × 6 = 60 cells, 38×38, +38 stride, origin (45,162)"
    private const int GridCols = 6; // spec: ui_system.md §8.13
    private const int GridRows = 10; // spec: ui_system.md §8.13
    private const int GridCellCount = GridCols * GridRows; // = 60
    private const int CellSide = 38; // spec: ui_system.md §8.13 — 38×38
    private const float GridOriginX = 45f; // spec: ui_system.md §8.13 — origin (45,162)
    private const float GridOriginY = 162f; // spec: ui_system.md §8.13

    // Cell action ids
    // spec: ui_system.md §8.13 — "cell action ids 200..259"
    private const int CellActionBase = 200; // spec: ui_system.md §8.13

    // Atlas ids
    // spec: ui_system.md §8.13 — uitex 4 = tradekeepwindow.dds; uitex 2 = inventwindow.dds
    private const int MainTexId = 4; // spec: ui_system.md §8.13
    private const int BottomTexId = 2; // spec: ui_system.md §8.13

    // Button label msg ids
    // spec: ui_system.md §8.13 — msg 2213 = add-money; msg 2214 = set-money
    private const int MsgAddMoney = 2213; // spec: ui_system.md §8.13
    private const int MsgSetMoney = 2214; // spec: ui_system.md §8.13

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private bool _open;
    private int _activeSide; // 0 = my-offer, 1 = their-offer
    private readonly Panel[] _gridCells = new Panel[GridCellCount];

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    /// Geometry pass: builds the 318×732 right-anchored trade window.
    ///
    /// spec: Docs/RE/specs/ui_system.md §8.13 CODE-CONFIRMED.
    /// </summary>
    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudTradeWindow";

        // Right-anchored, off-screen until opened by the trade state machine
        // spec: ui_system.md §8.13 — "X = inventory panel X (same right-dock column); off-screen default"
        AnchorLeft = 1f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 0f;
        OffsetLeft = TradeW;
        OffsetTop = 0f;
        OffsetRight = TradeW + TradeW;
        OffsetBottom = TradeH;

        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        // Backdrop
        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.07f, 0.07f, 0.13f, 0.93f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.3f, 0.5f, 0.35f, 0.85f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        // Main frame from uitex 4 (tradekeepwindow.dds), dst (0,85,318,625) src (317,0)
        // spec: ui_system.md §8.13 CODE-CONFIRMED
        Texture2D? mainTex = atlas.GetById(MainTexId);
        if (mainTex is null)
            GD.PrintErr("[HudTradeWindow] tradekeepwindow.dds (uitex 4) unavailable (VFS offline). " +
                        "spec: Docs/RE/specs/ui_system.md §8.13.");

        if (mainTex is not null)
        {
            AtlasTexture? frameSlice = atlas.SliceById(MainTexId, 317, 0, 318, 625);
            if (frameSlice is not null)
            {
                var frameImg = new TextureRect
                {
                    Name = "MainFrame",
                    Texture = frameSlice,
                    Position = new Vector2(0f, 85f),
                    Size = new Vector2(318f, 625f),
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                AddChild(frameImg);
            }
        }

        // Bottom strip (uitex 2, dst 0,36,318,50, src 0,683)
        // spec: ui_system.md §8.13 CODE-CONFIRMED
        if (atlas.GetById(BottomTexId) is not null)
        {
            AtlasTexture? footerSlice = atlas.SliceById(BottomTexId, 0, 683, 318, 50);
            if (footerSlice is not null)
            {
                var footerImg = new TextureRect
                {
                    Name = "FooterStrip",
                    Texture = footerSlice,
                    Position = new Vector2(0f, 36f),
                    Size = new Vector2(318f, 50f),
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                AddChild(footerImg);
            }
        }

        // Side toggle buttons A/B (actions 261/262)
        // spec: ui_system.md §8.13 — "Side/tab button A: dst(25,105,65,20) action 261; B: dst(90,105,65,20) action 262"
        var sideA = new Button
        {
            Name = "SideA",
            Text = "My offer",
            Position = new Vector2(25f, 105f),
            Size = new Vector2(65f, 20f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        sideA.Pressed += () => SetActiveSide(0); // action 261
        AddChild(sideA);

        var sideB = new Button
        {
            Name = "SideB",
            Text = "Their offer",
            Position = new Vector2(90f, 105f),
            Size = new Vector2(65f, 20f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        sideB.Pressed += () => SetActiveSide(1); // action 262
        AddChild(sideB);

        // Money amount label (dst 51,598,128,15 — live text)
        // spec: ui_system.md §8.13 CODE-CONFIRMED
        var moneyLbl = new Label
        {
            Name = "MoneyLabel",
            Text = "0",
            Position = new Vector2(51f, 598f),
            Size = new Vector2(128f, 15f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(moneyLbl);

        // Add-money button (action 263, msg 2213)
        // spec: ui_system.md §8.13 — "Add-money button dst(183,592,53,22) action 263 msg 2213 (cyan)"
        string addMoneyCaption = text.GetCaption(MsgAddMoney, $"[{MsgAddMoney}]");
        var addMoneyBtn = new Button
        {
            Name = "AddMoneyBtn",
            Text = addMoneyCaption,
            Position = new Vector2(183f, 592f),
            Size = new Vector2(53f, 22f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        addMoneyBtn.Pressed += () => OnAddMoney(); // action 263
        AddChild(addMoneyBtn);

        // Set-money button (action 264, msg 2214)
        // spec: ui_system.md §8.13 — "Remove/set-money button dst(238,592,53,22) action 264 msg 2214 (red)"
        string setMoneyCaption = text.GetCaption(MsgSetMoney, $"[{MsgSetMoney}]");
        var setMoneyBtn = new Button
        {
            Name = "SetMoneyBtn",
            Text = setMoneyCaption,
            Position = new Vector2(238f, 592f),
            Size = new Vector2(53f, 22f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        setMoneyBtn.Pressed += () => OnSetMoney(); // action 264
        AddChild(setMoneyBtn);

        // Big LOCK/READY button (action 260) — uitex 2 src (301,947)
        // spec: ui_system.md §8.13 — "Big LOCK/READY button dst(259,655,59,77) action 260"
        var lockBtn = new Button
        {
            Name = "LockBtn",
            Text = "LOCK",
            Position = new Vector2(259f, 655f),
            Size = new Vector2(59f, 77f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        lockBtn.Pressed += () => OnLockCommit(); // action 260
        AddChild(lockBtn);

        // 60-cell trade grid (10×6, 38×38, origin 45,162, action 200..259)
        // spec: ui_system.md §8.13 CODE-CONFIRMED — ONE grid toggled between sides
        BuildTradeGrid(atlas);

        GD.Print("[HudTradeWindow] Built — 318×732 right-anchored KeepPanel/TradeKeepWindow. " +
                 "ONE 60-cell grid (10×6, 38×38, origin 45,162, actions 200..259), toggled my/their. " +
                 "Inbound S2C 4/24 + 4/25: TODO(world-campaign). " +
                 "Outbound C2S 2/23/24/25: TODO(world-campaign). " +
                 "Open trigger: S2C 4/23 phase=3 (no hotkey). " +
                 "spec: Docs/RE/specs/ui_system.md §8.13 CODE-CONFIRMED.");
    }

    private void BuildTradeGrid(HudAtlasLibrary atlas)
    {
        // spec: ui_system.md §8.13 — "10×6=60 cells, 38×38, +38 stride, origin (45,162), action 200..259"
        for (int row = 0; row < GridRows; row++)
        {
            for (int col = 0; col < GridCols; col++)
            {
                int idx = row * GridCols + col;
                float x = GridOriginX + col * CellSide;
                float y = GridOriginY + row * CellSide;

                var cell = new Panel
                {
                    Name = $"TradeCell{CellActionBase + idx}",
                    Position = new Vector2(x, y),
                    Size = new Vector2(CellSide, CellSide),
                    MouseFilter = MouseFilterEnum.Stop,
                };
                var cs = new StyleBoxFlat();
                cs.BgColor = new Color(0.06f, 0.08f, 0.07f, 0.75f);
                cs.SetBorderWidthAll(1);
                cs.BorderColor = new Color(0.3f, 0.45f, 0.3f, 0.65f);
                cell.AddThemeStyleboxOverride("panel", cs);
                AddChild(cell);
                _gridCells[idx] = cell;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Side toggle
    // -------------------------------------------------------------------------

    private void SetActiveSide(int side)
    {
        // spec: ui_system.md §8.13 — "side toggle (action 261/262) swaps tab button highlight frames
        //   and re-renders the active side"
        _activeSide = side;
        GD.Print($"[HudTradeWindow] Side toggle → side={side} (0=my-offer, 1=their-offer). " +
                 "TODO(world-campaign): repopulate cells from trade state. " +
                 "spec: Docs/RE/specs/ui_system.md §8.13.");
    }

    // -------------------------------------------------------------------------
    // Action handlers
    // -------------------------------------------------------------------------

    private void OnAddMoney()
    {
        // spec: ui_system.md §8.13 — "action 263: add money (gated: own money ≥ 1,000,000 else msg 45023;
        //   opens amount-entry modal caption msg 2215)"
        // TODO(world-campaign): IApplicationUseCases.TradeAddMoney → C2S 2/24 CmsgTradeSlotAdd (money category)
        GD.Print("[HudTradeWindow] action 263 = add-money → TODO(world-campaign): C2S 2/24. " +
                 "spec: Docs/RE/specs/ui_system.md §8.13.");
    }

    private void OnSetMoney()
    {
        // spec: ui_system.md §8.13 — "action 264: remove/set money (gated: staged > 0 else msg 45023;
        //   amount-entry caption msg 2216)"
        // TODO(world-campaign): IApplicationUseCases.TradeSetMoney → C2S 2/24
        GD.Print("[HudTradeWindow] action 264 = set-money → TODO(world-campaign): C2S 2/24. " +
                 "spec: Docs/RE/specs/ui_system.md §8.13.");
    }

    private void OnLockCommit()
    {
        // spec: ui_system.md §8.13 — "action 260: lock/ready/commit → C2S 2/25 CmsgTradeConfirm"
        // TODO(world-campaign): IApplicationUseCases.TradeConfirm → C2S 2/25 CmsgTradeConfirm
        GD.Print("[HudTradeWindow] action 260 = lock/commit → TODO(world-campaign): C2S 2/25 CmsgTradeConfirm. " +
                 "spec: Docs/RE/specs/ui_system.md §8.13.");
    }

    // -------------------------------------------------------------------------
    // Toggle (open by trade state machine, not a hotkey)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Shows or hides the trade window.
    /// Normally opened by S2C 4/23 phase=3 (not a keyboard hotkey).
    /// spec: Docs/RE/specs/ui_system.md §8.13 — "no hotkey; opened by trade state machine S2C 4/23".
    /// </summary>
    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;

        if (_open)
        {
            OffsetLeft = -TradeW;
            OffsetRight = 0f;
        }
        else
        {
            OffsetLeft = TradeW;
            OffsetRight = TradeW + TradeW;
        }

        Visible = _open;
    }

    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey { Keycode: Key.Escape, Pressed: true })
        {
            // spec: ui_system.md §8.13 — "key 27 (ESC) closes (visible-gate teardown)"
            Toggle(false);
            GetViewport().SetInputAsHandled();
        }
    }
}