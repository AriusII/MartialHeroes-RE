// Ui/Hud/HudGuildWarInfoWindow.cs
//
// In-game guild-war info display window — `GuildWarInfoPanel` (slot 224, key `j`, READ-ONLY).
//
// A display/read-only guild-war information window: a two-wide list of up to 10 entries,
// each = an item/icon + a name + a numeric value, plus an overall active flag.
// It emits NO C2S packet — row buttons select/show, OK/Close dismiss.
//
// Master service slot 224. Toggled by key `j` (ASCII 106).
// Also opened by server event S2C 5/73.
//
// Geometry (CODE-CONFIRMED, panel-local origin):
//   Title-bar panel    (0,  0, 618,  36)  top chrome (drag-handle host)
//   Body panel         (0, 36, 618, 273)  list host
//   Close button       (584, 11, 11, 11)  action 31 (close / drag)
//   OK button          (253, 235, 94, 27) action 30 (OK / close)
//   Embedded descriptor widget (0, 0, 220, 200)  tooltip/detail
//   spec: Docs/RE/specs/ui_system.md §8.31.1 CODE-CONFIRMED
//
// Row layout — two-wide list (10 rows, 5 row-groups):
//   Row Y = 34·(r/2) + 49 (34-px pitch; even rows left sub-col, odd rows right sub-col).
//   Left sub-col (even r):  icon x=62 (23×23), name x=95 (102×23), value x=211 (78×23)
//   Right sub-col (odd r): icon x=326 (23×23), name x=359 (102×23), value x=475 (78×23)
//   Per-row actions: icon→r, value→r+10, name→r+20 (icon 0..9, value 10..19, name 20..29)
//   spec: Docs/RE/specs/ui_system.md §8.31.1 CODE-CONFIRMED
//
// Atlas: literal path `data/ui/moonpa.dds` (hard-embedded; NOT a uitex id).
//   Static source rects:
//     Close button NORMAL (199,309) PRESSED (199,320) 11×11
//     OK button NORMAL (0,309) PRESSED (94,309) 94×27
//   spec: Docs/RE/specs/ui_system.md §8.31.2 CODE-CONFIRMED
//
// Action map (CODE-CONFIRMED):
//   0..9  = row icon (selection / display only)
//   10..19 = row value (display only)
//   20..29 = row name (display only)
//   30 = OK button → close window
//   31 = Close button + title-bar drag; ESC (key 27) also closes
//   spec: Docs/RE/specs/ui_system.md §8.31.3 CODE-CONFIRMED
//
// Populate: S2C 5/73 = refreshes slot 224 (copies info block, sets open-state, populates 10 rows).
//   Each row: presence flag + icon id + 17-byte name string + numeric value.
//   VALUE semantics (score/timer/wager, our guild vs opposing guild) CAPTURE-PENDING.
//   spec: Docs/RE/specs/ui_system.md §8.31.4 CODE-CONFIRMED shape; VALUE meaning capture-pending
//
// NO C2S from this panel. Display/read-only.
//   spec: Docs/RE/specs/ui_system.md §8.31.3 — "No C2S from this panel"
//
// PASSIVE: zero game logic; row selects are display-only; populate stubbed.

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
///     In-game guild-war info display window (GuildWarInfoPanel, master service slot 224, key `j`).
///     <para>
///         A two-wide list of up to 10 guild-war info entries (icon + name + value). Read-only;
///         emits NO C2S packet. Populated by S2C 5/73 (stubbed). Toggled by key `j` (ASCII 106).
///     </para>
///     <para>PASSIVE: zero game logic. Row clicks are display-only selection. No game-state mutation.</para>
///     spec: Docs/RE/specs/ui_system.md §8.31 CODE-CONFIRMED.
/// </summary>
public sealed partial class HudGuildWarInfoWindow : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // spec: Docs/RE/specs/ui_system.md §8.31 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    // Panel overall size (inferred from title 618×36 + body 618×273)
    // spec: ui_system.md §8.31.1 — "title-bar (0,0,618,36); body (0,36,618,273)"
    // spec: ui_system.md §8.31 — "width 618/309" (master-window builder uses 618 and 309)
    private const float PanelW = 618f; // spec: ui_system.md §8.31 CODE-CONFIRMED — "width 618"
    private const float PanelH = 309f; // spec: ui_system.md §8.31 CODE-CONFIRMED — "height 309"

    // Title-bar and body panels
    // spec: ui_system.md §8.31.1 CODE-CONFIRMED
    private const float TitleBarH = 36f; // spec: ui_system.md §8.31.1 "(0,0,618,36)"
    private const float BodyY = 36f; // spec: ui_system.md §8.31.1 "(0,36,618,273)"
    private const float BodyH = 273f; // spec: ui_system.md §8.31.1 CODE-CONFIRMED

    // Close button (action 31) and OK button (action 30)
    // spec: ui_system.md §8.31.1 CODE-CONFIRMED
    private const float CloseBtnX = 584f; // spec: ui_system.md §8.31.1 "(584,11,11,11)"
    private const float CloseBtnY = 11f;
    private const float CloseBtnSize = 11f;
    private const float OkBtnX = 253f; // spec: ui_system.md §8.31.1 "(253,235,94,27)"
    private const float OkBtnY = 235f;
    private const float OkBtnW = 94f;
    private const float OkBtnH = 27f;

    // Row count (10 entries)
    // spec: ui_system.md §8.31 — "up to 10 entries"
    private const int MaxRows = 10; // spec: ui_system.md §8.31 CODE-CONFIRMED

    // Row pitch and base Y
    // spec: ui_system.md §8.31.1 — "Row Y = 34·(r/2) + 49 (34-px pitch)"
    private const float RowPitch = 34f; // spec: ui_system.md §8.31.1 CODE-CONFIRMED
    private const float RowBaseY = 49f; // spec: ui_system.md §8.31.1 CODE-CONFIRMED

    // Left sub-column (even r)
    // spec: ui_system.md §8.31.1 — "even r: icon x=62 (23×23), name x=95 (102×23), value x=211 (78×23)"
    private const float LeftIconX = 62f; // spec: ui_system.md §8.31.1 CODE-CONFIRMED
    private const float LeftNameX = 95f; // spec: ui_system.md §8.31.1 CODE-CONFIRMED
    private const float LeftValueX = 211f; // spec: ui_system.md §8.31.1 CODE-CONFIRMED

    // Right sub-column (odd r)
    // spec: ui_system.md §8.31.1 — "odd r: icon x=326 (23×23), name x=359 (102×23), value x=475 (78×23)"
    private const float RightIconX = 326f; // spec: ui_system.md §8.31.1 CODE-CONFIRMED
    private const float RightNameX = 359f; // spec: ui_system.md §8.31.1 CODE-CONFIRMED
    private const float RightValueX = 475f; // spec: ui_system.md §8.31.1 CODE-CONFIRMED

    // Column widths/heights
    // spec: ui_system.md §8.31.1 CODE-CONFIRMED
    private const float IconSz = 23f; // spec: ui_system.md §8.31.1 "23×23"
    private const float NameW = 102f; // spec: ui_system.md §8.31.1 "102×23"
    private const float NameH = 23f;
    private const float ValueW = 78f; // spec: ui_system.md §8.31.1 "78×23"
    private const float ValueH = 23f;
    private readonly Label[] _nameLabels = new Label[MaxRows];
    private readonly Label[] _valueLabels = new Label[MaxRows];

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private bool _open;

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Geometry pass: builds the guild-war info display panel.
    ///     spec: Docs/RE/specs/ui_system.md §8.31.1 CODE-CONFIRMED.
    /// </summary>
    public void Build(HudAtlasLibrary atlas)
    {
        Name = "HudGuildWarInfoWindow";

        // Port choice: centred on screen (absolute origin debugger-pending §8.31.7)
        // spec: ui_system.md §8.31 — "master-window-placed; absolute origin debugger-pending"
        AnchorLeft = 0.5f;
        AnchorTop = 0.5f;
        AnchorRight = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -PanelW / 2f;
        OffsetTop = -PanelH / 2f;
        OffsetRight = PanelW / 2f;
        OffsetBottom = PanelH / 2f;
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        // Title-bar panel (0,0,618,36)
        // spec: ui_system.md §8.31.1 — "(0,0,618,36) top chrome (drag-handle host)"
        var titleBar = new Panel { Name = "TitleBar" };
        titleBar.Position = Vector2.Zero;
        titleBar.Size = new Vector2(PanelW, TitleBarH);
        var titleStyle = new StyleBoxFlat();
        titleStyle.BgColor = new Color(0.1f, 0.08f, 0.06f, 0.95f);
        titleBar.AddThemeStyleboxOverride("panel", titleStyle);
        AddChild(titleBar);

        // Title label
        var titleLbl = new Label
        {
            Name = "TitleLabel",
            Text = "Guild War Info",
            Position = new Vector2(10f, 8f),
            Size = new Vector2(300f, 20f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(titleLbl);

        // Close button (action 31)
        // spec: ui_system.md §8.31.1 — "(584,11,11,11) action 31"
        // spec: ui_system.md §8.31.2 — "Close NORMAL (199,309) PRESSED (199,320) 11×11"
        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Text = "×",
            Position = new Vector2(CloseBtnX, CloseBtnY),
            Size = new Vector2(CloseBtnSize, CloseBtnSize),
            MouseFilter = MouseFilterEnum.Stop
        };
        closeBtn.Pressed += OnClose; // action 31
        AddChild(closeBtn);

        // Body panel (0,36,618,273)
        // spec: ui_system.md §8.31.1 — "(0,36,618,273) list host"
        var bodyPanel = new Panel { Name = "BodyPanel" };
        bodyPanel.Position = new Vector2(0f, BodyY);
        bodyPanel.Size = new Vector2(PanelW, BodyH);
        var bodyStyle = new StyleBoxFlat();
        bodyStyle.BgColor = new Color(0.08f, 0.07f, 0.10f, 0.93f);
        bodyPanel.AddThemeStyleboxOverride("panel", bodyStyle);
        AddChild(bodyPanel);

        // Build 10 rows in two-wide layout
        // spec: ui_system.md §8.31.1 — "two-wide list: even rows left sub-col, odd rows right sub-col"
        // spec: ui_system.md §8.31.1 — "Row Y = 34·(r/2) + 49 (34-px pitch)"
        for (var r = 0; r < MaxRows; r++)
        {
            var isRight = r % 2 != 0; // spec: ui_system.md §8.31.1 — "even=left, odd=right"
            var rowY = BodyY + RowBaseY + RowPitch * (r / 2); // spec: ui_system.md §8.31.1

            var iconX = isRight ? RightIconX : LeftIconX; // spec: ui_system.md §8.31.1
            var nameX = isRight ? RightNameX : LeftNameX; // spec: ui_system.md §8.31.1
            var valueX = isRight ? RightValueX : LeftValueX; // spec: ui_system.md §8.31.1

            // Icon button (action = r, 0..9)
            // spec: ui_system.md §8.31.3 — "0..9: row icon — selection/display only"
            var capturedR = r;
            var iconBtn = new Button
            {
                Name = $"IconBtn{r}",
                Text = "□",
                Position = new Vector2(iconX, rowY),
                Size = new Vector2(IconSz, IconSz),
                Flat = true,
                MouseFilter = MouseFilterEnum.Stop
            };
            iconBtn.Pressed += () => OnRowIcon(capturedR); // action r
            AddChild(iconBtn);

            // Name button (action = r+20, 20..29)
            // spec: ui_system.md §8.31.3 — "20..29: row name — name display only"
            var nameBtn = new Button
            {
                Name = $"NameBtn{r}",
                Text = "",
                Position = new Vector2(nameX, rowY),
                Size = new Vector2(NameW, NameH),
                Flat = true,
                Alignment = HorizontalAlignment.Left,
                MouseFilter = MouseFilterEnum.Stop
            };
            nameBtn.Pressed += () => OnRowName(capturedR + 20); // action r+20
            AddChild(nameBtn);

            var nameLbl = new Label
            {
                Name = $"NameLbl{r}",
                Text = "",
                Position = new Vector2(nameX + 2f, rowY + 3f),
                Size = new Vector2(NameW - 4f, NameH - 6f),
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(nameLbl);
            _nameLabels[r] = nameLbl;

            // Value button (action = r+10, 10..19)
            // spec: ui_system.md §8.31.3 — "10..19: row value — numeric value display only"
            var valueBtn = new Button
            {
                Name = $"ValueBtn{r}",
                Text = "",
                Position = new Vector2(valueX, rowY),
                Size = new Vector2(ValueW, ValueH),
                Flat = true,
                MouseFilter = MouseFilterEnum.Stop
            };
            valueBtn.Pressed += () => OnRowValue(capturedR + 10); // action r+10
            AddChild(valueBtn);

            var valueLbl = new Label
            {
                Name = $"ValueLbl{r}",
                Text = "",
                Position = new Vector2(valueX + 2f, rowY + 3f),
                Size = new Vector2(ValueW - 4f, ValueH - 6f),
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(valueLbl);
            _valueLabels[r] = valueLbl;
        }

        // OK button (action 30)
        // spec: ui_system.md §8.31.1 — "(253,235,94,27) action 30"
        // spec: ui_system.md §8.31.2 — "OK NORMAL (0,309) PRESSED (94,309) 94×27"
        var okBtn = new Button
        {
            Name = "OkBtn",
            Text = "OK",
            Position = new Vector2(OkBtnX, OkBtnY),
            Size = new Vector2(OkBtnW, OkBtnH),
            MouseFilter = MouseFilterEnum.Stop
        };
        okBtn.Pressed += OnClose; // action 30 — close window
        AddChild(okBtn);

        GD.Print("[HudGuildWarInfoWindow] Built — GuildWarInfoPanel slot 224 (key 'j'). " +
                 "10-row two-wide list (icon 0..9, value 10..19, name 20..29). " +
                 "Close (31) + OK (30). Read-only: NO C2S. " +
                 "Atlas: literal data/ui/moonpa.dds (VFS-pending). " +
                 "Populate: TODO(capture): S2C 5/73 (344B guild-war info block). " +
                 "spec: Docs/RE/specs/ui_system.md §8.31 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Toggle (key `j`)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Toggles the guild-war info window (key `j`, ASCII 106, slot 224).
    ///     spec: Docs/RE/specs/ui_system.md §8.31.6 CODE-CONFIRMED — key `j` toggle.
    /// </summary>
    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;
        Visible = _open;
        GD.Print($"[HudGuildWarInfoWindow] Toggle → open={_open}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.31.6 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Action handlers (display-only)
    // -------------------------------------------------------------------------

    private void OnRowIcon(int row)
    {
        // action = row (0..9) — selection/display only; no C2S
        // spec: ui_system.md §8.31.3 — "0..9: row icon — selection/display only"
        GD.Print($"[HudGuildWarInfoWindow] Row icon {row} (action {row}) — display only. " +
                 "spec: Docs/RE/specs/ui_system.md §8.31.3.");
    }

    private void OnRowName(int action)
    {
        // action = row+20 (20..29) — name display only; no C2S
        // spec: ui_system.md §8.31.3 — "20..29: row name — name display only"
        GD.Print($"[HudGuildWarInfoWindow] Row name action {action} — display only. " +
                 "spec: Docs/RE/specs/ui_system.md §8.31.3.");
    }

    private void OnRowValue(int action)
    {
        // action = row+10 (10..19) — numeric value display only; no C2S
        // spec: ui_system.md §8.31.3 — "10..19: row value — numeric value display only"
        GD.Print($"[HudGuildWarInfoWindow] Row value action {action} — display only. " +
                 "spec: Docs/RE/specs/ui_system.md §8.31.3.");
    }

    private void OnClose()
    {
        // actions 30 / 31 / ESC
        // spec: ui_system.md §8.31.3 — "30: OK → close; 31: close + drag; ESC → close"
        Toggle(false);
        GD.Print("[HudGuildWarInfoWindow] Closed (actions 30/31). " +
                 "spec: Docs/RE/specs/ui_system.md §8.31.3.");
    }

    // -------------------------------------------------------------------------
    // Input (ESC closes)
    // -------------------------------------------------------------------------

    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            // spec: ui_system.md §8.31.3 — "ESC (key 27) also closes"
            Toggle(false);
            GetViewport().SetInputAsHandled();
        }
    }
}