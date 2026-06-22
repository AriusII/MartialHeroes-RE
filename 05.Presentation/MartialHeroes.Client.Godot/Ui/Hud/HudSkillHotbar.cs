// Ui/Hud/HudSkillHotbar.cs
//
// Player skill hotbar — container origin (349, 13); two nine-slot data-driven grid loops.
//
// This is the skill HOTBAR (the always-visible action bar), NOT the skill book window.
//
// Container: anchored at absolute (349, 13); thin anchor strip W=7, H=504, inner-x=982.
//   spec: Docs/RE/specs/ui_hud_layout.md §3.5 / §5.10 CODE-CONFIRMED container origin.
//   spec: Docs/RE/specs/ui_hud_layout.md §3.5 CYCLE 11 — "container rect: x=349, y=13, w=7, h=504, inner-x=982 (texture-manifest key 3)".
//   spec: Docs/RE/specs/ui_hud_layout.md §3.5 CYCLE 11 — "built as two nine-slot loops".
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
using MartialHeroes.Client.Application.Contracts.Events;
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
    // spec: Docs/RE/specs/ui_hud_layout.md §3.5 CYCLE 11 RESOLVED — container x=349, y=13, w=7, h=504, inner-x=982
    // -------------------------------------------------------------------------

    private const float ContainerX = 349f; // spec: ui_hud_layout.md §3.5 CODE-CONFIRMED

    private const float ContainerY = 13f; // spec: ui_hud_layout.md §3.5 CODE-CONFIRMED

    // Anchor strip dimensions (NOT the visual extent — the anchor is a 7×504 thin strip).
    // spec: Docs/RE/specs/ui_hud_layout.md §3.5 CYCLE 11 — "container rect: x=349, y=13, width=7, height=504"
    private const float ContainerAnchorW = 7f; // spec: ui_hud_layout.md §3.5 CYCLE 11 — anchor width 7

    private const float ContainerAnchorH = 504f; // spec: ui_hud_layout.md §3.5 CYCLE 11 — anchor height 504

    // inner-x 982 = texture-manifest key 3 (the icon-frame chrome atlas).
    // spec: Docs/RE/specs/ui_hud_layout.md §3.5 CYCLE 11 — "inner-x=982 (texture-manifest key 3)"
    private const int ContainerInnerX = 982; // spec: ui_hud_layout.md §3.5 CYCLE 11

    // The build routine runs two nine-slot loops (not one) over the runtime slot registry.
    // spec: Docs/RE/specs/ui_hud_layout.md §3.5 CYCLE 11 — "built as two nine-slot loops"
    private const int SlotLoopCount = 2; // spec: ui_hud_layout.md §3.5 CYCLE 11 — two nine-slot loops
    private const int SlotsPerLoop = 9; // spec: ui_hud_layout.md §3.5 — "nine skill slots per loop"
    private const int SlotCount = SlotLoopCount * SlotsPerLoop; // 18 total slots across both loops

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

        // Container anchor strip at absolute (349, 13), size 7×504.
        // spec: ui_hud_layout.md §3.5 CYCLE 11 — "x=349, y=13, width=7, height=504"
        // The 7×504 is the ANCHOR rect, not the visible slot extent; the slots are data-driven children.
        // inner-x=982 = texture-manifest key 3 (icon-frame chrome). spec: ui_hud_layout.md §3.5 CYCLE 11.
        Position = new Vector2(ContainerX, ContainerY);
        Size = new Vector2(ContainerAnchorW, ContainerAnchorH); // spec: ui_hud_layout.md §3.5 CYCLE 11
        MouseFilter = MouseFilterEnum.Ignore;
        // Inner-x cite: ContainerInnerX=982 is the texture-manifest key for the icon-frame chrome.
        // This is referenced for correct chrome lookup when the atlas is live.
        // spec: Docs/RE/specs/ui_hud_layout.md §3.5 CYCLE 11 — inner-x=982 (texture-manifest key 3)
        _ = ContainerInnerX; // suppress unused-field warning; present for spec correctness

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

        // Build 18 skill slots (two 9-slot loops, each loop covering SlotsPerLoop=9 slots).
        // spec: Docs/RE/specs/ui_hud_layout.md §3.5 CYCLE 11 — "built as two nine-slot loops"
        // Real layout reads base X/Y from the runtime slot registry (+0x10 / +0x14 biased −92).
        // Fallback: slots laid out in a 9×2 grid (two rows of 9).
        // TODO(world-campaign): replace with registry-driven slot positions.
        for (var i = 0; i < SlotCount; i++)
        {
            var loopIdx = i / SlotsPerLoop; // 0 = first nine-slot loop, 1 = second nine-slot loop
            var slotInLoop = i % SlotsPerLoop;
            var slotX = slotInLoop * CellFallbackW;
            var slotY = loopIdx * CellFallbackH; // second loop row below first

            var slot = new Control
            {
                Name = $"SkillSlot{i}",
                // Fallback position: loop 0 = row 0, loop 1 = row 1; registry gives real X/Y.
                // spec: ui_hud_layout.md §3.5 CYCLE 11 — per-slot base X/Y from registry +0x10/+0x14
                Position = new Vector2(slotX, slotY), // placeholder; registry gives real X/Y
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
            // Caption font slot 4 — dominant in-game HUD caption font.
            // spec: Docs/RE/specs/ui_hud_layout.md CYCLE 11 — "dominant HUD caption font slot 4"
            HudFont.ApplyToLabel(keyLabel, 4); // spec: ui_hud_layout.md CYCLE 11 — slot 4
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

        GD.Print(
            $"[HudSkillHotbar] Built — anchor at ({ContainerX},{ContainerY}) size {ContainerAnchorW}×{ContainerAnchorH} inner-x={ContainerInnerX}. " +
            $"Two nine-slot loops = {SlotCount} total slots × {CellFallbackW}×{CellFallbackH} fallback cell. " +
            "Overlay frames: ready=(821,655) cooldown=(792,655) charge=(763,655) 29×29 uitex key 3. " +
            "spec: Docs/RE/specs/ui_hud_layout.md §3.5 CYCLE 11 — container x=349,y=13,w=7,h=504,inner-x=982 CODE-CONFIRMED; " +
            "two nine-slot loops CODE-CONFIRMED; §5.10a CODE-CONFIRMED overlay src-rects. " +
            "TODO(world-campaign): replace with registry-driven slot positions + per-skill overlay values.");
    }

    // -------------------------------------------------------------------------
    // Slot updates
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Populates the visible 18-slot bar from the world-entry 4/1 hotbar snapshot.
    ///     The 4/1 HotbarSlots region carries up to 240 entries (slot indices 0..239); this bar
    ///     shows the first <see cref="SlotCount" /> = 18 (two nine-slot loops). Entries whose
    ///     SlotIndex >= SlotCount belong to the 240-slot model but are not on this bar — they are
    ///     noted and skipped without error.
    ///     Icon resolution: EntryKey is RAW (skill-vs-item category-pending — no resolver in layer 05).
    ///     We cannot fabricate a skill-vs-item mapping, so we write the EntryKey into the key label
    ///     (as a numeric occupied marker) and leave the icon null, printing a per-slot diagnostic.
    ///     When the category resolver lands (world-campaign), replace this with a proper icon lookup.
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (HotbarSlots note — EntryKey raw, category-pending).
    /// </summary>
    public void OnHotbarInitialized(HotbarInitializedEvent evt)
    {
        // First clear all visible slots so a re-init from a new world-entry is clean.
        for (var i = 0; i < SlotCount; i++)
        {
            SetSlotIcon(i, null);
            if ((uint)i < (uint)_slotKeyLabels.Length && _slotKeyLabels[i] is not null)
                _slotKeyLabels[i].Text = $"{i + 1}"; // restore numeric key label
            SetSlotOverlay(i, SlotOverlayState.Ready);
        }

        if (evt.Slots.IsDefaultOrEmpty)
        {
            GD.Print("[HudSkillHotbar] OnHotbarInitialized: empty snapshot — all slots cleared. " +
                     "spec: Docs/RE/packets/4-1_game_state_tick.yaml (HotbarSlots note).");
            return;
        }

        var wiredCount = 0;
        var skippedCount = 0;

        foreach (var entry in evt.Slots)
        {
            // spec: Docs/RE/packets/4-1_game_state_tick.yaml (HotbarSlots note — SlotIndex 0..239).
            if (entry.SlotIndex >= SlotCount)
            {
                // This entry belongs to the 240-slot model but falls outside the 18 visible slots.
                // spec: Docs/RE/packets/4-1_game_state_tick.yaml (HotbarSlots note).
                skippedCount++;
                continue;
            }

            // EntryKey is RAW — skill-vs-item category is pending; no resolver in layer 05.
            // Write the raw key as the slot label and leave the icon null.
            // spec: Docs/RE/packets/4-1_game_state_tick.yaml (HotbarSlots note — "EntryKey raw; category-pending").
            if ((uint)entry.SlotIndex < (uint)_slotKeyLabels.Length && _slotKeyLabels[entry.SlotIndex] is not null)
                _slotKeyLabels[entry.SlotIndex].Text = $"#{entry.EntryKey}";

            // Icon: no clean by-key path in HudIconLibrary without category resolution; leave null.
            // spec: Docs/RE/packets/4-1_game_state_tick.yaml (HotbarSlots note).
            SetSlotIcon(entry.SlotIndex, null);
            // Overlay stays at Ready (default) — cooldown/charge state comes from separate events.
            SetSlotOverlay(entry.SlotIndex, SlotOverlayState.Ready);

            GD.Print(
                $"[HudSkillHotbar] slot {entry.SlotIndex} occupied: EntryKey={entry.EntryKey} Count={entry.Count} — " +
                "skill-vs-item category-pending — icon deferred (raw EntryKey shown). " +
                "spec: Docs/RE/packets/4-1_game_state_tick.yaml (HotbarSlots note).");
            wiredCount++;
        }

        GD.Print($"[HudSkillHotbar] OnHotbarInitialized complete: {wiredCount} slot(s) wired, " +
                 $"{skippedCount} slot(s) outside visible bar (SlotIndex >= {SlotCount}) noted. " +
                 "Icons deferred pending skill-vs-item category resolver (world-campaign). " +
                 "spec: Docs/RE/packets/4-1_game_state_tick.yaml (HotbarSlots note).");
    }

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