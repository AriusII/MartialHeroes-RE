using Godot;
using MartialHeroes.Client.Application.Engine;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Debug;
using MartialHeroes.Client.Godot.Input;
using MartialHeroes.Client.Godot.Ui.Hud;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// Root scene orchestrator. Owns the per-frame event drain and dispatches each
/// <see cref="IClientEvent"/> to the appropriate view subsystem.
///
/// Threading contract:
///   - The Application event channel may be written from any thread (the network reader,
///     the <see cref="GameEngineLoop"/> background task, or the synthetic feeder).
///   - ALL Godot node mutations happen on the Godot main thread, here in _Process or via
///     CallDeferred. No Node is touched from a background thread.
///
/// Events handled and their view targets (CAMPAIGN 17 Wave 2b — hub-fed HudMaster):
///   - <see cref="WorldSnapshotEvent"/>          → ActorRegistry.OnWorldSnapshot
///   - <see cref="SectorLoadedEvent"/>           → TerrainNode.OnSectorLoaded
///   - <see cref="SectorUnloadedEvent"/>         → TerrainNode.OnSectorUnloaded
///   - <see cref="ActorSpawnedEvent"/>           → ActorRegistry.OnActorSpawned
///   - <see cref="ActorMovedEvent"/>             → ActorRegistry.OnActorMoved
///   - <see cref="ActorDespawnedEvent"/>         → ActorRegistry.OnActorDespawned
///   - <see cref="ActorVitalsChangedEvent"/>     → IHudEventHub.PublishVitals (→ HudRightEdgeGauge)
///   - <see cref="ActorLeveledUpEvent"/>         → IHudEventHub.PublishVitals + PublishExpLevel
///   - <see cref="ActorStatSyncEvent"/>          → IHudEventHub.PublishExpLevel
///   - <see cref="CombatStatsRecomputedEvent"/>  → IHudEventHub.PublishVitals (max HP/MP update)
///   - <see cref="BuffSlotChangedEvent"/>        → IHudEventHub.PublishBuffState
///   - <see cref="SkillHotbarSlotSetEvent"/>     → TODO(hud-ii): needs a hub Hotbar channel
///   - <see cref="ChatBroadcastEvent"/>          → IHudEventHub.PublishChatLine (→ HudChatPanel)
///   - <see cref="MartialHeroes.Client.Application.Scene.SceneStateChangedEvent"/> → (no hub channel; handled by SceneHost)
///
/// spec: Docs/RE/specs/game_loop.md §6 — "updates the spatial transforms of the associated
///       Node3D on the next frame".
/// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — "pump per-frame needs".
/// </summary>
public sealed partial class GameLoop : Node
{
    // -------------------------------------------------------------------------
    // World-exit signal (engine-state-5 leave-world / logout).
    //
    // The master scene machine does not terminate at the world: leaving the world returns the
    // player toward char-select (state 5 → 4 default loop-return) or quits on logout (state 5 → 6).
    // This signal lets the layer-05 composition root (BootFlow) tear down World.tscn and re-enter
    // the front-end, mirroring the original's "quitting the world returns toward select rather than
    // straight to exit."
    // spec: Docs/RE/specs/client_runtime.md §7.5.1 (5 → 4 natural return) / §7.5.3 (logout → 6) /
    //       §7.9.5 ("state 5 pre-sets next = 4; world-leave sets 6").
    // -------------------------------------------------------------------------

    /// <summary>
    /// Raised when the player leaves the world. <paramref name="logout"/> distinguishes the two
    /// recovered exit transitions: <see langword="false"/> = leave-world → re-enter char-select
    /// (state 4); <see langword="true"/> = logout → quit (state 6).
    /// spec: Docs/RE/specs/client_runtime.md §7.5.1 / §7.5.3 / §7.9.5.
    /// </summary>
    [Signal]
    public delegate void WorldExitRequestedEventHandler(bool logout);

    // -------------------------------------------------------------------------
    // Child node references (assigned in _Ready from the scene tree)
    // -------------------------------------------------------------------------

    private ActorRegistry _actorRegistry = null!;
    private InputRouter _inputRouter = null!;
    private SyntheticWorldFeeder _syntheticFeeder = null!;
    private ClientContext _clientContext = null!;
    private TerrainNode _terrainNode = null!;

    // HudMaster reference — the sole, hub-fed in-game HUD (CAMPAIGN 17 Wave 2b).
    // GameLoop owns the reference so it can (a) wire the hit-test into HudInputHandler and
    // (b) delegate toggle keys (I/K/C) to the new substrate.
    // The HudMaster is built by InGameScene and passed in via SetHudMaster().
    private HudMaster? _hudMaster;

    // Hub reference — published into each frame for every event that carries HUD-relevant data.
    // Stored separately from ClientContext to avoid repeated property access in the hot path.
    private IHudEventHub? _hudHub;

    // Local-player vitals view state (latest confirmed values — drives PublishVitals on every change).
    // No game logic: values are read from event payloads; GameLoop never computes them.
    private uint _localHp, _localMaxHp, _localMp, _localMaxMp, _localStam, _localMaxStam;

    private RealWorldRenderer? _realWorldRenderer;

    // ---- Region tracking (for zone indicator) ----
    // Legacy world XZ of the local player; updated on ActorMoved/LocalPlayerSpawned.
    // Used to call RegionService.UpdatePosition each frame.
    // spec: Docs/RE/specs/world_systems.md Ch. 16 — 256-unit region grid.
    private float _localPlayerLegacyX;
    private float _localPlayerLegacyZ;
    private bool _hasLocalPlayer;

    // Stage-B: world-side effect renderer (3D particle/placeholder casts).
    // spec: Docs/RE/specs/effects.md §15.3 — action codes 0xC8/0xC9/0xCB; CODE-CONFIRMED.
    private EffectRenderer? _effectRenderer;

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        // *** INCONTOURNABLE — ce log apparaît avant TOUT le reste ***
        GD.Print("===== [GameLoop] _Ready ENTERED =====");

        // Spawn the debug baseline (green floor + emissive red cube) only when the dev baseline
        // flag is set. The original world build contains NO such scaffolding geometry — it renders
        // only the recovered scene graph (terrain/actors/effects). Gating this behind a dev flag,
        // default OFF, keeps production / fidelity screenshots clean.
        // spec: Docs/RE/specs/client_runtime.md §7.4 (BuildGameWorld has no debug primitives);
        // spec: Docs/RE/specs/game_loop.md §3.5 (render = scene-graph cull walk only).
        if (IsDevBaselineEnabled())
            SpawnDebugBaseline();

        // The entire _Ready body is wrapped defensively: any exception in subsystem wiring,
        // context resolution, or real-asset initialisation must NOT crash the scene.
        // F5 must always produce a Godot window; the window may be in degraded mode.
        try
        {
            ReadyInternal();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameLoop] _Ready failed: {ex}");
            GD.PrintErr("[GameLoop] Attempting emergency fallback to synthetic mode.");
            TryEmergencyFallback();
        }

        GD.Print("===== [GameLoop] _Ready COMPLETED =====");
    }

    /// <summary>
    /// DEV-ONLY scaffolding: spawns a large green ground plane and an emissive red reference cube
    /// at the world origin. This is NOT part of the original world build — the recovered client
    /// renders only the scene graph (terrain/actors/effects) and contains no debug primitives. It is
    /// therefore gated behind <see cref="IsDevBaselineEnabled"/> (default OFF) so production /
    /// fidelity screenshots show only real geometry. When enabled it serves as a depth/orientation
    /// cue and a guaranteed visual proof that the Godot scene executes even if all asset loading fails.
    /// spec: Docs/RE/specs/client_runtime.md §7.4; game_loop.md §3.5.
    /// </summary>
    private void SpawnDebugBaseline()
    {
        try
        {
            // --- Large ground plane ---
            var groundMesh = new MeshInstance3D();
            var planeMesh = new PlaneMesh();
            planeMesh.Size = new Vector2(200f, 200f);
            groundMesh.Mesh = planeMesh;

            var groundMat = new StandardMaterial3D();
            // Checkerboard-style green ground so depth and scale are readable.
            groundMat.AlbedoColor = new Color(0.22f, 0.55f, 0.22f);
            groundMat.RoughnessTexture = null;
            planeMesh.Material = groundMat;

            groundMesh.Name = "DebugGround";
            groundMesh.Position = Vector3.Zero;
            AddChild(groundMesh);

            // --- Reference cube (emissive red — visible even with no lighting) ---
            var cubeMesh = new MeshInstance3D();
            var boxMesh = new BoxMesh();
            boxMesh.Size = new Vector3(1.5f, 1.5f, 1.5f);
            cubeMesh.Mesh = boxMesh;

            var cubeMat = new StandardMaterial3D();
            cubeMat.AlbedoColor = new Color(1f, 0f, 0f);
            cubeMat.EmissionEnabled = true;
            cubeMat.Emission = new Color(1f, 0.1f, 0.1f);
            cubeMat.EmissionEnergyMultiplier = 2.0f;
            boxMesh.Material = cubeMat;

            cubeMesh.Name = "DebugReferenceCube";
            cubeMesh.Position = new Vector3(0f, 1.5f, 0f);
            AddChild(cubeMesh);

            GD.Print("[GameLoop] Debug baseline (ground+cube) spawned.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameLoop] SpawnDebugBaseline failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns true when the DEV-ONLY debug baseline (green plane + red origin cube) is enabled.
    /// Default OFF so the world renders only the recovered scene graph; enabled only via the
    /// <c>MH_DEV_BASELINE=1</c> environment variable or the <c>dev_baseline=1</c> key in
    /// <c>client_dir.cfg</c>. DEV_OFFLINE_FLOW must not add geometry to fidelity screenshots.
    /// spec: Docs/RE/specs/client_runtime.md §7.4 (no debug primitives in the original world build).
    /// </summary>
    private static bool IsDevBaselineEnabled()
    {
        // Dedicated env override — fastest to check.
        string? baselineEnv = System.Environment.GetEnvironmentVariable("MH_DEV_BASELINE");
        if (baselineEnv is "1" or "true" or "yes")
            return true;

        return ReadCfgFlag("dev_baseline");
    }

    /// <summary>
    /// Reads a boolean flag from <c>client_dir.cfg</c> (res:// or cwd). Returns false on any error or
    /// when the key is absent — fail-open to OFF so a missing config never spawns scaffolding.
    /// </summary>
    private static bool ReadCfgFlag(string key)
    {
        try
        {
            string? path = ResolveCfgPath();
            if (path is null) return false;

            foreach (string rawLine in System.IO.File.ReadLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                int eq = line.IndexOf('=');
                if (eq < 0) continue;

                string k = line[..eq].Trim();
                string v = line[(eq + 1)..].Trim();
                if (k.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return v is "1" or "true" or "yes";
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameLoop] ReadCfgFlag('{key}'): {ex.Message}");
        }

        return false;
    }

    private static string? ResolveCfgPath()
    {
        const string configResPath = "res://client_dir.cfg";
        try
        {
            string abs = ProjectSettings.GlobalizePath(configResPath);
            return System.IO.File.Exists(abs) ? abs : null;
        }
        catch
        {
            return System.IO.File.Exists("client_dir.cfg") ? "client_dir.cfg" : null;
        }
    }

    /// <summary>
    /// Called by InGameScene after it builds and adds HudMaster so GameLoop can wire the
    /// hit-test into HudInputHandler (the "UI is the gate" gate).
    /// spec: Docs/RE/specs/input_ui.md §3 / §6 — "UI hit-test always before world interaction".
    /// </summary>
    public void SetHudMaster(HudMaster hudMaster)
    {
        _hudMaster = hudMaster;
        if (_clientContext is not null)
        {
            // Re-wire the hit-test to HudMaster.HitTest (replaces any prior null delegate).
            // spec: Docs/RE/specs/input_ui.md §3 / §6.
            _clientContext.SetHudHitTest(hudMaster.HitTest);
            GD.Print("[GameLoop] HudInputHandler.HitTest wired to HudMaster.HitTest. spec: input_ui.md §3/§6.");
        }
    }

    /// <summary>The real _Ready body; separated so the defensive wrapper stays clean.</summary>
    private void ReadyInternal()
    {
        GD.Print("[GameLoop] ReadyInternal: resolving ClientContext");

        // Resolve the autoload singleton.
        _clientContext = GetNode<ClientContext>("/root/ClientContext");

        // Resolve child nodes — use HasNode for optional children so missing nodes don't crash.
        _actorRegistry = GetNode<ActorRegistry>("ActorRegistry");
        _inputRouter = GetNode<InputRouter>("InputRouter");
        _syntheticFeeder = GetNode<SyntheticWorldFeeder>("SyntheticWorldFeeder");

        // TerrainNode may not be in the scene tree yet — create it procedurally if absent.
        if (HasNode("TerrainNode"))
        {
            _terrainNode = GetNode<TerrainNode>("TerrainNode");
        }
        else
        {
            _terrainNode = new TerrainNode();
            _terrainNode.Name = "TerrainNode";
            AddChild(_terrainNode);
        }

        GD.Print("[GameLoop] ReadyInternal: child nodes resolved — wiring subsystems");

        // Cache the hub reference for hot-path use in DispatchEvent.
        _hudHub = _clientContext.HudEventHub;

        // Give subsystems their handles.
        try
        {
            _actorRegistry.Initialise(_clientContext);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameLoop] ActorRegistry.Initialise failed: {ex.Message}");
        }

        // Wire InputRouter with bus from the composition root.
        try
        {
            _inputRouter.Initialise(_clientContext);
            _inputRouter.InitialiseBus(_clientContext.InputBus);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameLoop] InputRouter.Initialise failed: {ex.Message}");
        }

        GD.Print("[GameLoop] ReadyInternal: subsystems wired — checking real-asset renderer");

        // Real-asset rendering path vs. synthetic feeder.
        // Activation via ClientPathResolver (config file / env override / auto-detect).
        // Each step guarded individually: a failure in real-asset init falls back to synthetic.
        // spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive.
        bool realRendererStarted = false;
        if (RealWorldRenderer.IsEnabled)
        {
            GD.Print("[GameLoop] Real assets enabled — attempting real-asset renderer.");
            try
            {
                _realWorldRenderer = new RealWorldRenderer();
                _realWorldRenderer.Name = "RealWorldRenderer";
                AddChild(_realWorldRenderer);
                _realWorldRenderer.Initialise(_clientContext, _terrainNode);
                realRendererStarted = true;
                GD.Print("[GameLoop] RealWorldRenderer initialised successfully.");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[GameLoop] RealWorldRenderer.Initialise failed: {ex}");
                GD.PrintErr("[GameLoop] Falling back to SyntheticWorldFeeder.");
                // Clean up the partially-added node if present.
                if (_realWorldRenderer is not null && IsInstanceValid(_realWorldRenderer))
                {
                    _realWorldRenderer.QueueFree();
                    _realWorldRenderer = null;
                }
            }
        }

        if (!realRendererStarted)
        {
            // Start the synthetic feeder (fires and forgets onto a Task; publishes only through
            // the legitimate Application event bus — no game rules inside).
            try
            {
                _syntheticFeeder.StartAsync(_clientContext);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[GameLoop] SyntheticWorldFeeder.StartAsync failed: {ex.Message}");
            }
        }

        // Stage-B: EffectRenderer — world-side 3D particle placeholder for cast effects.
        // Added as a Node3D child of GameLoop (the 3D scene root) so it lives in world space.
        // spec: Docs/RE/specs/effects.md §15.3 — action codes 0xC8/0xC9/0xCB; CODE-CONFIRMED.
        // spec: Docs/RE/specs/effects.md §15.4 — looping actor-anchored UserXEffect; CODE-CONFIRMED.
        try
        {
            _effectRenderer = new EffectRenderer { Name = "EffectRenderer" };
            AddChild(_effectRenderer);
            _effectRenderer.Bind(_clientContext.HudEventHub);
            GD.Print("[GameLoop] EffectRenderer added + bound to HudEventHub.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameLoop] EffectRenderer init failed: {ex.Message}");
        }

        // Load region data for the default starting area (area 0) alongside terrain boot.
        // This is fire-and-forget: region files may be absent (VFS offline → Unknown zone, no crash).
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1–§16.2 — load once per area change.
        _ = _clientContext.RegionService.LoadAreaAsync(areaId: 0).AsTask().ContinueWith(
            t =>
            {
                if (t.IsFaulted)
                    GD.PrintErr(
                        $"[GameLoop] RegionService.LoadAreaAsync(0) failed: {t.Exception?.InnerException?.Message}");
                else
                    GD.Print("[GameLoop] RegionService: area 0 region data loaded. " +
                             "spec: Docs/RE/specs/world_systems.md Ch. 16.");
            },
            TaskScheduler.Default);

        GD.Print("[GameLoop] Ready.");
    }

    /// <summary>
    /// Last-resort fallback called when <see cref="ReadyInternal"/> itself threw.
    /// Attempts to start the synthetic feeder on whatever context we managed to resolve.
    /// If even that fails, we remain silent — the window is at least open.
    /// </summary>
    private void TryEmergencyFallback()
    {
        try
        {
            _clientContext ??= GetNode<ClientContext>("/root/ClientContext");
            _syntheticFeeder ??= GetNode<SyntheticWorldFeeder>("SyntheticWorldFeeder");
            if (_clientContext is not null && _syntheticFeeder is not null)
            {
                _syntheticFeeder.StartAsync(_clientContext);
                GD.Print("[GameLoop] Emergency fallback: SyntheticWorldFeeder started.");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameLoop] Emergency fallback also failed: {ex.Message}. " +
                        "The window is open but all subsystems are offline.");
        }
    }

    /// <summary>
    /// In-world leave-world / logout trigger. ESC (when chat is not capturing it — the InputRouter
    /// consumes and marks ESC handled only while chat is active) requests a leave-world transition
    /// back toward char-select (engine state 5 → 4). This is the world-exit hook the master scene
    /// machine needs: the world is not a terminal scene.
    /// spec: Docs/RE/specs/client_runtime.md §7.5.1 (5 → 4 natural return) / §7.9.5.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key) return;

        if (key.PhysicalKeycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            // Leave-world (not logout): re-enter char-select. spec: client_runtime.md §7.5.1 (5 → 4).
            GD.Print("[GameLoop] Leave-world requested (ESC) → return to char-select. " +
                     "spec: Docs/RE/specs/client_runtime.md §7.5.1 (state 5 → 4).");
            EmitSignal(SignalName.WorldExitRequested, false);
        }
    }

    /// <summary>
    /// Drains the Application event channel on the main thread each frame.
    /// All pending events are processed synchronously within this call so that
    /// Godot node mutations happen safely on the main thread.
    ///
    // -------------------------------------------------------------------------
    // Single HUD key-command dispatcher
    //
    // The original client routes all HUD key commands through a command-code dispatch table
    // (one dispatcher, panels are passive recipients). Here we mirror that: one _Input override
    // in GameLoop dispatches I/K/O/C to the respective window's Toggle() method, eliminating
    // the per-panel _Input grabs that caused Enter-key contention and multiple owners.
    //
    // F4 fix. spec: Docs/RE/specs/input_ui.md §3a / §5 — single dispatch point.
    // spec: Docs/RE/specs/ui_system.md §15 — in-game HUD key command dispatch.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Single HUD key-command dispatcher. Routes I/K/O/C key presses to the correct window
    /// Toggle() rather than having each panel install a competing top-level _Input grab.
    /// Also routes Enter to the ChatWindow for focus (ChatWindow owns chat input exclusively).
    /// Only active in-game (EngineSceneState.InGame).
    /// spec: Docs/RE/specs/input_ui.md §3a / §5 — single dispatcher, panels are passive.
    /// spec: Docs/RE/specs/ui_system.md §15 — in-game HUD key command dispatch.
    /// </summary>
    public override void _Input(global::Godot.InputEvent evt)
    {
        if (_clientContext?.SceneMachine.Current.State != EngineSceneState.InGame)
            return;

        if (evt is not InputEventKey key || !key.Pressed || key.Echo)
            return;

        switch (key.Keycode)
        {
            case Key.I:
                // Toggle inventory+skill pair. spec: ui_system.md §8.10.1 ([I] toggles slots 158+159).
                _hudMaster?.ToggleInventory();
                GetViewport().SetInputAsHandled();
                break;

            case Key.K:
                // Toggle skill window. spec: ui_system.md §15 (K = skill toggle).
                _hudMaster?.ToggleSkill();
                GetViewport().SetInputAsHandled();
                break;

            case Key.O:
                // Options window — not yet recovered in HudMaster (HUD-II). No-op for now.
                // spec: ui_system.md §15 (O = options toggle).
                GetViewport().SetInputAsHandled();
                break;

            case Key.C:
                // Toggle character stats window.
                // spec: Docs/RE/formats/misc_data.md §5 — discript.sc category 102 "(C)".
                _hudMaster?.ToggleStats();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    /// <summary>
    /// spec: Docs/RE/specs/game_loop.md §6 — snapshot interpolation pipeline ends with
    ///       "updates the spatial transforms of the associated Node3D on the next frame".
    /// </summary>
    public override void _Process(double delta)
    {
        // Guard: if _clientContext was never resolved (extreme failure), skip frame silently.
        if (_clientContext is null) return;

        // Drain every event that arrived since the last frame.
        // TryRead never blocks; we stop when the queue is empty.
        // Individual dispatch errors are caught so one bad event cannot kill the frame loop.
        try
        {
            while (_clientContext.EventBus.Reader.TryRead(out IClientEvent? evt))
            {
                try
                {
                    DispatchEvent(evt);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[GameLoop] DispatchEvent error ({evt?.GetType().Name}): {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameLoop] _Process error: {ex.Message}");
        }

        // ---- Region zone poll (once per frame, main thread) ----
        // Calls RegionService.UpdatePosition with the local player's last-known legacy XZ.
        // RegionService only fires ZoneChangedEvent when the zone actually changes.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 (256-unit grid lookup).
        if (_hasLocalPlayer)
        {
            try
            {
                _clientContext.RegionService.UpdatePosition(_localPlayerLegacyX, _localPlayerLegacyZ);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[GameLoop] RegionService.UpdatePosition failed: {ex.Message}");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Internal dispatch — routes IClientEvent to the correct view subsystem.
    // NO game logic here: we translate event type to a view-method call only.
    // -------------------------------------------------------------------------

    private void DispatchEvent(IClientEvent evt)
    {
        switch (evt)
        {
            // ---- Actor lifecycle ----
            case ActorSpawnedEvent spawned:
                _actorRegistry.OnActorSpawned(spawned);
                break;

            case ActorMovedEvent moved:
                // Legacy fallback path — superseded by WorldSnapshotEvent when the engine loop runs.
                _actorRegistry.OnActorMoved(moved);
                // Track local-player legacy XZ for RegionService zone polling.
                // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — legacy XZ lookup.
                if (_hasLocalPlayer && moved.Key.Sort == MartialHeroes.Client.Domain.Actors.EntitySort.PlayerCharacter)
                {
                    var (fx, _, fz) = moved.MoveTarget.ToVector3Float();
                    _localPlayerLegacyX = fx;
                    _localPlayerLegacyZ = fz;
                }

                break;

            case ActorDespawnedEvent despawned:
                _actorRegistry.OnActorDespawned(despawned);
                break;

            // ---- Fixed-tick snapshot (primary interpolation path) ----
            case WorldSnapshotEvent snapshot:
                // spec: Docs/RE/specs/game_loop.md §6 — Godot interpolates between snapshots.
                _actorRegistry.OnWorldSnapshot(snapshot);
                break;

            // ---- Terrain streaming ----
            case SectorLoadedEvent loaded:
                // Primary path: drive TerrainNode heightmap for terrain geometry.
                // spec: Docs/RE/formats/terrain.md §9 (cell streaming policy).
                _terrainNode.OnSectorLoaded(loaded);

                // Phase 6a: also route through CellAssemblyHandoff to compose the full cell
                // (slots 0-8: .ted/.bud/.fx1-7) and publish CellAssembledEvent next-frame.
                // The handoff is null when the terrain VFS is unavailable (offline mode).
                // spec: Docs/RE/specs/assembly_graph.md §1/§4 — AreaComposer + CellAssemblyHandoff.
                try
                {
                    _clientContext.CellAssemblyHandoff?.OnSectorLoaded(loaded);
                }
                catch (Exception handoffEx)
                {
                    GD.PrintErr($"[GameLoop] CellAssemblyHandoff.OnSectorLoaded error: {handoffEx.Message}");
                }
                break;

            case SectorUnloadedEvent unloaded:
                _terrainNode.OnSectorUnloaded(unloaded);
                break;

            // ---- Phase 6a: assembled cell/area events ----
            case CellAssembledEvent cellEvt:
                // A fully assembled cell (all 9 slots: .ted/.bud/.fx1-7) is now available.
                // Phase 6a: log the event for headless verification; future phases extend rendering.
                // The existing TerrainNode + BudMeshBuilder path (driven by SectorLoadedEvent above)
                // already renders terrain/buildings. CellAssembledEvent provides the richer model
                // (all 9 slots + texture-path cache) for future slot rendering.
                // spec: Docs/RE/specs/assembly_graph.md §1 — assembled cell ready for presentation.
                GD.Print($"[GameLoop] CellAssembledEvent: cell=({cellEvt.Cell.MapX},{cellEvt.Cell.MapZ}) " +
                         $"resolved={cellEvt.Cell.IsResolved}. spec: assembly_graph.md §1.");
                break;

            case AreaAssembledEvent areaEvt:
                // Phase 6a: full area assembled. Log for headless verification.
                // spec: Docs/RE/specs/assembly_graph.md §1 — area load (Phase A).
                GD.Print($"[GameLoop] AreaAssembledEvent: area={areaEvt.Area.AreaId} " +
                         $"cellCount={areaEvt.Area.CellKeyCount}. spec: assembly_graph.md §1.");
                break;

            // ---- Vitals / stats / level ----
            case ActorVitalsChangedEvent vitals:
                // Publish into the HUD hub so HudRightEdgeGauge (and future panels) drain live data.
                // spec: Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml.
                // spec: MartialHeroes.Client.Application.Hud.IHudEventHub.PublishVitals.
                _localHp = vitals.CurrentHp;
                _localMp = vitals.CurrentMp;
                if (_localMaxHp == 0) _localMaxHp = vitals.CurrentHp;
                if (_localMaxMp == 0) _localMaxMp = vitals.CurrentMp;
                _hudHub?.PublishVitals(new HudVitalsEvent(
                    _localHp, _localMaxHp, _localMp, _localMaxMp, _localStam, _localMaxStam));
                break;

            case ActorLeveledUpEvent levelUp:
                // spec: Docs/RE/packets/5-32_level_up.yaml.
                _localHp = levelUp.CurrentHp;
                _localMp = levelUp.CurrentMp;
                _hudHub?.PublishVitals(new HudVitalsEvent(
                    _localHp, _localMaxHp, _localMp, _localMaxMp, _localStam, _localMaxStam));
                _hudHub?.PublishExpLevel(new ExpLevelEvent(0L, 0L, levelUp.NewLevel));
                break;

            case ActorStatSyncEvent statSync:
                // spec: Docs/RE/specs/handlers.md §4 (5/67 SmsgStatsUpdate).
                _hudHub?.PublishExpLevel(new ExpLevelEvent(statSync.CurrentXp, 0L, 0));
                break;

            case CombatStatsRecomputedEvent combatStats:
            {
                // Max HP/MP come from the Domain combat-stats aggregate; update local tracking.
                // spec: Docs/RE/specs/combat.md §1 / §2 — CombatStats aggregate.
                var s = combatStats.Stats;
                if (s.MaxLife > 0) _localMaxHp = (uint)s.MaxLife;
                if (s.MaxEnergy > 0) _localMaxMp = (uint)s.MaxEnergy;
                _hudHub?.PublishVitals(new HudVitalsEvent(
                    _localHp, _localMaxHp, _localMp, _localMaxMp, _localStam, _localMaxStam));
                break;
            }

            // ---- Buffs ----
            case BuffSlotChangedEvent buff:
                // Per-slot buff update — publish a full refresh snapshot with the latest slot state.
                // The hub BuffStates channel is latest-wins; we publish a one-slot event wrapped in a
                // full 30-slot array (all other slots zeroed) as a lightweight incremental update.
                // A future handler will publish a proper full 4/102 refresh.
                // spec: Docs/RE/specs/handlers.md §4 (5/31 SmsgBuffSlotUpdate).
                PublishBuffSlotUpdate(buff);
                break;

            // ---- Skill hotbar ----
            case SkillHotbarSlotSetEvent:
                // TODO(hud-ii): needs a hub Hotbar channel — no IHudEventHub.PublishHotbar exists yet.
                // spec: Docs/RE/specs/handlers.md §4 (5/33 SmsgSkillHotbarSlotSet).
                break;

            // ---- Chat ----
            case ChatBroadcastEvent chat:
                // Route through hub ChatLines so HudChatPanel drains it.
                // spec: Docs/RE/packets/5-7_chat_broadcast.yaml.
                // spec: MartialHeroes.Client.Application.Hud.IHudEventHub.PublishChatLine.
                _hudHub?.PublishChatLine(new ChatLineEvent(
                    chat.Channel,
                    chat.Text,
                    ChatLineEvent.SayColorArgb,
                    chat.SenderName));
                break;

            // ---- Scene lifecycle ----
            case MartialHeroes.Client.Application.Scene.SceneStateChangedEvent:
                // No hub channel for scene-state changes; handled by SceneHost.
                break;

            // ---- Local player spawn (3/7) ----
            case LocalPlayerSpawnedEvent localSpawn:
                // Track local player legacy XZ for RegionService zone polling.
                // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — legacy XZ for grid lookup.
            {
                var (spawnX, _, spawnZ) = localSpawn.Position.ToVector3Float();
                _localPlayerLegacyX = spawnX;
                _localPlayerLegacyZ = spawnZ;
                _hasLocalPlayer = true;

                // Seed vitals from spawn data; publish initial gauge state.
                _localHp = localSpawn.CurrentHp;
                _localMaxHp = localSpawn.MaxHp;
                _hudHub?.PublishVitals(new HudVitalsEvent(
                    _localHp, _localMaxHp, _localMp, _localMaxMp, _localStam, _localMaxStam));
            }

                // Translate to ActorSpawnedEvent so ActorRegistry can place the visual actor.
                // spec: Docs/RE/specs/login_flow.md §3.5 / §5.3 (3/7 SmsgCharSpawnResult → spawn).
                _actorRegistry.OnActorSpawned(new ActorSpawnedEvent(
                    localSpawn.Key,
                    localSpawn.Name,
                    localSpawn.Level,
                    localSpawn.Position,
                    localSpawn.CurrentHp,
                    localSpawn.MaxHp,
                    localSpawn.ServerClass));
                GD.Print($"[GameLoop] LocalPlayerSpawnedEvent: name='{localSpawn.Name}' " +
                         $"level={localSpawn.Level} pos=({localSpawn.Position.RawX},{localSpawn.Position.RawZ}) " +
                         $"slot={localSpawn.SlotIndex}. spec: login_flow.md §3.5 / §5.3.");
                break;

            case LocalPlayerSpawnFailedEvent spawnFailed:
                // Spawn failure: log for diagnostics; BootFlow already transitioned to World
                // so we show the failure in-world (timed message). spec: login_flow.md §5.3.
                GD.PrintErr($"[GameLoop] LocalPlayerSpawnFailedEvent: slot={spawnFailed.SlotIndex}. " +
                            "spec: Docs/RE/specs/login_flow.md §5.3 (Result 0 = failure).");
                break;

            // Equip / inventory / skill-point results are received but not yet
            // visually handled (no inventory window). Log nothing — silently ignore.
            case EquipResultEvent:
            case ItemSlotStateEvent:
            case NpcAcquireResultEvent:
            case SkillHotbarAssignResultEvent:
            case SkillPointUpdateEvent:
            case ActorStatsChangedEvent:
            case CombatAttackUpdateEvent:
            case CharacterListEvent:
            case LoginHandshakeCompletedEvent:
                break;

            default:
                // Unknown event type: ignore; new event types added by Application do not
                // require changes here unless we want to react to them.
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Hub publication helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Publishes a buff-slot update as a latest-wins snapshot on the hub BuffStates channel.
    /// Since IHudEventHub does not expose per-slot incremental writes (only full 30-slot refreshes),
    /// we maintain a local 30-slot mirror and publish it on every change.
    /// spec: Docs/RE/specs/handlers.md §4 (5/31 SmsgBuffSlotUpdate).
    /// spec: MartialHeroes.Client.Application.Hud.IHudEventHub.PublishBuffState.
    /// </summary>
    private void PublishBuffSlotUpdate(BuffSlotChangedEvent evt)
    {
        if (_hudHub is null) return;

        // Build a 30-slot snapshot; only the changed slot is non-zero in this incremental model.
        // A future 4/102 full-refresh handler will replace this with a proper complete snapshot.
        const int buffSlotCount = 30; // spec: Docs/RE/formats/misc_data.md §1.6 (30 icon slots)
        var builder = System.Collections.Immutable.ImmutableArray.CreateBuilder<BuffSlot>(buffSlotCount);
        for (int i = 0; i < buffSlotCount; i++)
        {
            if (i == evt.SlotIndex && evt.DurationTicks > 0)
                builder.Add(new BuffSlot((ushort)evt.EffectCode, (uint?)evt.DurationTicks));
            else
                builder.Add(new BuffSlot(BuffSlot.EmptyBuffId, null));
        }

        _hudHub.PublishBuffState(BuffStateEvent.FromSlots(builder.MoveToImmutable()));
    }
}