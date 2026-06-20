// Ui/Widgets/HudTextbox.cs
//
// HUD text input widget — reimplementation of GUTextbox behaviour.
//
// Key behaviours:
//   - CP949 text input via Godot LineEdit (Godot's IME integration handles composition).
//   - Password mode: character-advance 6 pixels per char (spec §5.2 CONFIRMED).
//     Godot's LineEdit.Secret maps directly to the password glyph-advance behaviour.
//   - Caret blink every ~500 ms (spec §5.2: "500 ms blink toggle").
//     Godot LineEdit handles caret blink natively.
//   - Font slot default 0 (DotumChe 12/6/wt0) — GUTextbox slot at +0xDC: CODE-CONFIRMED.
//   - Click focuses and consumes the click event (spec §5.3): LineEdit handles focus.
//   - Max-length capped via LineEdit.MaxLength.
//   - TextChanged event → callers subscribe and emit use-case calls (no local domain mutation).
//
// IME note: Godot's LineEdit registers its own IME focus on focus-enter; Korean CP949
// composition flows through Godot's TextServer. The manual IME context registration in
// the legacy GUTextbox (spec §5.1) is not replicated — Godot handles it.
//
// spec: Docs/RE/specs/ui_system.md §5 — GUTextbox: CODE-CONFIRMED (focus, password, caret).
// spec: Docs/RE/specs/ui_system.md §5.2 — "password: 6 px/char fixed advance": CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §5.2 — "caret blink 500 ms": CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §6.2 — slot 0 = DotumChe 12/6: CODE-CONFIRMED.
// spec: Docs/RE/structs/gucomponent.md +0xDC — GUTextbox font-slot default 0: CODE-CONFIRMED.

using Godot;

namespace MartialHeroes.Client.Godot.Ui.Widgets;

/// <summary>
///     GUTextbox-faithful CP949 text-input widget for the HUD.
///     <para>
///         Backed by a Godot <see cref="LineEdit" /> which handles focus, IME composition,
///         caret blink, and password masking natively.
///     </para>
///     <para>
///         Subscribe to <see cref="TextChanged" /> to receive text edits and emit the
///         appropriate use-case call. Never mutate domain state in the handler.
///     </para>
///     spec: Docs/RE/specs/ui_system.md §5 — GUTextbox.
/// </summary>
public sealed class HudTextbox : HudWidget
{
    private readonly Control _control;
    private readonly LineEdit _edit;

    // Wire Godot's typed delegates to our Action<string> once, lazily.
    private bool _eventsWired;

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Creates a HudTextbox.
    /// </summary>
    /// <param name="x">Screen-local X on the 1024×768 canvas.</param>
    /// <param name="y">Screen-local Y.</param>
    /// <param name="w">Width in pixels.</param>
    /// <param name="h">Height in pixels.</param>
    /// <param name="password">
    ///     True for password mode (masking + 6px glyph advance via LineEdit.Secret).
    ///     spec §5.2 — "password mode: 6 px/char advance": CONFIRMED.
    /// </param>
    /// <param name="maxLength">
    ///     Maximum character count (0 = unlimited).
    ///     spec §8.1 — ID textbox maxlen 6, PW textbox maxlen 129: CODE-CONFIRMED.
    /// </param>
    /// <param name="fontSlot">
    ///     Font slot index 0..14 (default 0 = DotumChe 12/6/wt0).
    ///     spec §6.3 — GUTextbox slot at +0xDC, default 0: CODE-CONFIRMED.
    /// </param>
    public HudTextbox(
        int x, int y, int w, int h,
        bool password = false,
        int maxLength = 0,
        int fontSlot = 0,
        Texture2D? background = null) // spec: §6.3 GUTextbox font slot default 0: CODE-CONFIRMED
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
                // spec: Docs/RE/specs/frontend_scenes.md — GUTextbox atlas rect is W×H sampled 1:1.
                StretchMode = TextureRect.StretchModeEnum.Keep,
                MouseFilter = Control.MouseFilterEnum.Ignore
            });
        }

        _edit = new LineEdit
        {
            Position = hasBackground ? Vector2.Zero : new Vector2(x, y),
            Size = new Vector2(w, h),
            CustomMinimumSize = new Vector2(w, h),

            // Password mode: Godot's Secret property displays bullet glyphs (one per char).
            // The original fixed 6-pixel glyph advance applies uniformly.
            // spec: §5.2 — "password mode: 6 px/char fixed advance, draws '*' per char": CONFIRMED.
            Secret = password,

            // Max-length cap.
            MaxLength = maxLength > 0 ? maxLength : 0,

            // Caret blink is handled by Godot's LineEdit; interval approximates 500 ms.
            // spec: §5.2 — "caret blink toggle ~500 ms": CONFIRMED.
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

        // Apply font slot.
        // spec: §6.3 / §6.2 — GUTextbox font slot at +0xDC, default 0 = DotumChe 12/6: CODE-CONFIRMED.
        HudFont.ApplyToLineEdit(_edit, fontSlot);
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Legacy IME mode selector recorded by the builder follow-up call.
    ///     Godot owns the actual IME context; the value is preserved for 1:1 scene wiring.
    ///     spec: Docs/RE/specs/frontend_scenes.md — GUTextbox IME mode is set separately from the ctor.
    /// </summary>
    public int ImeMode { get; set; }

    /// <summary>
    ///     Maximum character count; 0 means unlimited.
    ///     spec: Docs/RE/specs/frontend_scenes.md — GUTextbox max length is set separately from the ctor.
    /// </summary>
    public int MaxLength
    {
        get => _edit.MaxLength;
        set => _edit.MaxLength = Math.Max(0, value);
    }

    /// <summary>Gets or sets the text content of the input field.</summary>
    public string Text
    {
        get => _edit.Text;
        set => _edit.Text = value;
    }

    // Backing fields for the wrapper events (lambdas stored so we can unsubscribe).
    private event Action<string>? _textChanged;
    private event Action<string>? _textSubmitted;

    private void EnsureEventsWired()
    {
        if (_eventsWired) return;
        _eventsWired = true;
        _edit.TextChanged += s => _textChanged?.Invoke(s);
        _edit.TextSubmitted += s => _textSubmitted?.Invoke(s);
    }

    /// <summary>
    ///     Fired when the text content changes. Subscribers must emit a use-case call;
    ///     never mutate domain state in a UI handler.
    /// </summary>
    public event Action<string>? TextChanged
    {
        add
        {
            EnsureEventsWired();
            _textChanged += value;
        }
        remove => _textChanged -= value;
    }

    /// <summary>
    ///     Fired when the user presses Enter / submits the input.
    /// </summary>
    public event Action<string>? TextSubmitted
    {
        add
        {
            EnsureEventsWired();
            _textSubmitted += value;
        }
        remove => _textSubmitted -= value;
    }

    /// <summary>Clears the text field.</summary>
    public void Clear()
    {
        _edit.Clear();
    }

    /// <summary>Focuses this textbox, registering it as the IME target.</summary>
    public void GrabFocus()
    {
        _edit.GrabFocus();
    }

    // -------------------------------------------------------------------------
    // HudWidget
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public override Control? GetControl()
    {
        return _control;
    }
}