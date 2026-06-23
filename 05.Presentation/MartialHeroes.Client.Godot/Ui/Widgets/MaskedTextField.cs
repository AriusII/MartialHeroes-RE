using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Widgets;

public sealed partial class MaskedTextField : Control
{
    private const float StarAdvance = 6f;

    private const float TextOffsetY = 0f;

    private const float CaretBlinkMs = 500f;


    private readonly HudAtlasLibrary _atlas;
    private readonly string _atlasPath;
    private readonly bool _masked;
    private readonly int _maxLen;
    private readonly int _srcX;
    private readonly int _srcY;

    private Font? _cachedFont;
    private float _caretPhaseMs;
    private bool _caretVisible = true;
    private bool _focused;

    private string _maskedText = "";
    private int _maskedTextLen = -1;

    private string _text = "";


    public MaskedTextField(
        HudAtlasLibrary atlas,
        string atlasPath,
        int destX, int destY, int destW, int destH,
        int srcX, int srcY,
        bool masked,
        int maxLen = 0)
    {
        _atlas = atlas;
        _atlasPath = atlasPath;
        _srcX = srcX;
        _srcY = srcY;
        _masked = masked;
        _maxLen = maxLen;

        Position = new Vector2(destX, destY);
        Size = new Vector2(destW, destH);
        CustomMinimumSize = new Vector2(destW, destH);
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
    }


    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? "";
            RebuildMaskedTextIfNeeded();
            QueueRedraw();
        }
    }


    public event Action? TextSubmitted;

    public new void GrabFocus()
    {
        base.GrabFocus();
        _focused = true;
        _caretVisible = true;
        _caretPhaseMs = 0f;
        QueueRedraw();
    }


    public override void _Ready()
    {
        _cachedFont = HudFont.CreateSlot(0);

        RebuildMaskedTextIfNeeded();

        FocusEntered += OnFocusEntered;
        FocusExited += OnFocusExited;
    }

    public override void _Process(double delta)
    {
        if (!_focused) return;

        _caretPhaseMs += (float)(delta * 1000.0);
        if (_caretPhaseMs >= CaretBlinkMs)
        {
            _caretPhaseMs -= CaretBlinkMs;
            _caretVisible = !_caretVisible;
            QueueRedraw();
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            GrabFocus();
            AcceptEvent();
            return;
        }

        if (!_focused) return;

        if (@event is not InputEventKey key || !key.Pressed) return;

        switch (key.Keycode)
        {
            case Key.Backspace:
                if (_text.Length > 0)
                {
                    _text = _text[..^1];
                    RebuildMaskedTextIfNeeded();
                    QueueRedraw();
                }

                AcceptEvent();
                break;

            case Key.Enter:
            case Key.KpEnter:
                TextSubmitted?.Invoke();
                AcceptEvent();
                break;

            default:
                if (key.Unicode > 0 && key.Unicode >= 0x20)
                {
                    if (_maxLen <= 0 || _text.Length < _maxLen)
                    {
                        _text += char.ConvertFromUtf32((int)key.Unicode);
                        RebuildMaskedTextIfNeeded();
                        _caretVisible = true;
                        _caretPhaseMs = 0f;
                        QueueRedraw();
                    }

                    AcceptEvent();
                }

                break;
        }
    }


    public override void _Draw()
    {
        var bg = _atlas.SliceByPath(_atlasPath, _srcX, _srcY,
            (int)Size.X, (int)Size.Y);
        if (bg is not null)
            DrawTextureRect(bg, new Rect2(Vector2.Zero, Size), false);

        var fontSize = HudFont.RowHeight(0);
        var font = _cachedFont ?? HudFont.CreateSlot(0);

        if (_masked)
            DrawString(font, new Vector2(1f, TextOffsetY + fontSize), _maskedText,
                HorizontalAlignment.Left, (int)Size.X - 2, fontSize,
                new Color(1f, 1f, 1f));
        else
            DrawString(font, new Vector2(1f, TextOffsetY + fontSize), _text,
                HorizontalAlignment.Left, (int)Size.X - 2, fontSize,
                new Color(1f, 1f, 1f));

        if (_focused && _caretVisible)
        {
            var caretX = _masked
                ? 1f + _text.Length * StarAdvance
                : 1f + font.GetStringSize(_text, HorizontalAlignment.Left, -1, fontSize).X;
            caretX = Math.Min(caretX, Size.X - 2f);
            DrawLine(
                new Vector2(caretX, 2f),
                new Vector2(caretX, Size.Y - 2f),
                Colors.White);
        }
    }


    private void RebuildMaskedTextIfNeeded()
    {
        if (!_masked) return;
        if (_text.Length == _maskedTextLen) return;
        _maskedText = new string('*', _text.Length);
        _maskedTextLen = _text.Length;
    }


    private void OnFocusEntered()
    {
        _focused = true;
        _caretVisible = true;
        _caretPhaseMs = 0f;
        QueueRedraw();
    }

    private void OnFocusExited()
    {
        _focused = false;
        QueueRedraw();
    }
}