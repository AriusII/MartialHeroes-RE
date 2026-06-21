// World/GameLoop.EventDrain.cs
//
// Partial class — the per-frame Application channel/event-bus drain (_Process + DispatchEvent),
// hub publication helpers, and the local-player skinned-avatar attachment.
// See GameLoop.cs for the full file description and all spec cites.
//
// spec: Docs/RE/specs/game_loop.md §6 — per-frame drain + snapshot interpolation.
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive.

using System.Collections.Immutable;
using Godot;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Application.Contracts.Scene;
using MartialHeroes.Client.Application.Engine;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Actors.Actors;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class GameLoop
{
    // -------------------------------------------------------------------------
    // Per-frame channel drain
    // -------------------------------------------------------------------------

    /// <summary>
    ///     spec: Docs/RE/specs/game_loop.md §6 — snapshot interpolation pipeline ends with
    ///     "updates the spatial transforms of the associated Node3D on the next frame".
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
            while (_clientContext.EventBus.Reader.TryRead(out var evt))
                try
                {
                    DispatchEvent(evt);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[GameLoop] DispatchEvent error ({evt?.GetType().Name}): {ex.Message}");
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
            try
            {
                _clientContext.RegionService.UpdatePosition(_localPlayerLegacyX, _localPlayerLegacyZ);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[GameLoop] RegionService.UpdatePosition failed: {ex.Message}");
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
                if (_hasLocalPlayer && moved.Key.Sort == EntitySort.PlayerCharacter)
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

            // ---- Phase 6a / CYCLE 2 Phase 2-A: assembled cell/area events ----
            case CellAssembledEvent cellEvt:
                // A fully assembled cell (all 9 slots: .ted/.bud/.fx1-7) is now available.
                // Phase 6a: log for headless verification.
                // CYCLE 2 Phase 2-A: when compose_render is on, forward to RealWorldRenderer
                // which renders terrain/buildings FROM the composer output (not the direct VFS path).
                // spec: Docs/RE/specs/assembly_graph.md §1 — assembled cell ready for presentation.
                GD.Print($"[GameLoop] CellAssembledEvent: cell=({cellEvt.Cell.MapX},{cellEvt.Cell.MapZ}) " +
                         $"resolved={cellEvt.Cell.IsResolved}. spec: assembly_graph.md §1.");
                _realWorldRenderer?.OnCellAssembled(cellEvt.Cell);
                break;

            case AreaAssembledEvent areaEvt:
                // Phase 6a: full area assembled. Log for headless verification.
                // CYCLE 2 Phase 2-A: forward to RealWorldRenderer for composer-path rendering.
                // spec: Docs/RE/specs/assembly_graph.md §1 — area load (Phase A).
                GD.Print($"[GameLoop] AreaAssembledEvent: area={areaEvt.Area.AreaId} " +
                         $"cellCount={areaEvt.Area.CellKeyCount}. spec: assembly_graph.md §1.");
                _realWorldRenderer?.OnAreaAssembled(areaEvt.Area);
                break;

            // ---- Vitals / stats / level ----
            case ActorVitalsChangedEvent vitals:
                // Publish into the HUD hub so HudRightEdgeGauge (and future panels) drain live data.
                // spec: Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml.
                // spec: MartialHeroes.Client.Application.Contracts.Hud.IHudEventHub.PublishVitals.
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
                // spec: MartialHeroes.Client.Application.Contracts.Hud.IHudEventHub.PublishChatLine.
                _hudHub?.PublishChatLine(new ChatLineEvent(
                    chat.Channel,
                    chat.Text,
                    ChatLineEvent.SayColorArgb,
                    chat.SenderName));
                break;

            // ---- Scene lifecycle ----
            case SceneStateChangedEvent:
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

                // Register the live local player with RealWorldRenderer so the camera-follow and the
                // player-following terrain streaming track the REAL (server-spawned) player. There is no
                // offline demo character to follow; this is the sole follow target.
                // spec: World/RealWorldRenderer.cs SetLocalPlayer; resource_pipeline.md §4.3.
                var localVisual = _actorRegistry.TryGetActor(localSpawn.Key);
                if (localVisual is not null)
                    _realWorldRenderer?.SetLocalPlayer(localVisual);

                // Build the skinned, idle-animated avatar for the LOCAL PLAYER from the player's class
                // (ServerClass == .skn header SkinClassId / id_b ∈ {1..4}) via the SAME recovered chain
                // NpcRenderer uses (skin → bind → idle .mot), and attach it to the VisualActor. Reuses the
                // already-open VFS handle (no second mmap). When VFS is absent (RealWorldRenderer null /
                // Assets null) the VisualActor renders nothing (no fallback geometry). STRICTLY PASSIVE:
                // which clip plays is the idle the chain resolves; PlayStandingIdle is auto-engaged inside Build.
                // spec: Docs/RE/specs/skinning.md §8(e) (skin/bind/idle chain; g{SkinClassId}.bnd for {1..4}),
                //       §10 (col16 default standing idle plays looping; static look is faithful data).
                // spec: World/PlayerAvatarResolver.cs / World/VisualActor.cs (AttachSkinnedAvatar swap).
                TryAttachLocalPlayerAvatar(localSpawn.Key, localSpawn.ServerClass);

                GD.Print($"[GameLoop] LocalPlayerSpawnedEvent: name='{localSpawn.Name}' " +
                         $"level={localSpawn.Level} pos=({localSpawn.Position.RawX},{localSpawn.Position.RawZ}) " +
                         $"slot={localSpawn.SlotIndex} class={localSpawn.ServerClass}. spec: login_flow.md §3.5 / §5.3.");
                break;

            case LocalPlayerSpawnFailedEvent spawnFailed:
                // Spawn failure: log for diagnostics; BootFlow already transitioned to World
                // so we show the failure in-world (timed message). spec: login_flow.md §5.3.
                GD.PrintErr($"[GameLoop] LocalPlayerSpawnFailedEvent: slot={spawnFailed.SlotIndex}. " +
                            "spec: Docs/RE/specs/login_flow.md §5.3 (Result 0 = failure).");
                break;

            // ---- World bootstrap (4/1 world-entry carrier) ----
            case InGameWorldBootstrappedEvent worldBoot:
                // The 4/1 SmsgGameStateTick world-entry form supplies AreaId at body offset 12.
                // The AreaId rendered as a zero-padded 3-digit decimal selects the on-disk area
                // directory (e.g. 6 → "006") and cold-starts the area.
                // spec: Docs/RE/specs/world_entry.md §2.3 — 4/1 reads AreaId, cold-starts the area
                //        by its 3-digit decimal directory.
                // spec: Docs/RE/specs/world_entry.md §3.1 — AreaId → zero-padded 3-digit dir → <id>.lst.
                GD.Print($"[GameLoop] InGameWorldBootstrappedEvent: server AreaId={worldBoot.AreaId} " +
                         "(3-digit dir → <id>.lst). spec: world_entry.md §2.3/§3.1.");
                if (_realWorldRenderer is not null)
                    // Drive the server area cold-start. If assets are unavailable, OnWorldEntered
                    // logs and returns — the client renders nothing (requires real VFS).
                    _realWorldRenderer.OnWorldEntered(worldBoot.AreaId, worldBoot.Position);
                else
                    // RealWorldRenderer is null (real assets absent): no area re-target.
                    GD.Print("[GameLoop] InGameWorldBootstrappedEvent: RealWorldRenderer is null " +
                             "— server AreaId noted but no area re-target performed (VFS required).");

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
        }
    }

    // -------------------------------------------------------------------------
    // Hub publication helpers
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Publishes a buff-slot update as a latest-wins snapshot on the hub BuffStates channel.
    ///     Since IHudEventHub does not expose per-slot incremental writes (only full 30-slot refreshes),
    ///     we maintain a local 30-slot mirror and publish it on every change.
    ///     spec: Docs/RE/specs/handlers.md §4 (5/31 SmsgBuffSlotUpdate).
    ///     spec: MartialHeroes.Client.Application.Contracts.Hud.IHudEventHub.PublishBuffState.
    /// </summary>
    private void PublishBuffSlotUpdate(BuffSlotChangedEvent evt)
    {
        if (_hudHub is null) return;

        // Build a 30-slot snapshot; only the changed slot is non-zero in this incremental model.
        // A future 4/102 full-refresh handler will replace this with a proper complete snapshot.
        const int buffSlotCount = 30; // spec: Docs/RE/formats/misc_data.md §1.6 (30 icon slots)
        var builder = ImmutableArray.CreateBuilder<BuffSlot>(buffSlotCount);
        for (var i = 0; i < buffSlotCount; i++)
            if (i == evt.SlotIndex && evt.DurationTicks > 0)
                builder.Add(new BuffSlot((ushort)evt.EffectCode, (uint?)evt.DurationTicks));
            else
                builder.Add(new BuffSlot(BuffSlot.EmptyBuffId, null));

        _hudHub.PublishBuffState(BuffStateEvent.FromSlots(builder.MoveToImmutable()));
    }

    // -------------------------------------------------------------------------
    // Local-player skinned-avatar attachment (retires the static-capsule debt)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Builds the in-world local player's skinned, idle-animated avatar from its class and attaches it
    ///     to the freshly-spawned <see cref="VisualActor" />.
    ///     Resolution: <paramref name="serverClass" /> is the wire character class == the .skn header
    ///     SkinClassId / id_b ∈ {1,2,3,4}; <see cref="PlayerAvatarResolver.TryBuild" /> walks the recovered
    ///     chain (skinlist .skn whose IdB == class → g{class}.bnd → actormotion col2 == class → col16 idle
    ///     .mot) and returns a <see cref="SkinnedCharacterBuilder.Build" /> root passed to
    ///     <see cref="VisualActor.AttachSkinnedAvatar" />.
    ///     VFS-safe: when <see cref="RealWorldRenderer" /> is null or its VFS handle is null (VFS absent),
    ///     this is a no-op and the VisualActor renders nothing. Never throws (the resolver guards every step).
    ///     spec: Docs/RE/specs/skinning.md §8(e) (skin/bind/idle chain; g{SkinClassId}.bnd for {1,2,3,4}),
    ///     §10 (the col16 default standing idle plays, looping).
    /// </summary>
    /// <param name="key">The local player's composite actor identity (looks up its VisualActor).</param>
    /// <param name="serverClass">Wire character class == SkinClassId ∈ {1,2,3,4}.</param>
    private void TryAttachLocalPlayerAvatar(ActorKey key, ushort serverClass)
    {
        // Need the open VFS handle — reuse RealWorldRenderer's (no second mmap). Offline → keep capsule.
        var assets = _realWorldRenderer?.Assets;
        if (assets is null)
        {
            GD.Print("[GameLoop] Local-player avatar: VFS handle unavailable (offline / real assets " +
                     "disabled) — keeping placeholder capsule. spec: skinning.md §8(e).");
            return;
        }

        var visual = _actorRegistry.TryGetActor(key);
        if (visual is null)
        {
            GD.PrintErr("[GameLoop] Local-player avatar: VisualActor not found after spawn — " +
                        "keeping placeholder. spec: login_flow.md §5.3.");
            return;
        }

        var avatar = PlayerAvatarResolver.TryBuild(assets, serverClass);
        if (avatar is null)
        {
            // The body .skn for this class did not resolve — VisualActor keeps the capsule (no crash).
            GD.Print($"[GameLoop] Local-player avatar: class={serverClass} did not resolve a skinned " +
                     "avatar — keeping placeholder capsule. spec: skinning.md §8(e).");
            return;
        }

        visual.AttachSkinnedAvatar(avatar);
        GD.Print($"[GameLoop] Local-player avatar attached (class={serverClass}, skinned+idle). " +
                 "spec: skinning.md §8(e) / §10.5.");
    }
}