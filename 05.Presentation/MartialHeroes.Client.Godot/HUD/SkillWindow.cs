using Godot;
using MartialHeroes.Client.Godot.Adapters;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Screens;
using MartialHeroes.Client.Godot.Screens.Widgets;

namespace MartialHeroes.Client.Godot.HUD;

/// <summary>
/// Toggleable skill catalogue browser (key K).
///
/// PASSIVE: reads <see cref="ClientContext.SkillCatalogue"/> (populated by the Application layer
/// from skills.scr) and renders it as a scrollable list of skill entries. Zero game logic.
///
/// Original chrome layer (stage-2, texture-driven):
///   Window chrome: uitex 0008 → data/ui/skillwindow.dds (1024×1024 DXT3).
///   Chrome source rect: (0, 0, 340, 520) on skillwindow.dds — PLAUSIBLE (full window panel strip).
///   Close button: inventwindow.dds shared modal frames (CONFIRMED §8.3/§8.1).
///   Skill icon sheets: skillicon.txt 12 entries (512×512 DXT2/3, §2.4 SAMPLE-VERIFIED).
///   Per-slot icon UV: within-sheet grid UNKNOWN (open item — §9 known unknown 1 in ui_manifests.md).
///   Icon grid stride assumed 48×48 per cell on a 512×512 sheet → 10 columns × 10 rows = 100 slots.
///   PLAUSIBLE — will be updated when the per-slot UV grid is recovered.
///
/// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive HUD.
/// spec: Docs/RE/specs/ui_system.md §8.5 — uitex integer binding; §8.1 close-button frames CONFIRMED.
/// spec: Docs/RE/formats/ui_manifests.md §1.4 — uitex 0008 = data/ui/skillwindow.dds.
/// spec: Docs/RE/formats/ui_manifests.md §2.4 — 12 skillicon.txt entries SAMPLE-VERIFIED.
/// spec: Docs/RE/formats/config_tables.md §2.8 skills.scr — stride 1504 bytes, ~2000 real records.
/// spec: Docs/RE/structs/skill.md Part A.2 — field layout used for display values.
/// </summary>
public sealed partial class SkillWindow : Control
{
    // -------------------------------------------------------------------------
    // Atlas binding constants
    // spec: Docs/RE/formats/ui_manifests.md §1.4 — uitex 0008 = data/ui/skillwindow.dds 1024×1024 DXT3.
    // -------------------------------------------------------------------------

    private const int SkillWinTexId = 8;
    private const string SkillWinPath = "data/ui/skillwindow.dds";

    // Window chrome source rect on skillwindow.dds.
    // Full layout of skillwindow.dds is unrecovered — PLAUSIBLE strip from top-left.
    // spec: Docs/RE/specs/ui_system.md §12 open item 6 — in-game window layouts gated on manifest.
    private const int ChromeSrcX = 0; // PLAUSIBLE
    private const int ChromeSrcY = 0; // PLAUSIBLE
    private const int ChromeW = 340; // PLAUSIBLE
    private const int ChromeH = 520; // PLAUSIBLE

    // Close button on inventwindow.dds (shared modal chrome).
    // spec: Docs/RE/specs/ui_system.md §8.1 "Quit-confirm Yes #1" button — CODE-CONFIRMED.
    private const int CloseTexId = 2;
    private const string CloseTexPath = "data/ui/inventwindow.dds";
    private const int CloseBtnNormX = 302;
    private const int CloseBtnNormY = 900;
    private const int CloseBtnHoverX = 415;
    private const int CloseBtnHoverY = 900;
    private const int CloseBtnPressX = 302; // PRESSED = NORMAL, spec §1.5
    private const int CloseBtnPressY = 900;
    private const int CloseBtnW = 113;
    private const int CloseBtnH = 40;

    // Skill icon cell dimensions: 23×23 px — CODE-CONFIRMED from three draw sites.
    // spec: Docs/RE/formats/ui_manifests.md §2.6 — "fixed 23×23 pixel cell": CODE-CONFIRMED.
    // The real (iconSrcX, iconSrcY) is authored data from the .do stance file, resolved via IconCatalogs.
    private const int IconCellW = IconCatalogs.IconCellW; // 23 — CODE-CONFIRMED
    private const int IconCellH = IconCatalogs.IconCellH; // 23 — CODE-CONFIRMED

    // msg.xdb id for close button caption.
    // spec: Docs/RE/specs/ui_system.md §10 — id 102 in button label range 101–107.
    private const uint MsgIdClose = 102;

    // -------------------------------------------------------------------------
    // Tunables
    // -------------------------------------------------------------------------

    // Number of .do stance slots to show in the demo list (slot 0 … DemoSlotCount-1).
    // spec: Docs/RE/formats/ui_manifests.md §2.7 — musajung.do has 301 non-zero records: SAMPLE-VERIFIED.
    private const int DemoSlotCount = 80;

    // Kept for the SkillCatalogue fallback path (when IconCatalogs is offline).
    private const int DemoSkillCount = 80;
    private const uint IdScanCeiling = 5_000;
    // spec: Docs/RE/formats/config_tables.md §2.8 — valid skill_id < 10,000,000: CONFIRMED.

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
    // Context + asset loader
    // -------------------------------------------------------------------------

    private ClientContext? _context;
    private UiAssetLoader? _uiLoader;

    // Short-hand reference to the skill icon catalog (non-owning; owned by ClientContext).
    private IconCatalogs? _iconCatalogs;

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        try
        {
            _context = GetNode<ClientContext>("/root/ClientContext");
            // Pull the IconCatalogs reference (non-owning).
            _iconCatalogs = _context.IconCatalogs;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SkillWindow] Could not resolve ClientContext: {ex.Message}. " +
                        "Catalogue will be empty (offline mode).");
        }

        try
        {
            _uiLoader = UiAssetLoader.Open();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SkillWindow] UiAssetLoader.Open failed: {ex.Message} — chrome offline.");
        }

        try
        {
            BuildUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SkillWindow] _Ready failed: {ex.Message}");
        }

        Visible = false;
        GD.Print("[SkillWindow] Ready. Chrome wired to uitex 0008 (skillwindow.dds). " +
                 "Skill icons use real 23×23 cells from musajung.do+skillicon.txt (IconCatalogs) " +
                 "when VFS is available. spec: Docs/RE/formats/ui_manifests.md §2.6 CODE-CONFIRMED.");
    }

    public override void _ExitTree()
    {
        _uiLoader?.Dispose();
        _uiLoader = null;
    }

    public override void _Input(InputEvent ev)
    {
        // Toggle on key K press (not held).
        // spec: Docs/RE/specs/input_ui.md §4 — skill window key toggle.
        if (ev is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.K)
        {
            Visible = !Visible;
            if (Visible)
            {
                MoveToFront();
                PopulateList();
            }

            GetViewport().SetInputAsHandled();
        }

        // Drag.
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
        // Anchor to the right of the InventoryWindow.
        AnchorLeft = 0f;
        AnchorTop = 0.5f;
        AnchorRight = 0f;
        AnchorBottom = 0.5f;
        OffsetLeft = 670f;
        OffsetTop = -260f;
        OffsetRight = 1010f;
        OffsetBottom = 260f;

        var outerPanel = new PanelContainer();
        outerPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        outerPanel.CustomMinimumSize = new Vector2(340, 520);
        AddChild(outerPanel);

        // ---- Window chrome TextureRect (skillwindow.dds) ----
        // spec: Docs/RE/formats/ui_manifests.md §1.4 — uitex 0008 = data/ui/skillwindow.dds.
        _windowChrome = new TextureRect
        {
            Name = "SkillWindowChrome",
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

        // OPEN ITEM: skill window title msg.xdb id is unrecovered.
        // Using plain fallback text.
        var titleLabel = new Label
        {
            Text = "Skills (Catalogue Browser)",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        titleRow.AddChild(titleLabel);

        // Close button backed by inventwindow.dds (shared modal chrome).
        // spec: Docs/RE/specs/ui_system.md §8.1 CODE-CONFIRMED frames.
        var closeBtn = BuildCloseButton();
        titleRow.AddChild(closeBtn);

        // ---- Count line ----
        _countLabel = new Label { Text = "Loading…" };
        vbox.AddChild(_countLabel);

        // ---- Scrollable list ----
        var scroll = new ScrollContainer();
        scroll.CustomMinimumSize = new Vector2(330, 440);
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        vbox.AddChild(scroll);

        var list = new VBoxContainer { Name = "SkillList" };
        scroll.AddChild(list);
    }

    // -------------------------------------------------------------------------
    // Chrome + button helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Binds the window chrome TextureRect to the skill-window strip on skillwindow.dds.
    /// spec: Docs/RE/formats/ui_manifests.md §1.4 — uitex 0008 = data/ui/skillwindow.dds.
    /// Source rect: (0,0,340,520) — PLAUSIBLE.
    /// </summary>
    private void BindWindowChrome()
    {
        if (_windowChrome is null) return;

        // Primary: UiCatalogs uitex id 8.
        if (_context?.UiCatalogs is { } cats)
        {
            ImageTexture? tex = cats.GetTexture(SkillWinTexId);
            if (tex is not null)
            {
                _windowChrome.Texture = new AtlasTexture
                {
                    Atlas = tex,
                    Region = new Rect2(ChromeSrcX, ChromeSrcY, ChromeW, ChromeH), // PLAUSIBLE
                    FilterClip = true,
                };
                GD.Print($"[SkillWindow] Chrome bound via UiCatalogs uitex {SkillWinTexId} " +
                         $"({SkillWinPath}). Rect2({ChromeSrcX},{ChromeSrcY},{ChromeW},{ChromeH}) // PLAUSIBLE");
                return;
            }
        }

        // Fallback: UiAssetLoader direct path.
        if (_uiLoader is not null)
        {
            AtlasTexture? at = _uiLoader.Slice(SkillWinPath, ChromeSrcX, ChromeSrcY, ChromeW, ChromeH);
            if (at is not null)
            {
                _windowChrome.Texture = at;
                GD.Print($"[SkillWindow] Chrome bound via UiAssetLoader ({SkillWinPath}). // PLAUSIBLE");
                return;
            }
        }

        GD.Print("[SkillWindow] skillwindow.dds unavailable — chrome invisible (VFS offline).");
    }

    /// <summary>
    /// Builds the close StateButton using the shared inventwindow.dds modal frames.
    /// spec: Docs/RE/specs/ui_system.md §8.1 — "Quit-confirm Yes #1" button: CODE-CONFIRMED.
    /// </summary>
    private Control BuildCloseButton()
    {
        // Get caption from msg.xdb id 102 (close label range).
        // spec: Docs/RE/specs/ui_system.md §10 — id 102 in 101–107 range.
        string caption = GetMsg(MsgIdClose, "X");

        if (_uiLoader is not null)
        {
            AtlasTexture? normFrame = _uiLoader.Slice(CloseTexPath, CloseBtnNormX, CloseBtnNormY, CloseBtnW, CloseBtnH);
            AtlasTexture? hoverFrame =
                _uiLoader.Slice(CloseTexPath, CloseBtnHoverX, CloseBtnHoverY, CloseBtnW, CloseBtnH);
            AtlasTexture? pressedFrame =
                _uiLoader.Slice(CloseTexPath, CloseBtnPressX, CloseBtnPressY, CloseBtnW, CloseBtnH);

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

        var fallback = new Button { Text = caption };
        fallback.Pressed += () => { Visible = false; };
        return fallback;
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

        // ── Primary path: IconCatalogs (VFS online, real icons from .do + skillicon.txt) ──
        // spec: Docs/RE/formats/ui_manifests.md §2.6 — "data-driven UV per skill": CODE-CONFIRMED.
        // spec: Docs/RE/formats/ui_manifests.md §2.7 — "musajung.do Map B (slotIndex)": CODE-CONFIRMED.
        if (_iconCatalogs is not null)
        {
            IReadOnlyList<(uint SlotIndex, AtlasTexture? Icon)> slots =
                _iconCatalogs.GetFirstSlots(DemoSlotCount);

            if (slots.Count > 0)
            {
                foreach ((uint slotIndex, AtlasTexture? icon) in slots)
                {
                    // We show the slotIndex as the "skill ID" since the instanceKey → skills.scr
                    // join is UNVERIFIED (spec: Docs/RE/formats/ui_manifests.md §9 item #11a).
                    AddRow(list, slotIndex, icon, "Slot", "—", "—", "Musa");
                }

                _countLabel.Text =
                    $"Showing {slots.Count} Musa-jung slots from musajung.do " +
                    $"(spec: Docs/RE/formats/ui_manifests.md §2.7 CODE-CONFIRMED + SAMPLE-VERIFIED). " +
                    $"Icons: 23×23 px — spec §2.6 CODE-CONFIRMED.";
                GD.Print($"[SkillWindow] Populated {slots.Count} real skill icon rows from IconCatalogs " +
                         "(musajung.do + skillicon.txt). " +
                         "spec: Docs/RE/formats/ui_manifests.md §2.6 23×23 cell CODE-CONFIRMED.");
                return;
            }
        }

        // ── Fallback: SkillCatalogue (VFS offline — no real icons) ──
        var catalogue = _context?.SkillCatalogue;
        if (catalogue is null || catalogue.Count == 0)
        {
            _countLabel.Text = "No data available (offline mode — VFS not found).";
            AddRow(list, 0, null, "Cat:—", "CD:—", "R:—", "—");
            return;
        }

        int shown = 0;
        for (uint id = 1; id <= IdScanCeiling && shown < DemoSkillCount; id++)
        {
            var def = catalogue.TryGet(id);
            if (def is null) continue;

            string catText = $"Cat:{def.Value.Category}";
            string cdText = def.Value.CooldownCentiseconds == 0 ? "CD:—" : $"CD:{def.Value.CooldownMs}ms";
            string rangeText = $"R:{def.Value.BaseRange:F1}";
            string targetText = TargetMnemonic(def.Value.TargetMode);

            // Offline: no real icon available — pass null (placeholder blank square).
            AddRow(list, def.Value.Id.Value, null, catText, cdText, rangeText, targetText);
            shown++;
        }

        _countLabel.Text =
            $"Showing {shown} skills from SkillCatalogue (offline — no icon data). " +
            $"IDs 1–{IdScanCeiling} scanned.";
        GD.Print($"[SkillWindow] Populated {shown} skill rows from SkillCatalogue (offline fallback, " +
                 "IconCatalogs unavailable).");
    }

    // -------------------------------------------------------------------------
    // Skill icon building (now delegated to IconCatalogs — see PopulateList)
    // -------------------------------------------------------------------------
    // The old PLAUSIBLE 48×48 grid is replaced by the real 23×23 data-driven lookup via
    // IconCatalogs.GetFirstSlots(). The helper methods were removed because PopulateList
    // now calls IconCatalogs directly; no per-slotIndex BuildSkillIcon call is needed here.
    //
    // spec: Docs/RE/formats/ui_manifests.md §2.6 — "fixed 23×23 pixel cell, data-driven UV":
    //       CODE-CONFIRMED. The old §9 known-unknown-1 "UV offset UNKNOWN" is now resolved.

    // -------------------------------------------------------------------------
    // Row builder
    // -------------------------------------------------------------------------

    private static void AddRow(
        VBoxContainer list,
        uint skillId,
        AtlasTexture? icon,
        string catText,
        string cdText,
        string rangeText,
        string targetText)
    {
        // Row height: 28px to comfortably contain a 23px icon + padding.
        // spec: Docs/RE/formats/ui_manifests.md §2.6 — "23×23 pixel cell": CODE-CONFIRMED.
        var rowPanel = new PanelContainer();
        rowPanel.CustomMinimumSize = new Vector2(0, 28);
        list.AddChild(rowPanel);

        var hbox = new HBoxContainer();
        rowPanel.AddChild(hbox);

        // Skill icon: 23×23 real cell (CODE-CONFIRMED) or blank placeholder (offline).
        // spec: Docs/RE/formats/ui_manifests.md §2.6 — "fixed 23×23 pixel": CODE-CONFIRMED.
        var iconRect = new TextureRect
        {
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(IconCellW, IconCellH),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        if (icon is not null) iconRect.Texture = icon;
        hbox.AddChild(iconRect);

        // ID label.
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

    private string GetMsg(uint id, string fallback)
    {
        if (_context?.UiCatalogs is { } cats)
            return cats.GetMessage((int)id, fallback);
        return fallback;
    }

    /// <summary>
    /// Short mnemonic for the TargetMode enum — keeps the row compact.
    /// spec: Docs/RE/structs/skill.md §A.5 TargetShapeMode — values 0..11: CONFIRMED.
    /// </summary>
    private static string TargetMnemonic(MartialHeroes.Client.Domain.Skills.SkillTargetMode mode) =>
        mode switch
        {
            MartialHeroes.Client.Domain.Skills.SkillTargetMode.SingleSelfOrPrimary => "Self",
            MartialHeroes.Client.Domain.Skills.SkillTargetMode.SingleTarget => "Single",
            MartialHeroes.Client.Domain.Skills.SkillTargetMode.SingleEnemyOrHeal => "S.Enem",
            MartialHeroes.Client.Domain.Skills.SkillTargetMode.ChainNearbyAoe => "Chain",
            MartialHeroes.Client.Domain.Skills.SkillTargetMode.ConeForwardAoe => "Cone",
            MartialHeroes.Client.Domain.Skills.SkillTargetMode.GroundPoint => "Ground",
            MartialHeroes.Client.Domain.Skills.SkillTargetMode.PartyAoe => "Party",
            MartialHeroes.Client.Domain.Skills.SkillTargetMode.FactionGatedSingle => "Faction",
            MartialHeroes.Client.Domain.Skills.SkillTargetMode.PkGatedSingle => "PK",
            MartialHeroes.Client.Domain.Skills.SkillTargetMode.RadialAoeBothFactions => "RadAoE",
            MartialHeroes.Client.Domain.Skills.SkillTargetMode.SelfOnly => "SelfO",
            _ => "?",
        };
}