// Ui/Widgets/MaskedTextField.cs
//
// 1:1 atlas-blit credential text field — replaces Godot LineEdit for the login credential boxes.
//
// The legacy GUTextbox draws its background from the atlas (field frame) and then renders text
// on top. For the masked (PW) field it draws the literal '*' glyph advancing 6 px per character
// in font slot 0. For the ID field it draws the stored string left-aligned in font slot 0.
//
// This widget is a plain Control with _Draw so NO Godot LineEdit chrome is involved — no
// StyleBoxFlat, no ColorRect, no default Godot theme theming.
//
// spec: Docs/RE/specs/frontend_layout_tables.md §2.7 — "mask bit set; '*' glyph, 6 px/char, font slot 0"
// spec: Docs/RE/specs/frontend_layout_tables.md §0.11 — "password masking is a field-flag, not IME mode"
// spec: Docs/RE/specs/frontend_layout_tables.md §2.1 — field dest (390,32,102,13) / (568,32,102,13);
//        field src (615,404,102,13) in A1 = login_slice1.dds for both ID and PW.

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Widgets;

/// <summary>
///     1:1 atlas-blit credential text-input field.
///     <para>
///         Renders the field background as a true atlas blit (no Godot control chrome),
///         then draws text in font slot 0: ID field shows the typed string left-aligned; PW field
///         shows N literal '*' glyphs at 6 px per character.
///     </para>
///     <para>Captures keyboard input while focused. Caret blinks at ~500 ms.</para>
///     spec: Docs/RE/specs/frontend_layout_tables.md §2.7 — mask mechanism, 6 px/char, slot 0.
///     spec: Docs/RE/specs/frontend_layout_tables.md §0.11 — password masking = field flag.
/// </summary>
public sealed partial class MaskedTextField : Control
{
    // -------------------------------------------------------------------------
    // Constants
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.7
    // -------------------------------------------------------------------------

    // spec: Docs/RE/specs/frontend_layout_tables.md §2.7 "6 px/char" at slot 0 (DotumChe 12 px).
    private const float StarAdvance = 6f; // spec: frontend_layout_tables.md §2.7 "6 px/char, font slot 0"

    // Vertical text offset within the field (top of field to baseline).
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.7 / §2.1 field 102×13.
    private const float TextOffsetY = 0f; // spec: frontend_layout_tables.md §2.7

    // Caret blink period 500 ms.
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.7 "blinks ~500 ms cadence"
    private const float CaretBlinkMs = 500f; // spec: frontend_layout_tables.md §2.7

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    private readonly HudAtlasLibrary _atlas;
    private readonly string _atlasPath;
    private readonly bool _masked; // true = PW field; false = ID field
    private readonly int _maxLen;
    private readonly int _srcX;
    private readonly int _srcY;
    private float _caretPhaseMs; // accumulated ms for caret blink
    private bool _caretVisible = true;
    private bool _focused;

    private string _text = "";

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <param name="atlas">HUD atlas library (null-safe offline).</param>
    /// <param name="atlasPath">VFS path of the atlas containing the field background.</param>
    /// <param name="destX">Canvas X position.</param>
    /// <param name="destY">Canvas Y position.</param>
    /// <param name="destW">Width in canvas pixels.</param>
    /// <param name="destH">Height in canvas pixels.</param>
    /// <param name="srcX">Source X in atlas (top-left of the field-background sub-rect).</param>
    /// <param name="srcY">Source Y in atlas.</param>
    /// <param name="masked">
    ///     True for PW field (renders '*' at 6 px/char, font slot 0).
    ///     spec: Docs/RE/specs/frontend_layout_tables.md §2.7.
    /// </param>
    /// <param name="maxLen">Maximum character count (0 = unlimited).</param>
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

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>Gets or sets the stored text.</summary>
    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? "";
            QueueRedraw();
        }
    }

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>Fired when the user presses Enter while this field is focused.</summary>
    public event Action? TextSubmitted;

    /// <summary>
    ///     Focuses this field, making it the active input target.
    ///     spec: Docs/RE/specs/frontend_layout_tables.md §2.7 "construct routine focuses the ID textbox at show time"
    /// </summary>
    public new void GrabFocus()
    {
        base.GrabFocus();
        _focused = true;
        _caretVisible = true;
        _caretPhaseMs = 0f;
        QueueRedraw();
    }

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        // Connect focus signals.
        FocusEntered += OnFocusEntered;
        FocusExited += OnFocusExited;
    }

    public override void _Process(double delta)
    {
        if (!_focused) return;

        // Caret blink. spec: frontend_layout_tables.md §2.7
        _caretPhaseMs += (float)(delta * 1000.0);
        if (_caretPhaseMs >= CaretBlinkMs) // spec: frontend_layout_tables.md §2.7
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
                // Accept printable ASCII and any Unicode character. Godot provides the Unicode
                // value in InputEventKey.Unicode for printable keys.
                if (key.Unicode > 0 && key.Unicode >= 0x20)
                {
                    if (_maxLen <= 0 || _text.Length < _maxLen)
                    {
                        _text += char.ConvertFromUtf32((int)key.Unicode);
                        _caretVisible = true;
                        _caretPhaseMs = 0f;
                        QueueRedraw();
                    }

                    AcceptEvent();
                }

                break;
        }
    }

    // -------------------------------------------------------------------------
    // Drawing
    // -------------------------------------------------------------------------

    public override void _Draw()
    {
        // Field background: 1:1 atlas blit. null offline → draw nothing.
        // spec: Docs/RE/specs/frontend_layout_tables.md §0.10 "every front-end widget is a 1:1 atlas blit"
        var bg = _atlas.SliceByPath(_atlasPath, _srcX, _srcY,
            (int)Size.X, (int)Size.Y);
        if (bg is not null)
            DrawTextureRect(bg, new Rect2(Vector2.Zero, Size), false);

        // Text rendering — font slot 0 = DotumChe 12 px.
        // spec: Docs/RE/specs/frontend_layout_tables.md §1/§2.7 "font slot 0".
        var fontSize = HudFont.RowHeight(0); // slot 0 = 12 px. spec: frontend_layout_tables.md §1.
        Font font = HudFont.CreateSlot(0);

        if (_masked)
        {
            // PW field: one '*' per character, advancing 6 px per char (spec: §2.7 "6 px/char, font slot 0").
            // spec: Docs/RE/specs/frontend_layout_tables.md §2.7 "'*' glyph, 6 px/char".
            var stars = new string('*', _text.Length);
            DrawString(font, new Vector2(1f, TextOffsetY + fontSize), stars,
                HorizontalAlignment.Left, (int)Size.X - 2, fontSize,
                new Color(1f, 1f, 1f));
        }
        else
        {
            // ID field: left-aligned clear text.
            // spec: Docs/RE/specs/frontend_layout_tables.md §2.7 "mask bit clear → draws the stored string left-aligned"
            DrawString(font, new Vector2(1f, TextOffsetY + fontSize), _text,
                HorizontalAlignment.Left, (int)Size.X - 2, fontSize,
                new Color(1f, 1f, 1f));
        }

        // Caret — only while focused.
        // spec: Docs/RE/specs/frontend_layout_tables.md §2.7 "drawn only while focused"
        if (_focused && _caretVisible)
        {
            // Position caret after the last character.
            // Masked field uses StarAdvance (6 px/char); clear field measures string width at fontSize.
            var caretX = _masked
                ? 1f + _text.Length * StarAdvance // spec: §2.7 "6 px/char"
                : 1f + font.GetStringSize(_text, HorizontalAlignment.Left, -1, fontSize).X;
            caretX = Math.Min(caretX, Size.X - 2f);
            DrawLine(
                new Vector2(caretX, 2f),
                new Vector2(caretX, Size.Y - 2f),
                Colors.White);
        }
    }

    // -------------------------------------------------------------------------
    // Focus handlers
    // -------------------------------------------------------------------------

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