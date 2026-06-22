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
using MartialHeroes.Client.Domain.Simulation.Simulation;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Presentation.World;

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

        // ---- TimedEventQueue drain (two-pass full-tree sweep) ----
        // Drain the universal "10001" deferred timed-event queue every frame.
        // The queue fires every entry with fire_time < now_ms (full-tree sweep; no early stop).
        // spec: Docs/RE/specs/effect-scheduling.md §5A.3 — two-pass full-tree sweep; no early stop.
        try
        {
            var nowMs = (long)Time.GetTicksMsec();
            _clientContext.TimedEventQueue.Drain(nowMs, _onTimedEventDelegate);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameLoop] TimedEventQueue.Drain error: {ex.Message}");
        }
    }

    /// <summary>
    ///     Handles a fired timed-event record from the <see cref="TimedEventQueue" />.
    ///     Currently logs the bare 10001 trigger; future deferred scene/connection actions route here.
    ///     spec: Docs/RE/specs/effect-scheduling.md §5A.1 — SceneConnectionEventId = 10001.
    /// </summary>
    private void OnTimedEvent(TimedEventRecord rec)
    {
        // spec: Docs/RE/specs/effect-scheduling.md §5A.1 — event_id 10001 = generic scene/connection trigger.
        GD.Print($"[GameLoop] TimedEventQueue fired: eventId={rec.EventId} fireTime={rec.FireTime}ms. " +
                 "spec: Docs/RE/specs/effect-scheduling.md §5A.1.");
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
                // Place the VisualActor node in the scene tree.
                _actorRegistry.OnActorSpawned(spawned);

                // NEARBY ACTOR body-avatar build (spec: Docs/RE/specs/skinning.md §8(e)).
                // The local-player path calls _actorRegistry.OnActorSpawned DIRECTLY (from
                // LocalPlayerSpawnedEvent) then TryAttachLocalPlayerAvatar — it does NOT flow
                // through this DispatchEvent ActorSpawnedEvent arm. Nearby actors DO arrive here
                // (from GamePacketHandler.World 4/4 tag-1/2/3 paths). So: build body avatar here
                // for ALL actors that come through DispatchEvent, because the local-player path
                // bypasses this arm entirely — there is no double-build risk.
                // Guard: assets must be non-null (VFS required for skin loading).
                // spec: Docs/RE/specs/skinning.md §8(e) — body-only build; ActorSpawnedEvent carries no EquipGids.
            {
                var assets = _realWorldRenderer?.Assets;
                if (assets is not null)
                {
                    var nearbyVisual = _actorRegistry.TryGetActor(spawned.Key);
                    if (nearbyVisual is not null) nearbyVisual.TryBuildBodyAvatar(assets, spawned.ServerClass);
                }
            }

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

            // ---- 4/4 KindByte==5 lightweight in-place visual refresh ----
            // spec: Docs/RE/structs/actor.md (4/4 KindByte==5 = visual-only refresh; NOT a respawn).
            case ActorVisualRefreshedEvent refreshed:
                _actorRegistry.OnActorVisualRefreshed(refreshed);
                break;

            // ---- 5/10 death — victim motion + HUD modal ----
            // victim motion: ActorRegistry.OnActorDied → visual.PlayDeathMotion().
            // HUD modal: HudMaster.OnActorDied decides respawn modal for the LOCAL player.
            // spec: Docs/RE/packets/5-10_combat_death.yaml (death cause {0,1,2,3}; 5/0 despawn is separate).
            case ActorDiedEvent died:
                _actorRegistry.OnActorDied(died);
                _hudMaster?.OnActorDied(died);
                break;

            // ---- 4/13 local-player state sync — track legacy XZ then reconcile transform ----
            // Update local-player legacy XZ for RegionService zone polling first (matches the
            // ActorMovedEvent pattern that already does the same), then delegate transform
            // reconcile (snap vs smooth glide) to ActorRegistry.
            // spec: Docs/RE/packets/4-13_local_player_state_sync.yaml
            //       (>200-unit delta = teleport; mode 5 = no write).
            // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — legacy XZ for region grid lookup.
            case LocalPlayerStateSyncedEvent synced:
                if (synced.Mode != 5) // mode 5 = no write; don't update tracking if skipped.
                {
                    var (sx, _, sz) = synced.Position.ToVector3Float();
                    _localPlayerLegacyX = sx;
                    _localPlayerLegacyZ = sz;
                }

                _actorRegistry.OnLocalPlayerStateSynced(synced);
                break;

            // ---- 4/1 world-entry hotbar snapshot ----
            // spec: Docs/RE/packets/4-1_game_state_tick.yaml (HotbarSlots note — EntryKey raw, category-pending).
            case HotbarInitializedEvent hotbar:
                _hudMaster?.OnHotbarInitialized(hotbar);
                break;

            // ---- 4/1 world-entry roster (Table A) ----
            // spec: Docs/RE/specs/world_systems.md §13.3 — WorldEntryTableA roster model.
            case RosterSnapshotEvent roster:
                _hudMaster?.OnRosterSnapshot(roster);
                break;

            // ---- 4/1 world-entry scene-entity state (Table B) ----
            // There is NO HUD/world render surface for this yet — drain cleanly with a summary log.
            // spec: Docs/RE/specs/world_systems.md §13.3 — WorldEntryTableB tracked-entity state + categories.
            case SceneEntitySnapshotEvent sceneEnt:
                GD.Print($"[GameLoop] SceneEntitySnapshotEvent: actorSlots={sceneEnt.ActorSlots.Length} " +
                         $"categories={sceneEnt.Categories.Length}. " +
                         "No render surface yet — drained cleanly. " +
                         "spec: Docs/RE/specs/world_systems.md §13.3.");
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
            case ChatBroadcastEvent:
                // GamePacketHandler.Chat.HandleChatBroadcast ALREADY calls
                // _hudEventHub.PublishChatLine with the correct per-channel ARGB colour
                // (ResolveChatColour: code 7=pink 0xFFFF797C, code 10=yellow, 16/17=red).
                // Publishing again here with the hardcoded SayColorArgb (white) would OVERWRITE
                // the correct colour. ChatBroadcastEvent carries no ColorArgb field, so this layer
                // MUST NOT re-derive the colour — that authority belongs to the core handler.
                // Silently consume the bus event (it is published for other layer-05 subscribers).
                // spec: Docs/RE/specs/chat.md §3 (channel → colour table).
                // spec: Docs/RE/packets/5-7_chat_broadcast.yaml (HandleChatBroadcast publishes first).
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
                // NpcRenderer uses (skin → bind → idle .mot), now WITH the equipment overlay: the core
                // surfaces the local player's six visible-gear GIDs on LocalPlayerSpawnedEvent.EquipGids
                // (the +0x58 descriptor table, fixed slot set {3,4,6,2,11,14}). Reuses the already-open VFS
                // handle (no second mmap). When VFS is absent the VisualActor renders nothing (no fallback
                // geometry). STRICTLY PASSIVE: which clip plays is the idle the chain resolves;
                // PlayStandingIdle is auto-engaged inside Build.
                // spec: Docs/RE/specs/skinning.md §8(e) (skin/bind/idle chain; g{SkinClassId}.bnd for {1..4}),
                //       §10 (col16 default standing idle plays looping; static look is faithful data).
                // spec: Docs/RE/specs/equipment_visuals.md §1.1/§3 (six-slot {3,4,6,2,11,14} overlay).
                // spec: World/PlayerAvatarResolver.cs / World/VisualActor.cs (AttachSkinnedAvatar swap).
                TryAttachLocalPlayerAvatar(localSpawn.Key, localSpawn.ServerClass, localSpawn.EquipGids);

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
    ///     Builds the in-world local player's skinned, idle-animated avatar from its class, composes its
    ///     equipment overlay from the surfaced gear GIDs, and attaches it to the freshly-spawned
    ///     <see cref="VisualActor" />.
    ///     Resolution: <paramref name="serverClass" /> is the wire character class == the .skn header
    ///     SkinClassId / id_b ∈ {1,2,3,4};
    ///     <see cref="PlayerAvatarResolver.TryBuild(RealClientAssets, ushort, IReadOnlyList{EquipmentPart})" />
    ///     walks the recovered chain (skinlist .skn whose IdB == class → g{class}.bnd → actormotion
    ///     col2 == class → col16 idle .mot), composes the resolved overlay parts onto the shared skeleton,
    ///     and returns a build root passed to <see cref="VisualActor.AttachSkinnedAvatar" />.
    ///     <paramref name="equipGids" /> are the six visible-gear GIDs (fixed overlay slots
    ///     <c>{3,4,6,2,11,14}</c>, slot id == array index) the core decoded from the +0x58 descriptor table;
    ///     <see cref="BridgeEquipGidsToParts" /> reduces each non-weapon GID to its mesh GID via the
    ///     engine-free <see cref="EquipOverlayResolver" /> (the SAME math the char-select
    ///     <c>SlotAppearanceResolver</c> path uses) into <see cref="EquipmentPart" />s for the resolver.
    ///     VFS-safe: when <see cref="RealWorldRenderer" /> is null or its VFS handle is null (VFS absent),
    ///     this is a no-op and the VisualActor renders nothing. <paramref name="equipGids" />
    ///     default/empty ⇒ body only (faithful no-op). Never throws (the resolver guards every step).
    ///     spec: Docs/RE/specs/skinning.md §8(e) (skin/bind/idle chain; g{SkinClassId}.bnd for {1,2,3,4}),
    ///     §10 (the col16 default standing idle plays, looping);
    ///     Docs/RE/specs/equipment_visuals.md §1.1/§3 (six-slot {3,4,6,2,11,14} overlay; non-weapon GID math).
    /// </summary>
    /// <param name="key">The local player's composite actor identity (looks up its VisualActor).</param>
    /// <param name="serverClass">Wire character class == SkinClassId ∈ {1,2,3,4}.</param>
    /// <param name="equipGids">
    ///     The six visible-gear GIDs for overlay slots <c>{3,4,6,2,11,14}</c> (slot id == array index),
    ///     decoded by the core from the +0x58 equip table. Default/empty ⇒ body only.
    ///     spec: Docs/RE/structs/spawn_descriptor.md (+0x58); Docs/RE/specs/equipment_visuals.md §1.1.
    /// </param>
    private void TryAttachLocalPlayerAvatar(ActorKey key, ushort serverClass, ImmutableArray<uint> equipGids)
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

        // The core now carries the local player's six visible-gear GIDs on LocalPlayerSpawnedEvent.EquipGids
        // (the +0x58 descriptor table, fixed slot set {3,4,6,2,11,14}), surfaced on BOTH the 3/14 and 4/1
        // spawn paths. Bridge those raw GIDs into the resolved EquipmentParts the equip-aware 3-arg
        // PlayerAvatarResolver.TryBuild consumes: BridgeEquipGidsToParts reduces each non-weapon GID to its
        // mesh GID via the engine-free EquipOverlayResolver (the SAME GID math the char-select
        // SlotAppearanceResolver path uses), so body + gear render as ONE composed avatar under the shared
        // skeleton. The weapon (slot 14) is DEFERRED (its mesh GID needs appearance digits this event does
        // not surface, and the hand bone-id is debugger-pending) — flagged, never fabricated.
        // An empty/default EquipGids → empty parts → body-only (faithful no-op, no crash).
        // spec: Docs/RE/specs/equipment_visuals.md §1.1/§3 (six-slot overlay; non-weapon GID scale-10000) /
        //       §5 (weapon hand-bone deferred); Docs/RE/specs/skinning.md §8(e).
        var equipParts = BridgeEquipGidsToParts(equipGids, serverClass);
        var avatar = PlayerAvatarResolver.TryBuild(assets, serverClass, equipParts);
        if (avatar is null)
        {
            // The body .skn for this class did not resolve — VisualActor keeps the capsule (no crash).
            GD.Print($"[GameLoop] Local-player avatar: class={serverClass} did not resolve a skinned " +
                     "avatar — keeping placeholder capsule. spec: skinning.md §8(e).");
            return;
        }

        visual.AttachSkinnedAvatar(avatar);
        GD.Print($"[GameLoop] Local-player avatar attached (class={serverClass}, skinned+idle, " +
                 $"equipParts={equipParts.Count}). spec: skinning.md §8(e) / §10.5; equipment_visuals.md §1.1.");
    }

    /// <summary>
    ///     Bridges the core's raw local-player equip GIDs (<see cref="LocalPlayerSpawnedEvent.EquipGids" />,
    ///     fixed overlay slots <c>{3,4,6,2,11,14}</c>, slot id == array index) into the resolved
    ///     <see cref="EquipmentPart" />s the equip-aware
    ///     <see cref="PlayerAvatarResolver.TryBuild(RealClientAssets, ushort, IReadOnlyList{EquipmentPart})" />
    ///     consumes. This closes the layer-04→05 TYPE GAP: the core carries only raw GIDs (its
    ///     <c>LocalPlayerSpawnedEvent</c> lives in <c>Application.Contracts</c>, which cannot reference the
    ///     <c>Application.World.EquipmentPart</c> type), so the per-part GID→mesh-GID reduction runs HERE,
    ///     off the engine-free <see cref="EquipOverlayResolver" /> — the SAME deterministic GID math the
    ///     char-select <c>SlotAppearanceResolver</c> path uses (no catalogue indirection, no key64
    ///     duplication: the non-weapon reduction is <see cref="EquipOverlayResolver.ResolveNonWeaponGid" />).
    ///     <para>
    ///         Non-weapon slots <c>{3,4,6,2,11}</c>: <c>mesh_gid = ResolveNonWeaponGid(equipGid)</c>, emitted
    ///         as a deform <see cref="EquipmentPart" /> (<see cref="EquipmentPart.IsHandWeapon" /> = false);
    ///         the downstream <see cref="EquipmentPartResolver" /> loads <c>data/char/skin/g{mesh_gid}.skn</c>
    ///         and a genuinely-absent <c>.skn</c> is skipped (null mesh, no crash). The hand bone-id is the
    ///         deferred default (0) inside that resolver — never fabricated here.
    ///     </para>
    ///     <para>
    ///         WEAPON slot 14 is DEFERRED (flagged, NOT fabricated): the weapon mesh GID needs the §3.1
    ///         appearance digits (b/c/d) that <see cref="LocalPlayerSpawnedEvent" /> does not surface, so the
    ///         weapon part is NOT emitted — composing it would require inventing those digits. A non-zero
    ///         slot-14 GID is logged as deferred. spec: Docs/RE/specs/equipment_visuals.md §3.1 (weapon GID
    ///         digit formula) / §5 (weapon hand-bone deferred, bone-id 0 debugger-pending).
    ///     </para>
    ///     An empty/default <paramref name="equipGids" /> yields an empty list ⇒ body only (faithful no-op).
    ///     Never throws. spec: Docs/RE/specs/equipment_visuals.md §1.1 / §3.2 / §3.4 (six-slot set,
    ///     non-weapon scale-10000 GID); Docs/RE/structs/spawn_descriptor.md (+0x58, slot id == array index).
    /// </summary>
    /// <param name="equipGids">The six visible-gear GIDs, slot id == array index (default/empty ⇒ none).</param>
    /// <param name="serverClass">Wire class == SkinClassId (diagnostics only — GID math needs no class here).</param>
    private static IReadOnlyList<EquipmentPart> BridgeEquipGidsToParts(
        ImmutableArray<uint> equipGids, ushort serverClass)
    {
        // Default/empty (the core surfaced no descriptor / no worn gear) → body only (faithful).
        // spec: Docs/RE/specs/equipment_visuals.md §1.1 (empty overlay set = body only).
        if (equipGids.IsDefaultOrEmpty) return [];

        // The fixed overlay-slot list, array index == slot id, IDENTICAL to the core's iteration order and
        // EquipOverlayResolver.LocalPlayerRebuildSlots. spec: Docs/RE/specs/equipment_visuals.md §1.1 / §3.4.
        ReadOnlySpan<int> overlaySlots = [3, 4, 6, 2, 11, 14]; // spec: equipment_visuals.md §1.1

        var parts = new List<EquipmentPart>(overlaySlots.Length);
        for (var i = 0; i < overlaySlots.Length && i < equipGids.Length; i++)
        {
            var slot = overlaySlots[i];
            var equipGid = (int)equipGids[i];
            if (equipGid == 0) continue; // empty slot → no node. spec: equipment_visuals.md §3 (gid 0 = empty).

            if (slot == EquipOverlayResolver.WeaponSlot)
            {
                // WEAPON (slot 14) DEFERRED: ResolveWeaponGid needs the §3.1 appearance digits (b/c/d) the
                // LocalPlayerSpawnedEvent does not carry. Do NOT fabricate them — flag and skip. The weapon
                // overlay is gated on the core surfacing those digits (or a resolved weapon mesh GID).
                // spec: Docs/RE/specs/equipment_visuals.md §3.1 (weapon GID digit formula) / §5.
                GD.Print($"[GameLoop] Local-player weapon (slot 14, gid={equipGid}) DEFERRED — needs §3.1 " +
                         $"appearance digits not on LocalPlayerSpawnedEvent (class={serverClass}); not fabricated. " +
                         "spec: equipment_visuals.md §3.1/§5.");
                continue;
            }

            // Non-weapon {3,4,6,2,11}: mesh_gid = 10000*(equipGid/10000) + equipGid%100. The SAME engine-free
            // reduction the char-select path applies (ClassAppearanceResolver.ResolveWornItemGid is the twin
            // formula); reusing EquipOverlayResolver keeps the in-world path single-sourced.
            // spec: Docs/RE/specs/equipment_visuals.md §3.2 / §3.4 (non-weapon GID scale 10000).
            var meshGid = (int)EquipOverlayResolver.ResolveNonWeaponGid(equipGid);

            parts.Add(new EquipmentPart
            {
                Slot = slot,
                EquipmentGid = equipGid,
                MeshGid = meshGid,
                TextureId = 0, // tex_id resolves from the part .skn header IdA downstream (no per-slot tex here)
                IsHandWeapon = false, // non-weapon deform overlay under the shared skeleton. spec: §4.
                IsOffHand = false
            });
        }

        return parts;
    }
}