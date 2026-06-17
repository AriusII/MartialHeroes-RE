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
// Overlay rect VALUES are data-driven (debugger-pending). The three status frames at
// atlas source X = 763/792/821 are mutually exclusive frames in ONE destination cell,
// not three separate destination offsets.
// TODO(spec): hotbar overlay-rect values debugger-pending.
//
// spec: Docs/RE/specs/ui_hud_layout.md §3.5 — container origin CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_hud_layout.md §5.10 — data-driven nine-slot loop.
// spec: Docs/RE/specs/ui_system.md §8.6.1 — uitex 10/11 = skillpipe.dds / skillpipe_02.dds.

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
/// Player skill hotbar: container at (349, 13) holding 9 data-driven skill slots.
///
/// <para>PASSIVE: reads HudIconLibrary for skill icons; zero game logic.
/// Per-slot base X/Y from registry deferred (uses fallback 58×58 grid until world-campaign wires
/// the live registry).</para>
///
/// spec: Docs/RE/specs/ui_hud_layout.md §3.5 / §5.7 CODE-CONFIRMED.
/// </summary>
public sealed partial class HudSkillHotbar : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // spec: Docs/RE/specs/ui_hud_layout.md §3.5 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private const float ContainerX = 349f; // spec: ui_hud_layout.md §3.5
    private const float ContainerY = 13f;  // spec: ui_hud_layout.md §3.5
    private const int SlotCount = 9;       // spec: ui_hud_layout.md §3.5 — "nine skill slots"

    // Layout variants by entity KIND word (CODE-CONFIRMED)
    // spec: ui_hud_layout.md §3.5 — KIND table
    private const float CellFallbackW = 58f; // spec: ui_hud_layout.md §3.5 — fallback cell 58×58
    private const float CellFallbackH = 58f; // spec: ui_hud_layout.md §3.5 — fallback cell 58×58

    // skillpipe.dds uitex id 10 for the hotbar chrome
    // spec: Docs/RE/specs/ui_system.md §8.6.1 — uitex 10 = data/ui/skillpipe.dds
    private const int SkillpipeTexId = 10; // spec: ui_system.md §8.6.1

    // -------------------------------------------------------------------------
    // Child controls
    // -------------------------------------------------------------------------

    private readonly TextureRect[] _slotIcons = new TextureRect[SlotCount];
    private readonly Label[] _slotKeyLabels = new Label[SlotCount];

    // -------------------------------------------------------------------------
    // Build (geometry pass)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Geometry pass: positions the hotbar container at (349, 13) and builds 9 slots.
    ///
    /// Uses fallback 58×58 cells (smallest observed variant) until the live slot registry
    /// is available via the world-campaign follow-up.
    ///
    /// spec: Docs/RE/specs/ui_hud_layout.md §3.5 CODE-CONFIRMED container origin.
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

        // Optional: load hotbar chrome from skillpipe.dds (uitex 10).
        // spec: ui_system.md §8.6.1 — uitex 10 = data/ui/skillpipe.dds
        Texture2D? pipeTex = atlas.GetById(SkillpipeTexId);

        // Build 9 skill slots in a horizontal row (fallback layout).
        // Real layout reads base X/Y from the runtime slot registry (+0x10 / +0x14 biased −92).
        // TODO(world-campaign): replace with registry-driven slot positions.
        for (int i = 0; i < SlotCount; i++)
        {
            float slotX = i * CellFallbackW;

            var slot = new Control
            {
                Name = $"SkillSlot{i}",
                Position = new Vector2(slotX, 0f), // placeholder; registry gives real X/Y
                Size = new Vector2(CellFallbackW, CellFallbackH),
                MouseFilter = MouseFilterEnum.Ignore,
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

            // Icon TextureRect — centre of the slot cell
            var icon = new TextureRect
            {
                Name = "Icon",
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
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
                MouseFilter = MouseFilterEnum.Ignore,
            };
            keyLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
            keyLabel.OffsetRight = CellFallbackW;
            keyLabel.OffsetBottom = 16f;
            slot.AddChild(keyLabel);
            _slotKeyLabels[i] = keyLabel;

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
                };
                chromeTex.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                slot.AddChild(chromeTex);
                chromeTex.ZIndex = -1; // behind bg panel
            }
        }

        GD.Print($"[HudSkillHotbar] Built — container at ({ContainerX},{ContainerY}), {SlotCount} slots " +
                 $"× {CellFallbackW}×{CellFallbackH} (fallback; registry data-driven pending). " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §3.5 CODE-CONFIRMED origin. " +
                 "TODO(spec): hotbar overlay-rect values debugger-pending.");
    }

    /// <summary>
    /// Sets the skill icon for the slot at index <paramref name="slotIndex"/> (0-based).
    /// Called by the world-campaign HUD wiring when the hotbar-slot-set event arrives.
    /// TODO(world-campaign): connect to SkillHotbarSlotSetEvent drain.
    /// </summary>
    public void SetSlotIcon(int slotIndex, AtlasTexture? icon)
    {
        if ((uint)slotIndex >= (uint)SlotCount) return;
        _slotIcons[slotIndex].Texture = icon;
    }
}
