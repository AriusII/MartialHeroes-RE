
using System.Threading.Channels;
using Godot;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudBuffBar : Control
{

    private const int SlotCount = BuffStateEvent.SlotCount;
    private const int FlowingIconSide = 23;
    private const int FixedIconSide = 25;
    private const int FlowThreshold = 80;

    private const float ContainerX = 545f;

    private const int IconSpacing = 2;


    private readonly TextureRect[] _slots = new TextureRect[SlotCount];
    private ChannelReader<BuffStateEvent>? _buffStates;


    private HudIconLibrary? _icons;


    public void Build(HudIconLibrary icons)
    {
        Name = "HudBuffBar";
        _icons = icons;

        AnchorLeft = 0f;
        AnchorTop = 0f;
        AnchorRight = 0f;
        AnchorBottom = 0f;
        OffsetLeft = ContainerX;
        OffsetTop = 0f;
        OffsetRight = ContainerX + (FlowingIconSide + IconSpacing) * SlotCount;
        OffsetBottom = FixedIconSide + 4f;
        MouseFilter = MouseFilterEnum.Ignore;

        for (var i = 0; i < SlotCount; i++)
        {
            float x = i * (FlowingIconSide + IconSpacing);
            var slot = new TextureRect
            {
                Name = $"BuffSlot{i}",
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Position = new Vector2(x, 0f),
                Size = new Vector2(FlowingIconSide, FlowingIconSide),
                Visible = false,
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(slot);
            _slots[i] = slot;
        }

        GD.Print($"[HudBuffBar] Built — 30 slots at X={ContainerX}, flowing 23×23 / fixed 25×25. " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §2/§5.10; misc_data.md §1.6 CODE-CONFIRMED.");
    }


    public void BindHub(IHudEventHub hub)
    {
        _buffStates = hub.BuffStates;
        GD.Print("[HudBuffBar] BindHub: BuffStates channel connected.");
    }


    public override void _Process(double delta)
    {
        if (_buffStates is null) return;

        BuffStateEvent? latest = null;
        while (_buffStates.TryRead(out var ev))
            latest = ev;

        if (latest is null) return;
        ApplyBuffState(latest);
    }

    private void ApplyBuffState(BuffStateEvent ev)
    {
        for (var i = 0; i < SlotCount; i++)
            _slots[i].Visible = false;

        var flowingIdx = 0;
        for (var i = 0; i < SlotCount && i < ev.Slots.Length; i++)
        {
            var slot = ev.Slots[i];
            if (slot.IsEmpty) continue;

            var icon = _icons?.GetBuffIcon(slot.BuffId);
            if (icon is null) continue;

            if (slot.BuffId <= FlowThreshold)
            {
                if (flowingIdx >= SlotCount) break;
                float x = flowingIdx * (FlowingIconSide + IconSpacing);
                _slots[flowingIdx].Texture = icon;
                _slots[flowingIdx].Position = new Vector2(x, 0f);
                _slots[flowingIdx].Size = new Vector2(FlowingIconSide, FlowingIconSide);
                _slots[flowingIdx].Visible = true;
                flowingIdx++;
            }
            else
            {
                if (i < SlotCount)
                {
                    _slots[i].Texture = icon;
                    _slots[i].Size = new Vector2(FixedIconSide, FixedIconSide);
                    _slots[i].Visible = true;
                }
            }
        }
    }
}