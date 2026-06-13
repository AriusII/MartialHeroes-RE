// Screens/Widgets/StateButton.cs
//
// GU-faithful 3-frame atlas button, matching the legacy GUButton / "7-state" constructor
// semantics described in the UI system spec.
//
// PASSIVE: zero game logic.  The button renders atlas sprites, tracks input state, and fires
// a C# event on a confirmed click.  It never mutates domain state or calls Application use-cases
// directly — callers subscribe to <see cref="ActionFired"/> and do that themselves.
//
// KEY SPEC FACTS IMPLEMENTED HERE:
//   §1.5  Frame-selection precedence: disabled > pressed > hovered > normal.
//         DISABLED always reuses the NORMAL frame (no distinct disabled sprite origin from ctor).
//   §1.5  Caption tints: disabled → grey 0xFF666666; hovered → yellow 0xFFFFFF00;
//         normal/pressed → per-widget tint (default white).
//   §4.1  Hit-test: AABB on world rect; disabled button never reports hover.
//   §4.2  One global capture: action fires ONLY on release-inside (press-inside / release-inside).
//   §4.3  Drag off → OnHoverExit → un-press; drag back → OnHoverEnter → re-press;
//         release outside → capture cleared, action NOT fired.
//   §7.1  Alpha-fade show/hide: current alpha chases target at ±64 per tick toward [0,255]
//         (GUComponent fast path, not the GUComponentEx ±32 slow path).
//         Fade is implemented here so the button can participate in the panel show/hide cycle.
//
// spec: Docs/RE/specs/ui_system.md §1.5 (frame-state machine), §4 (hit-test/capture/drag),
//       §7.1 (alpha-fade show/hide), §13.2 (Godot reconstruction guidance).

using Godot;

namespace MartialHeroes.Client.Godot.Screens.Widgets;

/// <summary>
/// A GU-faithful atlas button with NORMAL / HOVER / PRESSED frame states and a caption label.
///
/// <para>
/// Construct via <see cref="WidgetFactory.MakeStateButton"/> rather than directly, so the atlas
/// textures are wired via <see cref="UiAssetLoader"/> and the caption theme is applied consistently.
/// Manual construction is also valid: set <see cref="NormalFrame"/>, <see cref="HoverFrame"/>,
/// <see cref="PressedFrame"/>, <see cref="ActionId"/>, and optionally <see cref="Caption"/> and
/// <see cref="CaptionTint"/> before adding to the tree.
/// </para>
///
/// <para><b>Frame-state precedence</b> (spec §1.5):
/// disabled ≻ pressed ≻ hovered ≻ normal.
/// DISABLED renders the NORMAL frame with the grey caption tint (no distinct disabled sprite origin,
/// matching the legacy three-constructor rule).
/// </para>
///
/// <para><b>Click semantics</b> (spec §4.2–4.3):
/// <see cref="ActionFired"/> fires on release-inside only.  Pressing then dragging off un-presses
/// the button visually (re-presses on re-enter); releasing outside cancels the action entirely.
/// </para>
///
/// <para><b>Alpha fade</b> (spec §7.1):
/// Call <see cref="ShowFade"/> / <see cref="HideFade"/> to start a ±64-per-tick fade.  The button
/// draws at zero alpha when fully hidden and skips input handling while hidden.
/// </para>
/// </summary>
public sealed partial class StateButton : Control
{
    // -------------------------------------------------------------------------
    // Caption tint constants — spec: Docs/RE/specs/ui_system.md §1.5 CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Caption colour when the button is disabled.
    /// spec: Docs/RE/specs/ui_system.md §1.5 — "Disabled → grey 0xFF666666".
    /// </summary>
    public static readonly Color CaptionDisabledColor = new(0x66, 0x66, 0x66, 0xFF);

    /// <summary>
    /// Caption colour when the button is hovered.
    /// spec: Docs/RE/specs/ui_system.md §1.5 — "Hovered → yellow 0xFFFFFF00".
    /// </summary>
    public static readonly Color CaptionHoverColor = new(1f, 1f, 0f, 1f);

    // Alpha-fade step per _Process tick.
    // spec: Docs/RE/specs/ui_system.md §7.1 — "alpha chases ±64 per tick toward [0,255]".
    // This is the GUComponent fast-path (not the GUComponentEx ±32 slow path).
    private const int FadeStep = 64; // spec: Docs/RE/specs/ui_system.md §7.1

    // -------------------------------------------------------------------------
    // Configuration (set before _Ready or during construction)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Atlas sub-rect for the NORMAL state.
    /// spec: Docs/RE/specs/ui_system.md §1.5 — "+0xC8/+0xCC NORMAL (srcX,srcY); also DISABLED".
    /// </summary>
    public AtlasTexture? NormalFrame { get; set; }

    /// <summary>
    /// Atlas sub-rect for the HOVER state.
    /// spec: Docs/RE/specs/ui_system.md §1.5 — "+0xD0/+0xD4 HOVER (srcX,srcY)".
    /// When null the NORMAL frame is used (2-state button behaviour).
    /// </summary>
    public AtlasTexture? HoverFrame { get; set; }

    /// <summary>
    /// Atlas sub-rect for the PRESSED state.
    /// spec: Docs/RE/specs/ui_system.md §1.5 — "+0xD8/+0xDC PRESSED (srcX,srcY)".
    /// When null the NORMAL frame is used.
    /// </summary>
    public AtlasTexture? PressedFrame { get; set; }

    /// <summary>
    /// Integer action identifier delivered to <see cref="ActionFired"/> on a confirmed click.
    /// spec: Docs/RE/specs/ui_system.md §1.2 — "actionId at +0x10; fires on release-inside".
    /// </summary>
    public int ActionId { get; set; }

    /// <summary>
    /// Optional caption text drawn over the button face.
    /// spec: Docs/RE/specs/ui_system.md §1.5 — "+0xA4 caption string (CP949 std::string)".
    /// Captions arrive already-decoded from msg.xdb; we render the .NET string as-is.
    /// </summary>
    public string Caption { get; set; } = string.Empty;

    /// <summary>
    /// Per-widget caption tint for the normal and pressed states (default white).
    /// spec: Docs/RE/specs/ui_system.md §1.2 — "+0x0C tint/colour (RGB low 24 bits)".
    /// Disabled overrides to <see cref="CaptionDisabledColor"/>; hover overrides to
    /// <see cref="CaptionHoverColor"/>; this value is used for all other states.
    /// </summary>
    public Color CaptionTint { get; set; } = Colors.White;

    /// <summary>
    /// When true the button is disabled: input is ignored, the NORMAL frame is shown, and
    /// the caption renders in <see cref="CaptionDisabledColor"/>.
    /// spec: Docs/RE/specs/ui_system.md §4.1 — "disabled button never reports hover".
    /// </summary>
    public bool IsDisabled { get; set; }

    // -------------------------------------------------------------------------
    // C# event — the caller wires this instead of Godot signals, keeping the widget
    // layer-pure (no [Signal] bureaucracy for a foundation primitive).
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fired when the user presses and releases the button inside its bounds.
    /// The integer argument is <see cref="ActionId"/>.
    /// spec: Docs/RE/specs/ui_system.md §4.2 — "action fires on release-inside only".
    /// </summary>
    public event Action<int>? ActionFired;

    // -------------------------------------------------------------------------
    // Private interaction state (view state only — not domain state)
    // -------------------------------------------------------------------------

    private bool _isHovered;
    private bool _isPressed; // true while the mouse button is held down after pressing inside
    private bool _hasCapture; // true while we own the "global click capture" (spec §4.2)

    // Alpha-fade state (spec §7.1)
    private float _currentAlpha = 1f; // 0..1, drives Modulate
    private float _targetAlpha = 1f; // 0 = hiding, 1 = showing

    // Godot child controls built in _Ready
    private TextureRect _spriteRect = null!;
    private Label _captionLabel = null!;

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public override void _Ready()
    {
        // The button Control itself is transparent; children render the content.
        MouseFilter = MouseFilterEnum.Stop; // capture mouse events

        // Sprite layer — fills the button rect exactly.
        _spriteRect = new TextureRect
        {
            Name = "Sprite",
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _spriteRect.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_spriteRect);

        // Caption label centred over the sprite.
        _captionLabel = new Label
        {
            Name = "Caption",
            Text = Caption,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.Off,
            ClipText = false,
        };
        _captionLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_captionLabel);

        RefreshVisuals();
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        // Alpha fade: current alpha chases target at ±FadeStep/tick.
        // spec: Docs/RE/specs/ui_system.md §7.1 — "±64 per tick toward [0,255]".
        // Here we work in [0,1] normalised range: step = FadeStep / 255.
        // Early-exit when already at steady state to avoid writing Modulate every frame (finding 6).
        if (_currentAlpha == _targetAlpha) return;

        float step = FadeStep / 255f;
        if (_currentAlpha < _targetAlpha)
            _currentAlpha = Mathf.Min(_currentAlpha + step, _targetAlpha);
        else
            _currentAlpha = Mathf.Max(_currentAlpha - step, _targetAlpha);

        // Apply alpha to this control (affects all children).
        Modulate = new Color(1f, 1f, 1f, _currentAlpha);
    }

    /// <inheritdoc/>
    public override void _GuiInput(InputEvent @event)
    {
        // While hidden (alpha → 0) or disabled, ignore all input.
        // spec: Docs/RE/specs/ui_system.md §4.1 — "disabled button never reports hover".
        if (IsDisabled || _currentAlpha < 0.01f)
            return;

        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                // Mouse-down inside → take capture and press visually.
                // spec: Docs/RE/specs/ui_system.md §4.2 — "mouse-down on focus-eligible widget
                //       → g_ClickCapture = this".
                _isPressed = true;
                _hasCapture = true;
                RefreshVisuals();
                AcceptEvent();
            }
            else if (_hasCapture)
            {
                // Mouse-up while we own capture.
                bool releasedInside = GetRect().HasPoint(mb.Position);
                _isPressed = false;
                _hasCapture = false;

                if (releasedInside)
                {
                    // Confirmed click: fire action.
                    // spec: Docs/RE/specs/ui_system.md §4.2 — "action fires on release-inside only".
                    ActionFired?.Invoke(ActionId);
                }
                // Release outside → capture cleared, action NOT fired (spec §4.3 "drag off to cancel").

                RefreshVisuals();
                AcceptEvent();
            }
        }
        else if (@event is InputEventMouseMotion motion)
        {
            if (!_hasCapture) return;

            // While captured, track entry/exit for visual re-press / un-press.
            // spec: Docs/RE/specs/ui_system.md §4.3 — "OnHoverExit clears pressed byte;
            //       OnHoverEnter re-presses".
            bool inside = GetRect().HasPoint(motion.Position);
            if (inside && !_isPressed)
            {
                // Cursor re-entered the button while dragging: re-press.
                _isPressed = true;
                RefreshVisuals();
            }
            else if (!inside && _isPressed)
            {
                // Cursor left the button while dragging: un-press.
                _isPressed = false;
                RefreshVisuals();
            }
        }
    }

    /// <inheritdoc/>
    public override void _Notification(int what)
    {
        // Track hover (MOUSE_ENTER / MOUSE_EXIT) for the visual hover state.
        // spec: Docs/RE/specs/ui_system.md §4.1 — "hovered byte set by hit-test".
        if (what == NotificationMouseEnter)
        {
            if (!IsDisabled)
            {
                _isHovered = true;
                RefreshVisuals();
            }
        }
        else if (what == NotificationMouseExit)
        {
            _isHovered = false;
            if (!_hasCapture)
            {
                // Only un-press on pure exit (not drag-tracked exit — that is handled in _GuiInput).
                _isPressed = false;
            }

            RefreshVisuals();
        }
    }

    // -------------------------------------------------------------------------
    // Public show/hide API (alpha fade)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Starts a fade-in to full alpha.
    /// spec: Docs/RE/specs/ui_system.md §7.1 — "show/hide target byte +0x8C; alpha chases ±64/tick".
    /// </summary>
    public void ShowFade() => _targetAlpha = 1f;

    /// <summary>
    /// Starts a fade-out to zero alpha.
    /// spec: Docs/RE/specs/ui_system.md §7.1 — "hide → alpha falls −64 toward 0".
    /// </summary>
    public void HideFade() => _targetAlpha = 0f;

    /// <summary>Pins alpha immediately to 1 (bypasses the fade), matching spec +0x0F forced-alpha.</summary>
    public void ShowInstant()
    {
        _currentAlpha = 1f;
        _targetAlpha = 1f;
        Modulate = Colors.White;
    }

    /// <summary>Pins alpha immediately to 0 (bypasses the fade), matching spec +0x0F forced-alpha.</summary>
    public void HideInstant()
    {
        _currentAlpha = 0f;
        _targetAlpha = 0f;
        Modulate = new Color(1f, 1f, 1f, 0f);
    }

    // -------------------------------------------------------------------------
    // Caption text update (live update after construction)
    // -------------------------------------------------------------------------

    /// <summary>Updates the caption text on the label child (if already in the tree).</summary>
    public void SetCaption(string text)
    {
        Caption = text;
        if (_captionLabel is not null)
        {
            _captionLabel.Text = text;
        }
    }

    // -------------------------------------------------------------------------
    // Internal: visual state refresh (zero allocations — only sets existing properties)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Selects the correct frame and caption tint based on the current interaction state.
    /// spec: Docs/RE/specs/ui_system.md §1.5 — frame-selection precedence: disabled ≻ pressed ≻ hovered ≻ normal.
    /// </summary>
    private void RefreshVisuals()
    {
        if (_spriteRect is null || _captionLabel is null) return;

        AtlasTexture? frame;
        Color captionColor;

        if (IsDisabled)
        {
            // DISABLED: NORMAL frame + grey caption.
            // spec: Docs/RE/specs/ui_system.md §1.5 — "DISABLED always equals NORMAL from the ctor".
            frame = NormalFrame;
            captionColor = CaptionDisabledColor;
        }
        else if (_isPressed)
        {
            // PRESSED: dedicated frame if available, else NORMAL.
            frame = PressedFrame ?? NormalFrame;
            captionColor = CaptionTint;
        }
        else if (_isHovered)
        {
            // HOVER: dedicated frame if available, else NORMAL; yellow caption.
            // spec: Docs/RE/specs/ui_system.md §1.5 — "hovered → yellow 0xFFFFFF00".
            frame = HoverFrame ?? NormalFrame;
            captionColor = CaptionHoverColor;
        }
        else
        {
            // NORMAL.
            frame = NormalFrame;
            captionColor = CaptionTint;
        }

        _spriteRect.Texture = frame;
        _captionLabel.Text = Caption;
        _captionLabel.AddThemeColorOverride("font_color", captionColor);
    }
}