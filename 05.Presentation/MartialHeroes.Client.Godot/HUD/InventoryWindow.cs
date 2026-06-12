using Godot;
using MartialHeroes.Client.Godot.Autoload;

namespace MartialHeroes.Client.Godot.HUD;

/// <summary>
/// Toggleable inventory catalogue browser (key I).
///
/// PASSIVE: reads <see cref="ClientContext.ItemCatalogue"/> (already populated by the
/// Application layer from items.csv, CP949-decoded) and renders it as a scrollable grid of
/// labelled item slots.  Zero game logic — this node never mutates any catalogue record or
/// domain state.
///
/// Control hierarchy (built procedurally in _Ready):
///   PanelContainer (draggable, anchored centre-left)
///     VBoxContainer
///       HBoxContainer (title bar)
///         Label "Inventory"
///         Button "X" → hides the window
///       Label _countLabel  — "Showing N / Total" count
///       ScrollContainer
///         GridContainer (3 columns)
///           N × PanelContainer (item slot)
///             VBoxContainer
///               Label  item_id   — numeric id
///               Label  item_name — CP949-decoded name (from ItemCatalogue)
///
/// Wiring: the node self-wires to <c>/root/ClientContext</c> in its own _Ready — no
/// Initialise call from GameLoop is required.  Add this node under the same parent as the
/// HUD node in World.tscn (see wiring notes below).
///
/// Demo range: the first <see cref="DemoItemCount"/> items found by probing IDs
/// 1..<see cref="IdScanCeiling"/> are listed so the catalogue wiring is immediately visible.
/// A proper enumerator on ItemCatalogue (<c>IEnumerable&lt;ItemCatalogueRecord&gt; AllRecords</c>)
/// would allow a full listing without the probe scan; that surface belongs in
/// <c>Client.Infrastructure.Catalog.ItemCatalogue</c> — request from the Application engineer.
///
/// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive HUD.
/// spec: Docs/RE/formats/config_tables.md §4 items.csv — CP949 names confirmed.
/// </summary>
public sealed partial class InventoryWindow : Control
{
    // ---- tunables ----------------------------------------------------------------

    /// <summary>Maximum demo items to display.</summary>
    private const int DemoItemCount = 64;

    /// <summary>
    /// Upper bound of the ID probe range used to collect demo items.
    /// items.csv has 89,712 rows; IDs are not necessarily contiguous starting from 1,
    /// so we scan a generous ceiling and stop once we have DemoItemCount hits.
    /// spec: Docs/RE/formats/config_tables.md §4.1 — "Total rows: 89,712": CONFIRMED.
    /// </summary>
    private const uint IdScanCeiling = 5_000;

    /// <summary>Grid columns in the item slot grid.</summary>
    private const int GridColumns = 3;

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
            GD.PrintErr($"[InventoryWindow] Could not resolve ClientContext: {ex.Message}. " +
                        "Catalogue will be empty (offline mode).");
        }

        try
        {
            BuildUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[InventoryWindow] _Ready failed: {ex.Message}");
        }

        // Hidden by default — toggled by key I.
        Visible = false;
    }

    public override void _Input(InputEvent ev)
    {
        // Toggle on key I press (not held).
        // spec: Docs/RE/specs/input_ui.md §4 — inventory key toggle.
        if (ev is InputEventKey key && key.Pressed && !key.Echo
            && key.Keycode == Key.I)
        {
            Visible = !Visible;
            if (Visible)
            {
                MoveToFront();
                // Lazy-populate on first show so it is built after _context is set.
                PopulateGrid();
            }

            GetViewport().SetInputAsHandled();
        }

        // Drag — title-bar initiated.
        if (ev is InputEventMouseButton mb)
        {
            if (mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                // Start drag only when click is inside this control.
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
        // Root anchor: centre-left of the viewport.
        AnchorLeft = 0f;
        AnchorTop = 0.5f;
        AnchorRight = 0f;
        AnchorBottom = 0.5f;
        OffsetLeft = 320f; // offset right of the stats panel
        OffsetTop = -260f;
        OffsetRight = 620f;
        OffsetBottom = 260f;

        var outerPanel = new PanelContainer();
        outerPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        outerPanel.CustomMinimumSize = new Vector2(300, 520);
        AddChild(outerPanel);

        var vbox = new VBoxContainer();
        outerPanel.AddChild(vbox);

        // ---- Title bar ----
        var titleRow = new HBoxContainer();
        vbox.AddChild(titleRow);

        var titleLabel = new Label
        {
            Text = "Inventory (Catalogue Browser)",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        titleRow.AddChild(titleLabel);

        var closeBtn = new Button { Text = "X" };
        closeBtn.Pressed += () => { Visible = false; };
        titleRow.AddChild(closeBtn);

        // ---- Count line ----
        _countLabel = new Label { Text = "Loading…" };
        vbox.AddChild(_countLabel);

        // ---- Scrollable grid ----
        var scroll = new ScrollContainer();
        scroll.CustomMinimumSize = new Vector2(290, 430);
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        vbox.AddChild(scroll);

        // The grid is populated lazily in PopulateGrid(), called on first show.
        var grid = new GridContainer { Columns = GridColumns };
        grid.Name = "ItemGrid";
        scroll.AddChild(grid);
    }

    // -------------------------------------------------------------------------
    // Grid population (lazy — called on first Visible=true)
    // -------------------------------------------------------------------------

    private bool _populated;

    private void PopulateGrid()
    {
        if (_populated) return;
        _populated = true;

        // Locate the grid child.
        var scroll = FindChild("ItemGrid", true, false) as GridContainer;
        if (scroll is null)
        {
            GD.PrintErr("[InventoryWindow] ItemGrid node not found — cannot populate.");
            return;
        }

        var catalogue = _context?.ItemCatalogue;
        if (catalogue is null || catalogue.Count == 0)
        {
            _countLabel.Text = "ItemCatalogue not available (offline mode).";
            AddSlot(scroll, 0, "(no data — VFS offline)", "#888888");
            return;
        }

        // Probe IDs 1..IdScanCeiling to collect up to DemoItemCount items.
        // This is a demo limitation; a proper AllRecords enumerator on ItemCatalogue
        // (to be added to Client.Infrastructure) would allow a full, ordered listing.
        int shown = 0;
        for (uint id = 1; id <= IdScanCeiling && shown < DemoItemCount; id++)
        {
            var rec = catalogue.TryGet(id);
            if (rec is null) continue;

            // Build a display label: "Name (id=N)" — trim empty names.
            string displayName = string.IsNullOrWhiteSpace(rec.Name)
                ? $"(id={rec.ItemId})"
                : $"{rec.Name} (id={rec.ItemId})";

            // Colour-code by tier rank for quick visual differentiation.
            // spec: Docs/RE/formats/config_tables.md §4.3 col22 item_tier_rank: CONFIRMED.
            string colour = rec.ItemTierRank switch
            {
                0 => "#cccccc", // common / grey
                1 => "#44dd44", // uncommon / green
                2 => "#4488ff", // rare / blue
                3 => "#cc44ff", // epic / purple
                4 => "#ffaa00", // legendary / orange
                _ => "#ffffff",
            };

            AddSlot(scroll, rec.ItemId, displayName, colour);
            shown++;
        }

        _countLabel.Text = $"Showing {shown} of {catalogue.Count} items (first {shown} found in IDs 1–{IdScanCeiling})";
        GD.Print($"[InventoryWindow] Populated {shown} item slots from ItemCatalogue (total={catalogue.Count}).");
    }

    private static void AddSlot(GridContainer grid, uint itemId, string displayName, string colour)
    {
        var slotPanel = new PanelContainer();
        slotPanel.CustomMinimumSize = new Vector2(90, 56);
        grid.AddChild(slotPanel);

        var inner = new VBoxContainer();
        slotPanel.AddChild(inner);

        var idLabel = new Label
        {
            Text = $"#{itemId}",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        idLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        inner.AddChild(idLabel);

        var nameLabel = new Label
        {
            Text = displayName,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        nameLabel.CustomMinimumSize = new Vector2(86, 0);
        if (Color.HtmlIsValid(colour))
            nameLabel.AddThemeColorOverride("font_color", new Color(colour));
        inner.AddChild(nameLabel);
    }
}