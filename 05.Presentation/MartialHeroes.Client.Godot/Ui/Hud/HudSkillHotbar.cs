using Godot;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudSkillHotbar : Control
{
    public enum SlotOverlayState
    {
        Ready,

        Cooldown,

        Charge
    }

    private const float ContainerX = 349f;

    private const float ContainerY = 13f;

    private const float ContainerAnchorW = 7f;

    private const float ContainerAnchorH = 504f;

    private const int ContainerInnerX = 982;

    private const int SlotLoopCount = 2;
    private const int SlotsPerLoop = 9;
    private const int SlotCount = SlotLoopCount * SlotsPerLoop;

    private const float CellFallbackW = 58f;
    private const float CellFallbackH = 58f;

    private const int OverlayTexId = 3;
    private const int OverlayReadySrcX = 821;
    private const int OverlayReadySrcY = 655;
    private const int OverlayCooldownSrcX = 792;
    private const int OverlayCooldownSrcY = 655;
    private const int OverlayChargeSrcX = 763;
    private const int OverlayChargeSrcY = 655;
    private const int OverlaySide = 29;

    private const float OverlayDstOffX = 12f;
    private const float OverlayDstOffY = 12f;

    private const int SkillpipeTexId = 10;
    private readonly TextureRect?[] _overlayCharge = new TextureRect?[SlotCount];
    private readonly TextureRect?[] _overlayCooldown = new TextureRect?[SlotCount];

    private readonly TextureRect?[] _overlayReady = new TextureRect?[SlotCount];
    private readonly SlotOverlayState[] _overlayState = new SlotOverlayState[SlotCount];


    private readonly TextureRect[] _slotIcons = new TextureRect[SlotCount];
    private readonly Label[] _slotKeyLabels = new Label[SlotCount];


    public void Build(HudAtlasLibrary atlas, HudIconLibrary icons)
    {
        Name = "HudSkillHotbar";

        Position = new Vector2(ContainerX, ContainerY);
        Size = new Vector2(ContainerAnchorW, ContainerAnchorH);
        MouseFilter = MouseFilterEnum.Ignore;
        _ = ContainerInnerX;

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

        var pipeTex = atlas.GetById(SkillpipeTexId);

        for (var i = 0; i < SlotCount; i++)
        {
            var loopIdx = i / SlotsPerLoop;
            var slotInLoop = i % SlotsPerLoop;
            var slotX = slotInLoop * CellFallbackW;
            var slotY = loopIdx * CellFallbackH;

            var slot = new Control
            {
                Name = $"SkillSlot{i}",
                Position = new Vector2(slotX, slotY),
                Size = new Vector2(CellFallbackW, CellFallbackH),
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(slot);

            var bg = new Panel { Name = "Bg" };
            bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            var bgStyle = new StyleBoxFlat();
            bgStyle.BgColor = new Color(0.08f, 0.08f, 0.08f, 0.78f);
            bgStyle.SetBorderWidthAll(1);
            bgStyle.BorderColor = new Color(0.45f, 0.45f, 0.45f, 0.85f);
            bg.AddThemeStyleboxOverride("panel", bgStyle);
            slot.AddChild(bg);

            if (pipeTex is not null)
            {
                var chromeTex = new TextureRect
                {
                    Name = "Chrome",
                    Texture = pipeTex,
                    StretchMode = TextureRect.StretchModeEnum.Scale,
                    MouseFilter = MouseFilterEnum.Ignore,
                    ZIndex = -1
                };
                chromeTex.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                slot.AddChild(chromeTex);
            }

            var icon = new TextureRect
            {
                Name = "Icon",
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore
            };
            icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            icon.OffsetLeft = 2f;
            icon.OffsetTop = 2f;
            icon.OffsetRight = -2f;
            icon.OffsetBottom = -2f;
            slot.AddChild(icon);
            _slotIcons[i] = icon;

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
            HudFont.ApplyToLabel(keyLabel, 4);
            slot.AddChild(keyLabel);
            _slotKeyLabels[i] = keyLabel;


            var overlayReady = new TextureRect
            {
                Name = "OverlayReady",
                Texture = readyTex,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Position = new Vector2(OverlayDstOffX, OverlayDstOffY),
                Size = new Vector2(OverlaySide, OverlaySide),
                MouseFilter = MouseFilterEnum.Ignore,
                Visible = true,
                ZIndex = 1
            };
            slot.AddChild(overlayReady);
            _overlayReady[i] = overlayReady;

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


    public void OnHotbarInitialized(HotbarInitializedEvent evt)
    {
        for (var i = 0; i < SlotCount; i++)
        {
            SetSlotIcon(i, null);
            if ((uint)i < (uint)_slotKeyLabels.Length && _slotKeyLabels[i] is not null)
                _slotKeyLabels[i].Text = $"{i + 1}";
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
            if (entry.SlotIndex >= SlotCount)
            {
                skippedCount++;
                continue;
            }

            if ((uint)entry.SlotIndex < (uint)_slotKeyLabels.Length && _slotKeyLabels[entry.SlotIndex] is not null)
                _slotKeyLabels[entry.SlotIndex].Text = $"#{entry.EntryKey}";

            SetSlotIcon(entry.SlotIndex, null);
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

    public void SetSlotIcon(int slotIndex, AtlasTexture? icon)
    {
        if ((uint)slotIndex >= SlotCount) return;
        _slotIcons[slotIndex].Texture = icon;
    }

    public void SetSlotOverlay(int slotIndex, SlotOverlayState state)
    {
        if ((uint)slotIndex >= SlotCount) return;
        if (_overlayState[slotIndex] == state) return;

        _overlayState[slotIndex] = state;

        if (_overlayReady[slotIndex] is not null) _overlayReady[slotIndex]!.Visible = state == SlotOverlayState.Ready;
        if (_overlayCooldown[slotIndex] is not null)
            _overlayCooldown[slotIndex]!.Visible = state == SlotOverlayState.Cooldown;
        if (_overlayCharge[slotIndex] is not null)
            _overlayCharge[slotIndex]!.Visible = state == SlotOverlayState.Charge;
    }
}