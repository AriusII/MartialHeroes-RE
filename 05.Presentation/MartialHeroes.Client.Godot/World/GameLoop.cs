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
/// Events handled and their view targets:
///   - <see cref="WorldSnapshotEvent"/>          → ActorRegistry.OnWorldSnapshot
///   - <see cref="SectorLoadedEvent"/>           → TerrainNode.OnSectorLoaded
///   - <see cref="SectorUnloadedEvent"/>         → TerrainNode.OnSectorUnloaded
///   - <see cref="ActorSpawnedEvent"/>           → ActorRegistry.OnActorSpawned + GameHud.OnActorSpawned
///   - <see cref="ActorMovedEvent"/>             → ActorRegistry.OnActorMoved
///   - <see cref="ActorDespawnedEvent"/>         → ActorRegistry.OnActorDespawned
///   - <see cref="ActorVitalsChangedEvent"/>     → GameHud.OnActorVitalsChanged
///   - <see cref="ActorLeveledUpEvent"/>         → GameHud.OnActorLeveledUp
///   - <see cref="ActorStatSyncEvent"/>          → GameHud.OnActorStatSync
///   - <see cref="CombatStatsRecomputedEvent"/>  → GameHud.OnCombatStatsRecomputed
///   - <see cref="BuffSlotChangedEvent"/>        → GameHud.OnBuffSlotChanged
///   - <see cref="SkillHotbarSlotSetEvent"/>     → GameHud.OnSkillHotbarSlotSet
///   - <see cref="ChatBroadcastEvent"/>          → GameHud.OnChatBroadcast
///   - <see cref="ClientStateChangedEvent"/>     → GameHud.OnClientStateChanged
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
    private RealWorldRenderer? _realWorldRenderer;

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
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
    }

    /// <summary>The real _Ready body; separated so the defensive wrapper stays clean.</summary>
    private void ReadyInternal()
    {
        // Resolve the autoload singleton.
        _clientContext = GetNode<ClientContext>("/root/ClientContext");

        // Resolve child nodes — use HasNode for optional children so missing nodes don't crash.
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
        try
        {
            _actorRegistry.Initialise(_clientContext);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameLoop] ActorRegistry.Initialise failed: {ex.Message}");
        }

        try
        {
            _hud.Initialise(_clientContext);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameLoop] GameHud.Initialise failed: {ex.Message}");
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
    /// Drains the Application event channel on the main thread each frame.
    /// All pending events are processed synchronously within this call so that
    /// Godot node mutations happen safely on the main thread.
    ///
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

            // ---- Vitals / stats / level ----
            case ActorVitalsChangedEvent vitals:
                // spec: Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml.
                _hud.OnActorVitalsChanged(vitals);
                break;

            case ActorLeveledUpEvent levelUp:
                // spec: Docs/RE/packets/5-32_level_up.yaml.
                _hud.OnActorLeveledUp(levelUp);
                break;

            case ActorStatSyncEvent statSync:
                // spec: Docs/RE/specs/handlers.md §4 (5/67 SmsgStatsUpdate).
                _hud.OnActorStatSync(statSync);
                break;

            case CombatStatsRecomputedEvent combatStats:
                // spec: Docs/RE/specs/combat.md §1 / §2.
                _hud.OnCombatStatsRecomputed(combatStats);
                break;

            // ---- Buffs ----
            case BuffSlotChangedEvent buff:
                // spec: Docs/RE/specs/handlers.md §4 (5/31 SmsgBuffSlotUpdate).
                _hud.OnBuffSlotChanged(buff);
                break;

            // ---- Skill hotbar ----
            case SkillHotbarSlotSetEvent hotbar:
                // spec: Docs/RE/specs/handlers.md §4 (5/33 SmsgSkillHotbarSlotSet).
                _hud.OnSkillHotbarSlotSet(hotbar);
                break;

            // ---- Chat ----
            case ChatBroadcastEvent chat:
                // spec: Docs/RE/packets/5-7_chat_broadcast.yaml.
                _hud.OnChatBroadcast(chat);
                break;

            // ---- Client lifecycle ----
            case ClientStateChangedEvent stateChanged:
                _hud.OnClientStateChanged(stateChanged);
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
}