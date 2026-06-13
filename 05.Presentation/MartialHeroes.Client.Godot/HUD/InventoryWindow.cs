using Godot;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Screens;
using MartialHeroes.Client.Godot.Screens.Widgets;

namespace MartialHeroes.Client.Godot.HUD;

/// <summary>
/// Toggleable inventory catalogue browser (key I).
///
/// PASSIVE: reads <see cref="ClientContext.ItemCatalogue"/> (already populated by the
/// Application layer from items.csv, CP949-decoded) and renders it as a scrollable grid.
/// Zero game logic — this node never mutates any catalogue record or domain state.
///
/// Original chrome layer (stage-2, texture-driven):
///   Window chrome: uitex 0002 → data/ui/inventwindow.dds (1024×1024 DXT3).
///   Close button: shared modal chrome from inventwindow.dds.
///     NORMAL  src: (302, 900, 113, 40)  — spec: Docs/RE/specs/ui_system.md §8.1 "Yes #1" button CONFIRMED.
///     HOVER   src: (415, 900, 113, 40)  — spec: Docs/RE/specs/ui_system.md §8.1 CONFIRMED.
///     PRESSED src: (302, 900, 113, 40)  — spec: §8.1 — PRESSED = NORMAL for this button.
///   Grid cell background sub-rect: (0, 0, 30, 30) on inventwindow.dds — PLAUSIBLE.
///   Window chrome full panel: (318, 647, 340, 190) — spec: §8.3 CONFIRMED (shared modal chrome).
///   Caption "닫기" (close) from msg.xdb id 102 — spec: §10 known id range 101–107.
///
/// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive HUD.
/// spec: Docs/RE/specs/ui_system.md §8.5 — uitex integer binding; §8.3 — shared modal chrome.
/// spec: Docs/RE/formats/ui_manifests.md §1.4 — uitex 0002 = data/ui/inventwindow.dds.
/// spec: Docs/RE/formats/config_tables.md §4 items.csv — CP949 names confirmed.
/// </summary>
public sealed partial class InventoryWindow : Control
{
    // -------------------------------------------------------------------------
    // Atlas binding constants
    // spec: Docs/RE/formats/ui_manifests.md §1.4 — uitex 0002 = data/ui/inventwindow.dds.
    // -------------------------------------------------------------------------

    private const int   InvTexId    = 2;
    private const string InvTexPath = "data/ui/inventwindow.dds";
    // spec: Docs/RE/formats/ui_manifests.md §1.4 SAMPLE-VERIFIED.

    // Shared modal chrome panel on inventwindow.dds — used as window chrome.
    // spec: Docs/RE/specs/ui_system.md §8.3 — "340×190 chrome at source (318,647)": CODE-CONFIRMED.
    private const int ModalChromeSrcX = 318;
    private const int ModalChromeSrcY = 647;
    private const int ModalChromeW    = 340;
    private const int ModalChromeH    = 190;

    // Close button frames on inventwindow.dds.
    // spec: Docs/RE/specs/ui_system.md §8.1 "Quit-confirm Yes #1": CODE-CONFIRMED.
    private const int CloseBtnNormX    = 302;
    private const int CloseBtnNormY    = 900;
    private const int CloseBtnHoverX   = 415;
    private const int CloseBtnHoverY   = 900;
    private const int CloseBtnPressX   = 302; // PRESSED = NORMAL, spec §1.5
    private const int CloseBtnPressY   = 900;
    private const int CloseBtnW        = 113;
    private const int CloseBtnH        = 40;

    // Grid cell background sub-rect.
    // No per-cell layout recovered for inventwindow.dds — PLAUSIBLE small square from top-left.
    // spec: Docs/RE/specs/ui_system.md §12 open item 6 — in-game window layouts gated on manifest.
    private const int CellBgSrcX = 0;  // PLAUSIBLE
    private const int CellBgSrcY = 0;  // PLAUSIBLE
    private const int CellBgW    = 30; // PLAUSIBLE
    private const int CellBgH    = 30; // PLAUSIBLE

    // msg.xdb id for close button caption "닫기".
    // spec: Docs/RE/specs/ui_system.md §10 — id 102 in 101–107 button label range.
    private const uint MsgIdClose = 102;

    // -------------------------------------------------------------------------
    // Tunables
    // -------------------------------------------------------------------------

    private const int DemoItemCount  = 64;
    private const uint IdScanCeiling = 5_000;
    private const int GridColumns    = 4;
    // spec: Docs/RE/formats/config_tables.md §4.1 — "Total rows: 89,712": CONFIRMED.

    // -------------------------------------------------------------------------
    // Drag state (view-only)
    // -------------------------------------------------------------------------

    private bool _dragging;
    private Vector2 _dragOffset;

    // -------------------------------------------------------------------------
    // Child references
    // -------------------------------------------------------------------------

    private Label _countLabel = null!;
    private TextureRect _windowChrome = null!;

    // -------------------------------------------------------------------------
    // Catalogue + asset loader references
    // -------------------------------------------------------------------------

    private ClientContext? _context;
    private UiAssetLoader? _uiLoader;

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        // Self-wire to ClientContext autoload singleton.
        try
        {
            _context = GetNode<ClientContext>("/root/ClientContext");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[InventoryWindow] Could not resolve ClientContext: {ex.Message}. " +
                        "Catalogue will be empty (offline mode).");
        }

        // Open a UiAssetLoader for atlas slicing.
        // We keep it separate from UiCatalogs so we can use UiAssetLoader.Slice for AtlasTexture creation.
        try
        {
            _uiLoader = UiAssetLoader.Open();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[InventoryWindow] UiAssetLoader.Open failed: {ex.Message} — chrome offline.");
        }

        try
        {
            BuildUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[InventoryWindow] _Ready failed: {ex.Message}");
        }

        Visible = false;
        GD.Print("[InventoryWindow] Ready. Chrome wired to uitex 0002 (inventwindow.dds).");
    }

    public override void _ExitTree()
    {
        _uiLoader?.Dispose();
        _uiLoader = null;
    }

    public override void _Input(InputEvent ev)
    {
        // Toggle on key I press (not held).
        // spec: Docs/RE/specs/input_ui.md §4 — inventory key toggle.
        if (ev is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.I)
        {
            Visible = !Visible;
            if (Visible)
            {
                MoveToFront();
                PopulateGrid();
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
            GlobalPosition = motion.Position - _dragOffset;
    }

    // -------------------------------------------------------------------------
    // UI construction
    // -------------------------------------------------------------------------

    private void BuildUi()
    {
        // Anchor: centre-left of the viewport.
        AnchorLeft   = 0f;
        AnchorTop    = 0.5f;
        AnchorRight  = 0f;
        AnchorBottom = 0.5f;
        OffsetLeft   = 320f;
        OffsetTop    = -260f;
        OffsetRight  = 660f;
        OffsetBottom = 260f;

        // Window root container.
        var outerPanel = new PanelContainer();
        outerPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        outerPanel.CustomMinimumSize = new Vector2(340, 520);
        AddChild(outerPanel);

        // ---- Window chrome TextureRect (inventwindow.dds modal chrome) ----
        // spec: Docs/RE/specs/ui_system.md §8.3 — "340×190 chrome at (318,647) from inventwindow.dds".
        // We stretch the modal chrome to fill the full window for a cohesive look.
        _windowChrome = new TextureRect
        {
            Name        = "InvWindowChrome",
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _windowChrome.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        outerPanel.AddChild(_windowChrome);
        BindWindowChrome();

        // ---- Content vbox (above chrome) ----
        var vbox = new VBoxContainer();
        outerPanel.AddChild(vbox);

        // ---- Title bar ----
        var titleRow = new HBoxContainer();
        vbox.AddChild(titleRow);

        // TODO(open-item): msg.xdb id for the inventory window title is unrecovered.
        //   id 111 is the login option-tab-1 label (action 111 'o'), not an inventory title —
        //   it will display wrong Korean text when VFS is loaded.
        //   spec: Docs/RE/specs/ui_system.md §10 / §8.1 action-id map. CODE-CONFIRMED (mismatch).
        //   Until the correct id is recovered, prefer the English fallback unconditionally.
        // PLAUSIBLE: inventory-window title id unrecovered; English fallback used.
        const string titleText = "Inventory";
        var titleLabel = new Label
        {
            Text = titleText,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        titleRow.AddChild(titleLabel);

        // Close button backed by inventwindow.dds sub-rect.
        // spec: Docs/RE/specs/ui_system.md §8.1 "Quit-confirm Yes #1" button frames: CODE-CONFIRMED.
        var closeBtn = BuildCloseButton();
        titleRow.AddChild(closeBtn);

        // ---- Count line ----
        _countLabel = new Label { Text = "Loading…" };
        vbox.AddChild(_countLabel);

        // ---- Scrollable grid ----
        var scroll = new ScrollContainer();
        scroll.CustomMinimumSize   = new Vector2(330, 430);
        scroll.SizeFlagsVertical   = SizeFlags.ExpandFill;
        vbox.AddChild(scroll);

        var grid = new GridContainer { Columns = GridColumns, Name = "ItemGrid" };
        scroll.AddChild(grid);
    }

    // -------------------------------------------------------------------------
    // Chrome + button helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Binds the window chrome TextureRect to the shared modal chrome on inventwindow.dds.
    /// spec: Docs/RE/specs/ui_system.md §8.3 — "(318,647) 340×190": CODE-CONFIRMED.
    /// spec: Docs/RE/formats/ui_manifests.md §1.4 — uitex 0002 = data/ui/inventwindow.dds.
    /// </summary>
    private void BindWindowChrome()
    {
        if (_windowChrome is null) return;

        // Try via UiCatalogs (uitex id 2) first.
        if (_context?.UiCatalogs is { } cats)
        {
            ImageTexture? tex = cats.GetTexture(InvTexId);
            if (tex is not null)
            {
                _windowChrome.Texture = new AtlasTexture
                {
                    Atlas      = tex,
                    Region     = new Rect2(ModalChromeSrcX, ModalChromeSrcY, ModalChromeW, ModalChromeH),
                    FilterClip = true,
                };
                GD.Print($"[InventoryWindow] Chrome bound via UiCatalogs uitex {InvTexId} " +
                         $"({InvTexPath}). Rect2({ModalChromeSrcX},{ModalChromeSrcY},{ModalChromeW},{ModalChromeH}) CODE-CONFIRMED §8.3");
                return;
            }
        }

        // Fallback: UiAssetLoader direct path.
        if (_uiLoader is not null)
        {
            AtlasTexture? at = _uiLoader.Slice(InvTexPath, ModalChromeSrcX, ModalChromeSrcY, ModalChromeW, ModalChromeH);
            if (at is not null)
            {
                _windowChrome.Texture = at;
                GD.Print($"[InventoryWindow] Chrome bound via UiAssetLoader ({InvTexPath}).");
                return;
            }
        }

        GD.Print("[InventoryWindow] inventwindow.dds unavailable — chrome invisible (VFS offline).");
    }

    /// <summary>
    /// Builds the close StateButton from inventwindow.dds sub-rects.
    /// spec: Docs/RE/specs/ui_system.md §8.1 — "Yes #1" button frames: CODE-CONFIRMED.
    /// Caption from msg.xdb id 102 — spec §10 id range 101–107.
    /// </summary>
    private Control BuildCloseButton()
    {
        // Get caption from msg.xdb id 102 (close/cancel label range).
        // spec: Docs/RE/specs/ui_system.md §10 — id 102 in 101–107 button label range.
        string caption = GetMsg(MsgIdClose, "X");

        if (_uiLoader is not null)
        {
            AtlasTexture? normFrame    = _uiLoader.Slice(InvTexPath, CloseBtnNormX,  CloseBtnNormY,  CloseBtnW, CloseBtnH);
            AtlasTexture? hoverFrame   = _uiLoader.Slice(InvTexPath, CloseBtnHoverX, CloseBtnHoverY, CloseBtnW, CloseBtnH);
            AtlasTexture? pressedFrame = _uiLoader.Slice(InvTexPath, CloseBtnPressX, CloseBtnPressY, CloseBtnW, CloseBtnH);

            if (normFrame is not null)
            {
                var stateBtn = new StateButton
                {
                    Name          = "CloseBtn",
                    CustomMinimumSize = new Vector2(CloseBtnW, CloseBtnH),
                    NormalFrame   = normFrame,
                    HoverFrame    = hoverFrame,
                    PressedFrame  = pressedFrame,
                    Caption       = caption,
                    ActionId      = 0,
                };
                stateBtn.ActionFired += _ => { Visible = false; };
                return stateBtn;
            }
        }

        // Fallback: plain Godot Button.
        var fallback = new Button { Text = caption };
        fallback.Pressed += () => { Visible = false; };
        return fallback;
    }

    // -------------------------------------------------------------------------
    // Grid population (lazy — called on first Visible=true)
    // -------------------------------------------------------------------------

    private bool _populated;

    private void PopulateGrid()
    {
        if (_populated) return;
        _populated = true;

        var grid = FindChild("ItemGrid", true, false) as GridContainer;
        if (grid is null)
        {
            GD.PrintErr("[InventoryWindow] ItemGrid node not found — cannot populate.");
            return;
        }

        var catalogue = _context?.ItemCatalogue;
        if (catalogue is null || catalogue.Count == 0)
        {
            _countLabel.Text = "ItemCatalogue not available (offline mode).";
            AddSlot(grid, 0, "(no data — VFS offline)", "#888888");
            return;
        }

        int shown = 0;
        for (uint id = 1; id <= IdScanCeiling && shown < DemoItemCount; id++)
        {
            var rec = catalogue.TryGet(id);
            if (rec is null) continue;

            string displayName = string.IsNullOrWhiteSpace(rec.Name)
                ? $"(id={rec.ItemId})"
                : $"{rec.Name} (id={rec.ItemId})";

            // Colour-code by tier rank.
            // spec: Docs/RE/formats/config_tables.md §4.3 col22 item_tier_rank: CONFIRMED.
            string colour = rec.ItemTierRank switch
            {
                0 => "#cccccc",
                1 => "#44dd44",
                2 => "#4488ff",
                3 => "#cc44ff",
                4 => "#ffaa00",
                _ => "#ffffff",
            };

            AddSlot(grid, rec.ItemId, displayName, colour);
            shown++;
        }

        _countLabel.Text = $"Showing {shown} of {catalogue.Count} items (first {shown} found in IDs 1–{IdScanCeiling})";
        GD.Print($"[InventoryWindow] Populated {shown} item slots from ItemCatalogue (total={catalogue.Count}).");
    }

    private void AddSlot(GridContainer grid, uint itemId, string displayName, string colour)
    {
        var slotPanel = new PanelContainer();
        slotPanel.CustomMinimumSize = new Vector2(78, 78);
        grid.AddChild(slotPanel);

        // Background TextureRect from inventwindow.dds cell backing. // PLAUSIBLE
        if (_uiLoader is not null || _context?.UiCatalogs is not null)
        {
            var bg = new TextureRect
            {
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

            AtlasTexture? cellTex = null;
            if (_context?.UiCatalogs is { } cats)
            {
                ImageTexture? atlasImg = cats.GetTexture(InvTexId);
                if (atlasImg is not null)
                    cellTex = new AtlasTexture { Atlas = atlasImg, Region = new Rect2(CellBgSrcX, CellBgSrcY, CellBgW, CellBgH), FilterClip = true }; // PLAUSIBLE
            }
            if (cellTex is null && _uiLoader is not null)
                cellTex = _uiLoader.Slice(InvTexPath, CellBgSrcX, CellBgSrcY, CellBgW, CellBgH); // PLAUSIBLE

            if (cellTex is not null) bg.Texture = cellTex;
            slotPanel.AddChild(bg);
        }

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
            AutowrapMode        = TextServer.AutowrapMode.Word,
        };
        nameLabel.CustomMinimumSize = new Vector2(74, 0);
        if (Color.HtmlIsValid(colour))
            nameLabel.AddThemeColorOverride("font_color", new Color(colour));
        inner.AddChild(nameLabel);
    }

    // -------------------------------------------------------------------------
    // msg.xdb helpers
    // -------------------------------------------------------------------------

    private string GetMsg(uint id, string fallback)
    {
        if (_context?.UiCatalogs is { } cats)
            return cats.GetMessage((int)id, fallback);
        return fallback;
    }
}
