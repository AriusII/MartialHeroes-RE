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
/// HUD placement (CODE-CONFIRMED):
///   W = 732, right-anchored. In Godot terms: AnchorRight=1, OffsetLeft=-732, OffsetRight=+318.
///   This positions the panel with its left edge 414 px from the right of the viewport
///   (= 1024 - 732 + 318 - 318 = 1024 - 732 on a 1024-wide canvas). The +318 right-offset
///   is the recovered "screen_width + 318" right-anchor inset from the HUD-assembly call site —
///   the 318 px tail extends past the right viewport edge (classic slide-in from right).
///   spec: Docs/RE/specs/ui_hud_layout.md §1.1 — "W=732, right-anchored at screen_width+318" CODE-CONFIRMED.
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

    private const int InvTexId = 2;
    private const string InvTexPath = "data/ui/inventwindow.dds";
    // spec: Docs/RE/formats/ui_manifests.md §1.4 SAMPLE-VERIFIED.

    // Shared modal chrome panel on inventwindow.dds — used as window chrome.
    // spec: Docs/RE/specs/ui_system.md §8.3 — "340×190 chrome at source (318,647)": CODE-CONFIRMED.
    private const int ModalChromeSrcX = 318;
    private const int ModalChromeSrcY = 647;
    private const int ModalChromeW = 340;
    private const int ModalChromeH = 190;

    // Close button frames on inventwindow.dds.
    // spec: Docs/RE/specs/ui_system.md §8.1 "Quit-confirm Yes #1": CODE-CONFIRMED.
    private const int CloseBtnNormX = 302;
    private const int CloseBtnNormY = 900;
    private const int CloseBtnHoverX = 415;
    private const int CloseBtnHoverY = 900;
    private const int CloseBtnPressX = 302; // PRESSED = NORMAL, spec §1.5
    private const int CloseBtnPressY = 900;
    private const int CloseBtnW = 113;
    private const int CloseBtnH = 40;

    // Grid cell background sub-rect.
    // No per-cell layout recovered for inventwindow.dds — PLAUSIBLE small square from top-left.
    // spec: Docs/RE/specs/ui_system.md §12 open item 6 — in-game window layouts gated on manifest.
    private const int CellBgSrcX = 0; // PLAUSIBLE
    private const int CellBgSrcY = 0; // PLAUSIBLE
    private const int CellBgW = 30; // PLAUSIBLE
    private const int CellBgH = 30; // PLAUSIBLE

    // msg.xdb id for close button caption "닫기".
    // spec: Docs/RE/specs/ui_system.md §10 — id 102 in 101–107 button label range.
    private const uint MsgIdClose = 102;

    // -------------------------------------------------------------------------
    // HUD placement constants
    // spec: Docs/RE/specs/ui_hud_layout.md §1.1 — W=318 H=732, right-anchored at screen_width+318.
    // F4 fix: earlier code had width/height transposed (W=732, H=520). The spec's §1.1 correction
    //   states W=318 is the column width and 732 is the HEIGHT. The right-inset of 318 is the
    //   offset past the right viewport edge (the panel extends 318 px past screen right edge).
    // -------------------------------------------------------------------------

    /// <summary>
    /// Panel width: 318 px.
    /// spec: Docs/RE/specs/ui_hud_layout.md §1.1 — "W=318" (right column width). CODE-CONFIRMED.
    /// Corrected from the previous transposition (was 732 = the height).
    /// </summary>
    private const int InvPanelW = 318; // spec: Docs/RE/specs/ui_hud_layout.md §1.1 CODE-CONFIRMED

    /// <summary>
    /// Right-anchor inset: positive offset past the right viewport edge (same value as the width).
    /// In Godot: OffsetRight = +318 (with AnchorLeft/Right = 1.0) positions the panel's right
    /// border 318 px beyond the viewport's right edge. This matches the legacy "screen_width + 318"
    /// placement convention observed at the HUD-assembly call site.
    /// spec: Docs/RE/specs/ui_hud_layout.md §1.1 — "right-anchored at screen_width+318". CODE-CONFIRMED.
    /// </summary>
    private const int InvRightInset = 318; // spec: Docs/RE/specs/ui_hud_layout.md §1.1 CODE-CONFIRMED

    /// <summary>
    /// Panel height: 732 px.
    /// spec: Docs/RE/specs/ui_hud_layout.md §1.1 — "H=732" (right column height). CODE-CONFIRMED.
    /// Corrected from the previous transposition (was 520 PLAUSIBLE).
    /// </summary>
    private const int InvPanelH = 732; // spec: Docs/RE/specs/ui_hud_layout.md §1.1 CODE-CONFIRMED

    // -------------------------------------------------------------------------
    // Tunables
    // -------------------------------------------------------------------------

    private const int DemoItemCount = 64;
    private const int GridColumns = 4;
    // spec: Docs/RE/formats/config_tables.md §4.1 — "Total rows: 89,712": CONFIRMED.

    // Icon display size in pixels. The native DDS dimensions are used (no forced resize),
    // so we scale the display rect to fit the slot.
    // spec: Docs/RE/formats/ui_manifests.md §9 item #12 — "item icon native pixel size and
    //       inventory cell layout unpinned": OPEN.  We use 48×48 as a reasonable display size.
    private const int IconDisplaySize = 48;

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

    // Pre-fetched demo icon list from ItemIconCatalog (texturelist.txt first N entries).
    // Populated once in _Ready; null entries mean the DDS failed to load (offline fallback).
    // spec: Docs/RE/formats/ui_manifests.md §10.5 — "whole-texture blit": CODE-CONFIRMED.
    private IReadOnlyList<(int TexId, ImageTexture? Icon)>? _demoIcons;

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

        // Pre-fetch demo item icons from ItemIconCatalog (texturelist.txt first DemoItemCount entries).
        // This is a whole-texture blit per icon — no sub-rect, no atlas math.
        // spec: Docs/RE/formats/ui_manifests.md §10.5 — "whole-texture blit, no sub-rect": CODE-CONFIRMED.
        // spec: Docs/RE/formats/ui_manifests.md §10.3 — texturelist.txt keyed by tex_id: CODE-CONFIRMED.
        try
        {
            if (_context?.ItemIconCatalog is { } cat)
            {
                _demoIcons = cat.GetDemoIcons(DemoItemCount);
                int loaded = _demoIcons.Count(p => p.Icon is not null);
                GD.Print($"[InventoryWindow] Item icons: {_demoIcons.Count} entries fetched, " +
                         $"{loaded} DDS loaded. spec: Docs/RE/formats/ui_manifests.md §10 CODE-CONFIRMED.");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[InventoryWindow] ItemIconCatalog.GetDemoIcons failed: {ex.Message} — icons offline.");
            _demoIcons = null;
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

    /// <summary>
    /// Toggles the inventory window open/closed. Called by the single HUD key-command dispatcher
    /// (GameHud._Input) rather than overriding _Input here for the I key.
    /// F4 fix: panels expose Toggle(); the dispatcher in GameHud is the single owner of key routing.
    /// spec: Docs/RE/specs/input_ui.md §3a / §5 — single command dispatcher, no per-widget focus chain.
    /// spec: Docs/RE/specs/ui_system.md §15 — in-game HUD key command dispatch.
    /// </summary>
    public void Toggle()
    {
        Visible = !Visible;
        if (Visible)
        {
            MoveToFront();
            PopulateGrid();
        }
    }

    public override void _Input(InputEvent ev)
    {
        // Key-toggle (I) is now routed through GameHud._Input (single dispatcher).
        // Only drag handling remains here.
        // F4 fix: per-panel key grabs removed. spec: Docs/RE/specs/input_ui.md §3a / §5.

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
        // Right-anchored placement per recovered HUD-assembly call site.
        // W=318 H=732, anchored to the right viewport edge with a +318 right-inset past the edge.
        // F4 fix: width was incorrectly set to 732 (the height); corrected to 318.
        // In Godot anchor terms: both AnchorLeft and AnchorRight = 1.0 (right-edge anchor),
        //   OffsetLeft  = −InvPanelW  = −318  → panel left edge is 318 px left of viewport right.
        //   OffsetRight = +InvRightInset = +318 → panel right border extends 318 px past viewport right.
        // On a 1024-wide canvas this makes the left edge at pixel 1024 − 318 = 706 (abs) and
        // the right edge at 1024 + 318 = 1342, so 318 px are clipped (the off-screen portion).
        // spec: Docs/RE/specs/ui_hud_layout.md §1.1 — "W=318 H=732, right-anchored at screen_width+318" CODE-CONFIRMED.
        AnchorLeft = 1f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 0f;
        OffsetLeft = -InvPanelW; // spec: Docs/RE/specs/ui_hud_layout.md §1.1 CODE-CONFIRMED
        OffsetTop = 4f; // PLAUSIBLE — Y origin not recovered; small top margin
        OffsetRight = InvRightInset; // spec: Docs/RE/specs/ui_hud_layout.md §1.1 CODE-CONFIRMED
        OffsetBottom = InvPanelH + 4f; // PLAUSIBLE — height not a literal; using InvPanelH

        // Window root container.
        var outerPanel = new PanelContainer();
        outerPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        outerPanel.CustomMinimumSize = new Vector2(InvPanelW, InvPanelH);
        AddChild(outerPanel);

        // ---- Window chrome TextureRect (inventwindow.dds modal chrome) ----
        // spec: Docs/RE/specs/ui_system.md §8.3 — "340×190 chrome at (318,647) from inventwindow.dds".
        // We stretch the modal chrome to fill the full window for a cohesive look.
        _windowChrome = new TextureRect
        {
            Name = "InvWindowChrome",
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
        // Width spans the full panel minus small margins; height fills the remaining space.
        // spec: Docs/RE/specs/ui_hud_layout.md §1.1 — panel W=318 H=732 CODE-CONFIRMED.
        var scroll = new ScrollContainer();
        scroll.CustomMinimumSize = new Vector2(InvPanelW - 12, 430);
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
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
                    Atlas = tex,
                    Region = new Rect2(ModalChromeSrcX, ModalChromeSrcY, ModalChromeW, ModalChromeH),
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
            AtlasTexture? at = _uiLoader.Slice(InvTexPath, ModalChromeSrcX, ModalChromeSrcY, ModalChromeW,
                ModalChromeH);
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
            AtlasTexture? normFrame = _uiLoader.Slice(InvTexPath, CloseBtnNormX, CloseBtnNormY, CloseBtnW, CloseBtnH);
            AtlasTexture? hoverFrame =
                _uiLoader.Slice(InvTexPath, CloseBtnHoverX, CloseBtnHoverY, CloseBtnW, CloseBtnH);
            AtlasTexture? pressedFrame =
                _uiLoader.Slice(InvTexPath, CloseBtnPressX, CloseBtnPressY, CloseBtnW, CloseBtnH);

            if (normFrame is not null)
            {
                var stateBtn = new StateButton
                {
                    Name = "CloseBtn",
                    CustomMinimumSize = new Vector2(CloseBtnW, CloseBtnH),
                    NormalFrame = normFrame,
                    HoverFrame = hoverFrame,
                    PressedFrame = pressedFrame,
                    Caption = caption,
                    ActionId = 0,
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

        // The item catalogue has 89,712 records with large item_ids (e.g. 202110003+).
        // We cannot iterate all IDs by range scan — use the icon catalogue's texturelist.txt entries
        // as the display set, since those are the items with known icons.
        // The per-item tex_id→item_id join is spec-open (ui_manifests.md §9 item #12), so
        // we display the tex_id directly as the slot identifier for now.
        // spec: Docs/RE/formats/ui_manifests.md §10 — texturelist.txt tex_id list. CODE-CONFIRMED.
        // spec: Docs/RE/formats/ui_manifests.md §9 item #12 — inventory cell layout UNPINNED.
        if (_demoIcons is not null && _demoIcons.Count > 0)
        {
            int shown = 0;
            int total = _context?.ItemCatalogue?.Count ?? 0;
            foreach ((int texId, ImageTexture? icon) in _demoIcons)
            {
                if (shown >= DemoItemCount) break;
                // Display using tex_id as slot id; name is a placeholder until the join is pinned.
                string displayName = $"tex:{texId}";
                string colour = "#cccccc";

                AddSlot(grid, (uint)texId, displayName, colour, slotIndex: shown);
                shown++;
            }

            _countLabel.Text = $"Showing {shown} icons from texturelist.txt (catalogue: {total} items)";
            GD.Print($"[InventoryWindow] Populated {shown} icon slots from texturelist.txt. " +
                     $"ItemCatalogue total={total}. " +
                     "Note: tex_id→item_id join unpinned (spec: ui_manifests.md §9 item #12).");
            return;
        }

        // Fallback: no icons available (VFS offline).
        _countLabel.Text = "VFS offline — no item icons available.";
        AddSlot(grid, 0, "(VFS offline)", "#888888", slotIndex: 0);
        GD.Print("[InventoryWindow] No item icons — VFS offline.");
    }

    /// <param name="slotIndex">
    /// Zero-based display slot index. Used to pick the i-th demo icon from
    /// <see cref="_demoIcons"/> (texturelist.txt file order). The per-item tex_id column
    /// in items.csv is not yet confirmed in the spec (§9 item #12), so we use the
    /// texturelist.txt enumeration order as the demo icon mapping rather than a tex_id lookup.
    /// spec: Docs/RE/formats/ui_manifests.md §9 item #12 — inventory cell layout UNPINNED.
    /// spec: Docs/RE/formats/ui_manifests.md §10.5 — "whole-texture blit": CODE-CONFIRMED.
    /// </param>
    private void AddSlot(GridContainer grid, uint itemId, string displayName, string colour, int slotIndex)
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
                    cellTex = new AtlasTexture
                    {
                        Atlas = atlasImg, Region = new Rect2(CellBgSrcX, CellBgSrcY, CellBgW, CellBgH),
                        FilterClip = true
                    }; // PLAUSIBLE
            }

            if (cellTex is null && _uiLoader is not null)
                cellTex = _uiLoader.Slice(InvTexPath, CellBgSrcX, CellBgSrcY, CellBgW, CellBgH); // PLAUSIBLE

            if (cellTex is not null) bg.Texture = cellTex;
            slotPanel.AddChild(bg);
        }

        var inner = new VBoxContainer();
        slotPanel.AddChild(inner);

        // ---- Item icon from texturelist.txt (whole-texture blit, no sub-rect) ----
        // Demo mapping: slot i → i-th entry of texturelist.txt (file order).
        // The per-item tex_id column in items.csv is spec-open (§9 item #12), so we use
        // the manifest enumeration order as the demo approximation.
        // spec: Docs/RE/formats/ui_manifests.md §10.5 — "whole-texture blit": CODE-CONFIRMED.
        ImageTexture? icon = null;
        int iconTexId = -1;
        if (_demoIcons is not null && slotIndex < _demoIcons.Count)
        {
            (iconTexId, icon) = _demoIcons[slotIndex];
        }

        if (icon is not null)
        {
            var iconRect = new TextureRect
            {
                Texture = icon,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(IconDisplaySize, IconDisplaySize),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            inner.AddChild(iconRect);
        }

        // ---- Text labels ----
        var idLabel = new Label
        {
            Text = icon is not null
                ? $"#{itemId} tex={iconTexId}"
                : $"#{itemId}",
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