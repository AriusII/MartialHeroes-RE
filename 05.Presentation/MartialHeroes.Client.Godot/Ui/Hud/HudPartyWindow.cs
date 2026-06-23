using Godot;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudPartyWindow : Control
{
    private const float PartyW = 318f;
    private const float PartyH = 732f;

    private const int MemberCount = 8;
    private const float SlotBaseY = 159f;
    private const float SlotStrideY = 54f;

    private const float BarMaxW = 124f;
    private const float BarH = 5f;

    private const int ClassMsgBase = 22001;

    private const int MainTexId = 8;
    private const int FooterTexId = 2;
    private readonly Label[] _classLabels = new Label[MemberCount];
    private readonly ProgressBar[] _expBars = new ProgressBar[MemberCount];

    private readonly ProgressBar[] _hpBars = new ProgressBar[MemberCount];
    private readonly Label[] _levelLabels = new Label[MemberCount];
    private readonly ProgressBar[] _mpBars = new ProgressBar[MemberCount];
    private readonly Label[] _nameLabels = new Label[MemberCount];


    private bool _open;


    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudPartyWindow";

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

        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.07f, 0.07f, 0.13f, 0.93f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.4f, 0.35f, 0.2f, 0.85f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

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

        for (var k = 0; k < MemberCount; k++)
        {
            var baseY = SlotBaseY + k * SlotStrideY;
            BuildMemberSlot(atlas, text, k, baseY);
        }

        BuildActionButtons(atlas, text);

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
        var rowBtn = new Button
        {
            Name = $"MemberRowBtn{k}",
            Position = new Vector2(10f, baseY - 30f),
            Size = new Vector2(300f, 54f),
            Flat = true,
            MouseFilter = MouseFilterEnum.Stop
        };
        AddChild(rowBtn);

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
        (float x, float y, float w, float h, int action, string label)[] buttons =
        {
            (8f, 600f, 90f, 25f, 8, "Invite"),
            (109f, 600f, 90f, 25f, 11, "Leave"),
            (210f, 600f, 90f, 25f, 9, "Leader"),
            (8f, 642f, 90f, 25f, 10, "Kick"),
            (109f, 642f, 90f, 25f, 12, "Transfer"),
            (233f, 97f, 74f, 22f, 13, "Mini")
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
        switch (actionId)
        {
            case 8:
                GD.Print("[HudPartyWindow] action 8 = Invite → TODO(world-campaign): C2S 2/35 CmsgPartyInvite.");
                break;
            case 9:
            case 10:
                GD.Print($"[HudPartyWindow] action {actionId} = leader op → TODO(world-campaign): C2S 2/36.");
                break;
            case 11:
                GD.Print("[HudPartyWindow] action 11 = member op → TODO(world-campaign): C2S 2/37 CmsgPartyLeaderOp.");
                break;
            case 13:
                GD.Print("[HudPartyWindow] action 13 = MiniParty toggle (local UI).");
                break;
            case 14:
                Toggle(false);
                break;
        }
    }


    public void OnRosterSnapshot(RosterSnapshotEvent evt)
    {
        for (var k = 0; k < MemberCount; k++)
        {
            if (_nameLabels[k] is not null) _nameLabels[k].Text = "";
            if (_levelLabels[k] is not null) _levelLabels[k].Text = "";
            if (_classLabels[k] is not null) _classLabels[k].Text = "";
            if (_hpBars[k] is not null) _hpBars[k].Value = 0.0;
            if (_mpBars[k] is not null) _mpBars[k].Value = 0.0;
            if (_expBars[k] is not null) _expBars[k].Value = 0.0;
        }

        if (evt.Members.IsDefaultOrEmpty)
        {
            GD.Print("[HudPartyWindow] OnRosterSnapshot: empty roster. " +
                     "spec: Docs/RE/packets/4-1_game_state_tick.yaml (Table A); " +
                     "Docs/RE/specs/world_systems.md §13.3.");
            return;
        }

        var rowsFilled = 0;
        foreach (var member in evt.Members)
        {
            var rowIndex = rowsFilled;
            if (rowIndex >= MemberCount) break;

            var displayText = $"#{member.KeepGuard} [id:{member.ActorId}]";
            if (_nameLabels[rowIndex] is not null) _nameLabels[rowIndex].Text = displayText;

            GD.Print(
                $"[HudPartyWindow] row {rowIndex}: member #{member.KeepGuard} ActorId={member.ActorId} Aux={member.Aux} — " +
                "name/class/vitals roster-feed-pending (S2C 5/21 + 5/38). " +
                "spec: Docs/RE/packets/4-1_game_state_tick.yaml (Table A); " +
                "Docs/RE/specs/world_systems.md §13.3.");
            rowsFilled++;
        }

        GD.Print($"[HudPartyWindow] OnRosterSnapshot: {rowsFilled} member row(s) populated with member# + ActorId. " +
                 "Name/class/vitals feed-pending (S2C 5/21 + 5/38 channels — TODO world-campaign). " +
                 "spec: Docs/RE/packets/4-1_game_state_tick.yaml (Table A); " +
                 "Docs/RE/specs/world_systems.md §13.3.");
    }


    public void BindHub(IHudEventHub hub)
    {
        GD.Print("[HudPartyWindow] BindHub: roster-number population wired via OnRosterSnapshot (GameLoop drain). " +
                 "Name/class/vitals remain TODO(world-campaign): S2C 5/21 + 5/38.");
    }


    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;

        if (_open)
        {
            OffsetLeft = -PartyW;
            OffsetRight = 0f;
        }
        else
        {
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
            Toggle(false);
            GetViewport().SetInputAsHandled();
        }
    }
}