using Godot;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Godot.Autoload;
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
    private const int MainTexId = 8;
    private const int FooterTexId = 2;

    private readonly Label[] _classLabels = new Label[MemberCount];
    private readonly ProgressBar[] _expBars = new ProgressBar[MemberCount];
    private readonly ProgressBar[] _hpBars = new ProgressBar[MemberCount];
    private readonly Label[] _levelLabels = new Label[MemberCount];
    private readonly ProgressBar[] _mpBars = new ProgressBar[MemberCount];
    private readonly Label[] _nameLabels = new Label[MemberCount];
    private readonly uint[] _memberIds = new uint[MemberCount];

    private int _selectedMemberIndex = -1;
    private bool _open;
    private IApplicationUseCases? _useCases;

    public void Build(HudAtlasLibrary atlas, HudTextLibrary text, ClientContext ctx)
    {
        _useCases = ctx.UseCases;

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
    }

    private void BuildMemberSlot(HudAtlasLibrary atlas, HudTextLibrary text, int k, float baseY)
    {
        var capturedK = k;
        var rowBtn = new Button
        {
            Name = $"MemberRowBtn{k}",
            Position = new Vector2(10f, baseY - 30f),
            Size = new Vector2(300f, 54f),
            Flat = true,
            MouseFilter = MouseFilterEnum.Stop
        };
        rowBtn.Pressed += () => SelectMember(capturedK);
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

    private void SelectMember(int index)
    {
        _selectedMemberIndex = index;
    }

    private void OnAction(int actionId)
    {
        switch (actionId)
        {
            case 8:
                break;

            case 9:
            case 12:
                if (_selectedMemberIndex >= 0 && _selectedMemberIndex < MemberCount)
                {
                    var leaderId = _memberIds[_selectedMemberIndex];
                    if (leaderId != 0u)
                        _ = _useCases?.TransferPartyLeaderAsync(leaderId);
                }
                break;

            case 10:
                if (_selectedMemberIndex >= 0 && _selectedMemberIndex < MemberCount)
                {
                    var kickId = _memberIds[_selectedMemberIndex];
                    if (kickId != 0u)
                        _ = _useCases?.KickPartyMemberAsync(kickId);
                }
                break;

            case 11:
                _ = _useCases?.LeavePartyAsync();
                break;

            case 13:
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
            _memberIds[k] = 0u;
            if (_nameLabels[k] is not null) _nameLabels[k].Text = "";
            if (_levelLabels[k] is not null) _levelLabels[k].Text = "";
            if (_classLabels[k] is not null) _classLabels[k].Text = "";
            if (_hpBars[k] is not null) _hpBars[k].Value = 0.0;
            if (_mpBars[k] is not null) _mpBars[k].Value = 0.0;
            if (_expBars[k] is not null) _expBars[k].Value = 0.0;
        }

        _selectedMemberIndex = -1;

        if (evt.Members.IsDefaultOrEmpty) return;

        var rowsFilled = 0;
        foreach (var member in evt.Members)
        {
            if (rowsFilled >= MemberCount) break;
            _memberIds[rowsFilled] = member.ActorId;
            rowsFilled++;
        }
    }

    public void OnPartyMemberJoined(PartyMemberJoinedEvent evt)
    {
        var slot = evt.Sort < MemberCount ? evt.Sort : FindEmptySlot();
        if (slot < 0 || slot >= MemberCount) return;

        _memberIds[slot] = evt.ActorId;

        if (_nameLabels[slot] is not null)
            _nameLabels[slot].Text = evt.Name;
    }

    public void OnPartyMemberRemoved(PartyMemberRemovedEvent evt)
    {
        for (var k = 0; k < MemberCount; k++)
        {
            if (_memberIds[k] != evt.RemovedId) continue;

            _memberIds[k] = 0u;
            if (_nameLabels[k] is not null) _nameLabels[k].Text = "";
            if (_levelLabels[k] is not null) _levelLabels[k].Text = "";
            if (_classLabels[k] is not null) _classLabels[k].Text = "";
            if (_hpBars[k] is not null) _hpBars[k].Value = 0.0;
            if (_mpBars[k] is not null) _mpBars[k].Value = 0.0;
            if (_expBars[k] is not null) _expBars[k].Value = 0.0;

            if (_selectedMemberIndex == k)
                _selectedMemberIndex = -1;

            break;
        }
    }

    public void OnPartyMemberVitals(PartyMemberVitalsEvent evt)
    {
        var slot = FindSlotById(evt.MemberId);
        if (slot < 0) return;

        if (_nameLabels[slot] is not null && !string.IsNullOrEmpty(evt.MemberName))
            _nameLabels[slot].Text = evt.MemberName;

        var maxHp = evt.StatD > 0u ? evt.StatD : 1u;
        var maxMp = evt.StatF > 0u ? evt.StatF : 1u;
        var maxExp = evt.StatH > 0u ? evt.StatH : 1u;

        if (_hpBars[slot] is not null)
            _hpBars[slot].Value = (double)evt.StatC / maxHp;

        if (_mpBars[slot] is not null)
            _mpBars[slot].Value = (double)evt.StatE / maxMp;

        if (_expBars[slot] is not null)
            _expBars[slot].Value = (double)evt.StatG / maxExp;
    }

    public void OnPartyInviteState(PartyInviteStateEvent evt)
    {
        if (evt.MemberIds.IsDefaultOrEmpty) return;

        for (var k = 0; k < MemberCount; k++)
            _memberIds[k] = 0u;

        var filled = 0;
        foreach (var id in evt.MemberIds)
        {
            if (filled >= MemberCount) break;
            _memberIds[filled] = id;
            filled++;
        }
    }

    public void OnPartyAcceptResult(PartyAcceptResultEvent evt)
    {
        if (!evt.Success)
        {
            for (var k = 0; k < MemberCount; k++)
            {
                _memberIds[k] = 0u;
                if (_nameLabels[k] is not null) _nameLabels[k].Text = "";
                if (_hpBars[k] is not null) _hpBars[k].Value = 0.0;
                if (_mpBars[k] is not null) _mpBars[k].Value = 0.0;
                if (_expBars[k] is not null) _expBars[k].Value = 0.0;
            }
        }
    }

    public void BindHub(IHudEventHub hub)
    {
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

    private int FindSlotById(uint actorId)
    {
        for (var k = 0; k < MemberCount; k++)
            if (_memberIds[k] == actorId)
                return k;
        return -1;
    }

    private int FindEmptySlot()
    {
        for (var k = 0; k < MemberCount; k++)
            if (_memberIds[k] == 0u)
                return k;
        return -1;
    }
}
