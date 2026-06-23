using Godot;

namespace MartialHeroes.Client.Godot.Ui.Widgets;

public sealed class HudTextbox : HudWidget
{
    private readonly Control _control;
    private readonly LineEdit _edit;

    private bool _eventsWired;


    public HudTextbox(
        int x, int y, int w, int h,
        bool password = false,
        int maxLength = 0,
        int fontSlot = 0,
        Texture2D? background = null)
    {
        var hasBackground = background is not null;
        Control? root = null;
        if (hasBackground)
        {
            root = new Control
            {
                Position = new Vector2(x, y),
                Size = new Vector2(w, h),
                CustomMinimumSize = new Vector2(w, h),
                MouseFilter = Control.MouseFilterEnum.Pass
            };
            root.AddChild(new TextureRect
            {
                Texture = background,
                AnchorsPreset = (int)Control.LayoutPreset.FullRect,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Keep,
                MouseFilter = Control.MouseFilterEnum.Ignore
            });
        }

        _edit = new LineEdit
        {
            Position = hasBackground ? Vector2.Zero : new Vector2(x, y),
            Size = new Vector2(w, h),
            CustomMinimumSize = new Vector2(w, h),

            Secret = password,

            MaxLength = maxLength > 0 ? maxLength : 0,

            CaretBlink = true,
            CaretBlinkInterval = 0.5f
        };

        if (root is not null)
        {
            root.AddChild(_edit);
            _control = root;
        }
        else
        {
            _control = _edit;
        }

        HudFont.ApplyToLineEdit(_edit, fontSlot);
    }


    public int ImeMode { get; set; }

    public int MaxLength
    {
        get => _edit.MaxLength;
        set => _edit.MaxLength = Math.Max(0, value);
    }

    public string Text
    {
        get => _edit.Text;
        set => _edit.Text = value;
    }

    private event Action<string>? _textChanged;
    private event Action<string>? _textSubmitted;

    private void EnsureEventsWired()
    {
        if (_eventsWired) return;
        _eventsWired = true;
        _edit.TextChanged += s => _textChanged?.Invoke(s);
        _edit.TextSubmitted += s => _textSubmitted?.Invoke(s);
    }

    public event Action<string>? TextChanged
    {
        add
        {
            EnsureEventsWired();
            _textChanged += value;
        }
        remove => _textChanged -= value;
    }

    public event Action<string>? TextSubmitted
    {
        add
        {
            EnsureEventsWired();
            _textSubmitted += value;
        }
        remove => _textSubmitted -= value;
    }

    public void Clear()
    {
        _edit.Clear();
    }

    public void GrabFocus()
    {
        _edit.GrabFocus();
    }


    public override Control? GetControl()
    {
        return _control;
    }
}