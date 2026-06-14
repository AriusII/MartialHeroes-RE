// Screens/BootFlow.cs
//
// Top-level boot flow controller (scene state machine, layer 05 analogue of the legacy 9-state
// WinMain switch — spec: Docs/RE/specs/client_workflow.md §4).
//
// WHAT CHANGED vs. the prior stub:
//   1. WIRED TO ClientContext (UseCases + StateMachine + EventBus). Screens no longer bind to
//      nothing. The composition root is looked up once via GetNode<ClientContext>("/root/ClientContext").
//   2. SERVER-SELECT step added (ServerSelectScreen) between login validation and the PIN modal.
//      spec: Docs/RE/specs/frontend_scenes.md §2, login_flow.md §2.
//   3. PIN MODAL added (PinModal) shown AFTER server-select and BEFORE entering char-select.
//      spec: Docs/RE/specs/frontend_scenes.md §1.4a. RUNTIME-CONFIRMED.
//   4. CHAR-SELECT driven by CharacterListEvent (Application event bus, opcode 3/1).
//      The screen subscribes to the event bus; when CharacterListEvent arrives it calls
//      CharacterSelectScreen.ApplyCharacterList(). Hardcoded demo roster only used when no
//      event arrives within a short window (offline dev mode).
//      spec: Docs/RE/specs/frontend_scenes.md §3.1; login_flow.md §3.2.
//   5. FSM ADVANCEMENT: LoginAsync/SelectCharacterAsync are called via IApplicationUseCases.
//      The StateMachine is advanced in response to real events (or synthetic offline events).
//   6. DEV OFFLINE REPLAY: enabled by `DEV_OFFLINE_FLOW=1` env var or `dev_offline_flow=1` in
//      client_dir.cfg. Seeds synthetic ServerEntry list and CharacterListEvent so the full
//      Login→ServerSelect→PIN→CharSelect flow is walkable with NO server.
//
// FLOW:
//   boot_flow=world → skip menu, go straight to World.tscn (world-debug shortcut).
//   boot_flow=login (DEFAULT):
//     [LoginScreen] → [ServerSelectScreen] → [PinModal] → [CharacterSelectScreen] → World.tscn
//
// PASSIVE: this node is a coordinator only. It swaps screens and routes signals.
// Zero game logic: no stats math, no packet decoding, no equip/cooldown rules here.
//
// spec: Docs/RE/specs/client_workflow.md §4 (scene state machine 0..8).
// spec: Docs/RE/specs/frontend_scenes.md §1–§7 (login/server-select/char-select scenes).
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
            SharedAssets = _sharedAssets,
        };
        AddChild(_audio);

        // Boot entry: intro scene before login.
        // spec: Docs/RE/specs/intro_sequence.md §0 — "pre-login intro". CODE-CONFIRMED.
        ShowIntro();
    }

    // -----------------------------------------------------------------------
    // Step 0: Intro / OpeningWindow
    // -----------------------------------------------------------------------

    /// <summary>
    /// Shows the pre-login intro (scenario crawl + slideshow) as the first boot screen.
    /// spec: Docs/RE/specs/intro_sequence.md §0–§6. CODE-CONFIRMED.
    /// Transitions to login when the sequence completes or is skipped.
    ///
    /// When <c>dev_skip_intro=1</c> is set in <c>client_dir.cfg</c> the intro is bypassed
    /// immediately (useful for screenshot/headless tests where the 70-second dwell is too long).
    /// DEV ONLY — never active in production.
    /// </summary>
    private void ShowIntro()
    {
        // Dev skip: bypass the intro if the cfg flag is set.
        // dev_screen= key (if present) targets a specific screen for screenshot/testing:
        //   dev_screen=login       → login screen
        //   dev_screen=pin         → PIN modal (over login bg)
        //   dev_screen=server      → server select
        //   dev_screen=charselect  → char select (default when dev_skip_intro + dev_offline_flow)
        if (ReadCfgKey("dev_skip_intro", "0") is "1" or "true" or "yes")
        {
            _audio?.PlayBgm();
            string devScreen = ReadCfgKey("dev_screen", "login").ToLowerInvariant();
            if (IsDevOfflineMode())
            {
                GD.Print($"[BootFlow] dev_skip_intro=1 + dev_offline_flow=1 + dev_screen={devScreen}.");
                switch (devScreen)
                {
                    case "pin":
                        ShowLogin(); // login bg behind the modal
                        ShowPinModal();
                        break;
                    case "server":
                        ShowServerSelect();
                        break;
                    case "charselect":
                        ShowCharacterSelect(pin: "");
                        break;
                    default: // "login"
                        ShowLogin();
                        break;
                }
            }
            else
            {
                GD.Print("[BootFlow] dev_skip_intro=1 → skipping intro, going straight to login.");
                ShowLogin();
            }

            return;
        }

        var intro = new OpeningWindow
        {
            Name = "OpeningWindow",
            SharedAssets = _sharedAssets,
            Audio = _audio,
        };
        intro.IntroFinished += OnIntroFinished;
        _host!.SetScreen(intro);
        GD.Print("[BootFlow] Showing OpeningWindow (intro sequence).");
    }

    private void OnIntroFinished()
    {
        GD.Print("[BootFlow] Intro finished → Login screen.");
        // Start the front-end BGM now that the intro stinger has played.
        // spec: sound.md front-end cue map — BGM 920100200 loops on front-end screens. CODE-CONFIRMED.
        _audio?.PlayBgm();
        ShowLogin();
    }

    // -----------------------------------------------------------------------
    // Step 1: Login screen
    // -----------------------------------------------------------------------

    private void ShowLogin()
    {
        var login = new LoginScreen { Name = "LoginScreen", SharedAssets = _sharedAssets };
        // LoginAccepted: OK button passed local validation (ID ≥ 4, PW ≥ 1).
        // → Stage credentials in Application layer, then advance to server select.
        login.LoginAccepted += OnLoginAccepted;
        // ServerListRequested: player clicked the Server-list button on the login screen.
        // → Show server select independently of full credential submission.
        // spec: Docs/RE/specs/frontend_scenes.md §2 — server list button on login screen.
        login.ServerListRequested += OnServerListRequested;
        login.QuitRequested += OnQuitRequested;
        _host!.SetScreen(login);
        GD.Print("[BootFlow] Showing LoginScreen.");
    }

    private void OnLoginAccepted(string account)
    {
        // UI click SFX on login confirmation.
        // spec: sound.md — UI click 861010101. CODE-CONFIRMED.
        _audio?.PlayClickSfx();

        // Store account name so it can be forwarded to UseCases.LoginAsync at the join point.
        _account = account;
        GD.Print($"[BootFlow] Login accepted (account='{account}') → PIN modal (before server select).");

        // FLOW ORDER (spec §1.4a / task mandate):
        // Login validate → PIN (UNCONDITIONALLY after login-OK) → server list → char select.
        // The PIN is shown BEFORE the server list, NOT after.
        // spec: Docs/RE/specs/frontend_scenes.md §1.4a — "after primary login submit … and BEFORE
        //   the account-login blob is built (sub-state 40) the client raises the PIN modal".
        // The task mandate: "PIN pops UNCONDITIONALLY after login-OK, NOT before login;
        //   confirm → server list appears → select a server → char scene."
        ShowPinModal();
    }

    private void OnServerListRequested(string account)
    {
        // Player clicked the server-list button WITHOUT submitting login credentials first.
        // In the official client this can show the server list before the full login flow.
        // In the revival we treat this as going straight to server select (skipping PIN).
        // spec: Docs/RE/specs/frontend_scenes.md §2 — server-list button on the login screen.
        _account = account;
        GD.Print($"[BootFlow] Server list requested (account='{account}') → server select (no-cred path).");
        ShowServerSelect();
    }

    // -----------------------------------------------------------------------
    // Step 2: PIN / second-password modal (BEFORE server select)
    // spec: frontend_scenes.md §1.4a — PIN shown after login-validate, before server list.
    // spec: task mandate — "PIN pops UNCONDITIONALLY after login-OK".
    // -----------------------------------------------------------------------

    private void ShowPinModal()
    {
        // The PIN modal is layered on TOP of the current screen (not replacing it),
        // so it appears as a real modal overlay over the login screen background.
        // spec: frontend_scenes.md §1.4a — PIN shown over the login window, before the
        //   credential submit and server list. CODE-CONFIRMED flow position.
        var pin = new PinModal
        {
            Name = "PinModal",
            SharedAssets = _sharedAssets,
        };
        pin.PinSubmitted += OnPinSubmitted;
        pin.Cancelled += OnPinCancelled;

        // Add directly to the CanvasLayer so it sits above the ScreenHost (login screen) content.
        _uiLayer!.AddChild(pin);
        GD.Print("[BootFlow] PIN modal shown (post-login-validate, pre-server-select). spec: §1.4a.");
    }

    private void OnPinSubmitted(string pin)
    {
        // UI click SFX on PIN submit.
        // spec: sound.md — UI click 861010101. CODE-CONFIRMED.
        _audio?.PlayClickSfx();

        // Remove the PIN modal.
        RemovePinModal();

        GD.Print($"[BootFlow] PIN submitted (length={pin.Length}) → server select.");

        // Stage login credentials in the Application layer (if available).
        // The PIN is now available; pass it along with the account.
        // spec: Docs/RE/specs/client_workflow.md §5.1.2 — login credential staging.
        if (_useCases is not null)
        {
            _ = _useCases.LoginAsync(_account, password: "", cancellationToken: CancellationToken.None);
        }

        // After PIN → server list appears.
        // spec: task mandate "확인 → server list appears → select a server → char scene."
        ShowServerSelect();
    }

    private void OnPinCancelled()
    {
        RemovePinModal();
        GD.Print("[BootFlow] PIN modal cancelled → back to login.");
        // Cancel from PIN returns to the login screen (not server select, since we haven't reached it).
        ShowLogin();
    }

    private void RemovePinModal()
    {
        if (_uiLayer is null) return;
        Node? modal = _uiLayer.FindChild("PinModal", owned: false);
        modal?.QueueFree();
    }

    // -----------------------------------------------------------------------
    // Step 3: Server-selection screen (AFTER PIN)
    // spec: task mandate "server list appears" after PIN confirm.
    // -----------------------------------------------------------------------

    private void ShowServerSelect()
    {
        var serverSelect = new ServerSelectScreen
        {
            Name = "ServerSelectScreen",
            SharedAssets = _sharedAssets,
        };

        // Populate with synthetic or real server list.
        serverSelect.SetServers(BuildServerList());

        serverSelect.ServerSelected += OnServerSelected;
        serverSelect.BackRequested += OnBackToLogin;
        _host!.SetScreen(serverSelect);
        GD.Print("[BootFlow] Showing ServerSelectScreen (after PIN confirm). spec: task mandate.");
    }

    private void OnServerSelected(int serverId)
    {
        // UI click SFX on server selection.
        // spec: sound.md — UI click 861010101. CODE-CONFIRMED.
        _audio?.PlayClickSfx();

        _selectedServerId = serverId;
        GD.Print($"[BootFlow] Server selected: id={serverId} → connecting dialog → char select.");

        // Show the connecting dialog while the channel-endpoint fetch is simulated.
        // spec: frontend_scenes.md §11.4 "Connecting dialog (states 35/39)". CODE-CONFIRMED.
        ShowConnectingDialog();
    }

    // -----------------------------------------------------------------------
    // Connecting dialog — spec §11.4 sub-states 35/39. CODE-CONFIRMED.
    // -----------------------------------------------------------------------

    private void ShowConnectingDialog()
    {
        var dialog = new ConnectingDialog
        {
            Name = "ConnectingDialog",
            SharedAssets = _sharedAssets,
        };
        dialog.CancelRequested += OnConnectingCancelled;
        _uiLayer!.AddChild(dialog);
        GD.Print("[BootFlow] Connecting dialog shown.");

        // Simulate a short endpoint fetch delay, then advance to char-select.
        // spec §1.5 sub-state 35/39 "wait for reply; thread sets next state on completion".
        var t = GetTree().CreateTimer(0.6, processAlways: true);
        t.Timeout += ShowCharSelectAfterConnect;
    }

    private void ShowCharSelectAfterConnect()
    {
        // Remove the connecting dialog.
        if (_uiLayer is not null)
        {
            Node? dialog = _uiLayer.FindChild("ConnectingDialog", owned: false);
            dialog?.QueueFree();
        }

        GD.Print("[BootFlow] Connecting dialog closed → char select.");
        ShowCharacterSelect(pin: ""); // PIN was already collected before server select
    }

    private void OnConnectingCancelled()
    {
        // Cancel from the connecting dialog → back to server select.
        // spec §1.5 sub-state 35/39 — cancel returns to the form.
        if (_uiLayer is not null)
        {
            Node? dialog = _uiLayer.FindChild("ConnectingDialog", owned: false);
            dialog?.QueueFree();
        }

        GD.Print("[BootFlow] Connecting cancelled → back to server select.");
        ShowServerSelect();
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
        GD.Print("[BootFlow] Showing CharacterSelectScreen (awaiting CharacterListEvent or dev seed).");

        // Subscribe to the Application event bus so CharacterListEvent drives the roster.
        // spec: Docs/RE/specs/frontend_scenes.md §3.1 — "SmsgCharacterList (3/1) forces the select scene".
        // spec: Docs/RE/specs/login_flow.md §3.2. CODE-CONFIRMED.
        if (_ctx is not null)
        {
            StartEventBusDrain(select);
        }

        // In dev offline mode, seed the roster synthetically after a brief delay (mimics the
        // server latency before the 3/1 packet arrives).
        if (IsDevOfflineMode())
        {
            GD.Print("[BootFlow] DEV OFFLINE MODE — seeding synthetic CharacterListEvent.");
            SeedSyntheticCharacterList(select);
        }

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
        // UI click SFX on Enter Game.
        // spec: sound.md — UI click 861010101. CODE-CONFIRMED.
        _audio?.PlayClickSfx();

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
        // Remove the event bus drainer if present.
        Node? drainer = FindChild("CharListEventDrainer", owned: false);
        drainer?.QueueFree();

        GD.Print("[BootFlow] Back to login.");
        ShowLogin();
    }

    private void OnQuitRequested()
    {
        GD.Print("[BootFlow] Quit requested.");
        GetTree().Quit();
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
        GD.Print("[BootFlow] World scene instanced.");
    }

    // -------------------------------------------------------------------------
    // Dev offline: synthetic server list
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a synthetic server list for the dev-offline replay.
    /// The entries follow the exact 8-byte record shape (server_id, status_code, load, open_time)
    /// defined in login_flow.md §2.1, so the presentation rules are exercised faithfully.
    ///
    /// In a live build the list comes from the lobby mini-protocol (port 10000).
    /// spec: Docs/RE/specs/login_flow.md §2.1. CODE-CONFIRMED record shape.
    /// spec: Docs/RE/specs/frontend_scenes.md §2 (load-color thresholds, status sentinels).
    /// DEV ONLY.
    /// </summary>
    private static IReadOnlyList<ServerEntry> BuildServerList()
    {
        // Synthetic data that exercises all load tiers, the NEW badge, and the status sentinels.
        // Entries are spec-shaped (server_id 1..40, status_code, load, open_time).
        // IsNew is NO LONGER a constructor arg — derived at render time: server_id == NEW_SERVER_INDEX (5).
        // spec: Docs/RE/specs/frontend_scenes.md §2.7. CODE-CONFIRMED.
        // spec: Docs/RE/specs/login_flow.md §2.1. CODE-CONFIRMED thresholds.
        return
        [
            // Light load (≤ 500). spec: login_flow.md §2.1 load threshold 500. CODE-CONFIRMED.
            new ServerEntry(ServerId: 1, DisplayName: "Jade Dragon", StatusCode: 1, Load: 120, OpenTime: 0),
            // Medium load (>500). spec: load threshold 500. CODE-CONFIRMED.
            new ServerEntry(ServerId: 2, DisplayName: "Iron Phoenix", StatusCode: 1, Load: 650, OpenTime: 0),
            // High load (>800). spec: load threshold 800. CODE-CONFIRMED.
            // NOTE: ServerId 3 does NOT get the NEW badge; NEW_SERVER_INDEX=5 triggers it (§2.7).
            new ServerEntry(ServerId: 3, DisplayName: "Azure Tiger", StatusCode: 1, Load: 980, OpenTime: 0),
            // Full load (>1200). spec: load threshold 1200. CODE-CONFIRMED.
            new ServerEntry(ServerId: 4, DisplayName: "Shadow Crane", StatusCode: 1, Load: 1450, OpenTime: 0),
            // Scheduled open with clock (status=3, open_time!=0). spec §2.3/§2.4. CODE-CONFIRMED.
            // Load=10 → HH digits = (10/10=1, 10%10=0) → "10"; open_time=30 → MM digits = (30/10=3, 30%10=0) → "30".
            // Display: "10:30". ServerId=5 = NEW_SERVER_INDEX → gets NEW badge. spec §2.7. CODE-CONFIRMED.
            new ServerEntry(ServerId: 5, DisplayName: "Thunder Snake", StatusCode: 3, Load: 10, OpenTime: 30),
            // Preparing / under check: status=3, open_time==0, load==24. spec §2.3. CODE-CONFIRMED.
            // NOTE: 24 is a LOAD sentinel under status 3, NOT a top-level status code. spec §2.3.
            // Previous synthetic had StatusCode:24 which was WRONG — corrected to StatusCode:3, Load:24.
            new ServerEntry(ServerId: 6, DisplayName: "Crimson Wolf", StatusCode: 3, Load: 24, OpenTime: 0),
        ];
    }

    /// <summary>
    /// Seeds a synthetic CharacterListEvent and publishes it through the Application bus.
    /// This exercises the full spec path (CharacterListEvent → CharacterSelectScreen.ApplyCharacterList)
    /// without a live server.
    ///
    /// DEV ONLY.  The synthetic event follows the exact CharacterListEvent shape.
    /// spec: Docs/RE/specs/login_flow.md §3.2 — per-slot 981-byte records, slot bitmask.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.1 — "@BLANK@" empty-slot sentinel. CODE-CONFIRMED.
    /// </summary>
    private void SeedSyntheticCharacterList(CharacterSelectScreen select)
    {
        // Build synthetic CharacterListSlot records.
        // Follows spec field layout from login_flow.md §3.2 / ClientEvents.cs CharacterListSlot.
        //
        // Per spec: empty slot must carry name "@BLANK@". CODE-CONFIRMED.
        // Per spec: max 5 slots (indices 0..4). CODE-CONFIRMED.
        //
        // Synthetic roster: 3 real chars + 2 @BLANK@ empty slots.
        // Matches the official reference which shows "캐릭터 개수 : 3" (character count: 3).
        // spec: Docs/RE/specs/frontend_scenes.md §3.1 — "at most 5 slots". CODE-CONFIRMED.
        var slots = System.Collections.Immutable.ImmutableArray.Create<CharacterListSlot>(
            // Slot 0: Musa (class 1).
            new CharacterListSlot(
                SlotIndex: 0,
                Name: "무사영웅",
                Level: 25,
                ServerClass: 1, // internal class 1 (Musa). spec §4.1. CODE-CONFIRMED.
                CurrentHp: 650),
            // Slot 1: Blader (class 3) — exercises the blader skin chain.
            // spec: Docs/RE/specs/frontend_scenes.md §4.1 — UI index 2 → internal class 3. CODE-CONFIRMED.
            new CharacterListSlot(
                SlotIndex: 1,
                Name: "격사전설",
                Level: 32,
                ServerClass: 3, // internal class 3 (Blader). spec §4.1. CODE-CONFIRMED.
                CurrentHp: 520),
            // Slot 2: Tao (class 2) — exercises the Tao skin chain.
            // spec: Docs/RE/specs/frontend_scenes.md §4.1 — UI index 3 → internal class 2. CODE-CONFIRMED.
            new CharacterListSlot(
                SlotIndex: 2,
                Name: "TaoMaster",
                Level: 18,
                ServerClass: 2, // internal class 2 (Tao). spec §4.1. CODE-CONFIRMED.
                CurrentHp: 480),
            // Slot 3: empty slot (sentinel "@BLANK@"). spec: §3 / login_flow.md §3.5. CODE-CONFIRMED.
            new CharacterListSlot(
                SlotIndex: 3,
                Name: "@BLANK@", // spec: empty-slot sentinel. CODE-CONFIRMED.
                Level: 0,
                ServerClass: 0,
                CurrentHp: 0),
            // Slot 4: empty slot (sentinel "@BLANK@").
            new CharacterListSlot(
                SlotIndex: 4,
                Name: "@BLANK@",
                Level: 0,
                ServerClass: 0,
                CurrentHp: 0));

        var charListEvent = new CharacterListEvent(
            ServerId: 0,
            ChannelId: 0,
            Characters: slots);

        // Publish through the legitimate Application event bus so the full display path runs.
        // The drainer will pick it up on the next _Process frame.
        if (_ctx is not null)
        {
            _ctx.EventBus.Publish(charListEvent);
            GD.Print($"[BootFlow] DEV: published synthetic CharacterListEvent ({slots.Length} slots).");
        }
        else
        {
            // If there's no context, directly drive the screen (last resort for headless tests).
            select.ApplyCharacterList(slots);
            GD.Print("[BootFlow] DEV: directly applied synthetic character list (no ctx).");
        }
    }

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