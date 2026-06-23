
using Godot;

namespace MartialHeroes.Client.Godot.Ui.Widgets;

public sealed class HudPanel : HudWidget
{
    private readonly Control _root;


    public HudPanel(int x, int y, int w, int h, Texture2D? background = null, bool modalFlag = false)
    {
        _root = new Control
        {
            Position = new Vector2(x, y),
            Size = new Vector2(w, h),
            CustomMinimumSize = new Vector2(w, h),
            ClipContents = modalFlag,
            MouseFilter = modalFlag ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Pass
        };

        if (background is not null)
        {
            var bg = new TextureRect
            {
                Texture = background,
                AnchorsPreset = (int)Control.LayoutPreset.FullRect,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Keep,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            _root.AddChild(bg);
        }
    }


    public override Control? GetControl()
    {
        return _root;
    }
}