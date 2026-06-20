// Ui/Hud/HudAnnouncePanel.cs
//
// In-game AnnouncePanel — scrolling announce banner (slot 221, CODE-CONFIRMED).
//
// This is the BANNER DELEGATE of the ErrorPanel notice sink. When ErrorPanel (slot 168)
// receives a notice, it forwards the text here. AnnouncePanel is non-interactive.
//   spec: Docs/RE/specs/ui_system.md §8.25.1 CODE-CONFIRMED.
//   spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 221.
//
// Geometry (CODE-CONFIRMED):
//   8 GULabels (two batches: 3 + 5), each 110×12, stacked vertically.
//   No buttons, no close control.
//   No atlas — texture id 0 (transparent, text-only / scrolling labels).
//   spec: Docs/RE/specs/ui_system.md §8.25.1 CODE-CONFIRMED.
//
// Content: supplied by the caller (the notice sink) + a rotating-text source keyed by game-state.
//   No hardcoded msg.xdb id. Non-interactive (event handler consumes no input).
//   spec: Docs/RE/specs/ui_system.md §8.25.1 — "supplied by caller; rotating-text; no msg.xdb".
//
// S2C routing:
//   Server notices route through global notice sink →
//     (a) chat log, AND (b) ErrorPanel (slot 168) which DELEGATES the banner to AnnouncePanel.
//   There is NO dedicated opcode that targets AnnouncePanel directly.
//   TODO(world-campaign): wire the global notice sink 4/500 / 4/132 / 4/138 etc. via ErrorPanel delegation.
//
// PASSIVE: zero game logic. ShowAnnounce(text) is the entire API surface.

using Godot;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
///     In-game scrolling announce banner (AnnouncePanel, slot 221).
///     <para>
///         Non-interactive text banner. Text arrives via <see cref="ShowAnnounce" />
///         (called by <see cref="HudErrorPanel" /> delegation). Rotates through up to 8 label slots.
///     </para>
///     <para>PASSIVE: zero game logic; no domain mutation; no atlas.</para>
///     spec: Docs/RE/specs/ui_system.md §8.25.1 CODE-CONFIRMED.
///     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 221.
/// </summary>
public sealed partial class HudAnnouncePanel : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited geometry constants
    // spec: Docs/RE/specs/ui_system.md §8.25.1 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    // 8 GULabel slots: two batches (3 + 5), each 110×12, stacked vertically.
    // spec: §8.25.1 — "8 GULabels (two batches: 3+5), each 110×12, stacked vertically"
    private const int LabelCount = 8; // spec: §8.25.1
    private const float LabelW = 110f; // spec: §8.25.1 — each label W=110
    private const float LabelH = 12f; // spec: §8.25.1 — each label H=12
    private const float LabelSpacing = 13f; // one pixel gap between labels (H=12 + 1 gap)

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private readonly Label[] _labels = new Label[LabelCount];
    private int _nextSlot; // rolling index into _labels for the next announce
    private double _scrollTimer;

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Geometry pass: builds the 8 scrolling label slots.
    ///     spec: Docs/RE/specs/ui_system.md §8.25.1 CODE-CONFIRMED — "8 GULabels, 110×12, stacked".
    /// </summary>
    public void Build()
    {
        Name = "HudAnnouncePanel";

        // Top-center anchored — positioned near the top of the screen.
        // Exact anchor is set by the master-window machinery (absolute coords = debugger-pending).
        // We use a top-center anchor as a best-effort placement.
        // spec: §8.25.1 — AnnouncePanel geometry (slot 221) exact origin = MED/debugger-pending.
        AnchorLeft = 0.5f;
        AnchorTop = 0f;
        AnchorRight = 0.5f;
        AnchorBottom = 0f;
        OffsetLeft = -LabelW / 2f;
        OffsetTop = 60f;
        OffsetRight = LabelW / 2f;
        OffsetBottom = 60f + LabelH * LabelCount + LabelSpacing * (LabelCount - 1);

        // No atlas — texture id 0 / transparent.
        // spec: §8.25.1 — "binds no atlas (texture id 0 — purely scrolling text labels)"
        MouseFilter = MouseFilterEnum.Ignore; // non-interactive

        // Build 8 label slots (two batches: 3 + 5)
        // spec: §8.25.1 — "8 GULabels (two batches: 3+5), each 110×12, stacked vertically"
        for (var i = 0; i < LabelCount; i++)
        {
            _labels[i] = new Label
            {
                Name = $"AnnounceLabel{i}",
                Text = string.Empty,
                HorizontalAlignment = HorizontalAlignment.Center,
                Position = new Vector2(0f, i * LabelSpacing),
                Size = new Vector2(LabelW, LabelH), // spec: §8.25.1 — 110×12
                MouseFilter = MouseFilterEnum.Ignore,
                Visible = false
            };
            // The two batches (3+5) correspond to the two GULabel-construction sequences:
            // batch A = labels 0..2, batch B = labels 3..7.
            // spec: §8.25.1 — "two batches: 3 + 5"
            AddChild(_labels[i]);
        }

        // Start hidden; shown when an announce arrives.
        Visible = false;

        GD.Print("[HudAnnouncePanel] Built — scrolling announce banner slot 221 (8×110×12 labels, " +
                 "two batches 3+5, no atlas, non-interactive). " +
                 "Caller: HudErrorPanel delegation via notice sink. " +
                 "TODO(world-campaign): wire global notice sink (§8.25.3 4/500/4/132/4/138 → ErrorPanel → here). " +
                 "spec: Docs/RE/specs/ui_system.md §8.25.1 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Shows a scrolling announce text. Called by <see cref="HudErrorPanel" /> as the banner delegate,
    ///     and directly by <see cref="HudMaster.ShowAnnounce" />.
    ///     spec: Docs/RE/specs/ui_system.md §8.25.1 — "supplied by the caller (the notice sink)".
    /// </summary>
    public void ShowAnnounce(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Rotate through label slots.
        // spec: §8.25.1 — "custom animated text rotator keyed by a client game-state word"
        var slot = _nextSlot % LabelCount;
        _nextSlot = (_nextSlot + 1) % LabelCount;

        if (_labels[slot] != null)
        {
            _labels[slot].Text = text;
            _labels[slot].Visible = true;
        }

        Visible = true;
        _scrollTimer = 0.0;

        GD.Print($"[HudAnnouncePanel] ShowAnnounce slot={slot}: \"{text}\". " +
                 "spec: Docs/RE/specs/ui_system.md §8.25.1 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // _Process — timed text fade-out
    // -------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (!Visible) return;

        // Simple timed rotation: fade older labels after ~8 seconds.
        // The exact rotate/scroll timing is "custom animated keyed by game-state word" (§8.25.1 MED).
        _scrollTimer += delta;
        if (_scrollTimer > 8.0)
        {
            _scrollTimer = 0.0;
            // Clear the oldest label slot in rolling fashion.
            var oldest = _nextSlot % LabelCount;
            if (_labels[oldest] != null) _labels[oldest].Visible = false;

            // Check if all labels are empty → hide the panel.
            var anyVisible = false;
            foreach (var lbl in _labels)
                if (lbl.Visible)
                {
                    anyVisible = true;
                    break;
                }

            if (!anyVisible) Visible = false;
        }
    }
}