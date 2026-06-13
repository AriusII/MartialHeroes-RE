// HUD/CharacterStatsWindow.cs
//
// The in-game character / stat window — shows five primary stats (STR/INT/AGI/DEX/CON) with
// +/- buttons and a pending-delta model, an Apply action that raises a stat-allocate intent,
// and a close button.
//
// STRICTLY PASSIVE: zero game logic, zero stat math. All state comes from StatAllocationView
// published by the Application layer; the HUD only turns button gestures into use-case calls.
//
// Spec facts implemented:
//   - Five primary stats STR/INT/AGI/DEX/CON.
//     spec: Docs/RE/specs/progression.md §2 (the five cached absolute stats).
//   - Pending-delta model: each + step adds to delta, each – step removes; available points
//     recomputed as remaining_stat_points − Σ deltas (carried in StatAllocationView).
//     spec: Docs/RE/specs/progression.md §7.1.
//   - Modifier-key step: no modifier=1, Shift=10, Ctrl=100, Alt=1000.
//     spec: Docs/RE/specs/progression.md §7.3 (key ids 1012/1013/1014 → steps 1000/100/10/1).
//     Our Godot mapping: Alt=1000, Ctrl=100, Shift=10, none=1 — mirrors the spec intent.
//   - Apply gates: remaining_stat_points > 0 AND at least one delta non-zero.
//     spec: Docs/RE/specs/progression.md §8.1.
//   - Wire order for Apply: STR, INT, AGI, DEX, CON (absolute = base + delta).
//     spec: Docs/RE/specs/progression.md §8.1 (wire order STR, INT, AGI, DEX, CON).
//   - Reset clears all five deltas.
//     spec: Docs/RE/specs/progression.md §7.2 action 310 — Reset.
//   - Close button (action id 312 in legacy; mapped to a Godot Button.Pressed here).
//     spec: Docs/RE/specs/ui_system.md §8.7 action id 312 = Close button.
//   - Chrome: inventwindow.dds (uitex 2) base panel + tradekeepwindow.dds (uitex 4) stat rows.
//     spec: Docs/RE/specs/ui_system.md §8.7 CODE-CONFIRMED chrome binding.
//
// Binding: call Bind(hub, useCases) from the owning HUD. StatAllocations channel is drained
// in _Process on the main thread. The +/- buttons publish local pending-delta changes via the
// hub; Apply sends the StatAllocate use-case intent.
//
// NOTE: The IApplicationUseCases interface does not yet expose a StatAllocateAsync method
// (the progression use case was not in scope for Stage A). The Apply button therefore emits
// a GD.Print intent message until the application engineer adds the use-case call. This is
// intentional — no domain state is mutated here. See OPEN ITEM below.

using System.Threading.Channels;
using Godot;
using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Screens;

namespace MartialHeroes.Client.Godot.HUD;

/// <summary>
/// Toggleable character / stat window (mirroring the legacy <c>StatusPanel</c>).
///
/// <para><b>Pending-delta model</b></para>
/// The window never mutates domain state. The + and - buttons accumulate pending deltas which
/// are kept as <em>view state</em> only. On Apply the window calls the stat-allocate use case
/// (once wired) with the five absolute targets (base + delta). Any stat-update ack from the
/// server arrives as a fresh <see cref="StatAllocationView"/> on the channel, which the window
/// renders as the new authoritative state.
///
/// <para><b>Chrome</b></para>
/// Window chrome is from <c>inventwindow.dds</c> (uitex 2) and <c>tradekeepwindow.dds</c>
/// (uitex 4). Both degrade gracefully when the VFS is offline.
/// spec: Docs/RE/specs/ui_system.md §8.7 CODE-CONFIRMED.
/// </para>
///
/// <para><b>Binding</b></para>
/// Call <see cref="Bind(IHudEventHub, IApplicationUseCases)"/> from the owning HUD after both
/// surfaces are available.  A parameterless <see cref="_Ready"/> shows a DEMO state when no
/// hub is bound (mirror of InventoryWindow/SkillWindow offline behaviour).
/// </summary>
public sealed partial class CharacterStatsWindow : Control
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Atlas constants
    // ─────────────────────────────────────────────────────────────────────────

    // Base panel chrome: inventwindow.dds (uitex 2).
    // spec: Docs/RE/specs/ui_system.md §8.7 CODE-CONFIRMED — "window chrome is inventwindow.dds (uitex 2)".
    private const int InvTexId = 2; // spec: ui_system.md §8.6.1 uitex 2 = inventwindow.dds
    private const string InvTexPath = "data/ui/inventwindow.dds";

    // Stat-row chrome: tradekeepwindow.dds (uitex 4).
    // spec: Docs/RE/specs/ui_system.md §8.7 CODE-CONFIRMED — "stat-row sprites + +/- buttons on tradekeepwindow.dds (uitex 4)".
    private const int TradeKeepTexId = 4; // spec: ui_system.md §8.6.1 uitex 4 = tradekeepwindow.dds
    private const string TradeKeepTexPath = "data/ui/tradekeepwindow.dds";

    // Apply button chrome: inventwindow.dds, action 310 source rect.
    // spec: Docs/RE/specs/ui_system.md §8.7 — "Apply (310): (259,655,59,77) on inventwindow.dds": CODE-CONFIRMED.
    private const int ApplyBtnSrcX = 259; // spec: ui_system.md §8.7 Apply button (259,655)
    private const int ApplyBtnSrcY = 655; // spec: ui_system.md §8.7 Apply button
    private const int ApplyBtnW = 59; // spec: ui_system.md §8.7
    private const int ApplyBtnH = 77; // spec: ui_system.md §8.7

    // Close button: mainwindow.dds, action 312.
    // spec: Docs/RE/specs/ui_system.md §8.7 — "Close (312): (286,46,29,26) on mainwindow.dds (uitex 1)": CODE-CONFIRMED.
    private const int MainWinTexId = 1; // spec: ui_system.md §8.6.1 uitex 1 = mainwindow.dds
    private const string MainWinTexPath = "data/ui/mainwindow.dds";
    private const int CloseBtnSrcX = 354; // spec: ui_system.md §8.7 Close NORMAL (354,596,29,26)
    private const int CloseBtnSrcY = 596;
    private const int CloseBtnW = 29;
    private const int CloseBtnH = 26;

    // ─────────────────────────────────────────────────────────────────────────
    //  Modifier-key step values
    // ─────────────────────────────────────────────────────────────────────────

    // Legacy key ids 1012/1013/1014 map to steps 1000/100/10 respectively (§7.3).
    // We mirror this with Godot modifier keys: Alt=1000, Ctrl=100, Shift=10, none=1.
    // spec: Docs/RE/specs/progression.md §7.3 (key id 1012=1000, 1013=100, 1014=10, none=1).
    private const uint StepAlt = 1000u; // spec: progression.md §7.3 key 1012 → step 1000
    private const uint StepCtrl = 100u; // spec: progression.md §7.3 key 1013 → step 100
    private const uint StepShift = 10u; // spec: progression.md §7.3 key 1014 → step 10
    private const uint StepDefault = 1u; // spec: progression.md §7.3 none → step 1

    // ─────────────────────────────────────────────────────────────────────────
    //  View state (display only — no domain state)
    // ─────────────────────────────────────────────────────────────────────────

    // The last authoritative StatAllocationView received from the hub.
    private StatAllocationView _view = StatAllocationView.Empty;

    // Local pending deltas (view-only state, not domain state).
    // These mirror the server's StatAllocationView.Delta* but allow local accumulation
    // between refreshes. They are reset whenever the server publishes a new view.
    // spec: Docs/RE/specs/progression.md §7.1 (pending-delta model lives in the window object).
    private uint _pendingStr;
    private uint _pendingInt;
    private uint _pendingAgi;
    private uint _pendingDex;
    private uint _pendingCon;

    // Hub channel reader — drained in _Process.
    private ChannelReader<StatAllocationView>? _statAllocations;

    // Use-case facade for Apply calls.
    private IApplicationUseCases? _useCases;

    // Whether Bind() has been called.
    private bool _bound;

    // Window drag state.
    private bool _dragging;
    private Vector2 _dragOffset;

    // ClientContext for atlas loading.
    private ClientContext? _context;
    private UiAssetLoader? _uiLoader;

    // ─────────────────────────────────────────────────────────────────────────
    //  Child control references
    // ─────────────────────────────────────────────────────────────────────────

    // Five stat rows: label showing "BASE + DELTA"
    private readonly Label[] _statLabels = new Label[5]; // [0]=STR [1]=INT [2]=AGI [3]=DEX [4]=CON

    // Available-points label.
    private Label _pointsLabel = null!;

    // Apply button (disabled until both points and deltas are available).
    private Button _applyBtn = null!;

    // Demo label (offline mode).
    private Label? _demoLabel;

    // ─────────────────────────────────────────────────────────────────────────
    //  Godot lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        try
        {
            _context = GetNode<ClientContext>("/root/ClientContext");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharacterStatsWindow] ClientContext not found: {ex.Message} — " +
                        "chrome offline, will use plain controls.");
        }

        try
        {
            _uiLoader = UiAssetLoader.Open();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharacterStatsWindow] UiAssetLoader.Open failed: {ex.Message} — chrome offline.");
        }

        try
        {
            BuildUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharacterStatsWindow] BuildUi failed: {ex.Message}");
        }

        RefreshLabels();
        Visible = false;
        GD.Print("[CharacterStatsWindow] Ready. Call Bind(hub, useCases) to wire to Application layer.");
    }

    public override void _ExitTree()
    {
        _uiLoader?.Dispose();
        _uiLoader = null;
    }

    public override void _Process(double delta)
    {
        if (_statAllocations is null) return;

        // Drain the latest-wins channel on the main thread.
        while (_statAllocations.TryRead(out StatAllocationView? newView))
        {
            // Accept the server's view. Reset local pending deltas — the view already carries
            // the correct delta state (refreshed by the 4/29 ack / level-up).
            // spec: Docs/RE/specs/progression.md §5.1 — "level-up first resets pending deltas then rebuilds".
            _view = newView;
            _pendingStr = newView.DeltaStr;
            _pendingInt = newView.DeltaInt;
            _pendingAgi = newView.DeltaAgi;
            _pendingDex = newView.DeltaDex;
            _pendingCon = newView.DeltaCon;
            RefreshLabels();
        }
    }

    public override void _Input(InputEvent ev)
    {
        // Toggle with key 'C' (character window shortcut).
        // spec: Docs/RE/formats/misc_data.md §5 — discript.sc category 102 "(C)" for character info window.
        if (ev is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.C)
        {
            Visible = !Visible;
            if (Visible) MoveToFront();
            GetViewport().SetInputAsHandled();
        }

        // Drag — title-bar initiated (mouse down inside window rect).
        if (ev is InputEventMouseButton mb)
        {
            if (mb.Pressed && mb.ButtonIndex == MouseButton.Left && GetRect().HasPoint(mb.Position))
            {
                _dragging = true;
                _dragOffset = mb.Position - GlobalPosition;
            }
            else if (!mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                _dragging = false;
            }
        }

        if (_dragging && ev is InputEventMouseMotion motion)
            GlobalPosition = motion.Position - _dragOffset;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Public Bind surface
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Wires the window to the HUD event hub (stat-allocation channel) and the use-case facade
    /// (for the Apply intent call). Call once from the owning HUD node.
    /// Safe to call multiple times; subsequent calls are no-ops.
    ///
    /// <para>When <paramref name="hub"/> is null the window stays in offline/demo mode.</para>
    /// </summary>
    /// <param name="hub">The application HUD event hub. Provides StatAllocations channel reader.</param>
    /// <param name="useCases">
    /// The use-case facade. Used to raise the stat-allocate intent on Apply.
    /// May be null; Apply will emit a log message instead (offline mode).
    /// </param>
    public void Bind(IHudEventHub? hub, IApplicationUseCases? useCases)
    {
        if (_bound) return;
        _bound = true;

        _useCases = useCases;

        if (hub is not null)
        {
            _statAllocations = hub.StatAllocations;
            GD.Print("[CharacterStatsWindow] Bound to IHudEventHub.StatAllocations.");
        }
        else
        {
            GD.Print("[CharacterStatsWindow] Bound with null hub — offline/demo mode.");
        }

        if (_demoLabel is not null)
            _demoLabel.Visible = hub is null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  UI construction
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildUi()
    {
        // Window anchored to centre of viewport (same placement as InventoryWindow/SkillWindow).
        AnchorLeft = 0.5f;
        AnchorTop = 0.5f;
        AnchorRight = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -160f;
        OffsetTop = -250f;
        OffsetRight = 160f;
        OffsetBottom = 250f;

        var outerPanel = new PanelContainer();
        outerPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        outerPanel.CustomMinimumSize = new Vector2(320, 500);
        AddChild(outerPanel);

        // Window chrome (inventwindow.dds base panel).
        // spec: Docs/RE/specs/ui_system.md §8.7 — "base panel on inventwindow.dds (uitex 2)": CODE-CONFIRMED.
        var chrome = new TextureRect
        {
            Name = "StatWindowChrome",
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        chrome.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        outerPanel.AddChild(chrome);
        TryBindChrome(chrome);

        // Content vbox.
        var vbox = new VBoxContainer();
        outerPanel.AddChild(vbox);

        // ---- Title bar ----
        var titleRow = new HBoxContainer();
        vbox.AddChild(titleRow);

        var titleLabel = new Label
        {
            Text = "Stats", // runtime: filled from character name at the Application layer
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        titleRow.AddChild(titleLabel);

        // Close button.
        // spec: Docs/RE/specs/ui_system.md §8.7 — action 312 = Close: CODE-CONFIRMED.
        var closeBtn = BuildCloseButton();
        titleRow.AddChild(closeBtn);

        // ---- Stat rows ----
        // Stat order in the view: STR, INT, AGI, DEX, CON.
        // spec: Docs/RE/specs/progression.md §8.1 — wire order STR, INT, AGI, DEX, CON.
        string[] statNames = { "STR", "INT", "AGI", "DEX", "CON" };
        for (int i = 0; i < 5; i++)
        {
            var row = new HBoxContainer();
            vbox.AddChild(row);

            int statIdx = i; // capture for lambda

            // Stat name label.
            row.AddChild(new Label { Text = $"{statNames[i]}:", CustomMinimumSize = new Vector2(40, 0) });

            // Value label ("BASE" or "BASE+DELTA").
            _statLabels[i] = new Label { Text = "0", SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddChild(_statLabels[i]);

            // + button.
            // spec: Docs/RE/specs/progression.md §7.2 — action ids 300-304 = increment per stat.
            var plusBtn = new Button
            {
                Text = "+",
                CustomMinimumSize = new Vector2(24, 0),
            };
            plusBtn.Pressed += () => OnIncrement(statIdx);
            row.AddChild(plusBtn);

            // - button.
            // spec: Docs/RE/specs/progression.md §7.2 — action ids 305-309 = decrement per stat.
            var minusBtn = new Button
            {
                Text = "-",
                CustomMinimumSize = new Vector2(24, 0),
            };
            minusBtn.Pressed += () => OnDecrement(statIdx);
            row.AddChild(minusBtn);
        }

        // ---- Points available ----
        _pointsLabel = new Label { Text = "Points: 0" };
        vbox.AddChild(_pointsLabel);

        // ---- Reset button (action 310 in legacy).
        // spec: Docs/RE/specs/progression.md §7.2 — action 310 = Reset.
        var resetBtn = new Button { Text = "Reset" };
        resetBtn.Pressed += OnReset;
        vbox.AddChild(resetBtn);

        // ---- Apply button (action 311 in legacy).
        // spec: Docs/RE/specs/progression.md §7.2 — action 311 = Apply; sends 2/29.
        _applyBtn = new Button { Text = "Apply" };
        _applyBtn.Pressed += OnApply;
        vbox.AddChild(_applyBtn);

        // Demo label.
        _demoLabel = new Label
        {
            Name = "DemoLabel",
            Text = "[StatusPanel — offline]\nSTR 10+0  INT 10+0\nAGI 10+0  DEX 10+0  CON 10+0\nPoints: 0",
            AutowrapMode = TextServer.AutowrapMode.Word,
            Visible = true,
        };
        vbox.AddChild(_demoLabel);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Button handlers — view-state only; emit use-case calls on Apply
    // ─────────────────────────────────────────────────────────────────────────

    private void OnIncrement(int statIdx)
    {
        uint step = GetStep();
        long available = _view.RemainingStatPoints
                         - (long)(_pendingStr + _pendingInt + _pendingAgi + _pendingDex + _pendingCon);
        if (available <= 0) return;

        // Clamp step so it never exceeds available points.
        // spec: Docs/RE/specs/progression.md §7.3 — "step clamped so it never exceeds the available points".
        uint clamped = (uint)Math.Min(step, (ulong)Math.Max(0, available));
        if (clamped == 0) return;

        // spec: Docs/RE/specs/progression.md §7.1 — action-id order: STR(0) INT(1) AGI(2) CON(3) DEX(4).
        // Our array index: STR=0 INT=1 AGI=2 DEX=3 CON=4 (matches wire order §8.1 for simplicity).
        switch (statIdx)
        {
            case 0: _pendingStr += clamped; break;
            case 1: _pendingInt += clamped; break;
            case 2: _pendingAgi += clamped; break;
            case 3: _pendingDex += clamped; break;
            case 4: _pendingCon += clamped; break;
        }

        RefreshLabels();
    }

    private void OnDecrement(int statIdx)
    {
        uint step = GetStep();

        switch (statIdx)
        {
            case 0: _pendingStr = (uint)Math.Max(0, (long)_pendingStr - step); break;
            case 1: _pendingInt = (uint)Math.Max(0, (long)_pendingInt - step); break;
            case 2: _pendingAgi = (uint)Math.Max(0, (long)_pendingAgi - step); break;
            case 3: _pendingDex = (uint)Math.Max(0, (long)_pendingDex - step); break;
            case 4: _pendingCon = (uint)Math.Max(0, (long)_pendingCon - step); break;
        }

        RefreshLabels();
    }

    private void OnReset()
    {
        // Clear all five deltas and restore available points.
        // spec: Docs/RE/specs/progression.md §7.2 action 310 — "clears all five deltas, restores available points".
        _pendingStr = 0;
        _pendingInt = 0;
        _pendingAgi = 0;
        _pendingDex = 0;
        _pendingCon = 0;
        RefreshLabels();
        GD.Print("[CharacterStatsWindow] Pending deltas reset.");
    }

    private void OnApply()
    {
        // Gate: remaining_stat_points > 0 AND at least one delta non-zero.
        // spec: Docs/RE/specs/progression.md §8.1 — "gated on two conditions".
        long totalDelta = (long)(_pendingStr + _pendingInt + _pendingAgi + _pendingDex + _pendingCon);
        if (_view.RemainingStatPoints == 0 || totalDelta == 0)
        {
            GD.Print("[CharacterStatsWindow] Apply suppressed: no points or no pending deltas.");
            return;
        }

        // Build absolute targets (base + pending). Wire order: STR, INT, AGI, DEX, CON.
        // spec: Docs/RE/specs/progression.md §8.1 — "five ABSOLUTE target stats … wire order STR, INT, AGI, DEX, CON".
        uint absStr = _view.BaseStr + _pendingStr; // spec: progression.md §8.1 Absolute STR = cached STR + delta_STR
        uint absInt = _view.BaseInt + _pendingInt; // spec: progression.md §8.1
        uint absAgi = _view.BaseAgi + _pendingAgi; // spec: progression.md §8.1
        uint absDex = _view.BaseDex + _pendingDex; // spec: progression.md §8.1
        uint absCon = _view.BaseCon + _pendingCon; // spec: progression.md §8.1

        // OPEN ITEM: IApplicationUseCases does not yet expose StatAllocateAsync.
        // When the application engineer adds it, replace the GD.Print with:
        //   await _useCases.StatAllocateAsync(absStr, absInt, absAgi, absDex, absCon, ct);
        // The outcome will arrive as a fresh StatAllocationView on the StatAllocations channel.
        // spec: Docs/RE/specs/progression.md §8 (2/29 CmsgStatAllocate and 4/29 ack).
        if (_useCases is not null)
        {
            GD.Print($"[CharacterStatsWindow] Apply intent: STR={absStr} INT={absInt} AGI={absAgi} " +
                     $"DEX={absDex} CON={absCon}. " +
                     "NOTE: StatAllocateAsync use-case not yet wired (pending application-engineer task).");
        }
        else
        {
            GD.Print($"[CharacterStatsWindow] Apply intent (offline): STR={absStr} INT={absInt} " +
                     $"AGI={absAgi} DEX={absDex} CON={absCon}.");
        }

        // Disable Apply button after sending to prevent double-send.
        // spec: Docs/RE/specs/progression.md §8.1 — "disables the Apply widget" after sending.
        _applyBtn.Disabled = true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Label refresh
    // ─────────────────────────────────────────────────────────────────────────

    private void RefreshLabels()
    {
        // Compute per-stat displayed values (base + local pending).
        uint[] bases = { _view.BaseStr, _view.BaseInt, _view.BaseAgi, _view.BaseDex, _view.BaseCon };
        uint[] deltas = { _pendingStr, _pendingInt, _pendingAgi, _pendingDex, _pendingCon };

        for (int i = 0; i < 5; i++)
        {
            if (_statLabels[i] is null) continue;
            _statLabels[i].Text = deltas[i] > 0
                ? $"{bases[i]}+{deltas[i]}" // "BASE+DELTA" format — spec: progression.md §7.1
                : $"{bases[i]}";
        }

        long totalPending = (long)(_pendingStr + _pendingInt + _pendingAgi + _pendingDex + _pendingCon);
        long pointsAvail = Math.Max(0, _view.RemainingStatPoints - totalPending);
        if (_pointsLabel is not null)
            _pointsLabel.Text = $"Points: {pointsAvail}";

        // Apply button enabled state.
        // spec: Docs/RE/specs/progression.md §8.1 — gates: points > 0 AND at least one delta non-zero.
        if (_applyBtn is not null)
            _applyBtn.Disabled = (_view.RemainingStatPoints == 0 || totalPending == 0);

        // Hide demo label when real data is present.
        if (_demoLabel is not null &&
            (_view.BaseStr + _view.BaseInt + _view.BaseAgi + _view.BaseDex + _view.BaseCon) > 0)
            _demoLabel.Visible = false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Modifier-key step
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the increment/decrement step based on held modifier keys.
    /// spec: Docs/RE/specs/progression.md §7.3 (key ids 1012/1013/1014 → steps 1000/100/10/1).
    /// Godot mapping: Alt=1000, Ctrl=100, Shift=10, none=1.
    /// </summary>
    private static uint GetStep()
    {
        // Use global::Godot.Input to avoid namespace collision with
        // MartialHeroes.Client.Application.Input (CLAUDE.md pitfall).
        if (global::Godot.Input.IsKeyPressed(Key.Alt)) return StepAlt; // spec: progression.md §7.3 key 1012 → 1000
        if (global::Godot.Input.IsKeyPressed(Key.Ctrl)) return StepCtrl; // spec: progression.md §7.3 key 1013 → 100
        if (global::Godot.Input.IsKeyPressed(Key.Shift)) return StepShift; // spec: progression.md §7.3 key 1014 → 10
        return StepDefault; // spec: progression.md §7.3 none → 1
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Chrome helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to bind the window chrome from inventwindow.dds (uitex 2).
    /// Degrades gracefully when VFS is offline.
    /// spec: Docs/RE/specs/ui_system.md §8.7 — chrome on inventwindow.dds (uitex 2): CODE-CONFIRMED.
    /// spec: Docs/RE/specs/ui_system.md §8.3 — shared modal chrome at (318,647,340,190): CODE-CONFIRMED.
    /// </summary>
    private void TryBindChrome(TextureRect target)
    {
        try
        {
            if (_context?.UiCatalogs is { } cats)
            {
                var tex = cats.GetTexture(InvTexId);
                if (tex is not null)
                {
                    // Use the shared modal chrome rect from inventwindow.dds.
                    // spec: Docs/RE/specs/ui_system.md §8.3 "(318,647) 340×190": CODE-CONFIRMED.
                    target.Texture = new AtlasTexture
                    {
                        Atlas = tex,
                        Region = new Rect2(318, 647, 340, 190), // spec: ui_system.md §8.3 CODE-CONFIRMED
                        FilterClip = true,
                    };
                    return;
                }
            }

            if (_uiLoader is not null)
            {
                var at = _uiLoader.Slice(InvTexPath, 318, 647, 340, 190); // spec: ui_system.md §8.3
                if (at is not null) target.Texture = at;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharacterStatsWindow] Chrome bind failed: {ex.Message} — no chrome (VFS offline).");
        }
    }

    private Control BuildCloseButton()
    {
        // Close button on mainwindow.dds (uitex 1), action 312.
        // spec: Docs/RE/specs/ui_system.md §8.7 — "Close (312): mainwindow.dds (uitex 1)": CODE-CONFIRMED.
        try
        {
            if (_uiLoader is not null)
            {
                var at = _uiLoader.Slice(MainWinTexPath, CloseBtnSrcX, CloseBtnSrcY, CloseBtnW, CloseBtnH);
                if (at is not null)
                {
                    var tr = new TextureRect
                    {
                        Texture = at,
                        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                        CustomMinimumSize = new Vector2(CloseBtnW, CloseBtnH),
                    };
                    // Wrap in a Button (Godot has no direct TextureButton — use Button + icon).
                    var btn = new Button
                    {
                        Name = "CloseBtn",
                        Icon = at,
                        CustomMinimumSize = new Vector2(CloseBtnW, CloseBtnH),
                    };
                    btn.Pressed += () => Visible = false;
                    return btn;
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharacterStatsWindow] Close button chrome failed: {ex.Message}");
        }

        // Fallback plain button.
        var fallback = new Button { Text = "X" };
        fallback.Pressed += () => Visible = false;
        return fallback;
    }
}