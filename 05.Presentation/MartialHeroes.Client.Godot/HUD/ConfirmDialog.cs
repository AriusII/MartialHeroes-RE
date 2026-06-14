using Godot;

namespace MartialHeroes.Client.Godot.HUD;

/// <summary>
/// Confirm / info dialog — the most common HUD modal family (340×190, ~12 sites).
///
/// PASSIVE: presents a message string and two buttons (OK / Cancel). The gesture result is
/// forwarded via <see cref="Confirmed"/> / <see cref="Cancelled"/> events so the caller can
/// emit the appropriate use-case call. No game logic here.
///
/// Placement: inherits <see cref="CenteredModal"/> centring formula — 340×190 window
/// placed at centerX(340), centerY(190) on the reference 1024×768 canvas.
///
/// spec: Docs/RE/specs/ui_hud_layout.md §5.8 — "Confirm/info dialog family: W=340, H=190, ~12 sites,
///       tex idx 2; the most common dialog rect, recurs widely"
/// spec: Docs/RE/specs/ui_hud_layout.md §5.1  — centring formula CONFIRMED-formula
/// spec: Docs/RE/specs/ui_hud_layout.md §5.11 — absolute pixels pending known-resolution read
/// </summary>
public sealed partial class ConfirmDialog : CenteredModal
{
    // ── Dialog geometry — CONFIRMED from spec §5.8 ────────────────────────────────────────────
    // spec: Docs/RE/specs/ui_hud_layout.md §5.8 — "Confirm/info dialog: W=340, H=190"

    private const float DialogW = 340f; // spec: Docs/RE/specs/ui_hud_layout.md §5.8
    private const float DialogH = 190f; // spec: Docs/RE/specs/ui_hud_layout.md §5.8

    // ── Child node handles ─────────────────────────────────────────────────────────────────────

    private Label _messageLabel = null!;

    // ── Events (gesture intent forwarding) ────────────────────────────────────────────────────

    /// <summary>Raised when the player clicks OK/Confirm. Caller emits the use-case call.</summary>
    public event Action? Confirmed;

    /// <summary>Raised when the player clicks Cancel/Close. Caller handles the cancellation.</summary>
    public event Action? Cancelled;

    // ── Construction ──────────────────────────────────────────────────────────────────────────

    public ConfirmDialog()
    {
        // Override the base defaults with the recovered dialog family size.
        // spec: Docs/RE/specs/ui_hud_layout.md §5.8 — "W=340, H=190"
        SetModalSize(DialogW, DialogH); // spec: Docs/RE/specs/ui_hud_layout.md §5.8
        Name = "ConfirmDialog";
    }

    protected override string GetModalTitle() => "확인"; // "Confirm" in Korean (CP949, already decoded)

    /// <summary>
    /// Populates the content area with the message label and OK/Cancel buttons.
    /// No game logic — pure chrome layout.
    /// </summary>
    protected override void BuildContent(Control content)
    {
        // Message text — centred in the upper portion of the content area.
        _messageLabel = new Label
        {
            Name = "MessageLabel",
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _messageLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _messageLabel.Size = new Vector2(content.Size.X, content.Size.Y - 44f);
        _messageLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.93f, 0.88f, 1f));
        content.AddChild(_messageLabel);

        // Button row — OK and Cancel side by side at the bottom.
        var buttonRow = new HBoxContainer
        {
            Name = "ButtonRow",
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = MouseFilterEnum.Pass,
        };
        buttonRow.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        buttonRow.Position = new Vector2(0f, content.Size.Y - 40f);
        buttonRow.Size = new Vector2(content.Size.X, 36f);
        content.AddChild(buttonRow);

        var spacer = new Control { CustomMinimumSize = new Vector2(40f, 0f) };
        buttonRow.AddChild(spacer);

        var okBtn = new Button
        {
            Name = "OkButton",
            Text = "확인", // "OK" in Korean
            CustomMinimumSize = new Vector2(90f, 30f),
        };
        okBtn.Pressed += () =>
        {
            Hide();
            Confirmed?.Invoke();
        };
        buttonRow.AddChild(okBtn);

        var midSpacer = new Control { CustomMinimumSize = new Vector2(20f, 0f) };
        buttonRow.AddChild(midSpacer);

        var cancelBtn = new Button
        {
            Name = "CancelButton",
            Text = "취소", // "Cancel" in Korean
            CustomMinimumSize = new Vector2(90f, 30f),
        };
        cancelBtn.Pressed += () =>
        {
            Hide();
            Cancelled?.Invoke();
        };
        buttonRow.AddChild(cancelBtn);

        GD.Print($"[ConfirmDialog] Content built. W={DialogW} H={DialogH}. " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §5.8 (confirm/info dialog family).");
    }

    // ── Public API ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the dialog with the given message.
    /// PASSIVE: only sets display text and makes the dialog visible.
    /// The confirmation / cancellation intent is forwarded via <see cref="Confirmed"/> / <see cref="Cancelled"/>.
    /// </summary>
    public void Open(string message)
    {
        if (_messageLabel is not null)
            _messageLabel.Text = message;
        Show();
    }
}