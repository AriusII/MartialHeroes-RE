// Ui/Hud/HudBuffBar.cs
//
// In-game buff/state icon bar — 30 icon slots.
//
// Spec facts (CODE-CONFIRMED unless noted):
//   - 30 icon slots total.
//     spec: Docs/RE/formats/misc_data.md §1.6 — "buff bar has 30 icon slots".
//   - buff_id ≤ 80  → 23×23 px cell, flowing counter (left-to-right).
//     spec: misc_data.md §1.6 CODE-CONFIRMED.
//   - buff_id > 80  → 25×25 px cell, fixed per-slot.
//     spec: misc_data.md §1.6 CODE-CONFIRMED.
//   - Per-refresh reset: hide all 30 slots, re-show only active non-zero ones.
//     spec: misc_data.md §1.6 CODE-CONFIRMED.
//   - buff_id == 0 marks empty slot (skipped).
//     spec: misc_data.md §1.6 / packets/4-102_buff_state.yaml.
//
// Placement: container at base X=545 (from §5.10 data-driven composite placement).
//   Children at Y=0/92, W=276/92, H=91.
//   spec: Docs/RE/specs/ui_hud_layout.md §5.10 — buff icon-strip composite.
//
// Per-icon source-rect from buff_icon_position.xdb → stateicon.dds (uitex 26).
//   spec: Docs/RE/specs/ui_hud_layout.md §2 / formats/misc_data.md §1.3.
//
// PASSIVE: drains IHudEventHub.BuffStates in _Process.

using System.Threading.Channels;
using Godot;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
///     30-slot HUD buff/state icon bar. Reads from <see cref="IHudEventHub.BuffStates" />.
///     <para>
///         PASSIVE: zero game logic. Drains the buff-state channel each frame; rebuilds slot visuals
///         from the <see cref="HudIconLibrary" />.
///     </para>
///     spec: Docs/RE/specs/ui_hud_layout.md §2 / §5.10.
///     spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED (slot count, cell sizes, reset policy).
/// </summary>
public sealed partial class HudBuffBar : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private const int SlotCount = BuffStateEvent.SlotCount; // 30 — spec: misc_data.md §1.6
    private const int FlowingIconSide = 23; // spec: misc_data.md §1.6 — buff_id ≤ 80, 23×23
    private const int FixedIconSide = 25; // spec: misc_data.md §1.6 — buff_id > 80, 25×25
    private const int FlowThreshold = 80; // spec: misc_data.md §1.6 — split threshold

    // Container origin from §5.10 (buff icon-strip composite)
    // spec: Docs/RE/specs/ui_hud_layout.md §5.10 — "container at base X=545"
    private const float ContainerX = 545f; // spec: ui_hud_layout.md §5.10
    // Container children: W=276, H=91 at Y=0; W=92, H=91 at Y=92.
    // spec: ui_hud_layout.md §5.10 — "W=276 H=91 innerX=32 / W=92 H=91 at Y=92"

    private const int IconSpacing = 2; // small gutter between flowing icons

    // -------------------------------------------------------------------------
    // Child controls (30 slots)
    // -------------------------------------------------------------------------

    private readonly TextureRect[] _slots = new TextureRect[SlotCount];
    private ChannelReader<BuffStateEvent>? _buffStates;

    // -------------------------------------------------------------------------
    // Services
    // -------------------------------------------------------------------------

    private HudIconLibrary? _icons;

    // -------------------------------------------------------------------------
    // Build (geometry pass)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Geometry pass: positions the buff-bar container and builds 30 icon slots.
    ///     spec: Docs/RE/specs/ui_hud_layout.md §5.10 — container at X=545.
    /// </summary>
    public void Build(HudIconLibrary icons)
    {
        Name = "HudBuffBar";
        _icons = icons;

        // Container anchored at absolute X=545, top of screen.
        // spec: ui_hud_layout.md §5.10 CODE-CONFIRMED container placement.
        AnchorLeft = 0f;
        AnchorTop = 0f;
        AnchorRight = 0f;
        AnchorBottom = 0f;
        OffsetLeft = ContainerX; // spec: ui_hud_layout.md §5.10
        OffsetTop = 0f;
        // Width to hold up to 30 flowing 23×23 icons with 2px spacing
        OffsetRight = ContainerX + (FlowingIconSide + IconSpacing) * SlotCount;
        OffsetBottom = FixedIconSide + 4f; // 25px max icon + small pad
        MouseFilter = MouseFilterEnum.Ignore;

        // Build 30 slot TextureRects in a horizontal flowing strip.
        // Fixed-position slots (buff_id > 80) are placed by the server assignment order.
        // TODO(world-campaign): per-slot position from buff_icon_position.xdb lookup for fixed slots.
        for (var i = 0; i < SlotCount; i++)
        {
            float x = i * (FlowingIconSide + IconSpacing);
            var slot = new TextureRect
            {
                Name = $"BuffSlot{i}",
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Position = new Vector2(x, 0f),
                Size = new Vector2(FlowingIconSide, FlowingIconSide),
                Visible = false, // hidden until a buff occupies this slot
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(slot);
            _slots[i] = slot;
        }

        GD.Print($"[HudBuffBar] Built — 30 slots at X={ContainerX}, flowing 23×23 / fixed 25×25. " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §2/§5.10; misc_data.md §1.6 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Hub binding
    // -------------------------------------------------------------------------

    /// <summary>Binds to the HUD event hub's BuffStates channel.</summary>
    public void BindHub(IHudEventHub hub)
    {
        _buffStates = hub.BuffStates;
        GD.Print("[HudBuffBar] BindHub: BuffStates channel connected.");
    }

    // -------------------------------------------------------------------------
    // Per-frame drain
    // -------------------------------------------------------------------------

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
        // Per-refresh reset: hide all 30 slots first.
        // spec: misc_data.md §1.6 CODE-CONFIRMED — "per-refresh reset"
        for (var i = 0; i < SlotCount; i++)
            _slots[i].Visible = false;

        var flowingIdx = 0;
        for (var i = 0; i < SlotCount && i < ev.Slots.Length; i++)
        {
            var slot = ev.Slots[i];
            if (slot.IsEmpty) continue; // spec: misc_data.md §1.6 — buff_id==0 = skip

            var icon = _icons?.GetBuffIcon(slot.BuffId);
            if (icon is null) continue;

            if (slot.BuffId <= FlowThreshold)
            {
                // Flowing class: 23×23 in left-to-right order
                // spec: misc_data.md §1.6 CODE-CONFIRMED
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
                // Fixed class: 25×25 at fixed slot position
                // spec: misc_data.md §1.6 CODE-CONFIRMED — "fixed per-slot position"
                // TODO(world-campaign): use per-slot screen positions from buff_icon_position.xdb lookup.
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