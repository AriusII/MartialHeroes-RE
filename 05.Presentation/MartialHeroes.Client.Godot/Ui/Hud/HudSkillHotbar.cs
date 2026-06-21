// Ui/Hud/HudSkillHotbar.cs
//
// Player skill hotbar — container origin (349, 13); 9-slot data-driven grid.
//
// This is the skill HOTBAR (the always-visible action bar), NOT the skill book window.
//
// Container: anchored at absolute (349, 13); thin anchor strip W~7, H~504.
//   spec: Docs/RE/specs/ui_hud_layout.md §3.5 / §5.10 CODE-CONFIRMED container origin.
//
// Per-slot layout variant by entity KIND word (CODE-CONFIRMED):
//   KIND == 5         → 146 × 49
//   KIND ∈ {0,6,7,11,18} → 297 × 50
//   (anything else)  → 58 × 58  (fallback used here)
//
// Registry record layout (CODE-CONFIRMED shape; VALUES data-driven):
//   +0x00 u32 instance key
//   +0x04 u8  owning slot (0–8)
//   +0x08 u32 click-action id
//   +0x0C     icon texset
//   +0x10 i32 base X
//   +0x14 i32 base Y (biased −92)
//   +0x28..+0x2A overlay-present flags
//   +0x2C..+0x70 overlay rect params (VALUES debugger-pending)
//
// Cooldown overlay frames (CODE-CONFIRMED hardcoded atlas src-rects):
//   3 mutually-exclusive 29×29 frames from uitex registry key 3 (icon-frame texset):
//     ready/empty  → src (821, 655) 29×29    // spec: ui_hud_layout.md §5.10a
//     cooldown     → src (792, 655) 29×29    // spec: ui_hud_layout.md §5.10a
//     charge       → src (763, 655) 29×29    // spec: ui_hud_layout.md §5.10a
//   Destination cell (fallback 58×58 KIND): (baseX+12, baseY+12).
//     spec: ui_hud_layout.md §5.10a — "baseX+12, baseY+12 in the 58×58 branch"
//   These share ONE destination cell and are toggled visible/hidden by state.
//   Default render = ready/empty frame (no cooldown).
//
// Authored per-skill overlay VALUES are data-driven (.do table) — not in scope here.
//   TODO(spec): per-skill overlay values from .do stance table.
//
// spec: Docs/RE/specs/ui_hud_layout.md §3.5 — container origin CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_hud_layout.md §5.10 — data-driven nine-slot loop.
// spec: Docs/RE/specs/ui_hud_layout.md §5.10a — cooldown/charge/ready src-rects CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §8.6.1 — uitex 10=skillpipe.dds, 3=icon-frame texset.

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
///     Player skill hotbar: container at (349, 13) holding 9 data-driven skill slots.
///     <para>
///         PASSIVE: reads HudIconLibrary for skill icons; zero game logic.
///         Per-slot base X/Y from registry deferred (uses fallback 58×58 grid until world-campaign wires
///         the live registry). Cooldown overlay frames (ready/cooldown/charge) are hardcoded atlas
///         src-rects from uitex key 3, default = ready state.
///     </para>
///     spec: Docs/RE/specs/ui_hud_layout.md §3.5 / §5.7 / §5.10a CODE-CONFIRMED.
/// </summary>
public sealed partial class HudSkillHotbar : Control
{
    // -------------------------------------------------------------------------
    // Overlay state enum (per slot)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Which overlay frame is visible in a slot. Default = Ready (the empty/ready frame).
    ///     spec: Docs/RE/specs/ui_hud_layout.md §5.10a — "three mutually-exclusive 29×29 frames".
    /// </summary>
    public enum SlotOverlayState
    {
        /// <summary>Ready / empty state — src (821, 655). spec: ui_hud_layout.md §5.10a.</summary>
        Ready,

        /// <summary>On cooldown — src (792, 655). spec: ui_hud_layout.md §5.10a.</summary>
        Cooldown,

        /// <summary>Charged / available stack — src (763, 655). spec: ui_hud_layout.md §5.10a.</summary>
        Charge
    }
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // spec: Docs/RE/specs/ui_hud_layout.md §3.5 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private const float ContainerX = 349f; // spec: ui_hud_layout.md §3.5
    private const float ContainerY = 13f; // spec: ui_hud_layout.md §3.5
    private const int SlotCount = 9; // spec: ui_hud_layout.md §3.5 — "nine skill slots"

    // Layout variants by entity KIND word (CODE-CONFIRMED)
    // spec: ui_hud_layout.md §3.5 — KIND table
    private const float CellFallbackW = 58f; // spec: ui_hud_layout.md §3.5 — fallback cell 58×58
    private const float CellFallbackH = 58f; // spec: ui_hud_layout.md §3.5 — fallback cell 58×58

    // Cooldown / charge / ready overlay — hardcoded src-rects, uitex key 3 (icon-frame texset)
    // spec: Docs/RE/specs/ui_hud_layout.md §5.10a — CODE-CONFIRMED hardcoded src-rects
    private const int OverlayTexId = 3; // spec: ui_hud_layout.md §5.10a — uitex key 3 = icon-frame texset
    private const int OverlayReadySrcX = 821; // spec: ui_hud_layout.md §5.10a — ready/empty src X
    private const int OverlayReadySrcY = 655; // spec: ui_hud_layout.md §5.10a — ready/empty src Y
    private const int OverlayCooldownSrcX = 792; // spec: ui_hud_layout.md §5.10a — cooldown src X
    private const int OverlayCooldownSrcY = 655; // spec: ui_hud_layout.md §5.10a
    private const int OverlayChargeSrcX = 763; // spec: ui_hud_layout.md §5.10a — charge src X
    private const int OverlayChargeSrcY = 655; // spec: ui_hud_layout.md §5.10a
    private const int OverlaySide = 29; // spec: ui_hud_layout.md §5.10a — 29×29 frames

    // Destination cell offset within the 58×58 fallback KIND cell.
    // spec: ui_hud_layout.md §5.10a — "baseX+12, baseY+12 in the 58×58 branch"
    private const float OverlayDstOffX = 12f; // spec: ui_hud_layout.md §5.10a (58×58 branch)
    private const float OverlayDstOffY = 12f; // spec: ui_hud_layout.md §5.10a (58×58 branch)

    // skillpipe.dds uitex id 10 for the hotbar chrome
    // spec: Docs/RE/specs/ui_system.md §8.6.1 — uitex 10 = data/ui/skillpipe.dds
    private const int SkillpipeTexId = 10; // spec: ui_system.md §8.6.1
    private readonly TextureRect?[] _overlayCharge = new TextureRect?[SlotCount];
    private readonly TextureRect?[] _overlayCooldown = new TextureRect?[SlotCount];

    // Per-slot overlay: three TextureRects (ready/cooldown/charge); exactly one visible at a time.
    private readonly TextureRect?[] _overlayReady = new TextureRect?[SlotCount];
    private readonly SlotOverlayState[] _overlayState = new SlotOverlayState[SlotCount];

    // -------------------------------------------------------------------------
    // Child controls
    // -------------------------------------------------------------------------

    private readonly TextureRect[] _slotIcons = new TextureRect[SlotCount];
    private readonly Label[] _slotKeyLabels = new Label[SlotCount];

    // -------------------------------------------------------------------------
    // Build (geometry pass)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Geometry pass: positions the hotbar container at (349, 13) and builds 9 slots,
    ///     each with the three hardcoded cooldown/charge/ready overlay frames from uitex key 3.
    ///     Default overlay state for all slots = <see cref="SlotOverlayState.Ready" />.
    ///     spec: Docs/RE/specs/ui_hud_layout.md §3.5 CODE-CONFIRMED container origin.
    ///     spec: Docs/RE/specs/ui_hud_layout.md §5.10a — cooldown/charge/ready frames CODE-CONFIRMED.
    /// </summary>
    public void Build(HudAtlasLibrary atlas, HudIconLibrary icons)
    {
        Name = "HudSkillHotbar";

        // Container at absolute (349, 13).
        // spec: ui_hud_layout.md §3.5 CODE-CONFIRMED — "container origin 349, 13"
        Position = new Vector2(ContainerX, ContainerY);
        // Thin anchor strip: W~7, H~504 (anchor, not visible extent).
        // spec: ui_hud_layout.md §3.5 — "thin anchor strip W=7, H=504"
        // In practice we size to fit 9 × fallback cells.
        Size = new Vector2(SlotCount * CellFallbackW, CellFallbackH);
        MouseFilter = MouseFilterEnum.Ignore;

        // Load the overlay texset (uitex key 3 = icon-frame texset).
        // spec: ui_hud_layout.md §5.10a — "uitex registry key 3 (icon-frame texset)"
        var readyTex =
            atlas.SliceById(OverlayTexId, OverlayReadySrcX, OverlayReadySrcY, OverlaySide, OverlaySide);
        var cooldownTex = atlas.SliceById(OverlayTexId, OverlayCooldownSrcX, OverlayCooldownSrcY, OverlaySide,
            OverlaySide);
        var chargeTex =
            atlas.SliceById(OverlayTexId, OverlayChargeSrcX, OverlayChargeSrcY, OverlaySide, OverlaySide);

        if (readyTex is null)
            GD.PrintErr("[HudSkillHotbar] uitex key 3 (icon-frame texset) unavailable (VFS offline); " +
                        "overlay frames will render without texture. " +
                        "spec: Docs/RE/specs/ui_hud_layout.md §5.10a.");

        // Optional: load hotbar chrome from skillpipe.dds (uitex 10).
        // spec: ui_system.md §8.6.1 — uitex 10 = data/ui/skillpipe.dds
        var pipeTex = atlas.GetById(SkillpipeTexId);

        // Build 9 skill slots in a horizontal row (fallback layout).
        // Real layout reads base X/Y from the runtime slot registry (+0x10 / +0x14 biased −92).
        // TODO(world-campaign): replace with registry-driven slot positions.
        for (var i = 0; i < SlotCount; i++)
        {
            var slotX = i * CellFallbackW;

            var slot = new Control
            {
                Name = $"SkillSlot{i}",
                Position = new Vector2(slotX, 0f), // placeholder; registry gives real X/Y
                Size = new Vector2(CellFallbackW, CellFallbackH),
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(slot);

            // Slot background — dark cell
            var bg = new Panel { Name = "Bg" };
            bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            var bgStyle = new StyleBoxFlat();
            bgStyle.BgColor = new Color(0.08f, 0.08f, 0.08f, 0.78f);
            bgStyle.SetBorderWidthAll(1);
            bgStyle.BorderColor = new Color(0.45f, 0.45f, 0.45f, 0.85f);
            bg.AddThemeStyleboxOverride("panel", bgStyle);
            slot.AddChild(bg);

            // Optionally bind a slice of the skillpipe.dds chrome as slot background.
            if (pipeTex is not null)
            {
                // Layout of skillpipe.dds is unrecovered — use a full-atlas stretch.
                // TODO(spec): resolve skillpipe.dds per-slot source rects.
                var chromeTex = new TextureRect
                {
                    Name = "Chrome",
                    Texture = pipeTex,
                    StretchMode = TextureRect.StretchModeEnum.Scale,
                    MouseFilter = MouseFilterEnum.Ignore,
                    ZIndex = -1 // behind bg panel
                };
                chromeTex.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                slot.AddChild(chromeTex);
            }

            // Icon TextureRect — centre of the slot cell
            var icon = new TextureRect
            {
                Name = "Icon",
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore
            };
            icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            // Inset slightly so the border is visible
            icon.OffsetLeft = 2f;
            icon.OffsetTop = 2f;
            icon.OffsetRight = -2f;
            icon.OffsetBottom = -2f;
            slot.AddChild(icon);
            _slotIcons[i] = icon;

            // Key label ("1"–"9") at top-left
            var keyLabel = new Label
            {
                Name = "KeyLabel",
                Text = $"{i + 1}",
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                MouseFilter = MouseFilterEnum.Ignore
            };
            keyLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
            keyLabel.OffsetRight = CellFallbackW;
            keyLabel.OffsetBottom = 16f;
            slot.AddChild(keyLabel);
            _slotKeyLabels[i] = keyLabel;

            // --- Cooldown overlay frames (hardcoded src-rects, uitex key 3) ---
            // Three mutually-exclusive 29×29 frames at the overlay destination cell.
            // Destination (58×58 branch): (baseX+12, baseY+12) — here rel. to the slot container.
            // spec: Docs/RE/specs/ui_hud_layout.md §5.10a CODE-CONFIRMED
            //   ready/empty  → src (821, 655) 29×29
            //   cooldown     → src (792, 655) 29×29
            //   charge       → src (763, 655) 29×29
            // TODO(spec): per-skill overlay values from .do stance table (authored VALUES only).

            // Ready frame — visible by default
            var overlayReady = new TextureRect
            {
                Name = "OverlayReady",
                Texture = readyTex, // null-safe; renders nothing when null
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Position = new Vector2(OverlayDstOffX, OverlayDstOffY),
                Size = new Vector2(OverlaySide, OverlaySide),
                MouseFilter = MouseFilterEnum.Ignore,
                Visible = true, // default = ready
                ZIndex = 1
            };
            slot.AddChild(overlayReady);
            _overlayReady[i] = overlayReady;

            // Cooldown frame — hidden by default
            var overlayCooldown = new TextureRect
            {
                Name = "OverlayCooldown",
                Texture = cooldownTex,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Position = new Vector2(OverlayDstOffX, OverlayDstOffY),
                Size = new Vector2(OverlaySide, OverlaySide),
                MouseFilter = MouseFilterEnum.Ignore,
                Visible = false,
                ZIndex = 1
            };
            slot.AddChild(overlayCooldown);
            _overlayCooldown[i] = overlayCooldown;

            // Charge frame — hidden by default
            var overlayCharge = new TextureRect
            {
                Name = "OverlayCharge",
                Texture = chargeTex,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Position = new Vector2(OverlayDstOffX, OverlayDstOffY),
                Size = new Vector2(OverlaySide, OverlaySide),
                MouseFilter = MouseFilterEnum.Ignore,
                Visible = false,
                ZIndex = 1
            };
            slot.AddChild(overlayCharge);
            _overlayCharge[i] = overlayCharge;

            // Default overlay state = ready
            _overlayState[i] = SlotOverlayState.Ready;
        }

        GD.Print($"[HudSkillHotbar] Built — container at ({ContainerX},{ContainerY}), {SlotCount} slots " +
                 $"× {CellFallbackW}×{CellFallbackH} (fallback; registry data-driven pending). " +
                 "Overlay frames: ready=(821,655) cooldown=(792,655) charge=(763,655) 29×29 uitex key 3. " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §3.5 CODE-CONFIRMED origin; " +
                 "§5.10a CODE-CONFIRMED overlay src-rects. " +
                 "TODO(spec): per-skill overlay values from .do stance table.");
    }

    // -------------------------------------------------------------------------
    // Slot updates
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Sets the skill icon for the slot at index <paramref name="slotIndex" /> (0-based).
    ///     Called by the world-campaign HUD wiring when the hotbar-slot-set event arrives.
    ///     TODO(world-campaign): connect to SkillHotbarSlotSetEvent drain.
    /// </summary>
    public void SetSlotIcon(int slotIndex, AtlasTexture? icon)
    {
        if ((uint)slotIndex >= SlotCount) return;
        _slotIcons[slotIndex].Texture = icon;
    }

    /// <summary>
    ///     Sets the overlay frame state for slot <paramref name="slotIndex" />.
    ///     <para>
    ///         Only one of the three 29×29 overlay frames is visible at a time.
    ///         Default = <see cref="SlotOverlayState.Ready" /> (the ready/empty frame).
    ///     </para>
    ///     spec: Docs/RE/specs/ui_hud_layout.md §5.10a — "three mutually-exclusive 29×29 frames
    ///     toggled visible/hidden by a cooldown predicate and a charge-state lookup".
    ///     TODO(world-campaign): drive from cooldown-state events when the world-campaign wires
    ///     the skill-hotbar cooldown tracking.
    /// </summary>
    public void SetSlotOverlay(int slotIndex, SlotOverlayState state)
    {
        if ((uint)slotIndex >= SlotCount) return;
        if (_overlayState[slotIndex] == state) return;

        _overlayState[slotIndex] = state;

        // Exactly one frame visible at a time.
        // spec: ui_hud_layout.md §5.10a — "three mutually-exclusive frames sharing one dst cell"
        if (_overlayReady[slotIndex] is not null) _overlayReady[slotIndex]!.Visible = state == SlotOverlayState.Ready;
        if (_overlayCooldown[slotIndex] is not null)
            _overlayCooldown[slotIndex]!.Visible = state == SlotOverlayState.Cooldown;
        if (_overlayCharge[slotIndex] is not null)
            _overlayCharge[slotIndex]!.Visible = state == SlotOverlayState.Charge;
    }
}