// Screens/CharacterSelectScreen.cs
//
// The legacy CHARACTER-SELECT screen (master scene state 4), rebuilt as a Godot Control.
// Layout coordinates are INTEROP FACTS recovered in InitFromCharListAndBuildUI; each cites spec.
//
// OFFLINE STUB: in the real client this screen is built when the SmsgCharacterList (opcode 3/1)
// packet arrives (spec §6.4). There is NO network here. We render a single LOCAL demo slot
// ('Musa', the starter class) so the roster, Create/Delete, and Enter-Game gestures are visible.
// Create/Delete are presented for layout fidelity but inert (they would emit use-case calls in a
// networked build; there is no Application use case to call offline, and the task forbids adding
// game logic). Enter-Game advances the BootFlow node to the existing world boot.
//
// PASSIVE: this is a view. Reads UI atlas chrome + msg.xdb captions; turns Enter-Game into a C#
// signal the flow node consumes. No domain state, no packet parsing, no character validation.
//
// spec: Docs/RE/specs/ui_system.md §2.2 (select layout table), §2.3 (shared modal chrome),
//       §3.2 (select asset manifest), §6.2/§6.4 (state 4; built from char-list packet), §8.
// spec: Docs/RE/formats/config_tables.md §2.6 (class IDs: 1=Musa 무사).

using Godot;
using MartialHeroes.Client.Godot.Screens.Layout;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// Character-select Control on the 1024×768 reference canvas (spec §2.0), scaled by the parent
/// <see cref="ScreenHost"/>.
///
/// Layout (spec §2.2):
///   - title bar @ (0,0) 577×58 (mainwindow.dds)
///   - left char-list panel @ (0,0) 244×474 hosting per-slot rows (24px stride, spec §2.2)
///   - char-info panel with Lv/HP/class stat rows @ x=60 y={37,61,85}
///   - Create @ (42,325) 59×20 (action 413), Delete @ (112,325) 59×20 (action 531)
///   - GUCanvas3D live preview viewports: SKIPPED (spec §7 open item 3 — slot positions
///     unrecovered; bonus per task brief).
/// </summary>
public sealed partial class CharacterSelectScreen : Control
{
    // ---------------------------------------------------------------------
    // Outgoing intent — consumed by the BootFlow node (no game logic here).
    // ---------------------------------------------------------------------

    /// <summary>Raised when the player enters the game with the selected slot. Carries char name.</summary>
    [Signal]
    public delegate void EnterGameRequestedEventHandler(string characterName);

    /// <summary>Raised when the player goes back to the login screen.</summary>
    [Signal]
    public delegate void BackRequestedEventHandler();

    // ---------------------------------------------------------------------
    // View state
    // ---------------------------------------------------------------------

    // OFFLINE STUB demo roster. In a networked build these would come from the CharacterListEvent
    // payload (Application), never hardcoded. The single 'Musa' slot mirrors the project's existing
    // demo character so Enter-Game lands in the same world the synthetic/real renderer already shows.
    // spec: Docs/RE/formats/config_tables.md §2.6 — class 1 = Musa (무사). CONFIRMED.
    private static readonly DemoSlot[] DemoRoster =
    [
        new DemoSlot(Name: "Musa", ClassName: "무사", Level: 1, Hp: 100),
    ];

    private int _selectedSlot;
    private UiAssetLoader _assets = null!;
    private bool _ownsAssets;
    private Label _infoLv = null!;
    private Label _infoHp = null!;
    private Label _infoClass = null!;

    /// <summary>Optional shared asset loader injected by the flow node (one VFS for both screens).</summary>
    public UiAssetLoader? SharedAssets { get; set; }

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
            GD.PrintErr($"[Screens] CharacterSelectScreen build failed: {ex.Message}");
        }
    }

    public override void _ExitTree()
    {
        if (_ownsAssets) _assets?.Dispose();
    }

    // ---------------------------------------------------------------------
    // Construction
    // ---------------------------------------------------------------------

    private void BuildUi()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        int widgetCount = 0;

        // --- Backdrop: dim full-canvas blacksheet so the windows read as floating panels.
        //     spec §3.2 — data/ui/blacksheet.dds (dim/blackout overlay). ---
        var dim = new ColorRect { Color = new Color(0.06f, 0.06f, 0.08f) };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        Texture2D? black = _assets.LoadAtlas(CharacterSelectLayout.AtlasBlacksheet);
        if (black is not null)
        {
            var blackRect = new TextureRect
            {
                Texture = black,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Modulate = new Color(1, 1, 1, 0.7f),
            };
            blackRect.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(blackRect);
            widgetCount++;
        }

        widgetCount++;

        // --- Title bar @ (0,0) 577×58, mainwindow.dds. spec §2.2.
        //     English placeholder title (per-widget caption id not recovered — spec §7 item 4). ---
        var titleBar = MakeChrome(CharacterSelectLayout.TitleBar, CharacterSelectLayout.AtlasMainWindow);
        AddChild(titleBar);
        var titleLabel = MakeLabel("CHARACTER SELECT",
            CharacterSelectLayout.FontTitleHeight, new Color(0.95f, 0.86f, 0.55f));
        titleLabel.Position = new Vector2(16, 16);
        titleBar.AddChild(titleLabel);
        widgetCount += 2;

        // --- Left character-list panel @ (0,0) 244×474, offset below the title bar. spec §2.2. ---
        var listPanel = MakeChrome(CharacterSelectLayout.CharListPanel, CharacterSelectLayout.AtlasMainWindow);
        listPanel.Position = new Vector2(8, 64);
        AddChild(listPanel);
        widgetCount++;

        // Per-slot rows (24px stride). spec §2.2 — loop step 24px CODE-CONFIRMED; base Y PARTIAL.
        widgetCount += BuildSlotRows(listPanel);

        // Create / Delete buttons (panel-local). spec §2.2 actions 413 / 531. English placeholders.
        var createBtn = new Button { Text = "Create" };
        Place(createBtn, CharacterSelectLayout.CreateButton);
        createBtn.Disabled = true; // OFFLINE STUB — no create use case in presentation-only flow.
        createBtn.TooltipText = "Offline build — create is a networked use case (spec §6.4 char-list packet).";
        listPanel.AddChild(createBtn);
        var deleteBtn = new Button { Text = "Delete" };
        Place(deleteBtn, CharacterSelectLayout.DeleteButton);
        deleteBtn.Disabled = true; // OFFLINE STUB.
        deleteBtn.TooltipText = "Offline build — delete is a networked use case.";
        listPanel.AddChild(deleteBtn);
        widgetCount += 2;

        // --- Right-side character-info panel (Lv/HP/class). spec §2.2 char-info panel. ---
        var infoPanel = MakeChrome(CharacterSelectLayout.CharInfoPanel, CharacterSelectLayout.AtlasMainWindow);
        infoPanel.Position = new Vector2(264, 64);
        AddChild(infoPanel);
        widgetCount++;

        // Portrait box @ (0,12) 200×46. spec §2.2.
        var portrait = new ColorRect { Color = new Color(0.18f, 0.16f, 0.20f) };
        Place(portrait, CharacterSelectLayout.PortraitBox);
        infoPanel.AddChild(portrait);
        widgetCount++;

        // Stat rows — Lv / HP / class @ x=60 y={37,61,85}. spec §2.2.
        _infoLv = MakeStatRow(infoPanel, "Lv", CharacterSelectLayout.StatLabelLv);
        _infoHp = MakeStatRow(infoPanel, "HP", CharacterSelectLayout.StatLabelHp);
        _infoClass = MakeStatRow(infoPanel, "Class", CharacterSelectLayout.StatLabelClass);
        widgetCount += 3;

        // --- Enter Game button. Not a recovered atlas rect (the legacy "enter" is part of the
        //     per-slot action buttons, spec §2.2 — per-slot action button positions PARTIAL).
        //     We place a clear Enter-Game CTA below the info panel for the offline flow. ---
        var enterBtn = new Button { Text = "Enter Game" };
        enterBtn.Position = new Vector2(264, 264);
        enterBtn.Size = new Vector2(244, 44);
        enterBtn.AddThemeFontSizeOverride("font_size", 18);
        enterBtn.Pressed += OnEnterGamePressed;
        AddChild(enterBtn);
        widgetCount++;

        // Back-to-login button (top-right). spec §2.2 "Back tab button" @ (393,17).
        var backBtn = new Button { Text = "Back" };
        backBtn.Position = new Vector2(800, 18);
        backBtn.Size = new Vector2(90, 30);
        backBtn.Pressed += () => EmitSignal(SignalName.BackRequested);
        AddChild(backBtn);
        widgetCount++;

        RefreshInfo();

        GD.Print($"[Screens] CharacterSelectScreen built ({widgetCount} widgets; roster={DemoRoster.Length}; " +
                 $"vfs={(_assets.HasVfs ? "real-atlas" : "offline-fallback")}).");
    }

    /// <summary>Builds the per-slot list rows; returns the widget count added.</summary>
    private int BuildSlotRows(Control panel)
    {
        int count = 0;
        for (int i = 0; i < DemoRoster.Length; i++)
        {
            DemoSlot slot = DemoRoster[i];
            int y = CharacterSelectLayout.SlotRowBaseY + i * CharacterSelectLayout.SlotRowStride;

            // Stat icon placeholder @ x=12 (src 771, 34×18). spec §2.2.
            var icon = new ColorRect { Color = new Color(0.4f, 0.35f, 0.2f) };
            icon.Position = new Vector2(CharacterSelectLayout.SlotIconX, y);
            icon.Size = new Vector2(CharacterSelectLayout.SlotIconW, CharacterSelectLayout.SlotIconH);
            panel.AddChild(icon);
            count++;

            // Slot name label @ x=46 (157×18). spec §2.2.
            var nameBtn = new Button { Text = $"{slot.Name}  Lv{slot.Level} ({slot.ClassName})" };
            nameBtn.Position = new Vector2(CharacterSelectLayout.SlotLabelX, y);
            nameBtn.Size = new Vector2(CharacterSelectLayout.SlotLabelW, CharacterSelectLayout.SlotLabelH);
            nameBtn.AddThemeFontSizeOverride("font_size", 12);
            int slotIndex = i;
            nameBtn.Pressed += () =>
            {
                _selectedSlot = slotIndex;
                RefreshInfo();
            };
            panel.AddChild(nameBtn);
            count++;
        }

        return count;
    }

    // ---------------------------------------------------------------------
    // Intent handlers (NO game logic — emit a signal the flow node consumes).
    // ---------------------------------------------------------------------

    private void OnEnterGamePressed()
    {
        // OFFLINE STUB: there is no enter-game packet (spec §1-9 enter_game_request) sent here.
        // We hand the selected demo character's name to the flow node, which loads the existing
        // world scene — preserving today's world-boot behaviour exactly.
        // spec: Docs/RE/specs/ui_system.md §6.2 — character select (state 4) → in-game (state 5).
        string name = DemoRoster[_selectedSlot].Name;
        GD.Print($"[Screens] CharacterSelectScreen: Enter Game (offline stub) — character='{name}'.");
        EmitSignal(SignalName.EnterGameRequested, name);
    }

    private void RefreshInfo()
    {
        DemoSlot slot = DemoRoster[_selectedSlot];
        _infoLv.Text = $"Lv  {slot.Level}";
        _infoHp.Text = $"HP  {slot.Hp}";
        _infoClass.Text = $"Class  {slot.ClassName}";
    }

    // ---------------------------------------------------------------------
    // Widget factories
    // ---------------------------------------------------------------------

    /// <summary>
    /// Builds a chrome panel at the recovered rect. Uses the atlas sprite when available, else a
    /// themed PanelContainer at the same rect. spec §8.3 — atlas sub-rect mapping.
    /// </summary>
    /// <summary>
    /// Builds a chrome panel at the recovered rect. Uses the atlas sprite when available, else a
    /// neutral solid fallback. spec §8.3 — atlas sub-rect mapping.
    ///
    /// Atlas bleed fix: all AtlasTexture instances must have FilterClip=true so adjacent atlas
    /// content doesn't leak past the sub-rect boundary at non-integer scale factors.
    /// Where the exact source sub-rect for window chrome is unknown (spec §7 open items 2/3 —
    /// window chrome positions PARTIAL), we emit the solid/9-patch fallback instead of a
    /// bleeding full-atlas slice.
    /// TODO: replace the offline fallback Panel with a proper 9-patch chrome once the exact chrome
    ///       sub-rects for mainwindow.dds are recovered. Cite: spec §7 open items 2/3.
    /// </summary>
    private Control MakeChrome(WidgetRect rect, string atlasPath)
    {
        // Only slice the atlas when we have a confirmed source origin (srcX!=0 || srcY!=0).
        // When both are zero the src-rect is unrecovered (PARTIAL); use the solid fallback to
        // avoid a bleeding full-atlas slice — spec §7 open item 2/3 notes chrome positions PARTIAL.
        AtlasTexture? face = null;
        if (rect.SrcX != 0 || rect.SrcY != 0)
        {
            // Slice() always sets FilterClip=true — fixes atlas bleed. spec §8.3.
            face = _assets.Slice(atlasPath, rect.SrcX, rect.SrcY, rect.W, rect.H);
        }
        // When srcX/srcY == (0,0): PARTIAL chrome position; prefer solid fallback over a
        // bleeding full-atlas slice. TODO: populate SrcX/SrcY when the exact rect is recovered.

        if (face is not null)
        {
            var tr = new TextureRect
            {
                Texture = face,
                StretchMode = TextureRect.StretchModeEnum.Scale,
            };
            Place(tr, rect);
            return tr;
        }

        // Offline / PARTIAL-src fallback: neutral dark panel with a border so the window frame
        // reads even when atlas chrome is unavailable or unrecovered (spec §7 open items 2/3).
        var panel = new Panel();
        Place(panel, rect);
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.11f, 0.14f, 0.96f),
            BorderColor = new Color(0.45f, 0.38f, 0.25f),
        };
        style.SetBorderWidthAll(2);
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    private Label MakeStatRow(Control parent, string defaultText, WidgetRect rect)
    {
        var label = MakeLabel(defaultText, CharacterSelectLayout.FontRowHeight,
            new Color(0.85f, 0.85f, 0.9f));
        Place(label, rect, sizeFromRect: false);
        label.Size = new Vector2(rect.W + 40, rect.H + 6);
        parent.AddChild(label);
        return label;
    }

    private static Label MakeLabel(string text, int fontHeight, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", Mathf.Max(11, fontHeight));
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static void Place(Control c, WidgetRect rect, bool sizeFromRect = true)
    {
        c.Position = new Vector2(rect.X, rect.Y);
        if (sizeFromRect)
            c.Size = new Vector2(rect.W, rect.H);
        c.CustomMinimumSize = new Vector2(rect.W, rect.H);
    }

    /// <summary>An offline demo roster slot — view-only; never domain state.</summary>
    private readonly record struct DemoSlot(string Name, string ClassName, int Level, int Hp);
}