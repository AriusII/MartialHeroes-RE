using Godot;
using MartialHeroes.Client.Application.Hud;

namespace MartialHeroes.Client.Godot.HUD;

/// <summary>
/// Docked HUD frame showing the selected target's name, HP bar, and MP bar.
///
/// PASSIVE: binds to <see cref="IHudEventHub.TargetChanges"/>; hidden when no target is selected.
/// Zero game logic — values come directly from <see cref="TargetChangedEvent"/> (server-authoritative).
///
/// Combat spec: Docs/RE/specs/combat.md §10.1 — "the global hovered/selected target drives the
/// target-info tooltip / overhead name+level billboard"; §12.2 — "the HP-bar reads the actor's
/// absolute current HP" (ratio supplied by the Application layer, already computed from §5.1 5:53).
///
/// Channel drain: the integrator calls <see cref="Bind"/> once; from that point the widget
/// self-drains via <see cref="_Process(double)"/> on the Godot main thread using TryRead
/// (non-blocking, no async task on the render thread).
///
/// DEMO mode: when <see cref="Bind"/> is never called (scene tested in isolation) _Ready
/// populates a placeholder target so the widget is visible and testable.
///
/// Control hierarchy (procedural, no .tscn):
///   Control "TargetFrame"  (anchor top, below player stats)
///     PanelContainer
///       VBoxContainer
///         Label _nameLabel        — target name (CP949-decoded string from event)
///         HBoxContainer
///           Label "HP:"
///           ProgressBar _hpBar    — HP ratio [0,1]
///         HBoxContainer           — only shown when MpRatio > 0
///           Label "MP:"
///           ProgressBar _mpBar
/// </summary>
public sealed partial class TargetFrame : Control
{
    // -------------------------------------------------------------------------
    // Layout constants (PLAUSIBLE: exact chrome layout not recovered in spec)
    // spec: Docs/RE/specs/ui_system.md §12 open item 6 — in-game window layouts gated on manifest.
    // -------------------------------------------------------------------------

    private const float PanelWidth = 200f; // PLAUSIBLE
    private const float PanelHeight = 72f; // PLAUSIBLE

    // -------------------------------------------------------------------------
    // Child control handles (built in _Ready)
    // -------------------------------------------------------------------------

    private Label _nameLabel = null!;
    private ProgressBar _hpBar = null!;
    private ProgressBar _mpBar = null!;
    private HBoxContainer _mpRow = null!;

    // -------------------------------------------------------------------------
    // Hub reference (wired by integrator via Bind)
    // -------------------------------------------------------------------------

    private IHudEventHub? _hub;

    // -------------------------------------------------------------------------
    // View state (no domain state)
    // -------------------------------------------------------------------------

    // Whether the current target has any MP to display.
    private bool _hasMp;

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        GD.Print("[TargetFrame] _Ready start");
        try
        {
            BuildUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[TargetFrame] _Ready failed: {ex.Message}");
            EnsureFallback();
        }

        // Hidden until a real TargetChangedEvent arrives via Bind(hub).
        // Synthetic placeholder removed — widget starts empty per the no-invented-data discipline.
        // spec: layer-05 — no synthetic data without an explicit DEV_OFFLINE_FLOW guard.
        Visible = false;

        GD.Print("[TargetFrame] Ready. Hidden until a TargetChangedEvent arrives.");
    }

    /// <summary>
    /// Drains the <see cref="IHudEventHub.TargetChanges"/> channel each frame.
    /// All <see cref="Control"/> mutations happen here on the Godot main thread.
    /// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — "drain Application channels on
    /// _Process; never touch a Control from a background channel-reader task".
    /// </summary>
    public override void _Process(double delta)
    {
        if (_hub is null) return;

        // TryRead: non-blocking; drains all pending snapshots (latest-wins channel capacity=1).
        while (_hub.TargetChanges.TryRead(out TargetChangedEvent? evt))
            ApplyEvent(evt);
    }

    // -------------------------------------------------------------------------
    // Public Bind surface
    // -------------------------------------------------------------------------

    /// <summary>
    /// Wires this widget to the application-layer HUD event hub.
    /// Called once by the integration stage after the hub is created.
    /// </summary>
    public void Bind(IHudEventHub hub)
    {
        _hub = hub;
        // Clear the DEMO state (set by _Ready when no hub was bound) so the frame
        // hides until a real TargetChangedEvent arrives.
        Visible = false;
        _nameLabel.Text = string.Empty;
        GD.Print("[TargetFrame] Bound to IHudEventHub. Demo state cleared; hidden until TargetChangedEvent.");
    }

    // -------------------------------------------------------------------------
    // UI construction
    // -------------------------------------------------------------------------

    private void BuildUi()
    {
        // Anchor below the player stats chrome (top-left, offset from top).
        // PLAUSIBLE — exact Y offset not recovered; sits below the GameHud chrome box.
        // spec: Docs/RE/specs/ui_system.md §12 open item 6.
        AnchorLeft = 0f;
        AnchorTop = 0f;
        AnchorRight = 0f;
        AnchorBottom = 0f;
        OffsetLeft = 4f;
        OffsetTop = 140f; // PLAUSIBLE: below the 130 px GameHud chrome box
        OffsetRight = 4f + PanelWidth;
        OffsetBottom = 140f + PanelHeight;

        MouseFilter = MouseFilterEnum.Ignore;

        var panel = new PanelContainer
        {
            Name = "TargetPanel",
        };
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // Semi-transparent dark background to separate from world.
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0f, 0f, 0f, 0.6f);
        style.BorderWidthBottom = 1;
        style.BorderWidthTop = 1;
        style.BorderWidthLeft = 1;
        style.BorderWidthRight = 1;
        style.BorderColor = new Color(0.6f, 0.4f, 0.1f, 1f); // warm border — PLAUSIBLE
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);

        var vbox = new VBoxContainer();
        panel.AddChild(vbox);

        // Target name label.
        _nameLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            ClipText = true,
            CustomMinimumSize = new Vector2(PanelWidth - 8f, 20f),
        };
        // Bright white so CP949 Korean name glyphs are always readable.
        _nameLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 0.8f));
        vbox.AddChild(_nameLabel);

        // HP row.
        var hpRow = new HBoxContainer();
        vbox.AddChild(hpRow);
        hpRow.AddChild(new Label { Text = "HP: ", CustomMinimumSize = new Vector2(28f, 0f) });
        _hpBar = new ProgressBar
        {
            MinValue = 0.0,
            MaxValue = 1.0,
            Value = 1.0,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(PanelWidth - 44f, 14f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        // Red fill for enemy HP.
        // spec: Docs/RE/specs/combat.md §12.3 — physical damage is red; we match that colour family.
        var hpFill = new StyleBoxFlat();
        hpFill.BgColor = new Color(0.85f, 0.15f, 0.15f, 1f);
        _hpBar.AddThemeStyleboxOverride("fill", hpFill);
        hpRow.AddChild(_hpBar);

        // MP row (hidden by default; shown when MpRatio > 0).
        _mpRow = new HBoxContainer { Visible = false };
        vbox.AddChild(_mpRow);
        _mpRow.AddChild(new Label { Text = "MP: ", CustomMinimumSize = new Vector2(28f, 0f) });
        _mpBar = new ProgressBar
        {
            MinValue = 0.0,
            MaxValue = 1.0,
            Value = 0.0,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(PanelWidth - 44f, 14f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        // Blue fill for MP.
        var mpFill = new StyleBoxFlat();
        mpFill.BgColor = new Color(0.1f, 0.3f, 0.9f, 1f);
        _mpBar.AddThemeStyleboxOverride("fill", mpFill);
        _mpRow.AddChild(_mpBar);
    }

    // -------------------------------------------------------------------------
    // Event application (main thread only)
    // -------------------------------------------------------------------------

    private void ApplyEvent(TargetChangedEvent evt)
    {
        // spec: Docs/RE/specs/combat.md §10.1 — "cleared on world re-entry".
        // TargetChangedEvent.None has IsCleared == true.
        if (evt.IsCleared)
        {
            Visible = false;
            return;
        }

        // Populate controls from the immutable snapshot — no stat math here.
        // spec: Docs/RE/specs/combat.md §12.2 — HP ratio sourced from 5:53 absolute HP / max HP.
        _nameLabel.Text = evt.Name;
        _hpBar.Value = Math.Clamp(evt.HpRatio, 0f, 1f);

        // MP row: only shown when the target has an MP pool.
        // spec: Docs/RE/specs/combat.md §10.1 — MpRatio delivered by the target-frame event.
        bool showMp = evt.MpRatio > 0f;
        if (showMp != _hasMp)
        {
            _hasMp = showMp;
            _mpRow.Visible = showMp;
        }

        if (showMp)
            _mpBar.Value = Math.Clamp(evt.MpRatio, 0f, 1f);

        Visible = true;
    }

    // -------------------------------------------------------------------------
    // Fallback (partial _Ready failure)
    // -------------------------------------------------------------------------

    private void EnsureFallback()
    {
        _nameLabel ??= new Label();
        _hpBar ??= new ProgressBar();
        _mpBar ??= new ProgressBar();
        _mpRow ??= new HBoxContainer();
    }
}