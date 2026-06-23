
using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Widgets;

public static class HudWidgetFactory
{

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
            normalSrcX, normalSrcY,
            actionId: actionId,
            caption: caption,
            captionTint: captionTint,
            fontSlot: fontSlot);
    }

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


    public static HudLabel MakeLabel(
        int x, int y, int w, int h,
        string text = "",
        Color? color = null,
        int fontSlot = 0,
        bool multiline = false)
    {
        return new HudLabel(x, y, w, h, text, color, fontSlot, multiline);
    }

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

    public static HudPanel BuildTransparentPanel(int x, int y, int w, int h, bool modalFlag = false)
    {
        return new HudPanel(x, y, w, h, null, modalFlag);
    }


    public static HudTextbox MakeTextbox(
        int x, int y, int w, int h,
        bool password = false,
        int maxLength = 0,
        int fontSlot = 0)
    {
        return new HudTextbox(x, y, w, h, password, maxLength, fontSlot);
    }

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