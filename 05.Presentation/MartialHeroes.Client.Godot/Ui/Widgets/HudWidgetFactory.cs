// Ui/Widgets/HudWidgetFactory.cs
//
// Widget factory for the shared HUD substrate.
//
// Mirrors the legacy GUWindow/GUPanel widget-constructor argument contract:
//   Constructor(textureId, x, y, w, h, srcX, srcY, actionId)
// spec: Docs/RE/specs/ui_system.md §1.1 — universal constructor argument order: CODE-CONFIRMED.
//
// Factory methods:
//   - MakeButton / MakeButton2 / MakeButton3  — 7-state / 2-state / 3-state atlas buttons.
//   - MakeLabel                                — fixed-advance CP949 label.
//   - MakePanel                                — atlas-backed container.
//   - MakeList                                 — scrollable list.
//   - MakeTextbox                              — CP949 text input (password option).
//   - MakeScrollbar                            — vertical scrollbar.
//   - MakeCheckbox                             — atlas-backed toggle.
//
// All factory methods degrade gracefully offline (return a widget whose atlas frames are
// null — the widget renders without a sprite but is otherwise functional).
//
// spec: Docs/RE/specs/ui_system.md §1.1, §1.5, §6.2, §6.3.

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Widgets;

/// <summary>
/// Factory for creating GU-faithful HUD widgets backed by VFS atlas textures.
///
/// <para>All methods accept a <see cref="HudAtlasLibrary"/> for atlas access; they degrade
/// gracefully when the VFS is offline (null atlas → widget with no sprite background,
/// functional but invisible until VFS is available).</para>
///
/// <para>No game logic here — this is a pure construction API.</para>
///
/// spec: Docs/RE/specs/ui_system.md §1.1 — constructor arg order: CODE-CONFIRMED.
/// </summary>
public static class HudWidgetFactory
{
    // -------------------------------------------------------------------------
    // Button factories
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a 7-state (3-distinct-frame) atlas button.
    ///
    /// <para>Frame argument order matches the front-end 3-state builder convention:
    /// NORMAL, PRESSED, HOVER (spec §1.5 — "3-state takes N, P, H order": CODE-CONFIRMED).</para>
    ///
    /// spec: Docs/RE/specs/ui_system.md §1.5 — "7-state constructor fills NORMAL, HOVER, PRESSED".
    /// </summary>
    public static HudButton MakeButton(
        HudAtlasLibrary atlas,
        string vfsPath,
        int x, int y, int w, int h,
        int normalSrcX, int normalSrcY,
        int pressedSrcX, int pressedSrcY,
        int hoverSrcX, int hoverSrcY,
        int actionId,
        string caption       = "",
        Color? captionTint   = null,
        int fontSlot         = 0)
    {
        // spec: §1.5 — 3-state argument order NORMAL, PRESSED, HOVER: CODE-CONFIRMED.
        Texture2D? normal  = atlas.SliceByPath(vfsPath, normalSrcX,  normalSrcY,  w, h);
        Texture2D? pressed = atlas.SliceByPath(vfsPath, pressedSrcX, pressedSrcY, w, h);
        Texture2D? hover   = atlas.SliceByPath(vfsPath, hoverSrcX,   hoverSrcY,   w, h);

        return new HudButton(x, y, w, h,
            normalFrame:  normal,
            hoverFrame:   hover,
            pressedFrame: pressed,
            caption:      caption,
            captionTint:  captionTint,
            captionFontSlot: fontSlot)
        {
            ActionId = actionId,
        };
    }

    /// <summary>
    /// Creates a 2-state atlas button (HOVER = PRESSED = NORMAL; only caption tint changes on hover).
    ///
    /// spec: Docs/RE/specs/ui_system.md §1.5 — "2-state: HOVER = NORMAL, PRESSED = NORMAL".
    /// </summary>
    public static HudButton MakeButton2(
        HudAtlasLibrary atlas,
        string vfsPath,
        int x, int y, int w, int h,
        int normalSrcX, int normalSrcY,
        int actionId,
        string caption       = "",
        Color? captionTint   = null,
        int fontSlot         = 0)
        => MakeButton(atlas, vfsPath, x, y, w, h,
            normalSrcX, normalSrcY,
            normalSrcX, normalSrcY, // PRESSED = NORMAL
            normalSrcX, normalSrcY, // HOVER   = NORMAL
            actionId, caption, captionTint, fontSlot);

    /// <summary>
    /// Creates a 3-state atlas button (HOVER distinct; PRESSED = NORMAL).
    ///
    /// spec: Docs/RE/specs/ui_system.md §1.5 — "3-state: NORMAL + PRESSED; HOVER may differ".
    /// </summary>
    public static HudButton MakeButton3(
        HudAtlasLibrary atlas,
        string vfsPath,
        int x, int y, int w, int h,
        int normalSrcX, int normalSrcY,
        int hoverSrcX, int hoverSrcY,
        int actionId,
        string caption       = "",
        Color? captionTint   = null,
        int fontSlot         = 0)
        => MakeButton(atlas, vfsPath, x, y, w, h,
            normalSrcX, normalSrcY,
            normalSrcX, normalSrcY, // PRESSED = NORMAL
            hoverSrcX,  hoverSrcY,
            actionId, caption, captionTint, fontSlot);

    // -------------------------------------------------------------------------
    // Atlas button using uitex id
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a 2-state button from a uitex.txt tex_id atlas.
    ///
    /// spec: Docs/RE/formats/ui_manifests.md §1.4 — confirmed tex_id→path table.
    /// </summary>
    public static HudButton MakeButtonById(
        HudAtlasLibrary atlas,
        int texId,
        int x, int y, int w, int h,
        int normalSrcX, int normalSrcY,
        int actionId,
        string caption     = "",
        Color? captionTint = null,
        int fontSlot       = 0)
    {
        Texture2D? frame = atlas.SliceById(texId, normalSrcX, normalSrcY, w, h);
        return new HudButton(x, y, w, h,
            normalFrame:  frame,
            hoverFrame:   frame,
            pressedFrame: frame,
            caption:      caption,
            captionTint:  captionTint,
            captionFontSlot: fontSlot)
        {
            ActionId = actionId,
        };
    }

    // -------------------------------------------------------------------------
    // Label factory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a fixed-advance CP949 label.
    ///
    /// spec: Docs/RE/specs/ui_system.md §6.3 — fixed-advance grid rendering.
    /// spec: Docs/RE/specs/ui_system.md §6.2 — 15-slot font table.
    /// </summary>
    public static HudLabel MakeLabel(
        int x, int y, int w, int h,
        string text     = "",
        Color? color    = null,
        int fontSlot    = 0,
        bool multiline  = false)
        => new(x, y, w, h, text, color, fontSlot, multiline);

    // -------------------------------------------------------------------------
    // Panel factory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates an atlas-backed container panel.
    ///
    /// spec: Docs/RE/specs/ui_system.md §1 — GUPanel container role.
    /// </summary>
    public static HudPanel MakePanel(
        HudAtlasLibrary atlas,
        string vfsPath,
        int x, int y, int w, int h,
        int srcX, int srcY)
    {
        Texture2D? bg = atlas.SliceByPath(vfsPath, srcX, srcY, w, h);
        return new HudPanel(x, y, w, h, bg);
    }

    /// <summary>
    /// Creates a transparent (no-background) container panel.
    ///
    /// spec: Docs/RE/specs/ui_system.md §1 — GUPanel used as a container with no background.
    /// </summary>
    public static HudPanel MakePanel(int x, int y, int w, int h)
        => new(x, y, w, h, null);

    // -------------------------------------------------------------------------
    // List factory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a scrollable list.
    ///
    /// spec: Docs/RE/specs/ui_system.md §1 — GUList.
    /// spec: Docs/RE/specs/ui_system.md §4.4 — reverse hit-test.
    /// </summary>
    public static HudList MakeList(int x, int y, int w, int h)
        => new(x, y, w, h);

    // -------------------------------------------------------------------------
    // Textbox factory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a CP949 text input widget.
    ///
    /// spec: Docs/RE/specs/ui_system.md §5 — GUTextbox.
    /// spec: Docs/RE/specs/ui_system.md §5.2 — "password: 6 px/char advance": CONFIRMED.
    /// </summary>
    public static HudTextbox MakeTextbox(
        int x, int y, int w, int h,
        bool password  = false,
        int maxLength  = 0,
        int fontSlot   = 0)
        => new(x, y, w, h, password, maxLength, fontSlot);

    // -------------------------------------------------------------------------
    // Scrollbar factory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a vertical scrollbar.
    ///
    /// spec: Docs/RE/specs/ui_system.md §1 — GUScroll.
    /// </summary>
    public static HudScrollbar MakeScrollbar(
        int x, int y, int w, int h,
        double minValue = 0, double maxValue = 100, double pageSize = 10)
        => new(x, y, w, h, minValue, maxValue, pageSize);

    // -------------------------------------------------------------------------
    // Checkbox factory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates an atlas-backed toggle checkbox.
    ///
    /// spec: Docs/RE/specs/ui_system.md §1 — GUCheckBox: "checked = PRESSED frame".
    /// spec: Docs/RE/specs/ui_system.md §1.5 — 3-state ctor order NORMAL, PRESSED, HOVER.
    /// </summary>
    public static HudCheckbox MakeCheckbox(
        HudAtlasLibrary atlas,
        string vfsPath,
        int x, int y, int w, int h,
        int normalSrcX, int normalSrcY,
        int pressedSrcX, int pressedSrcY,
        int actionId)
    {
        Texture2D? normal  = atlas.SliceByPath(vfsPath, normalSrcX,  normalSrcY,  w, h);
        Texture2D? pressed = atlas.SliceByPath(vfsPath, pressedSrcX, pressedSrcY, w, h);

        return new HudCheckbox(x, y, w, h, normal, pressed)
        {
            ActionId = actionId,
        };
    }

    // -------------------------------------------------------------------------
    // Atlas sprite rect (image-only, no button behaviour)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a pure-image <see cref="TextureRect"/> from an atlas sub-rect.
    /// Returns null when the atlas is offline.
    ///
    /// spec: Docs/RE/specs/ui_system.md §1.3 — atlas pixel 1:1 on 1024×768 canvas.
    /// </summary>
    public static TextureRect? MakeAtlasRect(
        HudAtlasLibrary atlas,
        string vfsPath,
        int x, int y, int w, int h,
        int srcX, int srcY)
    {
        Texture2D? tex = atlas.SliceByPath(vfsPath, srcX, srcY, w, h);
        if (tex is null) return null;

        return new TextureRect
        {
            Texture     = tex,
            Position    = new Vector2(x, y),
            Size        = new Vector2(w, h),
            ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }
}
