// Screens/BootFlow.cs
//
// Top-level boot flow controller (scene state machine, layer 05 analogue of the legacy 9-state
// WinMain switch — spec: Docs/RE/specs/client_workflow.md §4).
//
// WHAT CHANGED vs. the prior stub:
//   1. WIRED TO ClientContext (UseCases + StateMachine + EventBus). Screens no longer bind to
//      nothing. The composition root is looked up once via GetNode<ClientContext>("/root/ClientContext").
//   2. RE-HOMED PIN + SERVER-LIST (CAMPAIGN 13, Lane 1 F1/F2/F5): the PIN modal (login sub-states
//      31/32) and the server-list (sub-states 34..41) are INTERNAL sub-views of the Login scene
//      (engine state 1), NOT top-level scenes. The LoginScreen owns the whole
//      connect→PIN→server-pick→submit chain and only then advances STRAIGHT to Loading (state 2).
//      BootFlow injects the sub-view factories (DEV prefill / server-list drainer / dev seed) and
//      routes LoginFlowCompleted → Loading. spec: frontend_scenes.md §1.5 / §11.3 / §11.4;
//      client_runtime.md §7.6 / §7.9.5.
//   3. CHAR-SELECT driven by CharacterListEvent (Application event bus, opcode 3/1).
//      The screen subscribes to the event bus; when CharacterListEvent arrives it calls
//      CharacterSelectScreen.ApplyCharacterList(). Hardcoded demo roster only used when no
//      event arrives within a short window (offline dev mode).
//      spec: Docs/RE/specs/frontend_scenes.md §3.1; login_flow.md §3.2.
//   4. FSM ADVANCEMENT: LoginAsync/SelectCharacterAsync are called via IApplicationUseCases.
//      The StateMachine is advanced in response to real events (or synthetic offline events).
//   5. WORLD-EXIT HOOK (F4): the World root (GameLoop) raises WorldExitRequested; BootFlow tears
//      down World.tscn and re-shows CharacterSelectScreen (engine state 5 → 4) or quits on logout
//      (5 → 6). The scene machine does not terminate at the world.
//      spec: Docs/RE/specs/client_runtime.md §7.5.1 / §7.5.3 / §7.9.5.
//   6. DEV OFFLINE REPLAY: enabled by `DEV_OFFLINE_FLOW=1` env var or `dev_offline_flow=1` in
//      client_dir.cfg. Seeds synthetic ServerEntry list and CharacterListEvent so the full
//      Login (incl. PIN + server-list sub-views) → CharSelect flow is walkable with NO server.
//
// FLOW (engine states in brackets):
//   boot_flow=world → skip menu, go straight to World.tscn (world-debug shortcut).
//   boot_flow=login (DEFAULT):
//     [1 LoginScreen — owns PIN(31/32) + ServerList(34..41) sub-views] → [2 LoadingScreen]
//       → [3 Opening] or directly → [4 CharacterSelectScreen] → [5 World.tscn]
//       → (leave-world) → [4 CharacterSelectScreen]  /  (logout) → quit.
//
// PASSIVE: this node is a coordinator only. It swaps screens and routes signals.
// Zero game logic: no stats math, no packet decoding, no equip/cooldown rules here.
//
// spec: Docs/RE/specs/client_runtime.md §7 (scene state machine 0..7 + sub-states).
// spec: Docs/RE/specs/frontend_scenes.md §1–§7 / §11 (login/server-select/PIN/char-select scenes).
// spec: Docs/RE/specs/login_flow.md §1–§4 (credential flow, PIN, server list).

using Godot;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.StateMachine;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Godot.Autoload;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// The boot-flow controller. Decides between the menu flow (login → server-select → PIN →
/// char-select → world) and the direct world boot, based on the <c>boot_flow=</c> key in
/// <c>client_dir.cfg</c>.
/// </summary>
public sealed partial class BootFlow : Node
{
    // -------------------------------------------------------------------------
    // Config keys
    // -------------------------------------------------------------------------

    private const string WorldScenePath = "res://Scenes/World.tscn";
    private const string BootFlowKey = "boot_flow";
    private const string ConfigResPath = "res://client_dir.cfg";

    /// <summary>
    /// Client-dir.cfg key enabling the dev-only offline flow replay.
    /// When set to "1" / "true" the flow is seeded with synthetic data so it is walkable
    /// with NO server.  DEV ONLY — never exposes real network logic.
    /// </summary>
    private const string DevOfflineKey = "dev_offline_flow";

    // -------------------------------------------------------------------------
    // Application surface (resolved once on _Ready)
    // -------------------------------------------------------------------------

    private ClientContext? _ctx;
    private IApplicationUseCases? _useCases;
    private ClientStateMachine? _stateMachine;

    // -------------------------------------------------------------------------
    // UI tree
    // -------------------------------------------------------------------------

    private CanvasLayer? _uiLayer;
    private ScreenHost? _host;
    private UiAssetLoader? _sharedAssets;

    // The instanced World scene (engine state 5). Held so the world-exit hook can tear it down on
    // leave-world / logout (state 5 → 4 / 6). spec: Docs/RE/specs/client_runtime.md §7.5.1 / §7.5.3.
    private Node? _world;

    // Front-end audio + cursor manager.
    // spec: Docs/RE/specs/sound.md — BGM 920100200, UI click 861010101. CODE-CONFIRMED.
    // spec: Docs/RE/specs/intro_sequence.md §4 — intro stinger 910061000. CODE-CONFIRMED.
    // spec: Docs/RE/specs/frontend_scenes.md §11 — data/cursor/stand.dds cursor. CODE-CONFIRMED.
    private FrontEndAudio? _audio;

    // Credential stash: held across screen transitions (login → server-select → PIN → char-select).
    // Never domain state; purely view-session state so the flow can reassemble the TAB string.
    private string _account = "";
    private string _password = "";
    private int _selectedServerId = 1; // default if user does not pick

    // Subscription cancel token for the event-bus drain.
    private CancellationTokenSource? _eventBusCts;

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        GD.Print("===== [BootFlow] _Ready ENTERED =====");

        // Resolve the ClientContext autoload (composition root).
        try
        {
            _ctx = GetNode<ClientContext>("/root/ClientContext");
            _useCases = _ctx.UseCases;
            _stateMachine = _ctx.StateMachine;
            GD.Print("[BootFlow] ClientContext resolved — StateMachine and UseCases available.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[BootFlow] ClientContext not found: {ex.Message} — offline mode.");
        }

        string flow = ReadCfgKey(BootFlowKey, "login");
        GD.Print($"[BootFlow] boot_flow='{flow}'.");

        try
        {
            if (flow.Equals("world", StringComparison.OrdinalIgnoreCase))
            {
                EnterWorld();
            }
            else
            {
                StartMenuFlow();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[BootFlow] MenuFlow failed: {ex.Message} — falling back to world.");
            EnterWorld();
        }

        GD.Print("===== [BootFlow] _Ready COMPLETED =====");
    }

    public override void _ExitTree()
    {
        _eventBusCts?.Cancel();
        _eventBusCts?.Dispose();
        _eventBusCts = null;

        _sharedAssets?.Dispose();
        _sharedAssets = null;
    }

    // -------------------------------------------------------------------------
    // Menu flow
    // -------------------------------------------------------------------------

    private void StartMenuFlow()
    {
        BuildMenuScaffold();

        // Boot entry: the LOGIN scene is GameState 1 — the FIRST interactive scene. The Opening
        // intro is GameState 3 (POST-login): it plays AFTER login + server-select + the loading/SKIP
        // gate and immediately BEFORE char-select (GameState 4) — it is NOT a pre-login splash.
        // spec: Docs/RE/specs/intro_sequence.md §0/§0.1 ("post-login intro"; strict order 0→1→2→3→4).
        // DEV-ONLY: dev_skip_intro + dev_screen may target a specific scene directly for testing.
        if (TryDevScreenDispatch()) return;
        ShowLogin();
    }

    /// <summary>
    /// Builds the menu UI scaffold (shared assets + CanvasLayer + ScreenHost + front-end audio).
    /// Shared by the boot entry (<see cref="StartMenuFlow"/>) and the world re-entry
    /// (<see cref="OnWorldExitRequested"/> → char-select, engine state 5 → 4).
    /// </summary>
    private void BuildMenuScaffold()
    {
        _sharedAssets = UiAssetLoader.Open();

        _uiLayer = new CanvasLayer { Name = "MenuUiLayer", Layer = 10 };
        AddChild(_uiLayer);

        _host = new ScreenHost { Name = "ScreenHost" };
        _uiLayer.AddChild(_host);

        // Create the audio manager and add it to the scene so it initialises its players.
        // spec: sound.md — BGM 920100200, click SFX 861010101. CODE-CONFIRMED.
        // spec: intro_sequence.md §4 — intro stinger 910061000. CODE-CONFIRMED.
        // spec: frontend_scenes.md §11 — cursor stand.dds. CODE-CONFIRMED.
        _audio = new FrontEndAudio
        {
            Name = "FrontEndAudio",
        };
        AddChild(_audio);
    }

    // -----------------------------------------------------------------------
    // Boot-entry DEV screen dispatch (dev_skip_intro + dev_screen shortcuts)
    // -----------------------------------------------------------------------

    /// <summary>
    /// DEV-ONLY boot-entry shortcut: when <c>dev_skip_intro=1</c> is set in <c>client_dir.cfg</c>,
    /// jumps straight to a target scene (selected by <c>dev_screen=</c>) for screenshot/headless
    /// tests, bypassing the full Login→…→Opening path (the Opening's ~70 s dwell is too long for a
    /// test loop). Returns <see langword="true"/> when it handled the boot (so the caller must not
    /// also start the normal flow). DEV ONLY — never active in production.
    ///
    /// dev_screen= targets: login | pin | server | loading | opening | charselect | create.
    /// </summary>
    private bool TryDevScreenDispatch()
    {
        if (ReadCfgKey("dev_skip_intro", "0") is not ("1" or "true" or "yes"))
            return false;

        // No BGM forced here — login/server are BGM-absent; char-select owns 920100200 and starts it
        // itself. spec: Docs/RE/specs/sound.md — login BGM-absent. CONFIRMED CAMPAIGN 9.
        if (!IsDevOfflineMode())
        {
            GD.Print("[BootFlow] dev_skip_intro=1 → skipping intro, going straight to login.");
            ShowLogin();
            return true;
        }

        string devScreen = ReadCfgKey("dev_screen", "login").ToLowerInvariant();
        GD.Print($"[BootFlow] dev_skip_intro=1 + dev_offline_flow=1 + dev_screen={devScreen}.");
        switch (devScreen)
        {
            case "pin":
                // PIN is an in-login sub-view (engine state 1). Show login, then raise it directly
                // for the screenshot/oracle. spec: frontend_scenes.md §11.3; client_runtime.md §7.6.
                ShowLogin();
                Callable.From(DevRaisePinSubView).CallDeferred();
                break;
            case "server":
                // Server-list is an in-login sub-view (engine state 1). Show login, then raise it
                // directly. spec: frontend_scenes.md §11.4; client_runtime.md §7.6.
                ShowLogin();
                Callable.From(DevRaiseServerSubView).CallDeferred();
                break;
            case "loading":
                // Dev shortcut: jump directly to the loading screen (engine state 2).
                // spec: frontend_scenes.md §2L. CODE-CONFIRMED entry point.
                ShowLoadingScreen();
                break;
            case "opening":
                // Dev shortcut: preview the post-login Opening intro (GameState 3) in isolation.
                // spec: Docs/RE/specs/intro_sequence.md §0.1.
                ShowOpeningIntro();
                break;
            case "charselect":
                ShowCharacterSelect(pin: "");
                break;
            case "create":
                // DEV: jump to char-select, then open the create form (for screenshot/oracle).
                ShowCharacterSelect(pin: "");
                Callable.From(DevOpenCreateForm).CallDeferred();
                break;
            default: // "login"
                ShowLogin();
                break;
        }

        return true;
    }

    // -----------------------------------------------------------------------
    // Step 1: Login screen
    // -----------------------------------------------------------------------

    private void ShowLogin()
    {
        var login = new LoginScreen { Name = "LoginScreen", SharedAssets = _sharedAssets, Audio = _audio };

        // DEV/TEST: pre-fill the credential fields with the hardcoded dev account so the
        // maintainer can walk the flow without typing. Guarded by dev-offline mode; never ships.
        if (IsDevOfflineMode())
        {
            login.DevPrefillId = DevAccountId();
            login.DevPrefillPw = DevAccountPw();
        }

        // LoginAccepted: OK button passed local validation (ID ≥ 4, PW ≥ 1) — stage credentials.
        login.LoginAccepted += OnLoginAccepted;
        login.QuitRequested += OnQuitRequested;

        // RE-HOME (F1/F2/F5): the server-list (sub-states 34..41) and the PIN modal (sub-states
        // 31/32) are INTERNAL tick sub-states of the Login scene (engine state 1) — NOT top-level
        // scenes. The Login scene owns the whole connect→PIN→server-pick→submit chain and only then
        // advances straight to Loading (engine state 2). BootFlow injects the sub-view factories
        // (so DEV prefill / drainer / dev seed stay here) and routes LoginFlowCompleted → Loading.
        // spec: Docs/RE/specs/frontend_scenes.md §1.5 / §11.3 / §11.4; client_runtime.md §7.6 / §7.9.5.
        login.PinFactory = CreateInLoginPin;
        login.ServerSelectFactory = CreateInLoginServerSelect;
        login.LoginFlowCompleted += OnLoginFlowCompleted;

        _host!.SetScreen(login);
        GD.Print("[BootFlow] Showing LoginScreen (owns the in-login PIN + server-list sub-views). " +
                 "spec: client_runtime.md §7.6.");
    }

    private void OnLoginAccepted(string account, string password)
    {
        // UI click SFX is handled centrally by AudioService.OnButtonActionFired (NodeAdded
        // subscription on StateButton.ActionFired). spec §3.8.1 (de-duplicate click path).

        // Store account + password and stage them in the Application layer. The Login scene now
        // drives the PIN/server sub-states internally; staging happens here on credential-validate.
        // spec: Docs/RE/specs/login_flow.md §4.2 — password forwarded with account to LoginAsync.
        // spec: Docs/RE/specs/client_workflow.md §5.1.2 — login credential staging.
        _account = account;
        _password = password;
        if (_useCases is not null)
            _ = _useCases.LoginAsync(_account, password: _password, cancellationToken: CancellationToken.None);

        GD.Print($"[BootFlow] Login accepted (account='{account}'); credentials staged. " +
                 "In-login PIN/server sub-states run inside engine state 1. spec: client_runtime.md §7.6.");
    }

    // -----------------------------------------------------------------------
    // In-login sub-view factories (engine state 1).
    // The PIN (sub-state 31/32) and server-list (sub-states 34..41) are children of the Login scene
    // (LoginScreen owns them). BootFlow only constructs each instance and attaches the DEV-only
    // prefill / the server-list drainer / the DEV seed; the Login scene drives the structural flow.
    // spec: Docs/RE/specs/frontend_scenes.md §1.5 / §11.3 / §11.4; client_runtime.md §7.6.
    // -----------------------------------------------------------------------

    private PinModal CreateInLoginPin()
    {
        var pin = new PinModal
        {
            Name = "PinModal",
            SharedAssets = _sharedAssets,
            HostInReferenceSpace = true, // child of the 1024×768 Login scene — fill reference rect.
        };

        // DEV/TEST: pre-enter the hardcoded dev PIN (shown masked) so OK can be clicked directly.
        // Guarded by dev-offline mode; never ships.
        if (IsDevOfflineMode())
            pin.DevPrefillPin = DevAccountPin();

        GD.Print("[BootFlow] In-login PIN sub-view created (engine state 1, sub-state 31→32). spec §11.3.");
        return pin;
    }

    private ServerSelectScreen CreateInLoginServerSelect()
    {
        var serverSelect = new ServerSelectScreen
        {
            Name = "ServerSelectScreen",
            SharedAssets = _sharedAssets,
        };

        // The server list is driven by the real lobby server-list response (port 10000) via the
        // ServerListDrainer (mirrors CharListEventDrainer). Offline there is no response; the DEV
        // seed is applied after wiring the drainer so it can be replaced by a real event.
        // spec: Docs/RE/specs/login_flow.md §1 step 2 / §2.1 — ServerListReceivedEvent on IClientEventBus.
        if (_ctx is not null)
        {
            var serverDrainer = new ServerListDrainer();
            serverDrainer.Bind(serverSelect, _ctx.EventBus);
            serverDrainer.Name = "ServerListDrainer";
            // Parent the drainer to the server-select sub-view so it is freed with it.
            serverSelect.AddChild(serverDrainer);
        }

        // DEV-ONLY: seed a couple of servers so the parchment-scroll layout actually RENDERS for
        // visual validation (guarded by dev-offline mode; NEVER shipped — real flow uses the lobby).
        // spec: Docs/RE/specs/login_flow.md §2.
        if (IsDevOfflineMode())
            serverSelect.SetServers(DevServerList());

        GD.Print("[BootFlow] In-login server-list sub-view created (engine state 1, sub-states 34..41). spec §11.4.");
        return serverSelect;
    }

    private void OnLoginFlowCompleted(int serverId, string pin)
    {
        // The Login scene completed the WHOLE connect→PIN→server-pick→submit chain INSIDE engine
        // state 1. The flow now advances straight to the Loading screen (engine state 2) — there is
        // no separate top-level server-select scene step. The PIN was collected internally; it is
        // not re-prompted at char-select.
        // spec: Docs/RE/specs/client_runtime.md §7.5.1 (1 → 2) / §7.9.5; frontend_scenes.md §1.5.
        _selectedServerId = serverId;
        GD.Print($"[BootFlow] Login flow complete (server_id={serverId}, pin_len={pin.Length}) → " +
                 "Loading (engine state 1 → 2). spec: client_runtime.md §7.5.1 / §7.9.5.");
        ShowLoadingScreen();
    }

    // -----------------------------------------------------------------------
    // Loading screen — spec: frontend_scenes.md §2L. CODE-CONFIRMED.
    //   Engine state 2 (Loading): shown between server-select and char-select.
    //   Background: rand()%3 over loading.dds / loading06.dds / loading08.dds (V-crop 0..0.75).
    //   Progress bar: 223 × percent/100 fill, left-to-right, design rect [−499,−170]×[−363,−140].
    //   BGM: 920100100 looping (explicitly stopped before char-select, §3.8.1 fix contract).
    //   Advance: LoadingComplete signal (preload-done + 500 ms grace), NOT bar == 100%.
    // -----------------------------------------------------------------------

    private void ShowLoadingScreen()
    {
        var loading = new LoadingScreen
        {
            Name = "LoadingScreen",
            SharedAssets = _sharedAssets,
        };
        loading.LoadingComplete += OnLoadingComplete;
        _host!.SetScreen(loading);
        GD.Print("[BootFlow] Showing LoadingScreen (engine state 2 Loading). spec: frontend_scenes.md §2L.");
    }

    private void OnLoadingComplete()
    {
        GD.Print("[BootFlow] LoadingComplete received. spec: frontend_scenes.md §2L.3.");
        // The loading screen already stopped its BGM (§3.8.1 fix contract).
        // GameState 2 (Loading) is the [OPENNING] SKIP gate: SKIP≠0 → char-select (GameState 4)
        // directly; SKIP==0 → play the post-login Opening intro (GameState 3), which then transitions
        // into char-select. spec: Docs/RE/specs/intro_sequence.md §0.1 (case 2 → state 4 on SKIP,
        // else state 3 → state 4).
        if (IsOpeningSkipped())
        {
            GD.Print("[BootFlow] [OPENNING] SKIP set → char-select (skip Opening). spec: intro_sequence.md §0.1.");
            ShowCharacterSelect(pin: ""); // PIN was already collected before server select
        }
        else
        {
            ShowOpeningIntro();
        }
    }

    // -----------------------------------------------------------------------
    // Step 3: Opening intro (post-login, GameState 3)
    // Plays AFTER the loading/SKIP gate (GameState 2) and immediately BEFORE char-select
    // (GameState 4). The Opening is a POST-login intro, never a pre-login splash.
    // spec: Docs/RE/specs/intro_sequence.md §0/§0.1 (ordering 2 → 3 → 4); §3.1 (slideshow → state 4).
    // -----------------------------------------------------------------------

    private void ShowOpeningIntro()
    {
        var intro = new OpeningWindow
        {
            Name = "OpeningWindow",
            SharedAssets = _sharedAssets,
            Audio = _audio,
        };
        intro.IntroFinished += OnIntroFinished;
        _host!.SetScreen(intro);
        GD.Print("[BootFlow] Showing OpeningWindow (post-login intro, GameState 3). spec: intro_sequence.md §0.1.");
    }

    private void OnIntroFinished()
    {
        // The Opening (GameState 3) transitions into char-select (GameState 4) when its slideshow
        // finishes or the player skips. Char-select's constructor starts the front-end BGM 920100200.
        // spec: Docs/RE/specs/intro_sequence.md §0.1 / §3.1; sound.md (char-select BGM).
        GD.Print("[BootFlow] Opening intro finished → char-select. spec: intro_sequence.md §3.1.");
        ShowCharacterSelect(pin: "");
    }

    /// <summary>
    /// The Opening (GameState 3) SKIP gate, read at the GameState-2 (Loading) boundary: mirrors the
    /// legacy <c>[OPENNING] SKIP</c> INI flag the original reads in case 2. When set, the Opening is
    /// bypassed and the flow goes straight to char-select (GameState 4). Default 0 → play the Opening.
    /// spec: Docs/RE/specs/intro_sequence.md §0.1 (case 2 reads SKIP: ≠0 → state 4, else state 3).
    /// spec: Docs/RE/specs/frontend_scenes.md §1.0.0 / §1.0.5 — unified path+section+key with
    ///   OpeningWindow.PersistSkip() so the write and read round-trip correctly.
    /// </summary>
    private static bool IsOpeningSkipped()
    {
        // Read from the same ConfigFile location that OpeningWindow.PersistSkip() writes.
        // OpeningWindow writes int 1; we read it back and test != 0.
        // spec: Docs/RE/specs/frontend_scenes.md §1.0.0 [OPENNING] SKIP. CODE-CONFIRMED.
        var cfg = new ConfigFile();
        if (cfg.Load(OpeningWindow.SkipCfgPath) != Error.Ok) return false;
        int skip = (int)cfg.GetValue(OpeningWindow.SkipCfgSection, OpeningWindow.SkipCfgKey, 0);
        return skip != 0;
    }

    // -----------------------------------------------------------------------
    // Step 4: Character-select screen
    // -----------------------------------------------------------------------

    private void ShowCharacterSelect(string pin)
    {
        var select = new CharacterSelectScreen
        {
            Name = "CharacterSelectScreen",
            SharedAssets = _sharedAssets,
        };
        select.EnterGameRequested += OnEnterGameRequested;
        select.BackRequested += OnBackToLogin;

        _host!.SetScreen(select);
        // Front-end BGM starts HERE — login/server are BGM-absent; char-select owns 920100200.
        // spec: Docs/RE/specs/sound.md — char-select BGM cue. CONFIRMED CAMPAIGN 9.
        _audio?.PlayBgm();
        GD.Print("[BootFlow] Showing CharacterSelectScreen (awaiting CharacterListEvent or dev seed).");

        // Subscribe to the Application event bus so CharacterListEvent drives the roster.
        // spec: Docs/RE/specs/frontend_scenes.md §3.1 — "SmsgCharacterList (3/1) forces the select scene".
        // spec: Docs/RE/specs/login_flow.md §3.2. CODE-CONFIRMED.
        if (_ctx is not null)
        {
            StartEventBusDrain(select);
        }

        // The roster is driven by the real CharacterListEvent (opcode 3/1) via the event-bus drain.
        // DEV-ONLY: publish a character row through the legitimate event bus so the populated
        // char-select RENDERS for visual validation (the drain applies it next frame — correct timing).
        // Guarded by dev-offline mode; NEVER shipped — the real flow uses the 3/1 list.
        // spec: Docs/RE/specs/frontend_scenes.md §3.1.
        if (IsDevOfflineMode() && _ctx is not null)
            _ctx.EventBus.Publish(new CharacterListEvent(
                ServerId: 0, ChannelId: 0, Characters: DevCharacterList()));

        // Advance the FSM toward CharacterSelection.
        // spec: Docs/RE/specs/client_workflow.md §4 — Login → CharacterSelection on auth.
        _stateMachine?.OnAuthenticated();
    }

    /// <summary>
    /// Drains the Application event bus on the main thread (_Process cycle) looking for
    /// CharacterListEvent and routing it to the CharacterSelectScreen.
    ///
    /// The drain uses a single-shot subscription: once CharacterListEvent is seen it is applied
    /// and the subscription stays active (in case the server re-sends the list, e.g. after a
    /// create/delete).
    ///
    /// Threading contract: the event bus reader is only consumed here, on the main thread via
    /// polling in _Process. All Control mutation is therefore on the main thread.
    /// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — "drain Application channels on _Process".
    /// </summary>
    private void StartEventBusDrain(CharacterSelectScreen select)
    {
        _eventBusCts?.Cancel();
        _eventBusCts = new CancellationTokenSource();

        // Poll the bus in _Process via a helper node wired to the select screen.
        var drainer = new CharListEventDrainer();
        drainer.Bind(select, _ctx!.EventBus);
        drainer.Name = "CharListEventDrainer";
        AddChild(drainer);
    }

    private void OnEnterGameRequested(string characterName, int slotIndex)
    {
        // UI click SFX is handled centrally by AudioService.OnButtonActionFired — no call here.
        // spec: Docs/RE/specs/frontend_scenes.md §3.8.1 (de-duplicate click path).

        GD.Print($"[BootFlow] Enter game: character='{characterName}' slot={slotIndex}.");

        // Call the use case to advance the FSM and send the 1/9 enter-game request.
        // spec: Docs/RE/specs/frontend_scenes.md §7 — "send 1/9; cache 880B descriptor; state 5".
        // spec: MartialHeroes.Client.Application.UseCases.IApplicationUseCases.SelectCharacterAsync.
        if (_useCases is not null)
        {
            _ = _useCases.SelectCharacterAsync(slotIndex, CancellationToken.None);
        }

        // Advance FSM.
        _stateMachine?.OnCharacterSelected();

        // Remove the event bus drainer (no longer needed once we enter the world).
        Node? drainer = FindChild("CharListEventDrainer", owned: false);
        drainer?.QueueFree();

        TeardownMenu();
        EnterWorld();

        // DEV OFFLINE FLOW: after the World scene is instantiated, publish a synthetic
        // LocalPlayerSpawnedEvent + a ClientStateChangedEvent (World state) so GameLoop.DispatchEvent
        // can spawn the local player and the HUD can initialise. We defer this onto the next frame
        // via CallDeferred so GameLoop._Ready has time to complete its wiring before the events arrive.
        // spec: Docs/RE/specs/login_flow.md §3.5 / §5.3 (3/7 → LocalPlayerSpawnedEvent).
        // spec: Docs/RE/specs/frontend_scenes.md §7 — "enter-game ack → in-world".
        if (IsDevOfflineMode())
        {
            GD.Print("[BootFlow] DEV OFFLINE MODE — scheduling synthetic enter-game + spawn events.");
            CallDeferred(MethodName.SeedSyntheticEnterGame, characterName, slotIndex);
        }
    }

    /// <summary>
    /// Called via CallDeferred after <see cref="EnterWorld"/> so the World scene's GameLoop is
    /// initialised before the events arrive on the bus.
    ///
    /// Publishes:
    ///   1. A synthetic <see cref="ClientStateChangedEvent"/> (Login → World) — exercises the FSM path
    ///      that <c>3/5 SmsgEnterGameAck</c> would normally trigger. spec: opcodes.md (3/5 → World).
    ///   2. A synthetic <see cref="LocalPlayerSpawnedEvent"/> materializing the chosen character.
    ///      spec: Docs/RE/specs/login_flow.md §3.5 / §5.3 (3/7 SmsgCharSpawnResult → spawn).
    ///
    /// DEV ONLY. Data is synthetic (no game logic derived here — no stats, no formula, no position
    /// computation beyond a safe demo origin). The events travel through the legitimate Application
    /// event bus so all downstream view nodes react exactly as with a real server.
    /// </summary>
    private void SeedSyntheticEnterGame(string characterName, int slotIndex)
    {
        if (_ctx is null) return;

        // Synthetic 3/5 analogue: advance FSM to World state so HUD and InputRouter activate.
        // spec: Docs/RE/specs/client_workflow.md §4 — 3/5 transitions Loading → World.
        _ctx.EventBus.Publish(new ClientStateChangedEvent(
            Previous: Client.Application.Events.ClientState.CharacterSelection,
            Current: Client.Application.Events.ClientState.World));

        // Synthetic 3/7 LocalPlayerSpawnedEvent: materializes the local player with demo values.
        // Key uses the unassigned raw-id sentinel (same as the real handler when no 5/3 id is known).
        // spec: Docs/RE/specs/login_flow.md §3.5 — "key on UnassignedRawId until 5/3 supplies real id".
        // Position: world origin — safe spawn point with no terrain dependency. spec: WorldCoordinates.
        var key = new MartialHeroes.Client.Domain.Actors.ActorKey(
            MartialHeroes.Client.Domain.Actors.ActorKey.UnassignedRawId,
            MartialHeroes.Client.Domain.Actors.EntitySort.PlayerCharacter);

        _ctx.EventBus.Publish(new LocalPlayerSpawnedEvent(
            Key: key,
            SlotIndex: slotIndex,
            Name: characterName,
            Level: 25, // demo level — no formula
            Position: MartialHeroes.Shared.Kernel.Numerics.Vector3Fixed.FromFloat(0f, 0f, 0f),
            CurrentHp: 650u,
            MaxHp: 650u,
            ServerClass: 1)); // Musa class. spec: login_flow.md §4.1. CODE-CONFIRMED.

        GD.Print($"[BootFlow] DEV: published synthetic ClientStateChangedEvent(World) + " +
                 $"LocalPlayerSpawnedEvent(name='{characterName}', slot={slotIndex}). " +
                 "spec: login_flow.md §3.5 / §5.3 / client_workflow.md §4.");
    }

    private void OnBackToLogin()
    {
        // Remove event bus drainers if present (char-list and server-list).
        Node? charDrainer = FindChild("CharListEventDrainer", owned: false);
        charDrainer?.QueueFree();
        Node? serverDrainer = FindChild("ServerListDrainer", owned: false);
        serverDrainer?.QueueFree();

        GD.Print("[BootFlow] Back to login.");
        ShowLogin();
    }

    private void OnQuitRequested()
    {
        GD.Print("[BootFlow] Quit requested.");
        GetTree().Quit();
    }

    /// <summary>DEV-ONLY: opens the create form on the live char-select screen (dev_screen=create).</summary>
    private void DevOpenCreateForm()
    {
        if (_host?.FindChild("CharacterSelectScreen", recursive: true, owned: false) is CharacterSelectScreen sel)
            sel.DevShowCreateForm();
        else
            GD.PrintErr("[BootFlow] DevOpenCreateForm: CharacterSelectScreen not found.");
    }

    /// <summary>DEV-ONLY: raises the in-login PIN sub-view on the live LoginScreen (dev_screen=pin).</summary>
    private void DevRaisePinSubView()
    {
        if (_host?.FindChild("LoginScreen", recursive: true, owned: false) is LoginScreen login)
            login.DevShowPinSubView();
        else
            GD.PrintErr("[BootFlow] DevRaisePinSubView: LoginScreen not found.");
    }

    /// <summary>DEV-ONLY: raises the in-login server-list sub-view on the live LoginScreen (dev_screen=server).</summary>
    private void DevRaiseServerSubView()
    {
        if (_host?.FindChild("LoginScreen", recursive: true, owned: false) is LoginScreen login)
            login.DevShowServerSubView();
        else
            GD.PrintErr("[BootFlow] DevRaiseServerSubView: LoginScreen not found.");
    }

    // -----------------------------------------------------------------------
    // World boot
    // -----------------------------------------------------------------------

    private void TeardownMenu()
    {
        // Stop the front-end BGM when leaving the menu flow (entering the world).
        // spec: sound.md — BGM 920100200 stops on world enter. CODE-CONFIRMED.
        _audio?.StopBgm();

        if (_uiLayer is not null && IsInstanceValid(_uiLayer))
        {
            _uiLayer.QueueFree();
            _uiLayer = null;
            _host = null;
        }
    }

    private void EnterWorld()
    {
        var packed = GD.Load<PackedScene>(WorldScenePath);
        if (packed is null)
        {
            GD.PrintErr($"[BootFlow] Could not load '{WorldScenePath}'.");
            return;
        }

        Node world = packed.Instantiate();
        AddChild(world);
        _world = world;

        // World-exit hook (engine state 5 → 4 / 6). The World root (GameLoop) raises
        // WorldExitRequested when the player leaves the world; BootFlow tears down World.tscn and
        // re-enters the front-end. The master scene machine does not terminate at the world.
        // spec: Docs/RE/specs/client_runtime.md §7.5.1 (5 → 4 natural return) / §7.5.3 (logout → 6) /
        //       §7.9.5 ("state 5 pre-sets next = 4; world-leave sets 6").
        if (world is global::MartialHeroes.Client.Godot.World.GameLoop gameLoop)
            gameLoop.WorldExitRequested += OnWorldExitRequested;
        else
            GD.PrintErr("[BootFlow] World root is not a GameLoop — world-exit hook not wired.");

        GD.Print("[BootFlow] World scene instanced + world-exit hook wired. spec: client_runtime.md §7.5.1.");
    }

    /// <summary>
    /// Handles the world-exit signal raised by the World scene (engine state 5).
    /// <paramref name="logout"/> selects the recovered exit transition:
    ///   false = leave-world → tear down the world and re-show CharacterSelectScreen (state 5 → 4);
    ///   true  = logout → quit the client (state 5 → 6).
    /// spec: Docs/RE/specs/client_runtime.md §7.5.1 / §7.5.3 / §7.9.5.
    /// </summary>
    private void OnWorldExitRequested(bool logout)
    {
        GD.Print($"[BootFlow] WorldExitRequested(logout={logout}). spec: client_runtime.md §7.5.1/§7.5.3.");

        TeardownWorld();

        if (logout)
        {
            // Logout → engine state 6 (quit). spec: client_runtime.md §7.5.3.
            GD.Print("[BootFlow] Logout → quit. spec: client_runtime.md §7.5.3 (state 5 → 6).");
            GetTree().Quit();
            return;
        }

        // Leave-world → re-enter char-select (engine state 4). The original re-enters select rather
        // than re-running login: rebuild the menu UI scaffold and go straight to char-select.
        // spec: client_runtime.md §7.5.1 (5 → 4 natural return) / §7.9.5.
        GD.Print("[BootFlow] Leave-world → re-show CharacterSelectScreen. spec: client_runtime.md §7.5.1 (5 → 4).");
        BuildMenuScaffold();
        ShowCharacterSelect(pin: "");
    }

    /// <summary>Tears down the instanced World scene (state-4/6 re-entry).</summary>
    private void TeardownWorld()
    {
        if (_world is not null && IsInstanceValid(_world))
        {
            if (_world is global::MartialHeroes.Client.Godot.World.GameLoop gameLoop)
                gameLoop.WorldExitRequested -= OnWorldExitRequested;
            _world.QueueFree();
        }

        _world = null;
    }

    // -------------------------------------------------------------------------
    // Dev/test hardcoded credentials (guarded by dev-offline mode; NEVER ship).
    // Lets the maintainer walk Login → PIN → ServerSelect → CharSelect without typing.
    // Overridable via client_dir.cfg keys dev_account_id / dev_account_pw / dev_account_pin.
    // DEV-ONLY conveniences — no game logic; applied only when IsDevOfflineMode() is true.
    // -------------------------------------------------------------------------

    private static string DevAccountId() => ReadCfgKey("dev_account_id", "xwdvg26");
    private static string DevAccountPw() => ReadCfgKey("dev_account_pw", "crfgb727*");
    private static string DevAccountPin() => ReadCfgKey("dev_account_pin", "1472");

    // DEV-ONLY validation seeds — populate the server list + the character row offline so the
    // maintainer can SEE the real rendering (offline-empty leaves every populated scene blank and
    // impossible to judge). Guarded by IsDevOfflineMode(); NEVER shipped; carry no game logic — the
    // real flow uses the lobby server-list + the 3/1 CharacterList from a connected server.
    private static IReadOnlyList<ServerEntry> DevServerList() =>
    [
        new ServerEntry(ServerId: 1, DisplayName: "무신", StatusCode: 0, Population: 120, Flag: 1),
        new ServerEntry(ServerId: 2, DisplayName: "천마", StatusCode: 0, Population: 640, Flag: 1),
    ];

    private static System.Collections.Immutable.ImmutableArray<CharacterListSlot> DevCharacterList() =>
        System.Collections.Immutable.ImmutableArray.Create(
            new CharacterListSlot(SlotIndex: 0, Name: "무사", Level: 25, ServerClass: 1, CurrentHp: 650),
            new CharacterListSlot(SlotIndex: 1, Name: "격사", Level: 32, ServerClass: 3, CurrentHp: 520),
            new CharacterListSlot(SlotIndex: 2, Name: "도사", Level: 18, ServerClass: 2, CurrentHp: 480),
            new CharacterListSlot(SlotIndex: 3, Name: "@BLANK@", Level: 0, ServerClass: 0, CurrentHp: 0),
            new CharacterListSlot(SlotIndex: 4, Name: "@BLANK@", Level: 0, ServerClass: 0, CurrentHp: 0));

    // -------------------------------------------------------------------------
    // Dev-offline-mode detection
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true when the dev-offline-flow replay is enabled.
    /// Controlled by the environment variable <c>DEV_OFFLINE_FLOW=1</c>
    /// or the <c>dev_offline_flow=1</c> key in <c>client_dir.cfg</c>.
    /// DEV ONLY — never active in production builds.
    /// </summary>
    private static bool IsDevOfflineMode()
    {
        // Environment variable override — fastest to check.
        string? envVal = System.Environment.GetEnvironmentVariable("DEV_OFFLINE_FLOW");
        if (envVal is "1" or "true" or "yes")
        {
            GD.Print("[BootFlow] DEV_OFFLINE_FLOW env var is set → offline replay active.");
            return true;
        }

        // Config file key.
        string val = ReadCfgKey(DevOfflineKey, "0");
        bool enabled = val is "1" or "true" or "yes";
        if (enabled)
            GD.Print("[BootFlow] dev_offline_flow=1 in client_dir.cfg → offline replay active.");
        return enabled;
    }

    // -------------------------------------------------------------------------
    // Config read helper
    // -------------------------------------------------------------------------

    private static string ReadCfgKey(string key, string defaultValue)
    {
        string? path = ResolveCfgPath();
        if (path is null) return defaultValue;

        try
        {
            foreach (string rawLine in File.ReadLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                int eq = line.IndexOf('=');
                if (eq < 0) continue;

                string k = line[..eq].Trim();
                string v = line[(eq + 1)..].Trim();
                if (k.Equals(key, StringComparison.OrdinalIgnoreCase) && v.Length > 0)
                    return v;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[BootFlow] ReadCfgKey('{key}'): {ex.Message}");
        }

        return defaultValue;
    }

    private static string? ResolveCfgPath()
    {
        try
        {
            string abs = ProjectSettings.GlobalizePath(ConfigResPath);
            return File.Exists(abs) ? abs : null;
        }
        catch
        {
            return File.Exists("client_dir.cfg") ? "client_dir.cfg" : null;
        }
    }
}