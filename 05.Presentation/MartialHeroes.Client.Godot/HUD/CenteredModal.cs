using Godot;

namespace MartialHeroes.Client.Godot.HUD;

/// <summary>
/// Base helper for the ~81 screen-centred modal/dialog windows in the in-game HUD.
///
/// Every modal uses the same placement formula:
///   X = (screen_width  − W) / 2   (centerX helper)
///   Y = (screen_height − H) / 2   (centerY helper)
///
/// Usage: derive from <see cref="CenteredModal"/> and call <see cref="SetModalSize"/> with the
/// window's recovered W×H. The base class applies the Godot anchor equivalent of the centring
/// formula and provides Show/Hide helpers, a title bar, and a Close button that calls
/// <see cref="Hide"/>.
///
/// Do NOT hardcode modal positions in derived classes — always call <see cref="SetModalSize"/>.
/// The centring formula is CONFIRMED-formula (spec §5.1, §5.8); pixel positions are pending a
/// known-resolution read of the runtime screen-size globals (spec §5.11).
///
/// spec: Docs/RE/specs/ui_hud_layout.md §5.1 — "centerX(W) = (screen_width − W) / 2"
/// spec: Docs/RE/specs/ui_hud_layout.md §5.8 — "X = centerX(W), Y = centerY(H)"
///       "overwhelmingly dominant HUD idiom; ~81 sites"
/// spec: Docs/RE/specs/ui_hud_layout.md §5.11 — "CONFIRMED-formula; absolute pixels pending a known-resolution read"
/// </summary>
public abstract partial class CenteredModal : Control
{
    // ── Default sizes (overridden per dialog family, spec §5.8) ────────────────────────────────

    /// <summary>
    /// Width of this modal. Set by <see cref="SetModalSize"/> before <see cref="BuildModal"/>.
    /// spec: Docs/RE/specs/ui_hud_layout.md §5.8 — each dialog family carries its own W×H.
    /// </summary>
    protected float ModalW { get; private set; } =
        340f; // spec: Docs/RE/specs/ui_hud_layout.md §5.8 (confirm dialog default)

    /// <summary>
    /// Height of this modal. Set by <see cref="SetModalSize"/> before <see cref="BuildModal"/>.
    /// spec: Docs/RE/specs/ui_hud_layout.md §5.8 — each dialog family carries its own W×H.
    /// </summary>
    protected float ModalH { get; private set; } =
        190f; // spec: Docs/RE/specs/ui_hud_layout.md §5.8 (confirm dialog default)

    // ── Child node handles ─────────────────────────────────────────────────────────────────────

    private Label _titleLabel = null!;
    private Button _closeButton = null!;

    /// <summary>Content area — derived classes add their widgets here.</summary>
    protected Control ContentArea { get; private set; } = null!;

    // ── Configuration ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the modal size before <see cref="BuildModal"/> is called. Override the defaults
    /// (340×190) to match a recovered dialog family from spec §5.8.
    ///
    /// Call this in the derived class's constructor or before <see cref="_Ready"/>.
    /// spec: Docs/RE/specs/ui_hud_layout.md §5.8 — each family carries its own W×H literals.
    /// </summary>
    protected void SetModalSize(float w, float h)
    {
        ModalW = w;
        ModalH = h;
    }

    // ── Godot lifecycle ────────────────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        try
        {
            BuildModal();
            Hide(); // modals are hidden until explicitly opened
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CenteredModal:{Name}] _Ready failed: {ex.Message}");
        }
    }

    // ── Chrome construction ────────────────────────────────────────────────────────────────────

    private void BuildModal()
    {
        // Apply the screen-centring formula via Godot anchors:
        //   X = (screen_width  − ModalW) / 2 → AnchorLeft=0.5, OffsetLeft=−ModalW/2, OffsetRight=+ModalW/2
        //   Y = (screen_height − ModalH) / 2 → AnchorTop=0.5, OffsetTop=−ModalH/2, OffsetBottom=+ModalH/2
        //
        // spec: Docs/RE/specs/ui_hud_layout.md §5.1  — "centerX(W) = (screen_width − W) / 2"
        // spec: Docs/RE/specs/ui_hud_layout.md §5.8  — "X = centerX(W), Y = centerY(H) CONFIRMED-formula"
        // spec: Docs/RE/specs/ui_hud_layout.md §5.11 — "CONFIRMED-formula; absolute pixels pending known-resolution read"
        AnchorLeft = 0.5f;
        AnchorTop = 0.5f;
        AnchorRight = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -(ModalW / 2f); // spec: Docs/RE/specs/ui_hud_layout.md §5.1 §5.8
        OffsetRight = ModalW / 2f; // spec: Docs/RE/specs/ui_hud_layout.md §5.1 §5.8
        OffsetTop = -(ModalH / 2f); // spec: Docs/RE/specs/ui_hud_layout.md §5.1 §5.8
        OffsetBottom = ModalH / 2f; // spec: Docs/RE/specs/ui_hud_layout.md §5.1 §5.8
        MouseFilter = MouseFilterEnum.Stop; // modals block input behind them

        // Window chrome panel.
        var chrome = new Panel { Name = "Chrome" };
        chrome.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var chromeStyle = new StyleBoxFlat();
        chromeStyle.BgColor = new Color(0.1f, 0.09f, 0.08f, 0.95f);
        chromeStyle.SetBorderWidthAll(2);
        chromeStyle.BorderColor = new Color(0.55f, 0.48f, 0.32f, 0.95f);
        chromeStyle.CornerRadiusTopLeft = 3;
        chromeStyle.CornerRadiusTopRight = 3;
        chrome.AddThemeStyleboxOverride("panel", chromeStyle);
        AddChild(chrome);

        // Title bar (top strip, 22px).
        const float TitleH = 22f;
        var titleBar = new Panel { Name = "TitleBar" };
        titleBar.Position = new Vector2(0f, 0f);
        titleBar.Size = new Vector2(ModalW, TitleH);
        var titleStyle = new StyleBoxFlat();
        titleStyle.BgColor = new Color(0.18f, 0.15f, 0.1f, 1f);
        titleStyle.SetBorderWidthAll(0);
        titleBar.AddThemeStyleboxOverride("panel", titleStyle);
        AddChild(titleBar);

        _titleLabel = new Label
        {
            Name = "TitleLabel",
            Text = GetModalTitle(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _titleLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _titleLabel.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.8f, 1f));
        titleBar.AddChild(_titleLabel);

        // Close button (top-right corner).
        _closeButton = new Button
        {
            Name = "CloseButton",
            Text = "✕",
            CustomMinimumSize = new Vector2(TitleH, TitleH),
        };
        _closeButton.Position = new Vector2(ModalW - TitleH, 0f);
        _closeButton.Pressed += Hide;
        AddChild(_closeButton);

        // Content area — occupies the space below the title bar.
        ContentArea = new Control
        {
            Name = "ContentArea",
            Position = new Vector2(0f, TitleH),
            Size = new Vector2(ModalW, ModalH - TitleH),
            MouseFilter = MouseFilterEnum.Pass,
        };
        AddChild(ContentArea);

        // Give derived classes a chance to populate the content area.
        BuildContent(ContentArea);

        GD.Print($"[CenteredModal:{Name}] Built. W={ModalW} H={ModalH}. " +
                 "center = (screen − size) / 2 on both axes. " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §5.1 §5.8 CONFIRMED-formula.");
    }

    // ── Abstract / virtual surface for derived modals ──────────────────────────────────────────

    /// <summary>Returns the title bar text for this modal. Override in derived classes.</summary>
    protected virtual string GetModalTitle() => string.Empty;

    /// <summary>
    /// Override to populate <paramref name="content"/> with dialog-specific widgets.
    /// Called from <see cref="BuildModal"/> after the chrome is built, with
    /// <paramref name="content"/> already anchored to the inner rect below the title bar.
    /// </summary>
    protected virtual void BuildContent(Control content)
    {
    }

    // ── Show / hide helpers ────────────────────────────────────────────────────────────────────

    /// <summary>Shows the modal and brings it to front. PASSIVE: view state only.</summary>
    public new void Show()
    {
        Visible = true;
        MoveToFront();
    }

    /// <summary>Hides the modal. PASSIVE: view state only.</summary>
    public new void Hide() => Visible = false;

    /// <summary>Returns true when the modal is currently visible.</summary>
    public bool IsOpen => Visible;
}