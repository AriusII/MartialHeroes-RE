// Ui/Hud/HudFriendWindow.cs
//
// In-game Friend window — `FriendPanel` (two-tab add/cut).
//
// Geometry (PARTIAL — outer rect not statically located):
//   Width/height stored on panel object. OK/confirm positioned at (width/2−56, height−66, 113×40).
//   spec: Docs/RE/specs/ui_system.md §8.14 — "UNVERIFIED outer geometry".
//   Port choice: right-docked 318×732 (consistent with the right-dock family §5.3).
//
// Atlas (CODE-CONFIRMED):
//   uitex 9 = messagewindow.dds — main chrome, both list panels, tab buttons, rows, textboxes.
//   uitex 1 = mainwindow.dds — 3 top-bar arrow/icon buttons (actions 15/16/17).
//   uitex 2 = inventwindow.dds — OK/confirm button.
//   spec: Docs/RE/specs/ui_system.md §8.14 CODE-CONFIRMED.
//
// Two tabs:
//   Tab A = add friend; Tab B = cut/remove friend.
//   Each tab: 30 record slots, 10 visible rows (189×17, x=15, y-stride −16 from base 212).
//   12×12 status glyph at x=155 per row.
//   spec: Docs/RE/specs/ui_system.md §8.14 CODE-CONFIRMED.
//
// Two textboxes (both 95×20, dst (82,46)):
//   TB#1 (action 2) = add-name field → "friend %s %s" command.
//   TB#2 (action 3) = cut-name field → "cut %s" command.
//   Max length 16, IME-disabled.
//   spec: Docs/RE/specs/ui_system.md §8.14 CODE-CONFIRMED.
//
// Captions:
//   msg 16012 — legend line (action 17). msg 100614 — add/cut modal hint.
//   spec: Docs/RE/specs/ui_system.md §8.14 CODE-CONFIRMED.
//
// Outbound:
//   C2S 2/49 = add / cut friend (tag byte 0=add / 1=cut + 16-byte name).
//   C2S 2/54 = refresh friend list / online-status (3-min throttle).
//   → TODO(world-campaign): IApplicationUseCases stubs.
// Inbound: S2C 5/26 (candidate) → stub rows empty: TODO(debugger/capture).
//   spec: Docs/RE/specs/ui_system.md §8.14 CODE-CONFIRMED opcodes.
//
// Toggle hotkey: UNVERIFIED. ESC closes / Tab cycles TB focus. TODO(spec): toggle hotkey.
//
// PASSIVE: zero game logic; intents → use-case calls (stubbed).

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
/// In-game Friend window (FriendPanel — two-tab add/cut friend-list).
///
/// <para>PASSIVE: renders friend rows from Application events; emits add/cut intents as
/// use-case calls (stubbed pending world-campaign). Inbound list (5/26 candidate) stubbed.</para>
///
/// spec: Docs/RE/specs/ui_system.md §8.14 CODE-CONFIRMED.
/// </summary>
public sealed partial class HudFriendWindow : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // spec: Docs/RE/specs/ui_system.md §8.14 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    // Port-choice geometry for outer panel (outer rect UNVERIFIED per spec §8.14)
    // Using 318×732 right-dock consistent with §5.3 family.
    private const float FriendW = 318f;
    private const float FriendH = 732f;

    // Friend list row constants
    // spec: ui_system.md §8.14 — "30 record slots, 10 visible rows, y=212 stride −16, 189×17, x=15"
    private const int VisibleRows = 10; // spec: ui_system.md §8.14
    private const float RowBaseY = 212f; // spec: ui_system.md §8.14 — "starts at local y=212"
    private const float RowStrideY = -16f; // spec: ui_system.md §8.14 — "stride −16 (upward)"
    private const float RowW = 189f; // spec: ui_system.md §8.14
    private const float RowH = 17f; // spec: ui_system.md §8.14
    private const float RowX = 15f; // spec: ui_system.md §8.14
    private const float StatusGlyphX = 155f; // spec: ui_system.md §8.14 — "12×12 status glyph at x=155"
    private const float StatusGlyphSize = 12f; // spec: ui_system.md §8.14

    // Textbox constants
    // spec: ui_system.md §8.14 — "both 95×20 at dst (82,46), maxlen 16, IME-disabled"
    private const float TbX = 82f; // spec: ui_system.md §8.14
    private const float TbY = 46f; // spec: ui_system.md §8.14
    private const float TbW = 95f; // spec: ui_system.md §8.14
    private const float TbH = 20f; // spec: ui_system.md §8.14
    private const int TbMaxLen = 16; // spec: ui_system.md §8.14

    // Atlas ids
    // spec: ui_system.md §8.14 — uitex 9=messagewindow.dds, 1=mainwindow.dds, 2=inventwindow.dds
    private const int MainTexId = 9; // spec: ui_system.md §8.14
    private const int ArrowTexId = 1; // spec: ui_system.md §8.14
    private const int ConfirmTexId = 2; // spec: ui_system.md §8.14

    // msg.xdb ids
    // spec: ui_system.md §8.14 — msg 16012 = legend (action 17); msg 100614 = add/cut hint
    private const int MsgLegend = 16012; // spec: ui_system.md §8.14 CODE-CONFIRMED
    private const int MsgModalHint = 100614; // spec: ui_system.md §8.14 CODE-CONFIRMED

    // Refresh 3-min throttle
    // spec: ui_system.md §8.14 — "3-min throttle" for C2S 2/54
    private const double RefreshThrottleSecs = 180.0; // spec: ui_system.md §8.14

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private bool _open;
    private int _activeTab; // 0=add, 1=cut
    private double _lastRefreshTime = -RefreshThrottleSecs; // allow first refresh immediately

    private LineEdit? _textboxAdd;
    private LineEdit? _textboxCut;

    private readonly Button[] _rowBtnsA = new Button[VisibleRows];
    private readonly Button[] _rowBtnsB = new Button[VisibleRows];
    private readonly Label[] _statusA = new Label[VisibleRows];
    private readonly Label[] _statusB = new Label[VisibleRows];
    private Control? _tabAContent;
    private Control? _tabBContent;

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    /// Geometry pass: builds the friend-list panel with two tabs and dual-tab rows.
    ///
    /// spec: Docs/RE/specs/ui_system.md §8.14 CODE-CONFIRMED.
    /// </summary>
    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudFriendWindow";

        // Right-anchored, off-screen until opened
        // spec: ui_system.md §8.14 — "outer geometry UNVERIFIED; port choice = right-dock 318×732"
        AnchorLeft = 1f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 0f;
        OffsetLeft = FriendW;
        OffsetTop = 0f;
        OffsetRight = FriendW + FriendW;
        OffsetBottom = FriendH;

        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        // Backdrop
        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.07f, 0.07f, 0.13f, 0.93f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.35f, 0.35f, 0.5f, 0.85f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        Texture2D? mainTex = atlas.GetById(MainTexId);
        if (mainTex is null)
            GD.PrintErr("[HudFriendWindow] messagewindow.dds (uitex 9) unavailable (VFS offline). " +
                        "spec: Docs/RE/specs/ui_system.md §8.14.");

        // Tab A button (uitex 9, dst 11,7,62,20, src 317,469, action 0)
        // spec: ui_system.md §8.14 CODE-CONFIRMED
        var tabA = new Button
        {
            Name = "TabA",
            Text = "Add",
            Position = new Vector2(11f, 7f),
            Size = new Vector2(62f, 20f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        tabA.Pressed += () => SelectTab(0);
        AddChild(tabA);

        // Tab B button (uitex 9, dst 73,7,62,20, src 317,535, action 1)
        // spec: ui_system.md §8.14 CODE-CONFIRMED
        var tabB = new Button
        {
            Name = "TabB",
            Text = "Cut",
            Position = new Vector2(73f, 7f),
            Size = new Vector2(62f, 20f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        tabB.Pressed += () => SelectTab(1);
        AddChild(tabB);

        // Header strip (uitex 9, dst 12,84,189,17, src 463,338)
        // spec: ui_system.md §8.14 CODE-CONFIRMED
        if (mainTex is not null)
        {
            AtlasTexture? hdrSlice = atlas.SliceById(MainTexId, 463, 338, 189, 17);
            if (hdrSlice is not null)
            {
                var hdr = new TextureRect
                {
                    Name = "HeaderStrip",
                    Texture = hdrSlice,
                    Position = new Vector2(12f, 84f),
                    Size = new Vector2(189f, 17f),
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                AddChild(hdr);
            }
        }

        // Textbox for Add (action 2, TB#1) — "friend %s %s" commit
        // spec: ui_system.md §8.14 — "TB#1 dst(82,46,95×20), focus action 2, commit builds 'friend %s %s'"
        _textboxAdd = new LineEdit
        {
            Name = "TextboxAdd",
            Position = new Vector2(TbX, TbY),
            Size = new Vector2(TbW, TbH),
            MaxLength = TbMaxLen,
            PlaceholderText = "Friend name...",
            MouseFilter = MouseFilterEnum.Stop,
        };
        _textboxAdd.TextSubmitted += (name) => CommitAddFriend(name);
        AddChild(_textboxAdd);

        // Textbox for Cut (action 3, TB#2) — "cut %s" commit
        // spec: ui_system.md §8.14 — "TB#2 dst(82,46,95×20), focus action 3, commit builds 'cut %s'"
        _textboxCut = new LineEdit
        {
            Name = "TextboxCut",
            Position = new Vector2(TbX, TbY),
            Size = new Vector2(TbW, TbH),
            MaxLength = TbMaxLen,
            PlaceholderText = "Friend name...",
            Visible = false,
            MouseFilter = MouseFilterEnum.Stop,
        };
        _textboxCut.TextSubmitted += (name) => CommitCutFriend(name);
        AddChild(_textboxCut);

        // Tab A content (friend rows)
        _tabAContent = BuildTabRows("TabA", _rowBtnsA, _statusA, false);
        AddChild(_tabAContent);

        // Tab B content (cut/remove rows)
        _tabBContent = BuildTabRows("TabB", _rowBtnsB, _statusB, true);
        _tabBContent.Visible = false;
        AddChild(_tabBContent);

        // Scroll controls (uitex 9, up/thumb/down at confirmed coords)
        // spec: ui_system.md §8.14 — "scroll-up dst(203,68,13,10); thumb dst(202,78,15,10); down dst(203,221,13,10)"
        BuildScrollButtons(atlas);

        // Refresh button (uitex 9, dst 130,236,83×22, src 365,606, action 18)
        // spec: ui_system.md §8.14 CODE-CONFIRMED — "3-min throttle"
        var refreshBtn = new Button
        {
            Name = "RefreshBtn",
            Text = "Refresh",
            Position = new Vector2(130f, 236f),
            Size = new Vector2(83f, 22f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        refreshBtn.Pressed += OnRefresh; // action 18
        AddChild(refreshBtn);

        // OK / confirm button (uitex 2, dst (w/2−56, h−66, 113×40), src 825,807, action 14)
        // spec: ui_system.md §8.14 — "confirm button horizontally centered near bottom"
        float okX = FriendW / 2f - 56f;
        float okY = FriendH - 66f;
        var okBtn = new Button
        {
            Name = "ConfirmBtn",
            Text = text.GetCaption(MsgModalHint, "OK"),
            Position = new Vector2(okX, okY),
            Size = new Vector2(113f, 40f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        okBtn.Pressed += OnConfirm; // action 14
        AddChild(okBtn);

        // Top-bar action buttons (uitex 1, actions 15/16/17)
        // spec: ui_system.md §8.14 — "3 small top-bar arrow/icon buttons (actions 15/16/17) on uitex 1"
        for (int i = 0; i < 3; i++)
        {
            int actionId = 15 + i;
            var topBtn = new Button
            {
                Name = $"TopBtn{actionId}",
                Text = "•",
                Position = new Vector2(265f + i * 16f, 7f),
                Size = new Vector2(14f, 14f),
                MouseFilter = MouseFilterEnum.Stop,
            };
            int captured = actionId;
            topBtn.Pressed += () => OnTopBarAction(captured);
            AddChild(topBtn);
        }

        SelectTab(0);

        GD.Print("[HudFriendWindow] Built — FriendPanel two-tab (add/cut). " +
                 "2 textboxes (95×20, maxlen 16); 10 visible rows/tab (189×17, stride −16 from y=212). " +
                 "Inbound list: TODO(debugger/capture): inbound friend feed (5/26 candidate). " +
                 "Outbound: TODO(world-campaign): C2S 2/49 (add/cut) + 2/54 (refresh 3-min throttle). " +
                 "spec: Docs/RE/specs/ui_system.md §8.14 CODE-CONFIRMED.");
    }

    private Control BuildTabRows(string tabName, Button[] rowBtns, Label[] statusGlyphs, bool hidden)
    {
        // spec: ui_system.md §8.14 — "30 record slots, 10 visible rows, y=212 stride −16, 189×17, x=15"
        var container = new Control { Name = tabName + "Rows" };
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        container.Visible = !hidden;

        for (int r = 0; r < VisibleRows; r++)
        {
            float rowY = RowBaseY + r * RowStrideY; // stride −16 (upward)

            var rowBtn = new Button
            {
                Name = $"{tabName}Row{r}",
                Text = "",
                Position = new Vector2(RowX, rowY),
                Size = new Vector2(RowW, RowH),
                Flat = true,
                MouseFilter = MouseFilterEnum.Stop,
            };
            int capturedRow = r;
            rowBtn.Pressed += () => OnRowSelect(tabName == "TabA" ? 0 : 1, capturedRow);
            container.AddChild(rowBtn);
            rowBtns[r] = rowBtn;

            // 12×12 status glyph at x=155
            // spec: ui_system.md §8.14 CODE-CONFIRMED
            var statusGlyph = new Label
            {
                Name = $"{tabName}Status{r}",
                Text = "",
                Position = new Vector2(StatusGlyphX, rowY),
                Size = new Vector2(StatusGlyphSize, StatusGlyphSize),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            container.AddChild(statusGlyph);
            statusGlyphs[r] = statusGlyph;
        }

        return container;
    }

    private void BuildScrollButtons(HudAtlasLibrary atlas)
    {
        // spec: ui_system.md §8.14 — scroll-up (203,68,13,10), thumb (202,78,15,10), down (203,221,13,10)
        var scrollUp = new Button
        {
            Name = "ScrollUp",
            Text = "↑",
            Position = new Vector2(203f, 68f),
            Size = new Vector2(13f, 10f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        scrollUp.Pressed += () => OnScroll(-1); // action 8/9
        AddChild(scrollUp);

        var scrollDown = new Button
        {
            Name = "ScrollDown",
            Text = "↓",
            Position = new Vector2(203f, 221f),
            Size = new Vector2(13f, 10f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        scrollDown.Pressed += () => OnScroll(+1); // action 10/11
        AddChild(scrollDown);
    }

    // -------------------------------------------------------------------------
    // Tab switching
    // -------------------------------------------------------------------------

    private void SelectTab(int tab)
    {
        _activeTab = tab;

        if (_tabAContent is not null) _tabAContent.Visible = (tab == 0);
        if (_tabBContent is not null) _tabBContent.Visible = (tab == 1);
        if (_textboxAdd is not null) _textboxAdd.Visible = (tab == 0);
        if (_textboxCut is not null) _textboxCut.Visible = (tab == 1);
    }

    // -------------------------------------------------------------------------
    // Action handlers
    // -------------------------------------------------------------------------

    private void CommitAddFriend(string name)
    {
        // spec: ui_system.md §8.14 — "TB#1 commit → 'friend %s %s' command → C2S 2/49 tag=0"
        if (string.IsNullOrWhiteSpace(name)) return;
        string trimmed = name.Length > TbMaxLen ? name[..TbMaxLen] : name;
        // TODO(world-campaign): IApplicationUseCases.FriendAdd(trimmed) → C2S 2/49 tag=0
        GD.Print($"[HudFriendWindow] Add friend '{trimmed}' → TODO(world-campaign): C2S 2/49 tag=0. " +
                 "spec: Docs/RE/specs/ui_system.md §8.14.");
        if (_textboxAdd is not null) _textboxAdd.Text = "";
    }

    private void CommitCutFriend(string name)
    {
        // spec: ui_system.md §8.14 — "TB#2 commit → 'cut %s' command → C2S 2/49 tag=1"
        if (string.IsNullOrWhiteSpace(name)) return;
        string trimmed = name.Length > TbMaxLen ? name[..TbMaxLen] : name;
        // TODO(world-campaign): IApplicationUseCases.FriendCut(trimmed) → C2S 2/49 tag=1
        GD.Print($"[HudFriendWindow] Cut friend '{trimmed}' → TODO(world-campaign): C2S 2/49 tag=1. " +
                 "spec: Docs/RE/specs/ui_system.md §8.14.");
        if (_textboxCut is not null) _textboxCut.Text = "";
    }

    private void OnRefresh()
    {
        // spec: ui_system.md §8.14 — "action 18 = refresh (C2S 2/54, 3-min throttle)"
        double now = global::Godot.Time.GetTicksMsec() / 1000.0;
        if (now - _lastRefreshTime < RefreshThrottleSecs)
        {
            GD.Print("[HudFriendWindow] Refresh throttled (3-min cooldown). " +
                     "spec: Docs/RE/specs/ui_system.md §8.14.");
            return;
        }

        _lastRefreshTime = now;
        // TODO(world-campaign): IApplicationUseCases.FriendListRefresh() → C2S 2/54
        GD.Print("[HudFriendWindow] action 18 = refresh → TODO(world-campaign): C2S 2/54. " +
                 "spec: Docs/RE/specs/ui_system.md §8.14.");
    }

    private void OnConfirm()
    {
        // spec: ui_system.md §8.14 — "action 14 = confirm: opens add/cut entry modal, hint msg 100614"
        GD.Print("[HudFriendWindow] action 14 = confirm (modal hint msg 100614). " +
                 "spec: Docs/RE/specs/ui_system.md §8.14.");
    }

    private void OnTopBarAction(int actionId)
    {
        // spec: ui_system.md §8.14 — actions 15/16/17 on uitex 1 (page/scroll, aux, help/legend)
        if (actionId == 17)
            GD.Print($"[HudFriendWindow] action 17 = legend (msg {MsgLegend}). " +
                     "spec: Docs/RE/specs/ui_system.md §8.14 CODE-CONFIRMED.");
        else
            GD.Print($"[HudFriendWindow] action {actionId} top-bar. " +
                     "spec: Docs/RE/specs/ui_system.md §8.14.");
    }

    private void OnRowSelect(int tab, int row)
    {
        // spec: ui_system.md §8.14 — "actions 19..28 = visible row 0..9 select"
        // TODO(debugger/capture): inbound friend feed (5/26 candidate) — rows stub-empty
        GD.Print($"[HudFriendWindow] tab={tab} row={row} selected. " +
                 "TODO(debugger/capture): inbound friend feed (5/26 candidate). " +
                 "spec: Docs/RE/specs/ui_system.md §8.14.");
    }

    private void OnScroll(int direction)
    {
        // spec: ui_system.md §8.14 — "actions 8/9=up-arrows, 10/11=down-arrows (scroll)"
        GD.Print($"[HudFriendWindow] scroll direction={direction}.");
    }

    // -------------------------------------------------------------------------
    // Toggle
    // -------------------------------------------------------------------------

    /// <summary>
    /// Toggles the friend window on/off.
    /// Toggle key: UNVERIFIED. ESC closes / Tab cycles TB focus.
    /// spec: Docs/RE/specs/ui_system.md §8.14 — "open key UNVERIFIED; ESC blurs+closes".
    /// TODO(spec): toggle hotkey.
    /// </summary>
    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;

        if (_open)
        {
            OffsetLeft = -FriendW;
            OffsetRight = 0f;
        }
        else
        {
            OffsetLeft = FriendW;
            OffsetRight = FriendW + FriendW;
        }

        Visible = _open;
    }

    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey key && key.Pressed)
        {
            switch (key.Keycode)
            {
                case Key.Escape:
                    // spec: ui_system.md §8.14 — "ESC: blurs active textbox and closes"
                    _textboxAdd?.ReleaseFocus();
                    _textboxCut?.ReleaseFocus();
                    Toggle(false);
                    GetViewport().SetInputAsHandled();
                    break;

                case Key.Tab:
                    // spec: ui_system.md §8.14 — "Tab: cycles focus between the two boxes"
                    if (_activeTab == 0 && _textboxAdd is not null)
                    {
                        if (_textboxAdd.HasFocus()) _textboxAdd.ReleaseFocus();
                        else _textboxAdd.GrabFocus();
                    }
                    else if (_activeTab == 1 && _textboxCut is not null)
                    {
                        if (_textboxCut.HasFocus()) _textboxCut.ReleaseFocus();
                        else _textboxCut.GrabFocus();
                    }

                    GetViewport().SetInputAsHandled();
                    break;
            }
        }
    }
}