using Godot;
using MartialHeroes.Client.Godot.Autoload;

namespace MartialHeroes.Client.Godot.HUD;

/// <summary>
/// Toggleable skill catalogue browser (key K).
///
/// PASSIVE: reads <see cref="ClientContext.SkillCatalogue"/> (populated by the Application layer
/// from skills.scr) and renders it as a scrollable list of skill entries.  Zero game logic.
///
/// Control hierarchy (built procedurally in _Ready):
///   PanelContainer (draggable, anchored right of InventoryWindow)
///     VBoxContainer
///       HBoxContainer (title bar)
///         Label "Skills"
///         Button "✕" → hides the window
///       Label _countLabel  — "Showing N / Total" count
///       ScrollContainer
///         VBoxContainer
///           N × PanelContainer (skill row)
///             HBoxContainer
///               Label skill_id  — left-aligned, narrow
///               Label category  — e.g. "Cat:1"
///               Label cooldown  — e.g. "CD:500ms"
///               Label range     — e.g. "R:12.0"
///               Label target    — TargetMode mnemonic
///
/// Demo range: the first <see cref="DemoSkillCount"/> skills found by probing IDs
/// 1..<see cref="IdScanCeiling"/> are listed.  skills.scr has ~2000 valid records (confirmed).
/// A proper <c>IEnumerable&lt;SkillDefinition&gt; AllRecords</c> on SkillCatalogue would avoid
/// the probe; request that surface from the Application engineer.
///
/// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive HUD.
/// spec: Docs/RE/formats/config_tables.md §2.8 skills.scr — stride 1504 bytes, ~2000 real records.
/// spec: Docs/RE/structs/skill.md Part A.2 — field layout used for display values.
/// </summary>
public sealed partial class SkillWindow : Control
{
    // ---- tunables ----------------------------------------------------------------

    /// <summary>Maximum demo skills to display.</summary>
    private const int DemoSkillCount = 80;

    /// <summary>
    /// Upper probe ceiling for skill IDs.
    /// spec: Docs/RE/formats/config_tables.md §2.8 — valid skill_id &lt; 10,000,000: CONFIRMED.
    /// In practice observed IDs are well below 100,000; 5,000 catches all realistic records.
    /// </summary>
    private const uint IdScanCeiling = 5_000;

    // ---- drag state (view-only) --------------------------------------------------

    private bool _dragging;
    private Vector2 _dragOffset;

    // ---- child references -------------------------------------------------------

    private Label _countLabel = null!;

    // ---- catalogue reference (resolved in _Ready from the autoload) -------------

    private ClientContext? _context;

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        // Self-wire to the ClientContext autoload singleton.
        // ClientContext is registered as an autoload in project.godot under the name "ClientContext".
        // spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — composition root autoload.
        try
        {
            _context = GetNode<ClientContext>("/root/ClientContext");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SkillWindow] Could not resolve ClientContext: {ex.Message}. " +
                        "Catalogue will be empty (offline mode).");
        }

        try
        {
            BuildUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SkillWindow] _Ready failed: {ex.Message}");
        }

        // Hidden by default — toggled by key K.
        Visible = false;
    }

    public override void _Input(InputEvent ev)
    {
        // Toggle on key K press (not held).
        // spec: Docs/RE/specs/input_ui.md §4 — skill window key toggle.
        if (ev is InputEventKey key && key.Pressed && !key.Echo
            && key.Keycode == Key.K)
        {
            Visible = !Visible;
            if (Visible)
            {
                MoveToFront();
                PopulateList();
            }
            GetViewport().SetInputAsHandled();
        }

        // Drag — title-bar initiated.
        if (ev is InputEventMouseButton mb)
        {
            if (mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                if (GetRect().HasPoint(mb.Position))
                {
                    _dragging = true;
                    _dragOffset = mb.Position - GlobalPosition;
                }
            }
            else if (!mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                _dragging = false;
            }
        }

        if (_dragging && ev is InputEventMouseMotion motion)
        {
            GlobalPosition = motion.Position - _dragOffset;
        }
    }

    // -------------------------------------------------------------------------
    // UI construction
    // -------------------------------------------------------------------------

    private void BuildUi()
    {
        // Anchor to the right of the InventoryWindow (which sits at OffsetLeft=320).
        AnchorLeft = 0f;
        AnchorTop = 0.5f;
        AnchorRight = 0f;
        AnchorBottom = 0.5f;
        OffsetLeft = 640f;   // right of the inventory window
        OffsetTop = -260f;
        OffsetRight = 980f;
        OffsetBottom = 260f;

        var outerPanel = new PanelContainer();
        outerPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        outerPanel.CustomMinimumSize = new Vector2(340, 520);
        AddChild(outerPanel);

        var vbox = new VBoxContainer();
        outerPanel.AddChild(vbox);

        // ---- Title bar ----
        var titleRow = new HBoxContainer();
        vbox.AddChild(titleRow);

        var titleLabel = new Label
        {
            Text = "Skills (Catalogue Browser)",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        titleRow.AddChild(titleLabel);

        var closeBtn = new Button { Text = "X" };
        closeBtn.Pressed += () => { Visible = false; };
        titleRow.AddChild(closeBtn);

        // ---- Count line ----
        _countLabel = new Label { Text = "Loading…" };
        vbox.AddChild(_countLabel);

        // ---- Scrollable list ----
        var scroll = new ScrollContainer();
        scroll.CustomMinimumSize = new Vector2(330, 440);
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        vbox.AddChild(scroll);

        var list = new VBoxContainer();
        list.Name = "SkillList";
        scroll.AddChild(list);
    }

    // -------------------------------------------------------------------------
    // List population (lazy — called on first Visible=true)
    // -------------------------------------------------------------------------

    private bool _populated;

    private void PopulateList()
    {
        if (_populated) return;
        _populated = true;

        var list = FindChild("SkillList", true, false) as VBoxContainer;
        if (list is null)
        {
            GD.PrintErr("[SkillWindow] SkillList node not found — cannot populate.");
            return;
        }

        var catalogue = _context?.SkillCatalogue;
        if (catalogue is null || catalogue.Count == 0)
        {
            _countLabel.Text = "SkillCatalogue not available (offline mode).";
            AddRow(list, 0, "Cat:—", "CD:—", "R:—", "Self");
            return;
        }

        int shown = 0;
        for (uint id = 1; id <= IdScanCeiling && shown < DemoSkillCount; id++)
        {
            var def = catalogue.TryGet(id);
            if (def is null) continue;

            // spec: Docs/RE/structs/skill.md §A.2.5 — confirmed display fields.
            string catText = $"Cat:{def.Value.Category}";

            // CooldownCentiseconds is in centiseconds (×10 ms each).
            // spec: Docs/RE/structs/skill.md §A.2.5 — "+1334 u16 CombatRecast (centi-seconds): SAMPLE-VERIFIED".
            string cdText = def.Value.CooldownCentiseconds == 0
                ? "CD:—"
                : $"CD:{def.Value.CooldownMs}ms";

            // BaseRange is a world-space float.
            // spec: Docs/RE/structs/skill.md §A.2.5 — "+1312 f32 BaseRange: SAMPLE-VERIFIED".
            string rangeText = $"R:{def.Value.BaseRange:F1}";

            // TargetMode mnemonic.
            // spec: Docs/RE/structs/skill.md §A.5 — TargetShapeMode values 0..11 CONFIRMED.
            string targetText = TargetMnemonic(def.Value.TargetMode);

            AddRow(list, def.Value.Id.Value, catText, cdText, rangeText, targetText);
            shown++;
        }

        _countLabel.Text = $"Showing {shown} of {catalogue.Count} skills (first {shown} found in IDs 1–{IdScanCeiling})";
        GD.Print($"[SkillWindow] Populated {shown} skill rows from SkillCatalogue (total={catalogue.Count}).");
    }

    private static void AddRow(
        VBoxContainer list,
        uint skillId,
        string catText,
        string cdText,
        string rangeText,
        string targetText)
    {
        var rowPanel = new PanelContainer();
        rowPanel.CustomMinimumSize = new Vector2(0, 28);
        list.AddChild(rowPanel);

        var hbox = new HBoxContainer();
        rowPanel.AddChild(hbox);

        // ID label — fixed narrow width.
        var idLabel = new Label
        {
            Text = skillId == 0 ? "—" : $"#{skillId}",
            CustomMinimumSize = new Vector2(64, 0),
        };
        idLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.9f, 1.0f));
        hbox.AddChild(idLabel);

        AddCell(hbox, catText, 60, new Color(1.0f, 0.85f, 0.4f));
        AddCell(hbox, cdText, 80, new Color(0.8f, 0.8f, 0.8f));
        AddCell(hbox, rangeText, 56, new Color(0.6f, 1.0f, 0.6f));
        AddCell(hbox, targetText, 64, new Color(0.9f, 0.6f, 1.0f));
    }

    private static void AddCell(HBoxContainer row, string text, float minWidth, Color colour)
    {
        var label = new Label
        {
            Text = text,
            CustomMinimumSize = new Vector2(minWidth, 0),
        };
        label.AddThemeColorOverride("font_color", colour);
        row.AddChild(label);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Short mnemonic for the TargetMode enum — keeps the row compact.
    /// spec: Docs/RE/structs/skill.md §A.5 TargetShapeMode — values 0..11: CONFIRMED.
    /// </summary>
    private static string TargetMnemonic(MartialHeroes.Client.Domain.Skills.SkillTargetMode mode) =>
        mode switch
        {
            // spec: §A.5 value 0 — Self / single (movement): CONFIRMED.
            MartialHeroes.Client.Domain.Skills.SkillTargetMode.SingleSelfOrPrimary => "Self",
            // spec: §A.5 value 1 — Single with faction gate: CONFIRMED.
            MartialHeroes.Client.Domain.Skills.SkillTargetMode.SingleTarget => "Single",
            // spec: §A.5 value 2 — Single enemy or heal: CONFIRMED.
            MartialHeroes.Client.Domain.Skills.SkillTargetMode.SingleEnemyOrHeal => "S.Enem",
            // spec: §A.5 value 3 — Chain/nearby AoE: CONFIRMED.
            MartialHeroes.Client.Domain.Skills.SkillTargetMode.ChainNearbyAoe => "Chain",
            // spec: §A.5 value 4 — Cone/forward AoE: CONFIRMED.
            MartialHeroes.Client.Domain.Skills.SkillTargetMode.ConeForwardAoe => "Cone",
            // spec: §A.5 value 5 — Ground point: CONFIRMED.
            MartialHeroes.Client.Domain.Skills.SkillTargetMode.GroundPoint => "Ground",
            // spec: §A.5 value 6 — Party AoE: CONFIRMED.
            MartialHeroes.Client.Domain.Skills.SkillTargetMode.PartyAoe => "Party",
            // spec: §A.5 value 7 — Faction-gated single: CONFIRMED.
            MartialHeroes.Client.Domain.Skills.SkillTargetMode.FactionGatedSingle => "Faction",
            // spec: §A.5 value 9 — PK-gated: CONFIRMED.
            MartialHeroes.Client.Domain.Skills.SkillTargetMode.PkGatedSingle => "PK",
            // spec: §A.5 value 10 — Radial AoE both factions: CONFIRMED.
            MartialHeroes.Client.Domain.Skills.SkillTargetMode.RadialAoeBothFactions => "RadAoE",
            // spec: §A.5 value 11 — Self-only: CONFIRMED.
            MartialHeroes.Client.Domain.Skills.SkillTargetMode.SelfOnly => "SelfO",
            _ => "?",
        };
}
