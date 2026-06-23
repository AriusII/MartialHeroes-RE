
using Godot;

namespace MartialHeroes.Client.Godot.Ui.Widgets;

public sealed class HudCheckbox : HudWidget
{
    private readonly TextureButton _check;

    public HudCheckbox(int x, int y, int w, int h,
        Texture2D? normalFrame = null,
        Texture2D? pressedFrame = null)
    {
        var checkedTex = pressedFrame ?? normalFrame;

        _check = new TextureButton
        {
            Position = new Vector2(x, y),
            Size = new Vector2(w, h),
            CustomMinimumSize = new Vector2(w, h),
            ToggleMode = true,
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.Scale,
            TextureNormal = normalFrame,
            TexturePressed = checkedTex,
            TextureHover = normalFrame,
            TextureDisabled = normalFrame
        };

        _check.Toggled += pressed =>
        {
            FireAction();
            Toggled?.Invoke(pressed);
        };
    }

    public bool IsChecked
    {
        get => _check.ButtonPressed;
        set => _check.ButtonPressed = value;
    }

    public event Action<bool>? Toggled;

    public override Control? GetControl()
    {
        return _check;
    }
}