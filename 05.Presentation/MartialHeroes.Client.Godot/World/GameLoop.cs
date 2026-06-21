using Godot;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Application.Engine;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Input;
using MartialHeroes.Client.Godot.Ui.Hud;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
///     Root scene orchestrator. Owns the per-frame event drain and dispatches each
///     <see cref="IClientEvent" /> to the appropriate view subsystem.
///     Threading contract:
///     - The Application event channel may be written from any thread (the network reader,
///     the <see cref="GameEngineLoop" /> background task, or the inbound network dispatcher).
///     - ALL Godot node mutations happen on the Godot main thread, here in _Process or via
///     CallDeferred. No Node is touched from a background thread.
///     Events handled and their view targets (CAMPAIGN 17 Wave 2b — hub-fed HudMaster):
///     - <see cref="WorldSnapshotEvent" />          → ActorRegistry.OnWorldSnapshot
///     - <see cref="SectorLoadedEvent" />           → TerrainNode.OnSectorLoaded
///     - <see cref="SectorUnloadedEvent" />         → TerrainNode.OnSectorUnloaded
///     - <see cref="ActorSpawnedEvent" />           → ActorRegistry.OnActorSpawned
///     - <see cref="ActorMovedEvent" />             → ActorRegistry.OnActorMoved
///     - <see cref="ActorDespawnedEvent" />         → ActorRegistry.OnActorDespawned
///     - <see cref="ActorVitalsChangedEvent" />     → IHudEventHub.PublishVitals (→ HudRightEdgeGauge)
///     - <see cref="ActorLeveledUpEvent" />         → IHudEventHub.PublishVitals + PublishExpLevel
///     - <see cref="ActorStatSyncEvent" />          → IHudEventHub.PublishExpLevel
///     - <see cref="CombatStatsRecomputedEvent" />  → IHudEventHub.PublishVitals (max HP/MP update)
///     - <see cref="BuffSlotChangedEvent" />        → IHudEventHub.PublishBuffState
///     - <see cref="SkillHotbarSlotSetEvent" />     → TODO(hud-ii): needs a hub Hotbar channel
///     - <see cref="ChatBroadcastEvent" />          → IHudEventHub.PublishChatLine (→ HudChatPanel)
///     - <see cref="MartialHeroes.Client.Application.Contracts.Scene.SceneStateChangedEvent" /> → (no hub channel; handled
///     by SceneHost)
///     spec: Docs/RE/specs/game_loop.md §6 — "updates the spatial transforms of the associated
///     Node3D on the next frame".
///     spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — "pump per-frame needs".
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
    ///     Raised when the player leaves the world. <paramref name="logout" /> distinguishes the two
    ///     recovered exit transitions: <see langword="false" /> = leave-world → re-enter char-select
    ///     (state 4); <see langword="true" /> = logout → quit (state 6).
    ///     spec: Docs/RE/specs/client_runtime.md §7.5.1 / §7.5.3 / §7.9.5.
    /// </summary>
    [Signal]
    public delegate void WorldExitRequestedEventHandler(bool logout);

    // -------------------------------------------------------------------------
    // Child node references (assigned in _Ready from the scene tree)
    // -------------------------------------------------------------------------

    private ActorRegistry _actorRegistry = null!;
    private ClientContext _clientContext = null!;

    // Stage-B: world-side effect renderer (3D particle/placeholder casts).
    // spec: Docs/RE/specs/effects.md §15.3 — action codes 0xC8/0xC9/0xCB; CODE-CONFIRMED.
    private EffectRenderer? _effectRenderer;
    private bool _hasLocalPlayer;

    // Hub reference — published into each frame for every event that carries HUD-relevant data.
    // Stored separately from ClientContext to avoid repeated property access in the hot path.
    private IHudEventHub? _hudHub;

    // HudMaster reference — the sole, hub-fed in-game HUD (CAMPAIGN 17 Wave 2b).
    // GameLoop owns the reference so it can (a) wire the hit-test into HudInputHandler and
    // (b) delegate toggle keys (I/K/C) to the new substrate.
    // The HudMaster is built by InGameScene and passed in via SetHudMaster().
    private HudMaster? _hudMaster;
    private InputRouter _inputRouter = null!;

    // Local-player vitals view state (latest confirmed values — drives PublishVitals on every change).
    // No game logic: values are read from event payloads; GameLoop never computes them.
    private uint _localHp, _localMaxHp, _localMp, _localMaxMp, _localStam, _localMaxStam;

    // ---- Region tracking (for zone indicator) ----
    // Legacy world XZ of the local player; updated on ActorMoved/LocalPlayerSpawned.
    // Used to call RegionService.UpdatePosition each frame.
    // spec: Docs/RE/specs/world_systems.md Ch. 16 — 256-unit region grid.
    private float _localPlayerLegacyX;
    private float _localPlayerLegacyZ;

    private RealWorldRenderer? _realWorldRenderer;
    private TerrainNode _terrainNode = null!;

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        // *** INCONTOURNABLE — ce log apparaît avant TOUT le reste ***
        GD.Print("===== [GameLoop] _Ready ENTERED =====");

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
            // Real VFS is required — no synthetic fallback. Client renders nothing if assets are absent.
        }

        GD.Print("===== [GameLoop] _Ready COMPLETED =====");
    }

    /// <summary>
    ///     Called by InGameScene after it builds and adds HudMaster so GameLoop can wire the
    ///     hit-test into HudInputHandler (the "UI is the gate" gate).
    ///     spec: Docs/RE/specs/input_ui.md §3 / §6 — "UI hit-test always before world interaction".
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

        // Wire the terrain node into ActorRegistry so live actors from 4/4 are snapped to real
        // ground height as sectors become resident (debt #2 — eliminate fallback-Y race).
        // spec: Docs/RE/formats/terrain.md — TryGetGroundHeight bilinear ground height. CONFIRMED.
        // spec: Docs/RE/specs/world_systems.md — actor placement deferred until terrain resident.
        try
        {
            _actorRegistry.SetTerrainNode(_terrainNode);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameLoop] ActorRegistry.SetTerrainNode failed: {ex.Message}");
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

        // Real-asset rendering path. Activation via ClientPathResolver
        // (config file / env override / auto-detect). The real VFS is required — on a
        // failure the client renders nothing (there is no synthetic fallback).
        // spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive.
        var realRendererStarted = false;
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

                // Recover the 4/1 world entry that may have been published BEFORE this InGame scene existed.
                // The transient InGameWorldBootstrappedEvent is drained by whatever scene was active when 4/1
                // arrived (live: LoginScene/LoadScene), so the durable WorldEntryState is the reliable source
                // for the area cold-start on world-enter. The DispatchEvent InGameWorldBootstrappedEvent case
                // is KEPT for the rarer order where 4/1 arrives after InGame is already up; OnWorldEntered is
                // idempotent (guards against double area-load via _worldEntryInProgress / TargetAreaId), so
                // both orderings are safe.
                // spec: Docs/RE/specs/world_entry.md §2.3 / §3.1 — 4/1 AreaId cold-starts the area.
                if (_clientContext.WorldEntry is { IsActive: true } entry)
                {
                    GD.Print($"[GameLoop] InGameWorldBootstrappedEvent: server AreaId={entry.AreaId} " +
                             "(recovered from durable WorldEntryState — 3-digit dir → <id>.lst). " +
                             "spec: world_entry.md §2.3/§3.1.");
                    _realWorldRenderer.OnWorldEntered(entry.AreaId, entry.SpawnPosition);
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[GameLoop] RealWorldRenderer.Initialise failed: {ex}");
                GD.PrintErr(
                    "[GameLoop] RealWorldRenderer init failed — world will remain empty until VFS is resolved.");
                // Clean up the partially-added node if present.
                if (_realWorldRenderer is not null && IsInstanceValid(_realWorldRenderer))
                {
                    _realWorldRenderer.QueueFree();
                    _realWorldRenderer = null;
                }
            }
        }

        if (!realRendererStarted)
            // Real VFS absent — client renders nothing (faithful: the real client requires real assets).
            GD.Print("[GameLoop] Real assets unavailable — world will be empty until VFS is resolved.");

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
        _ = _clientContext.RegionService.LoadAreaAsync(0).AsTask().ContinueWith(
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
    ///     In-world leave-world / logout trigger. ESC (when chat is not capturing it — the InputRouter
    ///     consumes and marks ESC handled only while chat is active) requests a leave-world transition
    ///     back toward char-select (engine state 5 → 4). This is the world-exit hook the master scene
    ///     machine needs: the world is not a terminal scene.
    ///     spec: Docs/RE/specs/client_runtime.md §7.5.1 (5 → 4 natural return) / §7.9.5.
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
    ///     Single HUD key-command dispatcher. Routes I/K/O/C key presses to the correct window
    ///     Toggle() rather than having each panel install a competing top-level _Input grab.
    ///     Also routes Enter to the ChatWindow for focus (ChatWindow owns chat input exclusively).
    ///     Only active in-game (EngineSceneState.InGame).
    ///     spec: Docs/RE/specs/input_ui.md §3a / §5 — single dispatcher, panels are passive.
    ///     spec: Docs/RE/specs/ui_system.md §15 — in-game HUD key command dispatch.
    /// </summary>
    public override void _Input(InputEvent evt)
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
}