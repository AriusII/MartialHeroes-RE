using System.Buffers.Binary;
using System.Runtime.InteropServices;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Network.Protocol.Packets.World.Packets;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    // -------------------------------------------------------------------------
    // 4/1 — game-state tick / world-entry snapshot
    // -------------------------------------------------------------------------

    /// <summary>
    ///     4/1 — world-entry game-state tick. On form 1, reads spawn X/Z from the large 9100-byte body,
    ///     creates the local player from the cached select descriptor when needed, and publishes an
    ///     engine-free bootstrap event for the world scene. If no local actor can be materialized, drives
    ///     the recovered 5→4 scene fallback. spec: Docs/RE/specs/handlers.md §4/1;
    ///     Docs/RE/specs/client_runtime.md §7.5.2 and §9.1/§9.4.
    /// </summary>
    private void HandleGameStateTick(ReadOnlySpan<byte> payload)
    {
        // LIVE ENTER LADDER (CORRECTED 2026-06-21 against the real server): the ladder is 1/9 → 4/1 —
        // the 4/1 IS the enter confirmation, NO enter-ladder 3/5 in between. So capture the latch-armed
        // state (a 1/9 in flight) BEFORE clearing the latch, then drive the scene Select/Load → InGame
        // (state 5) on a latch-armed 4/1. 4/1's very first action is still to CLEAR the single in-flight
        // latch (it confirms the enter and closes the ladder). spec: Docs/RE/specs/login_flow.md §1 step 9
        // (CORRECTED 1/9 → 4/1); Docs/RE/specs/net_contracts.md §1.3 (CORRECTED: 4/1 confirms + clears the
        // latch; no enter-ladder 3/5); Docs/RE/specs/world_entry.md §2.3 / §3.3; handlers.md §4/1.
        var enterRequestPending = _inFlightLatch?.IsArmed ?? false;
        _inFlightLatch?.Clear();

        // A latch-armed 4/1 is the live enter confirmation: route the scene spine to InGame (state 5)
        // before building the world. A 4/1 with no 1/9 pending is an ordinary in-world tick (no-op).
        // spec: Docs/RE/specs/login_flow.md §1 step 9 (CORRECTED); client_runtime.md §7.5.3.
        if (enterRequestPending) _sceneStateMachine?.OnWorldEntryConfirmed(true);

        if (!SmsgGameStateTick.TryReadWorldEntrySeed(payload, out var seed))
        {
            if (_world.LocalActor is null) _sceneStateMachine?.OnGameStateTickNoLocalPlayer();

            return;
        }

        var position = Vector3Fixed.FromFloat(seed.SpawnX, 0f, seed.SpawnZ);

        if (_world.LocalActor is { } existing)
        {
            existing.SnapTo(position);
            // spec: Docs/RE/specs/world_entry.md §2.3 / §3.1 — persist the world entry so the InGame scene
            // can recover the cold-start after the SingleReader channel handoff.
            _worldEntry?.Record(seed.AreaId, position);
            _eventBus.Publish(new InGameWorldBootstrappedEvent(existing.Key, position, seed.AreaId));
            return;
        }

        if (!TryCreateLocalPlayerFromCachedDescriptor(
                position, out var actor, out var slotIndex, out var name, out var serverClass))
        {
            _sceneStateMachine?.OnGameStateTickNoLocalPlayer();
            return;
        }

        var localActor = actor!;
        _eventBus.Publish(new LocalPlayerSpawnedEvent(
            localActor.Key, slotIndex, name, localActor.Level, localActor.Position, localActor.CurrentHp,
            localActor.MaxHp,
            serverClass));
        // spec: Docs/RE/specs/world_entry.md §2.3 / §3.1 — persist the world entry so the InGame scene
        // can recover the cold-start after the SingleReader channel handoff.
        _worldEntry?.Record(seed.AreaId, position);
        _eventBus.Publish(new InGameWorldBootstrappedEvent(localActor.Key, position, seed.AreaId));
    }

    // -------------------------------------------------------------------------
    // 4/4 — area entity snapshot (17-byte header + tag loop)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     4/4 — area entity snapshot. Reinterprets the fixed 17-byte area header, then walks the variable
    ///     tag loop from payload[<see cref="SmsgAreaEntitySnapshot.HeaderSize" />..]: each iteration reads one
    ///     tag u8 (tag == 0 ends the loop) and the tag-specific record. Tags 1/2/3 carry a 892-byte actor
    ///     record (= 8-byte prefix + 880-byte SpawnDescriptor core + 4-byte trailer per §21), with the sort
    ///     carried by the tag (1 = PC, 2 = mob, 3 = NPC) and the actor lookup key at record +0; each spawns
    ///     and registers an actor and publishes <see cref="ActorSpawnedEvent" /> exactly like 5/3. Tags 4/6/9
    ///     raise the real engine-free overlay/ground-item events (tag 4 = ground item, tag 6 = guild
    ///     overlay, tag 9 = title/relation overlay). The loop is bounded and stops on any short read; on
    ///     drain it publishes <see cref="AreaPopulatedEvent" />. spec: Docs/RE/specs/handlers.md §4/4 + §21;
    ///     Docs/RE/specs/world_entry.md §2.4; Docs/RE/packets/4-4_ground_item_tag4.yaml.
    /// </summary>
    private bool HandleAreaEntitySnapshot(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgAreaEntitySnapshot.HeaderSize) return false;

        // Header is read for the area-centre recenter coords; only the two f32s are consumed. spec: §10.
        ref readonly var header = ref MemoryMarshal.AsRef<SmsgAreaEntitySnapshot>(payload);
        var areaCentreX = header.AreaCentreX; // recenter coords; the actor spawns carry absolute XZ. spec: §10.
        var areaCentreZ = header.AreaCentreZ;

        var spawnedActorCount = 0; // tag-1/2/3 actors spawned this snapshot (carried by AreaPopulatedEvent).

        // The 892-byte actor record splits 8 (prefix) + 880 (descriptor core) + 4 (trailer). spec: §21.
        const int actorPrefixSize = 8; // entity id-key u32 at +0 within this prefix. spec: handlers.md §21.

        var cursor = SmsgAreaEntitySnapshot.HeaderSize;
        const int maxIterations = 256; // bound the loop; tag == 0 normally terminates. spec: §10 (loop ends on tag 0).
        for (var i = 0; i < maxIterations; i++)
        {
            if (cursor >= payload.Length) break; // short read — stop.

            var tag = payload[cursor];
            cursor++;

            if (tag == 0) break; // tag == 0 terminates the loop. spec: handlers.md §10.

            switch (tag)
            {
                case 1: // player character (sort 1). spec: handlers.md §10 + §21.
                case 2: // mob (sort 2). spec: handlers.md §10 + §21.
                case 3: // NPC (sort 3). spec: handlers.md §10 + §21.
                    if (cursor + SmsgAreaEntitySnapshot.ActorRecordSize >
                        payload.Length) return true; // short read — consumed what we could.

                    var actorRecord =
                        payload.Slice(cursor, SmsgAreaEntitySnapshot.ActorRecordSize);
                    cursor += SmsgAreaEntitySnapshot.ActorRecordSize;

                    // Entity id-key u32 is in the 8-byte prefix at record +0; the sort is the tag. spec: §21.
                    var actorId = BinaryPrimitives.ReadUInt32LittleEndian(actorRecord[..sizeof(uint)]);
                    var key = new ActorKey(actorId, ToEntitySort(tag));

                    // The 880-byte SpawnDescriptor core follows the 8-byte prefix. spec: handlers.md §21.
                    var descriptorBytes =
                        actorRecord.Slice(actorPrefixSize, SpawnDescriptorReader.Size);
                    var reader = new SpawnDescriptorReader(descriptorBytes);

                    var name = reader.ReadName();
                    var level = reader.ReadLevel();
                    var currentHp = reader.ReadCurrentHp();
                    var currentMp = reader.ReadCurrentMp();
                    var currentStamina = reader.ReadCurrentStamina();
                    var serverClass = reader.ReadServerClass();

                    // Float -> fixed at the boundary; world Y forced to 0. spec: actor.md (coords float, Y = 0).
                    var position =
                        Vector3Fixed.FromFloat(reader.ReadWorldX(), 0f, reader.ReadWorldZ());

                    var spawnInfo = new SpawnInfo(key, level, currentHp, currentMp, currentStamina, serverClass);
                    var vitals = VitalsResolver(spawnInfo);

                    var actor = new Actor(key, level, vitals, currentHp, currentMp, currentStamina, position);
                    _world.Add(actor);

                    _eventBus.Publish(new ActorSpawnedEvent(
                        key, name, level, actor.Position, actor.CurrentHp, actor.MaxHp, serverClass));
                    spawnedActorCount++;
                    break;

                case 4: // ground item (24-byte record). spec: handlers.md §4/4 (tag-4); 4-4_ground_item_tag4.yaml.
                    if (cursor + SmsgAreaEntitySnapshot.GroundItemRecordSize > payload.Length)
                        return PublishAreaPopulated(areaCentreX, areaCentreZ, spawnedActorCount);

                    PublishGroundItem(payload.Slice(cursor, SmsgAreaEntitySnapshot.GroundItemRecordSize));
                    cursor += SmsgAreaEntitySnapshot.GroundItemRecordSize;
                    break;

                case 6: // guild-name overlay (36-byte record). spec: handlers.md §4/4 (tag-6).
                    if (cursor + SmsgAreaEntitySnapshot.GuildRecordSize > payload.Length)
                        return PublishAreaPopulated(areaCentreX, areaCentreZ, spawnedActorCount);

                    PublishGuildOverlay(payload.Slice(cursor, SmsgAreaEntitySnapshot.GuildRecordSize));
                    cursor += SmsgAreaEntitySnapshot.GuildRecordSize;
                    break;

                case 9: // title / relation overlay (24-byte record). spec: handlers.md §4/4 (tag-9).
                    if (cursor + SmsgAreaEntitySnapshot.TitleRecordSize > payload.Length)
                        return PublishAreaPopulated(areaCentreX, areaCentreZ, spawnedActorCount);

                    PublishTitleOverlay(payload.Slice(cursor, SmsgAreaEntitySnapshot.TitleRecordSize));
                    cursor += SmsgAreaEntitySnapshot.TitleRecordSize;
                    break;

                default:
                    // An unknown tag has no recoverable record size; stop the loop to avoid mis-stepping.
                    // spec: handlers.md §10 (only tags 1/2/3/4/6/9 are enumerated).
                    return PublishAreaPopulated(areaCentreX, areaCentreZ, spawnedActorCount);
            }
        }

        // The tag loop drained (zero terminator or the iteration bound) — the area is populated. spec: §4/4.
        return PublishAreaPopulated(areaCentreX, areaCentreZ, spawnedActorCount);
    }

    /// <summary>
    ///     Publishes the <see cref="AreaPopulatedEvent" /> when the 4/4 tag loop drains, returning true so the
    ///     caller treats the snapshot as consumed. spec: Docs/RE/specs/handlers.md §4/4.
    /// </summary>
    private bool PublishAreaPopulated(float areaCentreX, float areaCentreZ, int spawnedActorCount)
    {
        _eventBus.Publish(new AreaPopulatedEvent(areaCentreX, areaCentreZ, spawnedActorCount));
        return true;
    }

    /// <summary>
    ///     4/4 tag-4 ground-item sub-record (24 bytes): Key u32@+0, TemplateId u32@+4, WorldX f32@+0x10,
    ///     WorldZ f32@+0x14. Float -&gt; fixed at the boundary (Y forced 0). spec:
    ///     Docs/RE/packets/4-4_ground_item_tag4.yaml; Docs/RE/specs/handlers.md §4/4 (tag-4).
    /// </summary>
    private void PublishGroundItem(ReadOnlySpan<byte> record)
    {
        var key = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(0x00, sizeof(uint))); // +0x00 Key
        var templateId = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(0x04, sizeof(uint))); // +0x04 TemplateId
        var worldX = BinaryPrimitives.ReadSingleLittleEndian(record.Slice(0x10, sizeof(float))); // +0x10 WorldX
        var worldZ = BinaryPrimitives.ReadSingleLittleEndian(record.Slice(0x14, sizeof(float))); // +0x14 WorldZ

        // Float -> fixed at the network/application boundary; world Y forced to 0. spec: 4-4 yaml.
        var position = Vector3Fixed.FromFloat(worldX, 0f, worldZ);
        _eventBus.Publish(new GroundItemSpawnedEvent(key, templateId, position));
    }

    /// <summary>
    ///     4/4 tag-6 guild-name overlay sub-record (36 bytes): EntityId u32@+0, CP949 NUL-string @+0x05.
    ///     spec: Docs/RE/specs/handlers.md §4/4 (tag-6).
    /// </summary>
    private void PublishGuildOverlay(ReadOnlySpan<byte> record)
    {
        var entityId = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(0x00, sizeof(uint))); // +0x00 EntityId
        // Guild name occupies +0x05 up to 31 bytes (CP949, NUL-terminated). spec: handlers.md §4/4 (tag-6).
        var guildName = Cp949Text.Decode(record[0x05..]);
        _eventBus.Publish(new GuildOverlayEvent(entityId, guildName));
    }

    /// <summary>
    ///     4/4 tag-9 title / relation overlay sub-record (24 bytes): EntityId u32@+0, RelationState u8@+4,
    ///     OverlaySubCode u8@+5, CP949 TitleName 17-byte cell @+6. RelationState / OverlaySubCode value
    ///     MEANINGS are live-pending (world_entry.md §4 / handlers.md §4/4) — the raw bytes are forwarded,
    ///     no enum meaning is invented. spec: Docs/RE/specs/handlers.md §4/4 (tag-9).
    /// </summary>
    private void PublishTitleOverlay(ReadOnlySpan<byte> record)
    {
        var entityId = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(0x00, sizeof(uint))); // +0x00 EntityId
        var relationState = record[0x04]; // +0x04 — value meaning live-pending (world_entry.md §4 / handlers.md §4/4).
        var overlaySubCode =
            record[0x05]; // +0x05 — value meaning live-pending (world_entry.md §4 / handlers.md §4/4).
        var titleName = Cp949Text.Decode(record.Slice(0x06, 17)); // +0x06 TitleName (17-byte CP949 cell).
        _eventBus.Publish(new TitleOverlayEvent(entityId, relationState, overlaySubCode, titleName));
    }

    // -------------------------------------------------------------------------
    // 4/100 — combat attack / charge update
    // -------------------------------------------------------------------------

    /// <summary>
    ///     4/100 — combat-attack / charge UI state update. Decodes only the documented phase/sub-kind/value
    ///     fields (the remaining ~176 bytes are opaque per the spec and not surfaced). Phase 3 starts a timed
    ///     charge; phase 5 ends it. spec: Docs/RE/specs/handlers.md §3 (4/100).
    /// </summary>
    private bool HandleCombatAttackUpdate(ReadOnlySpan<byte> payload)
    {
        const int minSize = 188; // Min fixed payload 188 (0xBC). spec: handlers.md §3 (4/100).
        if (payload.Length < minSize) return false;

        // phase@+8 (u8); sub-kind@+10 (i8, 0xFF = reset); value@+12 (u32). spec: handlers.md §3 (4/100).
        var phase = payload[0x08];
        var subKind = unchecked((sbyte)payload[0x0A]);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x0C, 4));

        const byte startCharge = 3; // phase 3 starts a timed charge. spec: handlers.md §3 (4/100).
        const byte endCharge = 5; // phase 5 ends it. spec: handlers.md §3 (4/100).

        _eventBus.Publish(new CombatAttackUpdateEvent(
            phase, subKind, value, phase == startCharge, phase == endCharge));
        return true;
    }
}