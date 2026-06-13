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
        // Store account name so it can be forwarded to UseCases.LoginAsync at the join point.
        // The password is NOT stored — it lives only in the PIN modal / flow context
        // (in a real build it travels via RSA 1/4, never in a plain field here).
        // We store a placeholder: the password is already staged in ApplicationUseCases by the
        // login screen's own submit path. The UI PIN modal delivers only the PIN.
        _account = account;
        GD.Print($"[BootFlow] Login accepted (account='{account}') → server select.");

        // Stage login credentials in the Application layer (if available).
        // spec: Docs/RE/specs/client_workflow.md §5.1.2 — login credential staging.
        // In offline mode UseCases is a no-op sink.
        if (_useCases is not null)
        {
            // pin is null here — it is collected later by the PIN modal (after server select),
            // so the cancellationToken must be passed by name (the new LoginAsync inserts string? pin
            // before the token). spec: Docs/RE/specs/login_flow.md §1 step 1a.
            _ = _useCases.LoginAsync(account, password: "", cancellationToken: CancellationToken.None);
        }

        ShowServerSelect();
    }

    private void OnServerListRequested(string account)
    {
        // Player clicked the server-list button without submitting login first.
        // Store the account so it is available when they complete the flow.
        // The spec allows browsing the server list before entering credentials in full.
        // spec: Docs/RE/specs/frontend_scenes.md §2 — server-list button on the login screen.
        _account = account;
        GD.Print($"[BootFlow] Server list requested (account='{account}') → server select.");
        ShowServerSelect();
    }

    // -----------------------------------------------------------------------
    // Step 2: Server-selection screen
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
        GD.Print("[BootFlow] Showing ServerSelectScreen.");
    }

    private void OnServerSelected(int serverId)
    {
        _selectedServerId = serverId;
        GD.Print($"[BootFlow] Server selected: id={serverId} → PIN modal.");
        ShowPinModal();
    }

    // -----------------------------------------------------------------------
    // Step 3: PIN / second-password modal
    // -----------------------------------------------------------------------

    private void ShowPinModal()
    {
        // The PIN modal is layered on TOP of the current screen (not replacing it),
        // so it appears as a real modal overlay.
        var pin = new PinModal
        {
            Name = "PinModal",
            SharedAssets = _sharedAssets,
        };
        pin.PinSubmitted += OnPinSubmitted;
        pin.Cancelled += OnPinCancelled;

        // Add directly to the CanvasLayer so it sits above the ScreenHost content.
        _uiLayer!.AddChild(pin);
        GD.Print("[BootFlow] PIN modal shown.");
    }

    private void OnPinSubmitted(string pin)
    {
        // Remove the PIN modal (find and free it from the CanvasLayer).
        RemovePinModal();

        GD.Print($"[BootFlow] PIN submitted (length={pin.Length}) → char select.");
        ShowCharacterSelect(pin);
    }

    private void OnPinCancelled()
    {
        RemovePinModal();
        GD.Print("[BootFlow] PIN modal cancelled → back to server select.");
        ShowServerSelect();
    }

    private void RemovePinModal()
    {
        if (_uiLayer is null) return;
        Node? modal = _uiLayer.FindChild("PinModal", owned: false);
        modal?.QueueFree();
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
        // Synthetic data that exercises all load tiers and the NEW badge.
        // Entries are spec-shaped (server_id 1..40, status_code, load, open_time).
        // spec: Docs/RE/specs/login_flow.md §2.1. CODE-CONFIRMED thresholds.
        return
        [
            // Light load (≤ 500). spec: login_flow.md §2.1 load threshold 500. CODE-CONFIRMED.
            new ServerEntry(ServerId: 1, DisplayName: "Jade Dragon", StatusCode: 1, Load: 120, OpenTime: 0,
                IsNew: false),
            // Medium load (>500). spec: load threshold 500. CODE-CONFIRMED.
            new ServerEntry(ServerId: 2, DisplayName: "Iron Phoenix", StatusCode: 1, Load: 650, OpenTime: 0,
                IsNew: false),
            // High load (>800). spec: load threshold 800. CODE-CONFIRMED.
            new ServerEntry(ServerId: 3, DisplayName: "Azure Tiger", StatusCode: 1, Load: 980, OpenTime: 0,
                IsNew: true),
            // Full load (>1200). spec: load threshold 1200. CODE-CONFIRMED.
            new ServerEntry(ServerId: 4, DisplayName: "Shadow Crane", StatusCode: 1, Load: 1450, OpenTime: 0,
                IsNew: false),
            // Scheduled open (status=3). spec: status sentinel 3. CODE-CONFIRMED.
            // Load=10 → HH = "01"; open_time=30 → MM = "30" (HH:MM = "01:30").
            new ServerEntry(ServerId: 5, DisplayName: "Thunder Snake", StatusCode: 3, Load: 10, OpenTime: 30,
                IsNew: false),
            // Preparing / under check (status=24). spec: sentinel 24. CODE-CONFIRMED.
            new ServerEntry(ServerId: 6, DisplayName: "Crimson Wolf", StatusCode: 24, Load: 0, OpenTime: 0,
                IsNew: false),
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
        // Synthetic roster: 1 real char + 1 @BLANK@ empty slot. Covers both the enter-game path
        // and the create-character path (enter on @BLANK@).
        var slots = System.Collections.Immutable.ImmutableArray.Create<CharacterListSlot>(
            // Slot 0: real character.
            new CharacterListSlot(
                SlotIndex: 0,
                Name: "무사영웅",
                Level: 25,
                ServerClass: 1, // internal class 1 (Musa). spec §4.1. CODE-CONFIRMED.
                CurrentHp: 650),
            // Slot 1: empty slot (sentinel "@BLANK@"). spec: §3 / login_flow.md §3.5. CODE-CONFIRMED.
            new CharacterListSlot(
                SlotIndex: 1,
                Name: "@BLANK@", // spec: empty-slot sentinel. CODE-CONFIRMED.
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