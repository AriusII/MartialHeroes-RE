
using Godot;

namespace MartialHeroes.Client.Godot.Ui.Widgets;

public sealed class HudButton : HudWidget
{

    private static readonly Color DisabledCaptionColor = new(0x66 / 255f, 0x66 / 255f, 0x66 / 255f);

    private static readonly Color HoveredCaptionColor = new(1f, 1f, 0f);


    private readonly TextureButton _btn;
    private readonly Label _captionLabel;

    private readonly Color _captionTint;


    public HudButton(
        int x, int y, int w, int h,
        Texture2D? normalFrame,
        Texture2D? hoverFrame = null,
        Texture2D? pressedFrame = null,
        string caption = "",
        Color? captionTint = null,
        int captionFontSlot = 0)
    {
        _btn = new TextureButton
        {
            Position = new Vector2(x, y),
            Size = new Vector2(w, h),
            CustomMinimumSize = new Vector2(w, h),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.Keep
        };

        _btn.SetMeta("is_hud_button", Variant.From(true));

        _btn.TextureNormal = normalFrame;
        _btn.TextureHover = hoverFrame ?? normalFrame;
        _btn.TexturePressed = pressedFrame ?? normalFrame;
        _btn.TextureDisabled = normalFrame;
        _btn.TextureFocused = normalFrame;

        _captionLabel = new Label
        {
            Text = caption,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorsPreset = (int)Control.LayoutPreset.FullRect,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };

        _captionTint = captionTint ?? Colors.White;
        _captionLabel.AddThemeColorOverride("font_color", _captionTint);

        HudFont.ApplyToLabel(_captionLabel, captionFontSlot);

        _btn.AddChild(_captionLabel);

        _btn.MouseEntered += () =>
        {
            _captionLabel.AddThemeColorOverride("font_color", HoveredCaptionColor);
        };
        _btn.MouseExited += () =>
        {
            _captionLabel.AddThemeColorOverride("font_color",
                _btn.Disabled ? DisabledCaptionColor : _captionTint);
        };

        _btn.Pressed += FireAction;
    }


    public string Caption
    {
        get => _captionLabel.Text;
        set => _captionLabel.Text = value;
    }

    public bool IsDisabled
    {
        get => _btn.Disabled;
        set
        {
            _btn.Disabled = value;
            _captionLabel.AddThemeColorOverride("font_color",
                value ? DisabledCaptionColor : _captionTint);
        }
    }


    public override Control? GetControl()
    {
        return _btn;
    }
}