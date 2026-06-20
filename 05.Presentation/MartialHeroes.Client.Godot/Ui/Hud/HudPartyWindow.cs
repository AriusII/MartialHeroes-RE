// Ui/Hud/HudPartyWindow.cs
//
// In-game Party window — `PartyPanel` (right-dock, 318×732).
//
// Placement (CODE-CONFIRMED):
//   X = screenWidth + 318, Y = 0, W = 318, H = 732 — standard right-dock column.
//   Revealed: X = screenWidth − 318.
//   spec: Docs/RE/specs/ui_system.md §8.12 CODE-CONFIRMED.
//   spec: Docs/RE/specs/ui_hud_layout.md §3.3 / §5.3 CODE-CONFIRMED.
//
// Atlas (CODE-CONFIRMED):
//   uitex 8 = skillwindow.dds — main chrome + all member-slot widgets + PartyReqPanel.
//   uitex 2 = inventwindow.dds — 318×50 footer strip + 59×77 Close button.
//   spec: Docs/RE/specs/ui_system.md §8.12 CODE-CONFIRMED.
//
// 8 member slots (54px stride, baseline y=159):
//   Per-slot: name label, level label, class label (msg 22001–22005), 3 bars HP/MP/EXP (124×5).
//   Bars fill = min(124, 124·cur/max) px wide.
//   Per-slot select button action 0..7; per-slot context button action 16..23.
//   spec: Docs/RE/specs/ui_system.md §8.12 CODE-CONFIRMED.
//
// Action buttons A..G (actions 8..14):
//   8=Invite (C2S 2/35), 9/10/11=leader ops (C2S 2/36/37), 12=expel-stage, 13=MiniParty toggle,
//   14=Close; 1019..1026 = hotkey heal party member k.
//   spec: Docs/RE/specs/ui_system.md §8.12 CODE-CONFIRMED.
//
// Inbound populate: S2C 5/21 + S2C 5/38 — no hub channel yet.
//   Stub member rows empty: TODO(world-campaign).
//
// Toggle hotkey: key-table / capture-pending.
//   TODO(spec): toggle hotkey.
//
// PASSIVE: zero game logic; emits invite/leave/kick as use-case calls (TODO world-campaign stubs).

using Godot;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
///     In-game Party window (PartyPanel). 318×732 right-docked.
///     <para>
///         PASSIVE: renders member rows from Application events; emits party intents as use-case calls.
///         Member rows are stubbed empty pending the world-campaign party feed.
///     </para>
///     spec: Docs/RE/specs/ui_system.md §8.12 CODE-CONFIRMED.
///     spec: Docs/RE/specs/ui_hud_layout.md §3.3 / §5.3 CODE-CONFIRMED.
/// </summary>
public sealed partial class HudPartyWindow : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited placement constants
    // spec: Docs/RE/specs/ui_system.md §8.12 CODE-CONFIRMED
    // spec: Docs/RE/specs/ui_hud_layout.md §5.3 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private const float PartyW = 318f; // spec: ui_system.md §8.12 — W=318
    private const float PartyH = 732f; // spec: ui_system.md §8.12 — H=732

    // Member slot constants (CODE-CONFIRMED)
    // spec: Docs/RE/specs/ui_system.md §8.12 — "8 slots, 54px stride, baseline y=159"
    private const int MemberCount = 8; // spec: ui_system.md §8.12 CODE-CONFIRMED
    private const float SlotBaseY = 159f; // spec: ui_system.md §8.12 — baseline y=159
    private const float SlotStrideY = 54f; // spec: ui_system.md §8.12 — stride 54px

    // Bar fill constants
    // spec: ui_system.md §8.12 — "fill = min(124, 124·cur/max) px wide", atlas (136,781/786/791)
    private const float BarMaxW = 124f; // spec: ui_system.md §8.12 CODE-CONFIRMED
    private const float BarH = 5f; // spec: ui_system.md §8.12 — 124×5 bars

    // Class name msg.xdb ids (0..4 → msg 22001..22005)
    // spec: Docs/RE/specs/ui_system.md §8.12 CODE-CONFIRMED
    private const int ClassMsgBase = 22001; // spec: ui_system.md §8.12 — msg 22001–22005

    // Atlas ids
    // spec: ui_system.md §8.12 — uitex 8 = skillwindow.dds (primary), uitex 2 = inventwindow.dds
    private const int MainTexId = 8; // spec: ui_system.md §8.12
    private const int FooterTexId = 2; // spec: ui_system.md §8.12
    private readonly Label[] _classLabels = new Label[MemberCount];
    private readonly ProgressBar[] _expBars = new ProgressBar[MemberCount];

    // Per-slot bar controls (HP/MP/EXP) for live update
    private readonly ProgressBar[] _hpBars = new ProgressBar[MemberCount];
    private readonly Label[] _levelLabels = new Label[MemberCount];
    private readonly ProgressBar[] _mpBars = new ProgressBar[MemberCount];
    private readonly Label[] _nameLabels = new Label[MemberCount];

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private bool _open;

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Geometry pass: builds the 318×732 right-anchored party window.
    ///     spec: Docs/RE/specs/ui_system.md §8.12 CODE-CONFIRMED.
    /// </summary>
    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudPartyWindow";

        // Right-anchored, off-screen until toggled
        // spec: ui_system.md §8.12 — X=screenWidth+318, Y=0 (off-screen)
        AnchorLeft = 1f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 0f;
        OffsetLeft = PartyW;
        OffsetTop = 0f;
        OffsetRight = PartyW + PartyW;
        OffsetBottom = PartyH;

        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        // Backdrop
        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.07f, 0.07f, 0.13f, 0.93f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.4f, 0.35f, 0.2f, 0.85f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        // Main backdrop image (uitex 8, dst 0,85,318,627, src 0,0)
        // spec: ui_system.md §8.12 — "Main backdrop image 0,85,318,627 src(0,0) atlas 8"
        var mainTex = atlas.GetById(MainTexId);
        if (mainTex is not null)
        {
            var bdSlice = atlas.SliceById(MainTexId, 0, 0, 318, 627);
            if (bdSlice is not null)
            {
                var mainBd = new TextureRect
                {
                    Name = "MainBackdrop",
                    Texture = bdSlice,
                    Position = new Vector2(0f, 85f),
                    Size = new Vector2(318f, 627f),
                    MouseFilter = MouseFilterEnum.Ignore
                };
                AddChild(mainBd);
            }
        }
        else
        {
            GD.PrintErr("[HudPartyWindow] skillwindow.dds (uitex 8) unavailable (VFS offline). " +
                        "spec: Docs/RE/specs/ui_system.md §8.12.");
        }

        // Footer strip (uitex 2, dst 0,36,318,50, src 0,683)
        // spec: ui_system.md §8.12 — "Footer strip 0,36,318,50 src(0,683) atlas 2"
        var footerTex = atlas.GetById(FooterTexId);
        if (footerTex is not null)
        {
            var footerSlice = atlas.SliceById(FooterTexId, 0, 683, 318, 50);
            if (footerSlice is not null)
            {
                var footerImg = new TextureRect
                {
                    Name = "FooterStrip",
                    Texture = footerSlice,
                    Position = new Vector2(0f, 36f),
                    Size = new Vector2(318f, 50f),
                    MouseFilter = MouseFilterEnum.Ignore
                };
                AddChild(footerImg);
            }
        }

        // 8 member slots
        // spec: ui_system.md §8.12 — "8 slots, baseline y=159, stride 54px"
        for (var k = 0; k < MemberCount; k++)
        {
            var baseY = SlotBaseY + k * SlotStrideY; // spec: ui_system.md §8.12
            BuildMemberSlot(atlas, text, k, baseY);
        }

        // Action buttons A–G
        BuildActionButtons(atlas, text);

        // Close button G (uitex 2, dst 259,655,59,77, src 301,947, action 14)
        // spec: ui_system.md §8.12 CODE-CONFIRMED
        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Text = "X",
            Position = new Vector2(259f, 655f),
            Size = new Vector2(59f, 77f),
            MouseFilter = MouseFilterEnum.Stop
        };
        closeBtn.Pressed += () => Toggle(false);
        AddChild(closeBtn);

        GD.Print("[HudPartyWindow] Built — 318×732 right-anchored PartyPanel. " +
                 "8 member slots (stride 54, baseline y=159), 3 bars each (HP/MP/EXP, 124×5). " +
                 "Member rows stub-empty (TODO world-campaign: S2C 5/21 + 5/38 populate). " +
                 "spec: Docs/RE/specs/ui_system.md §8.12 CODE-CONFIRMED.");
    }

    private void BuildMemberSlot(HudAtlasLibrary atlas, HudTextLibrary text, int k, float baseY)
    {
        // Row select button (3-state), dst (10, y−30, 300, 54), src (359,667), action k
        // spec: ui_system.md §8.12 CODE-CONFIRMED
        var rowBtn = new Button
        {
            Name = $"MemberRowBtn{k}",
            Position = new Vector2(10f, baseY - 30f),
            Size = new Vector2(300f, 54f),
            Flat = true,
            MouseFilter = MouseFilterEnum.Stop
        };
        AddChild(rowBtn);

        // Name label — dst (25, y−15), runtime text
        // spec: ui_system.md §8.12 CODE-CONFIRMED
        var nameLbl = new Label
        {
            Name = $"MemberName{k}",
            Text = "",
            Position = new Vector2(25f, baseY - 15f),
            Size = new Vector2(130f, 14f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(nameLbl);
        _nameLabels[k] = nameLbl;

        // Level label — dst (55, y), runtime text
        // spec: ui_system.md §8.12 CODE-CONFIRMED
        var levelLbl = new Label
        {
            Name = $"MemberLevel{k}",
            Text = "",
            Position = new Vector2(55f, baseY),
            Size = new Vector2(50f, 14f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(levelLbl);
        _levelLabels[k] = levelLbl;

        // Class label — dst (25, y), msg 22001–22005 for class id 0..4
        // spec: ui_system.md §8.12 — "class name msg 22001–22005 (class id 0..4)"
        var classLbl = new Label
        {
            Name = $"MemberClass{k}",
            Text = "",
            Position = new Vector2(25f, baseY),
            Size = new Vector2(30f, 14f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(classLbl);
        _classLabels[k] = classLbl;

        // HP bar — dst (165, y−16, 124, 5), src (136,781), atlas 8
        // spec: ui_system.md §8.12 CODE-CONFIRMED — fill = min(124, 124·cur/max)
        var hpBar = new ProgressBar
        {
            Name = $"HP{k}",
            Position = new Vector2(165f, baseY - 16f),
            Size = new Vector2(BarMaxW, BarH),
            MaxValue = 1.0,
            Value = 0.0,
            ShowPercentage = false,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(hpBar);
        _hpBars[k] = hpBar;

        // MP bar — dst (165, y−8, 124, 5), src (136,786), atlas 8
        // spec: ui_system.md §8.12 CODE-CONFIRMED
        var mpBar = new ProgressBar
        {
            Name = $"MP{k}",
            Position = new Vector2(165f, baseY - 8f),
            Size = new Vector2(BarMaxW, BarH),
            MaxValue = 1.0,
            Value = 0.0,
            ShowPercentage = false,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(mpBar);
        _mpBars[k] = mpBar;

        // EXP bar — dst (165, y, 124, 5), src (136,791), atlas 8
        // spec: ui_system.md §8.12 CODE-CONFIRMED
        var expBar = new ProgressBar
        {
            Name = $"EXP{k}",
            Position = new Vector2(165f, baseY),
            Size = new Vector2(BarMaxW, BarH),
            MaxValue = 1.0,
            Value = 0.0,
            ShowPercentage = false,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(expBar);
        _expBars[k] = expBar;
    }

    private void BuildActionButtons(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        // Button layout (CODE-CONFIRMED from §8.12 table):
        // A: dst(8,600,90,25) action 8 — Invite (C2S 2/35 CmsgPartyInvite)
        // B: dst(109,600,90,25) action 11 — member op (C2S 2/37 CmsgPartyLeaderOp)
        // C: dst(210,600,90,25) action 9 — leader op (C2S 2/36)
        // D: dst(8,642,90,25)  action 10 — leader op on selected (C2S 2/36)
        // E: dst(109,642,90,25) action 12 — leader transfer / expel-stage
        // F: dst(233,97,74,22)  action 13 — toggle MiniParty mirror (local UI)
        // G: 259,655,59,77 action 14 — Close (handled separately above)
        // spec: Docs/RE/specs/ui_system.md §8.12 CODE-CONFIRMED

        (float x, float y, float w, float h, int action, string label)[] buttons =
        {
            (8f, 600f, 90f, 25f, 8, "Invite"), // A — C2S 2/35
            (109f, 600f, 90f, 25f, 11, "Leave"), // B — C2S 2/37
            (210f, 600f, 90f, 25f, 9, "Leader"), // C — C2S 2/36
            (8f, 642f, 90f, 25f, 10, "Kick"), // D — C2S 2/36
            (109f, 642f, 90f, 25f, 12, "Transfer"), // E — expel-stage
            (233f, 97f, 74f, 22f, 13, "Mini") // F — MiniParty toggle
        };

        foreach (var (x, y, w, h, action, label) in buttons)
        {
            var capturedAction = action;
            var btn = new Button
            {
                Name = $"ActionBtn{action}",
                Text = label,
                Position = new Vector2(x, y),
                Size = new Vector2(w, h),
                MouseFilter = MouseFilterEnum.Stop
            };
            btn.Pressed += () => OnAction(capturedAction);
            AddChild(btn);
        }
    }

    private void OnAction(int actionId)
    {
        // spec: ui_system.md §8.12 — actions 8/9/10/11 → outbound C2S opcodes 2/35,36,37
        switch (actionId)
        {
            case 8:
                // TODO(world-campaign): IApplicationUseCases.PartyInvite (C2S 2/35 CmsgPartyInvite)
                GD.Print("[HudPartyWindow] action 8 = Invite → TODO(world-campaign): C2S 2/35 CmsgPartyInvite.");
                break;
            case 9:
            case 10:
                // TODO(world-campaign): IApplicationUseCases.PartyLeaderOp (C2S 2/36)
                GD.Print($"[HudPartyWindow] action {actionId} = leader op → TODO(world-campaign): C2S 2/36.");
                break;
            case 11:
                // TODO(world-campaign): IApplicationUseCases.PartyMemberOp (C2S 2/37 CmsgPartyLeaderOp)
                GD.Print("[HudPartyWindow] action 11 = member op → TODO(world-campaign): C2S 2/37 CmsgPartyLeaderOp.");
                break;
            case 13:
                // Local UI: toggle the MiniParty mirror panel
                GD.Print("[HudPartyWindow] action 13 = MiniParty toggle (local UI).");
                break;
            case 14:
                Toggle(false);
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Hub binding
    // -------------------------------------------------------------------------

    /// <summary>
    ///     No hub channel exists for party roster yet.
    ///     TODO(world-campaign): party populate when hub exposes S2C 5/21 + 5/38 channels.
    /// </summary>
    public void BindHub(IHudEventHub hub)
    {
        // TODO(world-campaign): wire party roster events (S2C 5/21 SmsgPartyRosterEvent + 5/38 SmsgPartyMemberStats)
        // spec: Docs/RE/specs/ui_system.md §8.12 — populate via S2C 5/21 + 5/38
        GD.Print("[HudPartyWindow] BindHub: party roster populate deferred (TODO world-campaign: S2C 5/21 + 5/38).");
    }

    // -------------------------------------------------------------------------
    // Toggle
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Toggles the party window on/off.
    ///     Toggle key: UNVERIFIED (key-table / capture-pending).
    ///     spec: Docs/RE/specs/ui_system.md §8.12 — "toggle hotkey key-table/capture-pending".
    ///     TODO(spec): toggle hotkey.
    /// </summary>
    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;

        if (_open)
        {
            // Reveal: X = screenWidth − 318
            // spec: ui_system.md §8.12 — "on show: X = screenWidth − 318"
            OffsetLeft = -PartyW;
            OffsetRight = 0f;
        }
        else
        {
            // Park off-screen: X = screenWidth + 318
            // spec: ui_system.md §8.12 — "on hide: X = screenWidth" (≈ screenWidth + 0 off-right)
            OffsetLeft = PartyW;
            OffsetRight = PartyW + PartyW;
        }

        Visible = _open;
    }

    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey { Keycode: Key.Escape, Pressed: true })
        {
            // spec: ui_system.md §8.12 — "key event 27 (ESC) also closes"
            Toggle(false);
            GetViewport().SetInputAsHandled();
        }
    }
}