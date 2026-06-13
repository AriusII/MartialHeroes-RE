// Screens/PinModal.cs
//
// The second-password / PIN modal (frontend_scenes.md §1.4a).
//
// SPEC:
//   spec: Docs/RE/specs/frontend_scenes.md §1.4a (runtime-confirmed, section 1.4a).
//   spec: Docs/RE/specs/login_flow.md §1 step 1a, §4.2 (PIN capacity ≤ 4 chars). CODE-CONFIRMED.
//   spec: Docs/RE/formats/ui_manifests.md — "password.dds = Secondary password dialog"
//         (data/ui/password.dds, 1024×1024 DXT3).
//
// FLOW POSITION:
//   Shown AFTER primary login credential validation passes (sub-state 29 → 31 range)
//   and BEFORE the account-login blob is built (sub-state 40).
//   spec: Docs/RE/specs/frontend_scenes.md §1.4a (between §1.4 and §1.5 sub-state 40).
//
// THE PIN:
//   A first-class third login input. The client models it as its own input concept (with an
//   "is-PIN" flag). Its value ≤ 4 chars becomes the optional length-prefixed field of the
//   1/6 login blob ([0x2B][u32len account\0]([u32len PIN\0])).
//   The PIN is NOT the account password — the account password travels via the RSA 1/4 reply.
//   spec: Docs/RE/specs/login_flow.md §4.2. RUNTIME-CONFIRMED.
//
// DEV / OFFLINE:
//   In offline mode the modal is still shown; the player may leave the PIN empty (submit with "")
//   or enter up to 4 digits. The value is passed to the caller (BootFlow) but never sent on the
//   wire (no live transport).
//
// PASSIVE: zero game logic. Collects a short numeric string and emits PinSubmitted. No
//   domain state, no credential validation, no packet encoding in this file.

using Godot;
using MartialHeroes.Client.Godot.Screens.Widgets;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// Second-password / PIN modal overlay.
///
/// <para>Floats above the login screen and blocks interaction until the player submits or cancels.
/// Accepts up to 4 numeric characters.
/// spec: Docs/RE/specs/frontend_scenes.md §1.4a. RUNTIME-CONFIRMED.</para>
///
/// <para>Asset: <c>data/ui/password.dds</c> (1024×1024 DXT3 — "Secondary password dialog").
/// spec: Docs/RE/formats/ui_manifests.md §password.dds.</para>
/// </summary>
public sealed partial class PinModal : Control
{
    // PIN capacity: ≤ 4 characters + NUL.
    // spec: Docs/RE/specs/login_flow.md §4.2 "second-password / PIN capacity length < 5".
    // RUNTIME-CONFIRMED.
    private const int MaxPinLength = 4; // spec: login_flow.md §4.2. RUNTIME-CONFIRMED.

    // DDS path for the secondary-password dialog chrome.
    // spec: Docs/RE/formats/ui_manifests.md — "data/ui/password.dds = Secondary password dialog".
    private const string AtlasPassword = "data/ui/password.dds";

    // =========================================================================
    // Outgoing intents
    // =========================================================================

    /// <summary>
    /// Raised when the player submits the PIN (may be empty — user skipped).
    /// Carries the entered PIN string (≤ 4 chars, numeric in practice; may be empty).
    /// spec: Docs/RE/specs/login_flow.md §4.2 — optional field (present when PIN feature active).
    /// </summary>
    [Signal]
    public delegate void PinSubmittedEventHandler(string pin);

    /// <summary>Raised when the player cancels the PIN modal (dismisses back to login).</summary>
    [Signal]
    public delegate void CancelledEventHandler();

    // =========================================================================
    // View state
    // =========================================================================

    private LineEdit _pinEdit = null!;
    private Label _errorLabel = null!;
    private UiAssetLoader _assets = null!;
    private bool _ownsAssets;

    /// <summary>Optionally inject a shared asset loader.</summary>
    public UiAssetLoader? SharedAssets { get; set; }

    // =========================================================================
    // Godot lifecycle
    // =========================================================================

    public override void _Ready()
    {
        _assets = SharedAssets ?? UiAssetLoader.Open();
        _ownsAssets = SharedAssets is null;

        try
        {
            BuildUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PinModal] Build failed: {ex.Message}");
        }
    }

    public override void _ExitTree()
    {
        if (_ownsAssets) _assets?.Dispose();
    }

    // =========================================================================
    // UI construction
    // =========================================================================

    private void BuildUi()
    {
        // Take up the full reference canvas so clicks outside the panel are blocked.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // block clicks reaching things behind the modal

        // Semi-transparent darkening overlay.
        var dimmer = new ColorRect { Color = new Color(0f, 0f, 0f, 0.60f) };
        dimmer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        dimmer.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(dimmer);

        // Panel centred on the reference 1024×768 canvas.
        const int panelW = 320;
        const int panelH = 200;
        const int panelX = (1024 - panelW) / 2; // 352
        const int panelY = (768 - panelH) / 2; // 284

        var panel = new Panel
        {
            Position = new Vector2(panelX, panelY),
            Size = new Vector2(panelW, panelH),
        };
        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.09f, 0.14f, 0.98f),
            BorderColor = new Color(0.50f, 0.43f, 0.25f),
        };
        panelStyle.SetBorderWidthAll(2);
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(panel);

        // Try the password.dds chrome as the panel background.
        // spec: Docs/RE/formats/ui_manifests.md — password.dds "Secondary password dialog".
        Texture2D? pwTex = _assets.LoadAtlas(AtlasPassword);
        if (pwTex is not null)
        {
            var chromeBg = new TextureRect
            {
                Texture = pwTex,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            chromeBg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            panel.AddChild(chromeBg);
        }

        // Title label.
        var title = WidgetFactory.MakeLabel(
            "Second Password / PIN",
            16,
            new Color(0.95f, 0.86f, 0.55f));
        title.Position = new Vector2(12, 10);
        title.Size = new Vector2(296, 20);
        panel.AddChild(title);

        // Sub-caption.
        var sub = WidgetFactory.MakeLabel(
            "Enter your PIN (up to 4 digits), or leave empty to skip.",
            11,
            new Color(0.75f, 0.75f, 0.80f),
            multiline: true);
        sub.Position = new Vector2(12, 34);
        sub.Size = new Vector2(296, 32);
        panel.AddChild(sub);

        // PIN entry field — numeric in practice, ≤ 4 chars.
        // spec: Docs/RE/specs/login_flow.md §4.2 — "second-password / PIN capacity: ≤ 4 chars".
        // RUNTIME-CONFIRMED.
        _pinEdit = new LineEdit
        {
            // Password masking mirrors the "is-PIN" first-class input concept.
            Secret = true,
            MaxLength = MaxPinLength, // spec: login_flow.md §4.2 — length < 5. RUNTIME-CONFIRMED.
            PlaceholderText = "0000",
            Alignment = HorizontalAlignment.Center,
            Position = new Vector2(80, 76),
            Size = new Vector2(160, 28),
        };
        _pinEdit.TextSubmitted += _ => OnSubmitPressed();
        panel.AddChild(_pinEdit);

        // Error / hint label (shown on invalid input).
        _errorLabel = WidgetFactory.MakeLabel("", 11, new Color(0.90f, 0.35f, 0.35f));
        _errorLabel.Position = new Vector2(12, 110);
        _errorLabel.Size = new Vector2(296, 18);
        _errorLabel.HorizontalAlignment = HorizontalAlignment.Center;
        panel.AddChild(_errorLabel);

        // Submit button.
        var submitBtn = new Button
        {
            Text = "Submit",
            Position = new Vector2(60, 136),
            Size = new Vector2(90, 32),
        };
        submitBtn.Pressed += OnSubmitPressed;
        panel.AddChild(submitBtn);

        // Skip / Cancel button.
        var cancelBtn = new Button
        {
            Text = "Skip",
            Position = new Vector2(170, 136),
            Size = new Vector2(90, 32),
        };
        cancelBtn.Pressed += OnSkipPressed;
        panel.AddChild(cancelBtn);

        // Focus PIN field on ready.
        _pinEdit.GrabFocus();

        GD.Print("[PinModal] Built. Capacity = 4 chars (spec: login_flow.md §4.2 RUNTIME-CONFIRMED).");
    }

    // =========================================================================
    // Intent handlers
    // =========================================================================

    private void OnSubmitPressed()
    {
        string pin = _pinEdit.Text.Trim();

        // Validate: max 4 chars.
        // spec: Docs/RE/specs/login_flow.md §4.2 — "length < 5 (≤ 4 chars + NUL)". RUNTIME-CONFIRMED.
        if (pin.Length > MaxPinLength)
        {
            _errorLabel.Text = $"PIN must be at most {MaxPinLength} digits.";
            return;
        }

        _errorLabel.Text = "";
        GD.Print($"[PinModal] PIN submitted (length={pin.Length}) — emitting PinSubmitted.");
        EmitSignal(SignalName.PinSubmitted, pin);
    }

    private void OnSkipPressed()
    {
        // The PIN field is optional — user may skip entirely.
        // spec: Docs/RE/specs/login_flow.md §4.2 — "optional length-prefixed field".
        GD.Print("[PinModal] PIN skipped — emitting PinSubmitted with empty string.");
        EmitSignal(SignalName.PinSubmitted, "");
    }
}