// Ui/Widgets/HudWidgetFactory.cs
//
// Widget factory for the shared HUD substrate.
//
// Mirrors the legacy front-end builder argument contract:
//   WidgetRect = (X, Y, W, H, SrcX, SrcY), and every atlas blit samples W×H then draws W×H.
// spec: Docs/RE/specs/frontend_scenes.md — widget builder primitives.
//
// Factory methods:
//   - MakeButton / MakeButton2 / MakeButton3  — 7-state / 2-state / 3-state atlas buttons.
//   - MakeLabel                                — fixed-advance CP949 label.
//   - MakeTextbox                              — CP949 text input (password option).
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
///     Factory for creating GU-faithful HUD widgets backed by VFS atlas textures.
///     <para>
///         All methods accept a <see cref="HudAtlasLibrary" /> for atlas access; they degrade
///         gracefully when the VFS is offline (null atlas → widget with no sprite background,
///         functional but invisible until VFS is available).
///     </para>
///     <para>No game logic here — this is a pure construction API.</para>
///     spec: Docs/RE/specs/frontend_scenes.md — constructor arg order and 1:1 atlas rectangles.
/// </summary>
public static class HudWidgetFactory
{
    // -------------------------------------------------------------------------
    // Button factories
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Compatibility adapter for older call sites whose positional order is NORMAL, PRESSED, HOVER.
    ///     Prefer <see cref="BuildButton3State" /> for the IDA-confirmed NORMAL, HOVER, PRESSED contract.
    /// </summary>
    public static HudButton MakeButton(
        HudAtlasLibrary atlas,
        string vfsPath,
        int x, int y, int w, int h,
        int normalSrcX, int normalSrcY,
        int pressedSrcX, int pressedSrcY,
        int hoverSrcX, int hoverSrcY,
        int actionId,
        string caption = "",
        Color? captionTint = null,
        int fontSlot = 0)
    {
        return BuildButton3State(atlas, vfsPath, x, y, w, h,
            normalSrcX, normalSrcY,
            hoverSrcX, hoverSrcY,
            pressedSrcX, pressedSrcY,
            actionId: actionId,
            caption: caption,
            captionTint: captionTint,
            fontSlot: fontSlot);
    }

    /// <summary>
    ///     IDA-confirmed 3-state button builder:
    ///     BuildButton3State(tex, X, Y, W, H, NormSrcX, NormSrcY, HovSrcX, HovSrcY, PrsSrcX, PrsSrcY, color).
    ///     spec: Docs/RE/specs/frontend_scenes.md — source extent equals destination W×H; no scaling.
    /// </summary>
    public static HudButton BuildButton3State(
        HudAtlasLibrary atlas,
        string vfsPath,
        int x, int y, int w, int h,
        int normalSrcX, int normalSrcY,
        int hoverSrcX, int hoverSrcY,
        int pressedSrcX, int pressedSrcY,
        int color = -1,
        int actionId = -1,
        string caption = "",
        Color? captionTint = null,
        int fontSlot = 0)
    {
        Texture2D? normal = atlas.SliceByPath(vfsPath, normalSrcX, normalSrcY, w, h);
        Texture2D? hover = atlas.SliceByPath(vfsPath, hoverSrcX, hoverSrcY, w, h);
        Texture2D? pressed = atlas.SliceByPath(vfsPath, pressedSrcX, pressedSrcY, w, h);

        var tint = captionTint ?? ToGodotColor(color);
        return new HudButton(x, y, w, h,
            normal,
            hover,
            pressed,
            caption,
            tint,
            fontSlot)
        {
            ActionId = actionId
        };
    }

    /// <summary>
    ///     Creates a 2-state atlas button (HOVER = PRESSED = NORMAL; only caption tint changes on hover).
    ///     spec: Docs/RE/specs/ui_system.md §1.5 — "2-state: HOVER = NORMAL, PRESSED = NORMAL".
    /// </summary>
    public static HudButton MakeButton2(
        HudAtlasLibrary atlas,
        string vfsPath,
        int x, int y, int w, int h,
        int normalSrcX, int normalSrcY,
        int actionId,
        string caption = "",
        Color? captionTint = null,
        int fontSlot = 0)
    {
        return GUButton(atlas, vfsPath, x, y, w, h,
            normalSrcX, normalSrcY,
            actionId: actionId,
            caption: caption,
            captionTint: captionTint,
            fontSlot: fontSlot);
    }

    /// <summary>
    ///     Creates a 3-state atlas button (HOVER distinct; PRESSED = NORMAL).
    ///     spec: Docs/RE/specs/ui_system.md §1.5 — "3-state: NORMAL + PRESSED; HOVER may differ".
    /// </summary>
    public static HudButton MakeButton3(
        HudAtlasLibrary atlas,
        string vfsPath,
        int x, int y, int w, int h,
        int normalSrcX, int normalSrcY,
        int hoverSrcX, int hoverSrcY,
        int actionId,
        string caption = "",
        Color? captionTint = null,
        int fontSlot = 0)
    {
        return BuildButton3State(atlas, vfsPath, x, y, w, h,
            normalSrcX, normalSrcY,
            hoverSrcX, hoverSrcY,
            normalSrcX, normalSrcY, // PRESSED = NORMAL
            actionId: actionId,
            caption: caption,
            captionTint: captionTint,
            fontSlot: fontSlot);
    }

    /// <summary>
    ///     IDA-confirmed 1-state button builder:
    ///     GUButton(tex, X, Y, W, H, SrcX, SrcY, color).
    ///     spec: Docs/RE/specs/frontend_scenes.md — scroll/pager arrows are plain one-state buttons.
    /// </summary>
    public static HudButton GUButton(
        HudAtlasLibrary atlas,
        string vfsPath,
        int x, int y, int w, int h,
        int srcX, int srcY,
        int color = -1,
        int actionId = -1,
        string caption = "",
        Color? captionTint = null,
        int fontSlot = 0)
    {
        Texture2D? frame = atlas.SliceByPath(vfsPath, srcX, srcY, w, h);
        return new HudButton(x, y, w, h,
            frame,
            frame,
            frame,
            caption,
            captionTint ?? ToGodotColor(color),
            fontSlot)
        {
            ActionId = actionId
        };
    }

    // -------------------------------------------------------------------------
    // Label factory
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Creates a fixed-advance CP949 label.
    ///     spec: Docs/RE/specs/ui_system.md §6.3 — fixed-advance grid rendering.
    ///     spec: Docs/RE/specs/ui_system.md §6.2 — 15-slot font table.
    /// </summary>
    public static HudLabel MakeLabel(
        int x, int y, int w, int h,
        string text = "",
        Color? color = null,
        int fontSlot = 0,
        bool multiline = false)
    {
        return new HudLabel(x, y, w, h, text, color, fontSlot, multiline);
    }

    /// <summary>
    ///     IDA-confirmed panel builder:
    ///     BuildPanel(tex, X, Y, W, H, SrcX, SrcY, modalFlag, color).
    ///     spec: Docs/RE/specs/frontend_scenes.md — source extent equals destination W×H; no scaling.
    /// </summary>
    public static HudPanel BuildPanel(
        HudAtlasLibrary atlas,
        string vfsPath,
        int x, int y, int w, int h,
        int srcX, int srcY,
        bool modalFlag,
        int color = -1)
    {
        Texture2D? bg = atlas.SliceByPath(vfsPath, srcX, srcY, w, h);
        HudPanel panel = new(x, y, w, h, bg, modalFlag);
        if (panel.GetControl() is CanvasItem item) item.Modulate = ToGodotColor(color);
        return panel;
    }

    /// <summary>
    ///     Creates a transparent container panel for pure C# composition.
    ///     spec: Docs/RE/specs/frontend_scenes.md — GUPanel AddChild/AddChildWithAction composition.
    /// </summary>
    public static HudPanel BuildTransparentPanel(int x, int y, int w, int h, bool modalFlag = false)
    {
        return new HudPanel(x, y, w, h, null, modalFlag);
    }

    // -------------------------------------------------------------------------
    // Textbox factory
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Creates a CP949 text input widget.
    ///     spec: Docs/RE/specs/ui_system.md §5 — GUTextbox.
    ///     spec: Docs/RE/specs/ui_system.md §5.2 — "password: 6 px/char advance": CONFIRMED.
    /// </summary>
    public static HudTextbox MakeTextbox(
        int x, int y, int w, int h,
        bool password = false,
        int maxLength = 0,
        int fontSlot = 0)
    {
        return new HudTextbox(x, y, w, h, password, maxLength, fontSlot);
    }

    /// <summary>
    ///     IDA-confirmed textbox builder:
    ///     GUTextbox(tex, X, Y, W, H, SrcX, SrcY, color); IME/max length/action are set separately.
    ///     spec: Docs/RE/specs/frontend_scenes.md — textbox atlas rect is W×H sampled 1:1.
    /// </summary>
    public static HudTextbox BuildTextbox(
        HudAtlasLibrary atlas,
        string vfsPath,
        int x, int y, int w, int h,
        int srcX, int srcY,
        int color = -1,
        int imeMode = 0,
        bool password = false,
        int maxLength = 0,
        int fontSlot = 0,
        int actionId = -1)
    {
        Texture2D? bg = atlas.SliceByPath(vfsPath, srcX, srcY, w, h);
        HudTextbox textbox = new(x, y, w, h, password, maxLength, fontSlot, bg)
        {
            ImeMode = imeMode,
            ActionId = actionId
        };
        if (textbox.GetControl() is CanvasItem item) item.Modulate = ToGodotColor(color);
        return textbox;
    }

    // -------------------------------------------------------------------------
    // Checkbox factory
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Creates an atlas-backed toggle checkbox: off source then on source.
    ///     spec: Docs/RE/specs/frontend_scenes.md — GUCheckBox(tex, X, Y, W, H, OffSrcX, OffSrcY, OnSrcX, OnSrcY, color).
    /// </summary>
    public static HudCheckbox MakeCheckbox(
        HudAtlasLibrary atlas,
        string vfsPath,
        int x, int y, int w, int h,
        int offSrcX, int offSrcY,
        int onSrcX, int onSrcY,
        int actionId)
    {
        Texture2D? normal = atlas.SliceByPath(vfsPath, offSrcX, offSrcY, w, h);
        Texture2D? pressed = atlas.SliceByPath(vfsPath, onSrcX, onSrcY, w, h);

        return new HudCheckbox(x, y, w, h, normal, pressed)
        {
            ActionId = actionId
        };
    }

    // -------------------------------------------------------------------------
    // Atlas sprite rect (image-only, no button behaviour)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     IDA-confirmed image builder:
    ///     BuildImageComponent(tex, X, Y, W, H, SrcX, SrcY, color).
    ///     Returns null when the atlas is offline.
    ///     spec: Docs/RE/specs/ui_system.md §1.3 — atlas pixel 1:1 on 1024×768 canvas.
    /// </summary>
    public static TextureRect? BuildImageComponent(
        HudAtlasLibrary atlas,
        string vfsPath,
        int x, int y, int w, int h,
        int srcX, int srcY,
        int color = -1)
    {
        Texture2D? tex = atlas.SliceByPath(vfsPath, srcX, srcY, w, h);
        if (tex is null) return null;

        return new TextureRect
        {
            Texture = tex,
            Position = new Vector2(x, y),
            Size = new Vector2(w, h),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            // The texture is sliced to W×H above; draw at native size to preserve 1:1 pixels.
            // spec: Docs/RE/specs/frontend_scenes.md — source extent is forced to destination W×H.
            StretchMode = TextureRect.StretchModeEnum.Keep,
            Modulate = ToGodotColor(color),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
    }

    public static TextureRect? MakeAtlasRect(
        HudAtlasLibrary atlas,
        string vfsPath,
        int x, int y, int w, int h,
        int srcX, int srcY)
    {
        return BuildImageComponent(atlas, vfsPath, x, y, w, h, srcX, srcY);
    }

    private static Color ToGodotColor(int argb)
    {
        if (argb == -1) return Colors.White;
        var a = (byte)((argb >> 24) & 0xFF);
        var r = (byte)((argb >> 16) & 0xFF);
        var g = (byte)((argb >> 8) & 0xFF);
        var b = (byte)(argb & 0xFF);
        return Color.Color8(r, g, b, a);
    }
}