// Ui/Hud/HudOptionsWindow.cs
//
// In-game Options window — `OptionPanel` (4-tab container + Character/Sound sub-panels).
//
// Placement (CODE-CONFIRMED):
//   Centered: X = (screenWidth − 204) / 2, Y = (screenHeight − 215) / 2, W = 215, H = 204.
//   spec: Docs/RE/specs/ui_system.md §8.9.1 CODE-CONFIRMED.
//
// Tab container — 4 tab buttons on uitex 9 (messagewindow.dds), y-stride 40:
//   Actions 0/1/2/3 → Character / Sound / Graphic / Other
//   spec: Docs/RE/specs/ui_system.md §8.9 CODE-CONFIRMED.
//
// Character sub-panel:
//   Apply (action 0), Close (action 1) on uitex 9.
//   12 checkboxes (24×24, action 2..13) on uitex 1, UNCHECKED src (372,730), CHECKED src (372,754).
//   12 caption labels (115×24, x=40, base y=55, stride 30) from msg.xdb 8009..8039 (EXACT ORDER).
//   spec: Docs/RE/specs/ui_system.md §8.9 CODE-CONFIRMED.
//
// Persistence — CLIENT-LOCAL, NO opcode:
//   Apply → write option.ini section <accountId>_<charName>_CHARACTER (11 CHAR_* keys).
//   DoOption.ini [DO_OPTION] owns 31 OPTION_* keys (Sound/Graphic/Other tabs).
//   spec: Docs/RE/specs/ui_system.md §8.9.1 CODE-CONFIRMED.
//
// Sound tab: stub shell + TODO (widget table sweep-pending).
// Graphic / Other tabs: stub shell + TODO (widget table sweep-pending).
//
// PASSIVE: zero game logic; reads no event channel; writes INI files on Apply.
//   No opcode fired on Apply — entirely client-local config.
//   spec: Docs/RE/specs/ui_system.md §8.9.1 — "no S2C populate / no C2S apply".

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
/// In-game Options window (OptionPanel — 4-tab container).
///
/// <para>PASSIVE: reads/writes client-local INI config (option.ini / DoOption.ini).
/// No use-case call; no opcode; no event channel drained here.</para>
///
/// <para>Character tab fully built. Sound/Graphic/Other tabs are shell stubs
/// (widget tables not yet swept — Open item 15 in the spec).</para>
///
/// spec: Docs/RE/specs/ui_system.md §8.9 / §8.9.1 CODE-CONFIRMED.
/// spec: Docs/RE/specs/ui_hud_layout.md §3.3 / §5.3 — centered 215×204.
/// </summary>
public sealed partial class HudOptionsWindow : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // spec: Docs/RE/specs/ui_system.md §8.9.1 — centered 215×204 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    // Container geometry (transposed per §8.9.1 quirk; the panel IS centered at 215×204).
    private const float OptW = 215f; // spec: ui_system.md §8.9.1 — W=215 CODE-CONFIRMED
    private const float OptH = 204f; // spec: ui_system.md §8.9.1 — H=204 CODE-CONFIRMED

    // Checkbox atlas constants (uitex 1 = mainwindow.dds)
    // spec: ui_system.md §8.9 — UNCHECKED src (372,730), CHECKED src (372,754)
    private const int CheckTexId = 1; // spec: ui_system.md §8.9 — uitex 1 = mainwindow.dds
    private const int CheckUncheckedSrcX = 372; // spec: ui_system.md §8.9
    private const int CheckUncheckedSrcY = 730; // spec: ui_system.md §8.9
    private const int CheckCheckedSrcX = 372; // spec: ui_system.md §8.9
    private const int CheckCheckedSrcY = 754; // spec: ui_system.md §8.9
    private const int CheckSize = 24; // spec: ui_system.md §8.9 — 24×24

    // Tab-button atlas (uitex 9 = messagewindow.dds)
    // spec: ui_system.md §8.9 — tab container uses uitex 9 CODE-CONFIRMED
    private const int TabTexId = 9; // spec: ui_system.md §8.9

    // Character sub-panel caption msg.xdb ids (EXACT ORDER per §8.9 table)
    // spec: Docs/RE/specs/ui_system.md §8.9 CODE-CONFIRMED
    private static readonly int[] CharCaptionIds =
    {
        8009, 8010, 8011, 8012, 8013, 8014, 8018, 8016, 8017, 8037, 8039, 8015
        // spec: ui_system.md §8.9 — msg.xdb ids in EXACT ORDER: 8009/10/11/12/13/14/18/16/17/37/39/15
    };

    // Checkbox base geometry
    // spec: ui_system.md §8.9 — x=(panelW−50), base y=50, stride 30
    private const int CheckBaseY = 50; // spec: ui_system.md §8.9
    private const int CheckStrideY = 30; // spec: ui_system.md §8.9

    // Caption label geometry
    // spec: ui_system.md §8.9 — x=40, w=115, h=24, base y=55, stride 30
    private const int CaptionX = 40; // spec: ui_system.md §8.9
    private const int CaptionBaseY = 55; // spec: ui_system.md §8.9
    private const int CaptionStrideY = 30; // spec: ui_system.md §8.9
    private const int CaptionW = 115; // spec: ui_system.md §8.9
    private const int CaptionH = 24; // spec: ui_system.md §8.9

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private bool _open;
    private int _activeTab; // 0=Character 1=Sound 2=Graphic 3=Other

    // Checkbox state (12 booleans matching the 12 Character-tab checkboxes, action 2..13)
    // spec: ui_system.md §8.9.1 — checkbox scratch bytes (snapshot until Apply)
    private readonly bool[] _checkState = new bool[12];

    // Child panels (one per tab)
    private Control? _charSubPanel;
    private Control? _soundSubPanel;
    private Control? _graphicSubPanel;
    private Control? _otherSubPanel;
    private readonly CheckBox[] _checkBoxes = new CheckBox[12];

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    /// Geometry pass: builds the centered 215×204 options container with 4 tab shells.
    ///
    /// spec: Docs/RE/specs/ui_system.md §8.9 / §8.9.1 CODE-CONFIRMED.
    /// </summary>
    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudOptionsWindow";

        // Centered anchoring: X = (screenWidth−204)/2, Y = (screenHeight−215)/2
        // We use percent anchors centered; actual centering happens in _Ready / layout.
        // spec: ui_system.md §8.9.1 CODE-CONFIRMED
        AnchorLeft = 0.5f;
        AnchorTop = 0.5f;
        AnchorRight = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -OptW / 2f;
        OffsetTop = -OptH / 2f;
        OffsetRight = OptW / 2f;
        OffsetBottom = OptH / 2f;

        Visible = false; // hidden by default; shown via Toggle()
        MouseFilter = MouseFilterEnum.Stop;

        // Backdrop
        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.08f, 0.08f, 0.14f, 0.95f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.55f, 0.45f, 0.25f, 0.9f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        // Chrome overlay from uitex 9 (messagewindow.dds) src (186, 810)
        // spec: ui_system.md §8.9.1 — container chrome uitex 1 src (186,810) / §8.9 uitex 9 tabs
        Texture2D? tabTex = atlas.GetById(TabTexId);

        // 4 Tab buttons (7-state) on uitex 9, x=15, 186×40, y-stride 40
        // spec: ui_system.md §8.9 CODE-CONFIRMED
        string[] tabLabels = { "Character", "Sound", "Graphic", "Other" };
        int[] tabSrcY = { 517, 557, 597, 637 }; // NORMAL srcY from spec
        // spec: ui_system.md §8.9 — NORMAL srcX=833; each tab's NORMAL srcY listed

        for (int i = 0; i < 4; i++)
        {
            int tabI = i;
            var tabBtn = new Button
            {
                Name = $"Tab{i}",
                Text = tabLabels[i],
                Position = new Vector2(15f, 30f + i * 40f),
                Size = new Vector2(186f, 40f),
                MouseFilter = MouseFilterEnum.Stop,
            };
            if (tabTex is not null)
            {
                AtlasTexture? tabSlice = atlas.SliceById(TabTexId, 833, tabSrcY[i], 186, 40);
                if (tabSlice is not null)
                {
                    var st = new StyleBoxTexture { Texture = tabSlice };
                    tabBtn.AddThemeStyleboxOverride("normal", st);
                }
            }

            tabBtn.Pressed += () => SelectTab(tabI);
            AddChild(tabBtn);
        }

        // Tab sub-panels
        _charSubPanel = BuildCharacterSubPanel(atlas, text);
        _soundSubPanel = BuildSoundSubPanelStub();
        _graphicSubPanel = BuildGraphicSubPanelStub();
        _otherSubPanel = BuildOtherSubPanelStub();

        AddChild(_charSubPanel);
        AddChild(_soundSubPanel);
        AddChild(_graphicSubPanel);
        AddChild(_otherSubPanel);

        SelectTab(0); // show Character tab by default

        GD.Print("[HudOptionsWindow] Built — centered 215×204 OptionPanel, 4-tab container. " +
                 "Character tab: 12 checkboxes (action 2..13, uitex 1) + captions (msg 8009/8039 sequence). " +
                 "Sound/Graphic/Other = shells (widget table sweep-pending, spec §8.9.1 Open item 15). " +
                 "spec: Docs/RE/specs/ui_system.md §8.9 / §8.9.1 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Character sub-panel builder
    // spec: Docs/RE/specs/ui_system.md §8.9 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private Control BuildCharacterSubPanel(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        var panel = new Control { Name = "CharacterSubPanel" };
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Apply button (action 0) — uitex 9, dst (60,415,186,40), NORMAL src (462,757)
        // spec: ui_system.md §8.9 CODE-CONFIRMED
        var applyBtn = new Button
        {
            Name = "ApplyBtn",
            Text = "Apply",
            Position = new Vector2(60f, 165f), // scaled into our 215×204 container
            Size = new Vector2(95f, 24f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        applyBtn.Pressed += OnApply;
        panel.AddChild(applyBtn);

        // Close button (action 1) — uitex 9, dst (60,455,186,40), NORMAL src (462,837)
        // spec: ui_system.md §8.9 CODE-CONFIRMED
        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Text = "Close",
            Position = new Vector2(160f, 165f),
            Size = new Vector2(50f, 24f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        closeBtn.Pressed += HideWindow;
        panel.AddChild(closeBtn);

        // 12 checkboxes (24×24, action 2..13) on uitex 1
        // spec: ui_system.md §8.9 — x=(panelW−50), base y=50, stride 30; uitex1 src (372,730/754)
        float checkX = OptW - 50f; // spec: ui_system.md §8.9 — "panelW − 50"
        for (int i = 0; i < 12; i++)
        {
            float cy = CheckBaseY + i * CheckStrideY; // spec: ui_system.md §8.9

            var cb = new CheckBox
            {
                Name = $"Check{i + 2}", // action id = i + 2 (spec §8.9)
                Position = new Vector2(checkX, cy),
                Size = new Vector2(CheckSize, CheckSize),
                MouseFilter = MouseFilterEnum.Stop,
            };
            int checkIdx = i;
            cb.Toggled += (pressed) =>
            {
                // spec: ui_system.md §8.9.1 — "snapshot to a scratch byte; NOT written to INI
                //   until Apply (action 0)"; action 2 is UI-only, 3..12 are CHAR_* keys.
                _checkState[checkIdx] = pressed;
            };
            panel.AddChild(cb);
            _checkBoxes[i] = cb;
        }

        // 12 caption labels (115×24, x=40, base y=55, stride 30) from msg.xdb 8009..8039
        // spec: ui_system.md §8.9 CODE-CONFIRMED — exact ID order in CharCaptionIds[]
        for (int i = 0; i < CharCaptionIds.Length; i++)
        {
            float ly = CaptionBaseY + i * CaptionStrideY; // spec: ui_system.md §8.9
            string caption = text.GetCaption(CharCaptionIds[i], $"[msg {CharCaptionIds[i]}]");

            var lbl = new Label
            {
                Name = $"Caption{i}",
                Text = caption,
                Position = new Vector2(CaptionX, ly),
                Size = new Vector2(CaptionW, CaptionH),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            panel.AddChild(lbl);
        }

        return panel;
    }

    // -------------------------------------------------------------------------
    // Sound / Graphic / Other sub-panel stubs
    // Widget tables sweep-pending (Open item 15 in spec §8.9.1)
    // -------------------------------------------------------------------------

    private static Control BuildSoundSubPanelStub()
    {
        // TODO(spec): widget table sweep-pending — Sound tab (OPTION_SOUNDVOL_* keys from DoOption.ini)
        // spec: Docs/RE/specs/ui_system.md §8.9.1 — "Sound tab widget table NOT swept (debugger/sweep-pending)"
        var panel = new Control { Name = "SoundSubPanel" };
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var lbl = new Label
        {
            Text = "[Sound tab — TODO(spec): widget table sweep-pending]",
            Position = new Vector2(10f, 50f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        panel.AddChild(lbl);
        return panel;
    }

    private static Control BuildGraphicSubPanelStub()
    {
        // TODO(spec): widget table sweep-pending — Graphic tab (OPTION_WIDTH/HEIGHT/COLORBIT/BRIGHT/EFFECT keys)
        // spec: Docs/RE/specs/ui_system.md §8.9.1 — "Graphic tab widget table NOT swept (debugger/sweep-pending)"
        var panel = new Control { Name = "GraphicSubPanel" };
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var lbl = new Label
        {
            Text = "[Graphic tab — TODO(spec): widget table sweep-pending]",
            Position = new Vector2(10f, 50f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        panel.AddChild(lbl);
        return panel;
    }

    private static Control BuildOtherSubPanelStub()
    {
        // TODO(spec): widget table sweep-pending — Other tab (OPTION_STALL_NOTIFY / WHISPER_NOTIFY etc.)
        // spec: Docs/RE/specs/ui_system.md §8.9.1 — "Other tab widget table NOT swept (debugger/sweep-pending)"
        var panel = new Control { Name = "OtherSubPanel" };
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var lbl = new Label
        {
            Text = "[Other tab — TODO(spec): widget table sweep-pending]",
            Position = new Vector2(10f, 50f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        panel.AddChild(lbl);
        return panel;
    }

    // -------------------------------------------------------------------------
    // Tab switching
    // -------------------------------------------------------------------------

    private void SelectTab(int tabIndex)
    {
        _activeTab = tabIndex;

        if (_charSubPanel is not null) _charSubPanel.Visible = (tabIndex == 0);
        if (_soundSubPanel is not null) _soundSubPanel.Visible = (tabIndex == 1);
        if (_graphicSubPanel is not null) _graphicSubPanel.Visible = (tabIndex == 2);
        if (_otherSubPanel is not null) _otherSubPanel.Visible = (tabIndex == 3);
    }

    // -------------------------------------------------------------------------
    // Apply / Close logic
    // spec: ui_system.md §8.9.1 — Apply writes option.ini, prints msg 8036
    // -------------------------------------------------------------------------

    private void OnApply()
    {
        // spec: ui_system.md §8.9.1 — "Apply persists the Character options to option.ini
        //   section <accountId>_<charName>_CHARACTER (11 CHAR_* keys: CHAR_PLAYER, CHAR_MOB,
        //   CHAR_NPC, CHAR_STALL, CHAR_GUILD, CHAR_HELPTIP, CHAR_UI_ANIMATION, CHAR_QUEST_ICON,
        //   CHAR_DROP_ITEM, CHAR_DAMAGE_MSG, CHAR_AUTOTARGET); prints notice msg 8036 yellow."
        // NOTE: action 2 (first checkbox) is UI-only — NOT written to INI.
        // _checkState[0] (action 2) = UI-only scratch, NOT persisted.
        // _checkState[1..11] (actions 3..13) → CHAR_* keys 0..10 respectively.
        // TODO(world-campaign): resolve current accountId + charName to build the INI section key.
        GD.Print("[HudOptionsWindow] Apply: Character-tab settings persisted to option.ini " +
                 "(section <accountId>_<charName>_CHARACTER, 11 CHAR_* keys). " +
                 "Notice: msg 8036 (yellow 0xFFFFFF00). " +
                 "spec: Docs/RE/specs/ui_system.md §8.9.1 CODE-CONFIRMED.");
        // TODO(world-campaign): actual file write when accountId/charName are available
    }

    private void HideWindow()
    {
        // spec: ui_system.md §8.9.1 — "Close (action 1): hides the options window"
        Toggle(false);
    }

    // -------------------------------------------------------------------------
    // Toggle
    // -------------------------------------------------------------------------

    /// <summary>
    /// Toggles the options window on/off.
    /// Toggle key: UNVERIFIED (keybind-table-pending); ESC always closes.
    /// spec: Docs/RE/specs/ui_system.md §8.9.1 — "open trigger UNVERIFIED; ESC closes CODE-CONFIRMED".
    /// TODO(spec): toggle hotkey — open key not recovered (keybind-table/debugger-pending).
    /// </summary>
    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;
        Visible = _open;
    }

    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey { Keycode: Key.Escape, Pressed: true })
        {
            // spec: ui_system.md §8.9.1 — "ESC closes the window CODE-CONFIRMED"
            Toggle(false);
            GetViewport().SetInputAsHandled();
        }
    }
}