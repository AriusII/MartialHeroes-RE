using Godot;
using MartialHeroes.Client.Application.Engine;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Debug;
using MartialHeroes.Client.Godot.HUD;
using MartialHeroes.Client.Godot.Input;

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
/// New events handled in this wave:
///   - <see cref="WorldSnapshotEvent"/>     → ActorRegistry.OnWorldSnapshot (snapshot interpolation).
///   - <see cref="SectorLoadedEvent"/>      → TerrainNode.OnSectorLoaded.
///   - <see cref="SectorUnloadedEvent"/>    → TerrainNode.OnSectorUnloaded.
///
/// spec: Docs/RE/specs/game_loop.md §6 — "updates the spatial transforms of the associated
///       Node3D on the next frame".
/// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — "pump per-frame needs".
/// </summary>
public sealed partial class GameLoop : Node
{
    // -------------------------------------------------------------------------
    // Child node references (assigned in _Ready from the scene tree)
    // -------------------------------------------------------------------------

    private ActorRegistry _actorRegistry = null!;
    private GameHud _hud = null!;
    private InputRouter _inputRouter = null!;
    private SyntheticWorldFeeder _syntheticFeeder = null!;
    private ClientContext _clientContext = null!;
    private TerrainNode _terrainNode = null!;

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        // Resolve the autoload singleton.
        _clientContext = GetNode<ClientContext>("/root/ClientContext");

        // Resolve child nodes.
        _actorRegistry = GetNode<ActorRegistry>("ActorRegistry");
        _hud = GetNode<GameHud>("HUD");
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

        // Give subsystems their handles.
        _actorRegistry.Initialise(_clientContext);
        _hud.Initialise(_clientContext);

        // Wire InputRouter with bus from the composition root.
        _inputRouter.Initialise(_clientContext);
        _inputRouter.InitialiseBus(_clientContext.InputBus);

        // Start the synthetic feeder (fires and forgets onto a Task; publishes only through
        // the legitimate Application event bus — no game rules inside).
        _syntheticFeeder.StartAsync(_clientContext);

        GD.Print("[GameLoop] Ready.");
    }

    /// <summary>
    /// Drains the Application event channel on the main thread each frame.
    /// All pending events are processed synchronously within this call so that
    /// Godot node mutations happen safely on the main thread.
    ///
    /// spec: Docs/RE/specs/game_loop.md §6 — snapshot interpolation pipeline ends with
    ///       "updates the spatial transforms of the associated Node3D on the next frame".
    /// </summary>
    public override void _Process(double delta)
    {
        // Drain every event that arrived since the last frame.
        // TryRead never blocks; we stop when the queue is empty.
        while (_clientContext.EventBus.Reader.TryRead(out IClientEvent? evt))
        {
            DispatchEvent(evt);
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
                _hud.OnActorSpawned(spawned);
                break;

            case ActorMovedEvent moved:
                // Legacy fallback path — superseded by WorldSnapshotEvent when the engine loop runs.
                _actorRegistry.OnActorMoved(moved);
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
                _terrainNode.OnSectorLoaded(loaded);
                break;

            case SectorUnloadedEvent unloaded:
                _terrainNode.OnSectorUnloaded(unloaded);
                break;

            // ---- Client lifecycle ----
            case ClientStateChangedEvent stateChanged:
                _hud.OnClientStateChanged(stateChanged);
                break;

            default:
                // Unknown event type: ignore; new event types added by Application do not
                // require changes here unless we want to react to them.
                break;
        }
    }
}