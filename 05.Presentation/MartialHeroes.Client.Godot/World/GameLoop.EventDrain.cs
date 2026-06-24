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
using MartialHeroes.Client.Presentation.Screens;
using MartialHeroes.Client.Presentation.World;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class GameLoop
{
    public override void _Process(double delta)
    {
        if (_clientContext is null) return;

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

        if (_hasLocalPlayer)
            try
            {
                _clientContext.RegionService.UpdatePosition(_localPlayerLegacyX, _localPlayerLegacyZ);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[GameLoop] RegionService.UpdatePosition failed: {ex.Message}");
            }

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

    private void OnTimedEvent(TimedEventRecord rec)
    {
        GD.Print($"[GameLoop] TimedEventQueue fired: eventId={rec.EventId} fireTime={rec.FireTime}ms. " +
                 "spec: Docs/RE/specs/effect-scheduling.md §5A.1.");
    }


    private void DispatchEvent(IClientEvent evt)
    {
        switch (evt)
        {
            case ActorSpawnedEvent spawned:
                _actorRegistry.OnActorSpawned(spawned);

            {
                var assets = _realWorldRenderer?.Assets;
                if (assets is not null)
                {
                    var nearbyVisual = _actorRegistry.TryGetActor(spawned.Key);
                    if (nearbyVisual is not null)
                    {
                        if (spawned.Key.Sort == EntitySort.PlayerCharacter)
                        {
                            var skinClass = spawned.InternalClass != 0 ? spawned.InternalClass : spawned.ServerClass;
                            var avatar = PlayerAvatarResolver.TryBuild(
                                assets, skinClass,
                                BridgeEquipGidsToParts(assets, spawned.EquipGids, skinClass,
                                    spawned.AppearanceVariant, skinClass));
                            if (avatar is not null)
                            {
                                nearbyVisual.AttachSkinnedAvatar(avatar);
                                WireCombatClipSource(nearbyVisual, assets, skinClass);
                            }
                            else
                            {
                                nearbyVisual.TryBuildBodyAvatar(assets, spawned.ServerClass);
                            }
                        }
                        else
                        {
                            nearbyVisual.TryBuildBodyAvatar(assets, spawned.ServerClass);
                        }
                    }
                }
            }

                break;

            case ActorMovedEvent moved:
                _actorRegistry.OnActorMoved(moved);
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

            case ActorVisualRefreshedEvent refreshed:
                _actorRegistry.OnActorVisualRefreshed(refreshed);
                break;

            case ActorDiedEvent died:
                _actorRegistry.OnActorDied(died);
                _hudMaster?.OnActorDied(died);
                break;

            case LocalPlayerStateSyncedEvent synced:
                if (synced.Mode != 5)
                {
                    var (sx, _, sz) = synced.Position.ToVector3Float();
                    _localPlayerLegacyX = sx;
                    _localPlayerLegacyZ = sz;
                }

                _actorRegistry.OnLocalPlayerStateSynced(synced);
                break;

            case HotbarInitializedEvent hotbar:
                _hudMaster?.OnHotbarInitialized(hotbar);
                break;

            case RosterSnapshotEvent roster:
                _hudMaster?.OnRosterSnapshot(roster);
                break;

            case SceneEntitySnapshotEvent sceneEnt:
                GD.Print($"[GameLoop] SceneEntitySnapshotEvent: actorSlots={sceneEnt.ActorSlots.Length} " +
                         $"categories={sceneEnt.Categories.Length}. " +
                         "No render surface yet — drained cleanly. " +
                         "spec: Docs/RE/specs/world_systems.md §13.3.");
                break;

            case WorldSnapshotEvent snapshot:
                _actorRegistry.OnWorldSnapshot(snapshot);
                break;

            case SectorLoadedEvent loaded:
                _terrainNode.OnSectorLoaded(loaded);
                _hudMaster?.OnSectorLoaded(loaded.MapX, loaded.MapZ);

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
                _realWorldRenderer?.OnSectorUnloaded(unloaded);
                _hudMaster?.OnSectorUnloaded(unloaded.MapX, unloaded.MapZ);
                break;

            case CellAssembledEvent cellEvt:
                GD.Print($"[GameLoop] CellAssembledEvent: cell=({cellEvt.Cell.MapX},{cellEvt.Cell.MapZ}) " +
                         $"resolved={cellEvt.Cell.IsResolved}. spec: assembly_graph.md §1.");
                _realWorldRenderer?.OnCellAssembled(cellEvt.Cell);
                break;

            case AreaAssembledEvent areaEvt:
                GD.Print($"[GameLoop] AreaAssembledEvent: area={areaEvt.Area.AreaId} " +
                         $"cellCount={areaEvt.Area.CellKeyCount}. spec: assembly_graph.md §1.");
                _realWorldRenderer?.OnAreaAssembled(areaEvt.Area);
                break;

            case ActorVitalsChangedEvent vitals:
                _localHp = vitals.CurrentHp;
                _localMp = vitals.CurrentMp;
                if (_localMaxHp == 0) _localMaxHp = vitals.CurrentHp;
                if (_localMaxMp == 0) _localMaxMp = vitals.CurrentMp;
                _hudHub?.PublishVitals(new HudVitalsEvent(
                    _localHp, _localMaxHp, _localMp, _localMaxMp, _localStam, _localMaxStam));
                break;

            case ActorLeveledUpEvent levelUp:
                _localHp = levelUp.CurrentHp;
                _localMp = levelUp.CurrentMp;
                _hudHub?.PublishVitals(new HudVitalsEvent(
                    _localHp, _localMaxHp, _localMp, _localMaxMp, _localStam, _localMaxStam));
                _hudHub?.PublishExpLevel(new ExpLevelEvent(0L, 0L, levelUp.NewLevel));
                break;

            case ActorStatSyncEvent statSync:
                _hudHub?.PublishExpLevel(new ExpLevelEvent(statSync.CurrentXp, 0L, 0));
                break;

            case CombatStatsRecomputedEvent combatStats:
            {
                var s = combatStats.Stats;
                if (s.MaxLife > 0) _localMaxHp = (uint)s.MaxLife;
                if (s.MaxEnergy > 0) _localMaxMp = (uint)s.MaxEnergy;
                _hudHub?.PublishVitals(new HudVitalsEvent(
                    _localHp, _localMaxHp, _localMp, _localMaxMp, _localStam, _localMaxStam));
                break;
            }

            case BuffSlotChangedEvent buff:
                PublishBuffSlotUpdate(buff);
                break;

            case SkillHotbarSlotSetEvent:
                break;

            case ChatBroadcastEvent:
                break;

            case SceneStateChangedEvent:
                break;

            case LocalPlayerSpawnedEvent localSpawn:
            {
                var (spawnX, _, spawnZ) = localSpawn.Position.ToVector3Float();
                _localPlayerLegacyX = spawnX;
                _localPlayerLegacyZ = spawnZ;
                _hasLocalPlayer = true;

                _localHp = localSpawn.CurrentHp;
                _localMaxHp = localSpawn.MaxHp;
                _hudHub?.PublishVitals(new HudVitalsEvent(
                    _localHp, _localMaxHp, _localMp, _localMaxMp, _localStam, _localMaxStam));
            }

                _actorRegistry.OnActorSpawned(new ActorSpawnedEvent(
                    localSpawn.Key,
                    localSpawn.Name,
                    localSpawn.Level,
                    localSpawn.Position,
                    localSpawn.CurrentHp,
                    localSpawn.MaxHp,
                    localSpawn.ServerClass));

                var localVisual = _actorRegistry.TryGetActor(localSpawn.Key);
                if (localVisual is not null)
                {
                    _realWorldRenderer?.SetLocalPlayer(localVisual);

                    localVisual.IsLocalPlayer = true;
                    var collMgr = _realWorldRenderer?.GetCellCollisionManager();
                    if (collMgr is not null)
                        localVisual.SetCollisionManager(collMgr);
                }

                TryAttachLocalPlayerAvatar(localSpawn.Key, localSpawn.ServerClass, localSpawn.InternalClass,
                    localSpawn.AppearanceVariant, localSpawn.EquipGids);

                GD.Print($"[GameLoop] LocalPlayerSpawnedEvent: name='{localSpawn.Name}' " +
                         $"level={localSpawn.Level} pos=({localSpawn.Position.RawX},{localSpawn.Position.RawZ}) " +
                         $"slot={localSpawn.SlotIndex} class={localSpawn.ServerClass}. spec: login_flow.md §3.5 / §5.3.");
                break;

            case LocalPlayerSpawnFailedEvent spawnFailed:
                GD.PrintErr($"[GameLoop] LocalPlayerSpawnFailedEvent: slot={spawnFailed.SlotIndex}. " +
                            "spec: Docs/RE/specs/login_flow.md §5.3 (Result 0 = failure).");
                break;

            case InGameWorldBootstrappedEvent worldBoot:
                GD.Print($"[GameLoop] InGameWorldBootstrappedEvent: server AreaId={worldBoot.AreaId} " +
                         $"clock={worldBoot.ServerHour:D2}:{worldBoot.ServerMinute:D2}. " +
                         "spec: world_entry.md §2.3/§3.1, packets/4-1_game_state_tick.yaml §fields.Hour/Minute.");
                if (_realWorldRenderer is not null)
                {
                    _realWorldRenderer.OnWorldEntered(worldBoot.AreaId, worldBoot.Position);
                    _realWorldRenderer.UpdateEnvironmentClock(worldBoot.ServerHour, worldBoot.ServerMinute);
                }
                else
                {
                    GD.Print("[GameLoop] InGameWorldBootstrappedEvent: RealWorldRenderer is null " +
                             "— server AreaId noted but no area re-target performed (VFS required).");
                }

                break;

            case ActorSkillActionEvent skillAction:
                _actorRegistry.PlayActorAttack(skillAction.AttackerKey);
                break;

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


    private void PublishBuffSlotUpdate(BuffSlotChangedEvent evt)
    {
        if (_hudHub is null) return;

        const int buffSlotCount = 30;
        var builder = ImmutableArray.CreateBuilder<BuffSlot>(buffSlotCount);
        for (var i = 0; i < buffSlotCount; i++)
            if (i == evt.SlotIndex && evt.DurationTicks > 0)
                builder.Add(new BuffSlot((ushort)evt.EffectCode, (uint?)evt.DurationTicks));
            else
                builder.Add(new BuffSlot(BuffSlot.EmptyBuffId, null));

        _hudHub.PublishBuffState(BuffStateEvent.FromSlots(builder.MoveToImmutable()));
    }


    private void TryAttachLocalPlayerAvatar(ActorKey key, ushort serverClass, ushort internalClass,
        byte appearanceVariant, ImmutableArray<uint> equipGids)
    {
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

        var skinClass = internalClass != 0 ? internalClass : serverClass;
        var equipParts = BridgeEquipGidsToParts(assets, equipGids, serverClass, appearanceVariant, skinClass);
        var avatar = PlayerAvatarResolver.TryBuild(assets, skinClass, equipParts);
        if (avatar is null)
        {
            GD.Print($"[GameLoop] Local-player avatar: class={serverClass} did not resolve a skinned " +
                     "avatar — keeping placeholder capsule. spec: skinning.md §8(e).");
            return;
        }

        visual.AttachSkinnedAvatar(avatar);
        WireCombatClipSource(visual, assets, skinClass);
        GD.Print($"[GameLoop] Local-player avatar attached (class={skinClass}, skinned+idle, " +
                 $"equipParts={equipParts.Count}). spec: skinning.md §8(e) / §10.5; equipment_visuals.md §1.1.");
    }

    private static void WireCombatClipSource(VisualActor visual, RealClientAssets assets, int skinClass)
    {
        var appearanceKey = ClassAppearanceResolver
            .StarterBodyModelClassId(skinClass);
        visual.SetCombatClipSource(assets, appearanceKey, skinClass);
    }

    private static IReadOnlyList<EquipmentPart> BridgeEquipGidsToParts(
        RealClientAssets assets, ImmutableArray<uint> equipGids, ushort serverClass,
        int variantField, int classField)
    {
        if (equipGids.IsDefaultOrEmpty) return [];

        ReadOnlySpan<int> overlaySlots = [3, 4, 6, 2, 11, 14];

        var parts = new List<EquipmentPart>(overlaySlots.Length);
        for (var i = 0; i < overlaySlots.Length && i < equipGids.Length; i++)
        {
            var slot = overlaySlots[i];
            var equipGid = (int)equipGids[i];
            if (equipGid == 0) continue;

            if (slot == EquipOverlayResolver.WeaponSlot)
            {
                var weaponGid = WeaponGidResolver.Resolve(equipGid, variantField, classField);
                var weaponSkn = $"data/char/skin/g{weaponGid}.skn";
                if (weaponGid <= 0 || !assets.Contains(weaponSkn))
                {
                    GD.Print($"[GameLoop] Weapon (slot 14, equipGid={equipGid}) resolved gid={weaponGid} " +
                             $"-> '{weaponSkn}' absent in VFS (the B weapon-appearance digit is not surfaced " +
                             $"on the spawn event; class={classField} variant={variantField}); not fabricated. " +
                             "spec: equipment_visuals.md §3.1/§5.");
                    continue;
                }

                parts.Add(new EquipmentPart
                {
                    Slot = slot,
                    EquipmentGid = equipGid,
                    MeshGid = weaponGid,
                    TextureId = 0,
                    IsHandWeapon = true,
                    IsOffHand = false
                });
                continue;
            }

            var meshGid = (int)EquipOverlayResolver.ResolveNonWeaponGid(equipGid);

            parts.Add(new EquipmentPart
            {
                Slot = slot,
                EquipmentGid = equipGid,
                MeshGid = meshGid,
                TextureId = 0,
                IsHandWeapon = false,
                IsOffHand = false
            });
        }

        return parts;
    }

    private static class WeaponGidResolver
    {
        public static int Resolve(int partActorId, int variantField, int classField)
        {
            if (partActorId <= 0) return 0;

            var b = 0;
            var c = variantField;
            var d = classField;

            var weaponGid = 1000L * (b + 10L * (c + 10L * (d + 10L * (partActorId / 1000000L))));
            var gid = weaponGid + partActorId % 1000L;
            return gid is > 0 and < int.MaxValue ? (int)gid : 0;
        }
    }
}