// Ui/Hud/HudErrorPanel.cs
//
// In-game ErrorPanel — timed floating notice / error modal (slot 168, CODE-CONFIRMED).
//
// The SINGLE GLOBAL ON-SCREEN SINK for almost every server notice / error. Shows text with a
// per-second countdown caption, an OK button, and auto-dismisses on countdown expiry.
//   spec: Docs/RE/specs/ui_system.md §8.25.2 CODE-CONFIRMED.
//   spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 168.
//
// Built in THREE SCENES (in-game HUD, character-select scene, loading scene) — our port models
// the in-game (state 5) instance only.
//   spec: Docs/RE/specs/ui_system.md §8.25.2 — "built in three scenes".
//
// Geometry (CODE-CONFIRMED):
//   Root at (screen_center_x − 165, 300) ≈ (347, 300) on 1024×768 — BEST-EFFORT (debugger-pending).
//   Backdrop: W=330, H≈100 (uitex 2 sub-rect — MED).
//   Message label: centered.
//   Countdown label: formatted count-down (secs remaining).
//   OK button (action 2): centered at bottom.
//   spec: Docs/RE/specs/ui_system.md §8.25.2 CODE-CONFIRMED.
//
// Auto-dismiss: countdown elapses (default 5000 ms per §8.25.3 notice-sink default).
//   On-expiry action (mode-selected): C2S 2/35 / alternate send / return-to-town — MED (debugger-pending).
//   spec: Docs/RE/specs/ui_system.md §8.25.2 CODE-CONFIRMED — "auto-dismisses when countdown elapses".
//
// S2C routing (CODE-CONFIRMED routing; values/codes capture-pending):
//   4/500 SmsgShowPopupByCode → direct (5000 ms).
//   4/132 SmsgGmNoticeError, 4/138 SmsgNoticeError, 4/140, 4/146 → via notice sink.
//   broad result family → via notice sink.
//   TODO(world-campaign): wire 4/500 popup-by-code sink.
//   spec: Docs/RE/specs/ui_system.md §8.25.3 CODE-CONFIRMED routing.
//
// Delegation: on show, also calls HudAnnouncePanel.ShowAnnounce (if slot 221 exists).
//   spec: §8.25.1 — "ErrorPanel notice show forwards its text to AnnouncePanel when slot 221 exists".
//
// PASSIVE: zero game logic. ShowError(text, seconds) is the caller API.

using Godot;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
///     In-game timed floating notice / error modal (ErrorPanel, slot 168).
///     <para>
///         The global sink for server notices/errors. Shows text with a countdown;
///         auto-dismisses. Also delegates the banner to <see cref="HudAnnouncePanel" /> when present.
///     </para>
///     <para>
///         PASSIVE: zero game logic; no domain mutation.
///         Use <see cref="ShowError" /> from <see cref="HudMaster" />.
///     </para>
///     spec: Docs/RE/specs/ui_system.md §8.25.2 CODE-CONFIRMED.
///     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 168.
/// </summary>
public sealed partial class HudErrorPanel : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // spec: Docs/RE/specs/ui_system.md §8.25.2 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private const float PanelW = 330f; // spec: §8.25.2 — backdrop W=330 (approximate; exact sub-rect MED)
    private const float PanelH = 100f; // spec: §8.25.2 — H≈100 (exact = MED)
    private const int DefaultTimeoutMs = 5000; // spec: §8.25.3 — notice-sink default timeout 5000 ms
    private HudAnnouncePanel? _announceDelegate; // slot 221 delegate
    private Label? _countdownLabel;

    // -------------------------------------------------------------------------
    // Child references
    // -------------------------------------------------------------------------

    private Label? _messageLabel;
    private Button? _okButton;

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private bool _open;
    private double _remainingSecs;

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Geometry pass: builds the error modal.
    ///     spec: Docs/RE/specs/ui_system.md §8.25.2 CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 168.
    /// </summary>
    public void Build()
    {
        Name = "HudErrorPanel";

        // Placement: screen-centred-X, Y≈300. Exact origin = master-placed / debugger-pending.
        // spec: §8.25.2 — "root at ≈ (screen_center_x−165, 300) = (347,300) on 1024×768 — MED"
        AnchorLeft = 0.5f;
        AnchorTop = 0f;
        AnchorRight = 0.5f;
        AnchorBottom = 0f;
        OffsetLeft = -PanelW / 2f;
        OffsetTop = 300f; // spec: §8.25.2 — Y≈300 (MED)
        OffsetRight = PanelW / 2f;
        OffsetBottom = 300f + PanelH;

        Visible = false;
        _open = false;
        MouseFilter = MouseFilterEnum.Stop;

        // ── Backdrop (uitex 2 sub-rect — MED) ──
        // spec: §8.25.2 — "ErrorPanel background sub-rect on uitex 2 — MED"
        // TODO(assets): bind uitex 2 sub-rect when confirmed.
        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.06f, 0.04f, 0.04f, 0.95f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.80f, 0.20f, 0.10f, 0.9f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        // ── Message label ──
        _messageLabel = new Label
        {
            Name = "MessageLabel",
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
            Position = new Vector2(10f, 15f),
            Size = new Vector2(PanelW - 20f, 50f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_messageLabel);

        // ── Countdown label ──
        // spec: §8.25.2 — "per-second countdown caption"
        _countdownLabel = new Label
        {
            Name = "CountdownLabel",
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(10f, 60f),
            Size = new Vector2(PanelW - 20f, 16f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_countdownLabel);

        // ── OK button (action 2) ──
        // spec: §8.25.2 — "OK button (action 2) at bottom; pressing OK = close immediately"
        _okButton = new Button
        {
            Name = "OkButton",
            Text = "확인", // "OK" — CP949
            Position = new Vector2((PanelW - 80f) / 2f, PanelH - 28f),
            Size = new Vector2(80f, 24f),
            MouseFilter = MouseFilterEnum.Stop
        };
        _okButton.Pressed += OnOk;
        AddChild(_okButton);

        GD.Print("[HudErrorPanel] Built — timed notice/error modal slot 168 (§8.25.2). " +
                 "Countdown label + OK button. Default timeout 5000 ms. " +
                 "Delegates banner to AnnouncePanel (slot 221) when present. " +
                 "TODO(world-campaign): wire 4/500 popup-by-code sink. " +
                 "spec: Docs/RE/specs/ui_system.md §8.25.2 CODE-CONFIRMED.");
    }

    /// <summary>
    ///     Wires the AnnouncePanel delegate (slot 221).
    ///     Call from HudMaster.Build after both panels are constructed.
    ///     spec: Docs/RE/specs/ui_system.md §8.25.1 — "ErrorPanel forwards banner to AnnouncePanel when slot 221 exists".
    /// </summary>
    public void SetAnnounceDelegate(HudAnnouncePanel? announcePanel)
    {
        _announceDelegate = announcePanel;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Shows a timed error/notice modal. Auto-dismisses after <paramref name="seconds" />.
    ///     Also delegates the banner text to AnnouncePanel (slot 221) if present.
    ///     spec: Docs/RE/specs/ui_system.md §8.25.2 CODE-CONFIRMED —
    ///     "shows text with per-second countdown; auto-dismisses on expiry".
    ///     spec: Docs/RE/specs/ui_system.md §8.25.1 — "notice show forwards to AnnouncePanel when present".
    ///     TODO(world-campaign): wire 4/500 SmsgShowPopupByCode sink
    ///     (popup-by-code → one of seven preset strings; codes 1..7 capture-pending).
    ///     spec: Docs/RE/specs/ui_system.md §8.25.3 — SmsgShowPopupByCode 4/500.
    /// </summary>
    public void ShowError(string text, double seconds = DefaultTimeoutMs / 1000.0)
    {
        if (_messageLabel != null) _messageLabel.Text = text;
        _remainingSecs = seconds;
        _open = true;
        Visible = true;
        UpdateCountdownLabel();

        // Delegate banner to AnnouncePanel (slot 221) when present.
        // spec: §8.25.1 — "ErrorPanel notice show forwards text to AnnouncePanel when slot 221 exists"
        _announceDelegate?.ShowAnnounce(text);

        GD.Print($"[HudErrorPanel] ShowError: \"{text}\", timeout={seconds}s. " +
                 "Delegating to AnnouncePanel (slot 221) if present. " +
                 "spec: Docs/RE/specs/ui_system.md §8.25.2 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // _Process — countdown + auto-dismiss
    // -------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (!_open) return;

        _remainingSecs -= delta;
        if (_remainingSecs <= 0.0)
        {
            // Auto-dismiss on countdown expiry.
            // spec: §8.25.2 — "auto-dismisses when the countdown elapses"
            // On-expiry action (mode-selected): C2S 2/35 / alternate / return-to-town — MED.
            // TODO(world-campaign): emit on-expiry action per mode (§8.25.2 MED).
            _remainingSecs = 0.0;
            Close();
            return;
        }

        UpdateCountdownLabel();
    }

    private void UpdateCountdownLabel()
    {
        // spec: §8.25.2 — "per-second countdown caption"
        if (_countdownLabel != null)
            _countdownLabel.Text = $"{(int)_remainingSecs}";
    }

    // -------------------------------------------------------------------------
    // Action handlers
    // -------------------------------------------------------------------------

    private void OnOk()
    {
        // action 2 = OK — close immediately.
        // spec: §8.25.2 — "OK button (action 2)"
        Close();
        GD.Print("[HudErrorPanel] OK pressed (action 2) — closed. " +
                 "spec: Docs/RE/specs/ui_system.md §8.25.2 CODE-CONFIRMED.");
    }

    private void Close()
    {
        _open = false;
        Visible = false;
        _remainingSecs = 0.0;
        if (_messageLabel != null) _messageLabel.Text = string.Empty;
        if (_countdownLabel != null) _countdownLabel.Text = string.Empty;
    }

    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        // ESC dismissal — MED per §8.24.5; apply here as consistent UX.
        if (@event is InputEventKey { Keycode: Key.Escape, Pressed: true })
        {
            OnOk();
            GetViewport().SetInputAsHandled();
        }
    }
}