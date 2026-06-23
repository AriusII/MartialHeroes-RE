using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
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
    ///     4/1 — typed IPacketHandler seam. The source-generated router passes the 9100-byte struct;
    ///     we reconstruct a raw span over the inline PayloadBuffer so the existing handler can read the
    ///     pinned world-entry seed without copying. spec: Docs/RE/specs/handlers.md §4/1;
    ///     Docs/RE/packets/4-1_game_state_tick.yaml.
    /// </summary>
    public void Handle(in SmsgGameStateTick packet) // spec: Docs/RE/opcodes.md row 4/1; IPacketHandler seam
    {
        // The [InlineArray(9100)] PayloadBuffer is a contiguous byte array inside the Pack=1 struct.
        // MemoryMarshal.CreateReadOnlySpan over the first element gives the full 9100-byte payload span.
        // spec: Docs/RE/packets/4-1_game_state_tick.yaml (WireSize = 0x238C = 9100).
        var payload = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<SmsgGameStateTick.PayloadBuffer, byte>(
                ref Unsafe.AsRef(in packet.Payload)),
            SmsgGameStateTick.WireSize); // spec: Docs/RE/packets/4-1_game_state_tick.yaml (size: 9100)
        HandleGameStateTick(payload);
    }

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
            PublishInteriorSnapshot(payload);
            return;
        }

        if (!TryCreateLocalPlayerFromCachedDescriptor(
                position, out var actor, out var slotIndex, out var name, out var serverClass,
                out var equipGids))
        {
            _sceneStateMachine?.OnGameStateTickNoLocalPlayer();
            return;
        }

        var localActor = actor!;
        // Carry the six visible-gear GIDs ({3,4,6,2,11,14}) so the layer-05 render path composes the equip
        // overlay through EquipOverlayResolver (the GID/key64 math runs ONCE there, not here). Empty ⇒
        // body-only. spec: Docs/RE/structs/spawn_descriptor.md (+0x58 equip table);
        // Docs/RE/specs/equipment_visuals.md §1.1 / §3.
        _eventBus.Publish(new LocalPlayerSpawnedEvent(
            localActor.Key, slotIndex, name, localActor.Level, localActor.Position, localActor.CurrentHp,
            localActor.MaxHp,
            serverClass, equipGids));
        // spec: Docs/RE/specs/world_entry.md §2.3 / §3.1 — persist the world entry so the InGame scene
        // can recover the cold-start after the SingleReader channel handoff.
        _worldEntry?.Record(seed.AreaId, position);
        _eventBus.Publish(new InGameWorldBootstrappedEvent(localActor.Key, position, seed.AreaId));
        PublishInteriorSnapshot(payload);
    }

    /// <summary>
    ///     4/1 world-entry interior decode. Slices the three interior blocks off the 9100-byte payload using
    ///     the layer-02 <see cref="SmsgGameStateTick" /> consts and publishes three immutable snapshot events
    ///     (roster, scene-entity, hotbar) AFTER the <see cref="InGameWorldBootstrappedEvent" />. This is a
    ///     SNAPSHOT decode only — it never spawns the local player (the seed/spawn logic above is the SOLE
    ///     spawn site). Each block is decoded into a sized <see cref="ImmutableArray{T}" /> builder with no
    ///     per-record heap allocation in the loops. spec: Docs/RE/packets/4-1_game_state_tick.yaml
    ///     ("Interior record strides"); Docs/RE/specs/world_systems.md §13.3; Docs/RE/specs/world_entry.md.
    /// </summary>
    private void PublishInteriorSnapshot(ReadOnlySpan<byte> payload)
    {
        // The contiguous interior layout (24 + 3088 + 4044 + 1920 = 9076, then SpawnX @ 9076) means the
        // three blocks must all fit within the fixed 9100-byte body. Guard once. spec: 4-1 yaml (Interior strides).
        if (payload.Length < SmsgGameStateTick.WireSize) return;

        PublishRosterSnapshot(payload);
        PublishSceneEntitySnapshot(payload);
        PublishHotbarSnapshot(payload);
    }

    /// <summary>
    ///     4/1 ROSTER (WorldEntryTableA @ <see cref="SmsgGameStateTick.TableAOffset" />): walks the
    ///     <see cref="SmsgGameStateTick.TableASweepCount" /> stale-slot-sweep records (stride 16) and collects
    ///     the non-empty (ActorId != 0) members. Per record: ActorId u32@+4, KeepGuard u32@+8 (also the
    ///     displayed member number), Aux u32@+12. spec: Docs/RE/packets/4-1_game_state_tick.yaml (Table A note);
    ///     Docs/RE/specs/world_systems.md §13.3.
    /// </summary>
    private void PublishRosterSnapshot(ReadOnlySpan<byte> payload)
    {
        var table = payload.Slice(SmsgGameStateTick.TableAOffset, SmsgGameStateTick.TableASize);

        // Size the builder to the sweep count (the max possible non-empty rows); fill without per-row alloc.
        var rows = ImmutableArray.CreateBuilder<RosterMember>(SmsgGameStateTick.TableASweepCount);
        for (var i = 0; i < SmsgGameStateTick.TableASweepCount; i++)
        {
            var rec = table.Slice(i * SmsgGameStateTick.TableARecordStride, SmsgGameStateTick.TableARecordStride);

            var actorId = BinaryPrimitives.ReadUInt32LittleEndian(
                rec.Slice(SmsgGameStateTick.TableARecordActorIdOffset, sizeof(uint))); // +4. spec: 4-1 yaml (Table A)
            if (actorId == 0u) continue; // empty slot. spec: 4-1 yaml (Table A; ActorId 0 = empty)

            var keepGuard = BinaryPrimitives.ReadUInt32LittleEndian(
                rec.Slice(SmsgGameStateTick.TableARecordKeepGuardOffset, sizeof(uint))); // +8. spec: 4-1 yaml (Table A)
            var aux = BinaryPrimitives.ReadUInt32LittleEndian(
                rec.Slice(SmsgGameStateTick.TableARecordAuxOffset, sizeof(uint))); // +12. spec: 4-1 yaml (Table A)

            rows.Add(new RosterMember(actorId, keepGuard, aux));
        }

        _eventBus.Publish(new RosterSnapshotEvent(rows.ToImmutable()));
    }

    /// <summary>
    ///     4/1 SCENE-ENTITY (WorldEntryTableB @ <see cref="SmsgGameStateTick.TableBOffset" />): TableB is
    ///     HETEROGENEOUS — 240×16B actor-slot records (3840 bytes) + 20B gap + 21×8B category entries (168
    ///     bytes) + 16B world-target = 4044. Decodes the first
    ///     <see cref="SmsgGameStateTick.TableBActorSlotCount" /> actor-slot records (same id@+4/keep-guard@+8/
    ///     aux@+12 shape, non-empty id != 0), then skips the 20B gap and reads 21 category entries of 8B each
    ///     (category u32, value i32). spec: Docs/RE/specs/world_systems.md §13.3;
    ///     Docs/RE/packets/4-1_game_state_tick.yaml (Table B note).
    /// </summary>
    private void PublishSceneEntitySnapshot(ReadOnlySpan<byte> payload)
    {
        var table = payload.Slice(SmsgGameStateTick.TableBOffset, SmsgGameStateTick.TableBSize);

        // Actor-slot subregion: 240 × 16B records at the head of TableB. Same record shape as TableA.
        // spec: 4-1 yaml (TableB 240×16B actor-slot records).
        var slots = ImmutableArray.CreateBuilder<RosterMember>(SmsgGameStateTick.TableBActorSlotCount);
        for (var i = 0; i < SmsgGameStateTick.TableBActorSlotCount; i++)
        {
            var rec = table.Slice(i * SmsgGameStateTick.TableARecordStride, SmsgGameStateTick.TableARecordStride);

            var actorId = BinaryPrimitives.ReadUInt32LittleEndian(
                rec.Slice(SmsgGameStateTick.TableARecordActorIdOffset, sizeof(uint))); // +4. spec: 4-1 yaml (TableB)
            if (actorId == 0u) continue; // empty slot. spec: 4-1 yaml (TableB; id 0 = empty)

            var keepGuard = BinaryPrimitives.ReadUInt32LittleEndian(
                rec.Slice(SmsgGameStateTick.TableARecordKeepGuardOffset, sizeof(uint))); // +8. spec: 4-1 yaml (TableB)
            var aux = BinaryPrimitives.ReadUInt32LittleEndian(
                rec.Slice(SmsgGameStateTick.TableARecordAuxOffset, sizeof(uint))); // +12. spec: 4-1 yaml (TableB)

            slots.Add(new RosterMember(actorId, keepGuard, aux));
        }

        // After the 3840B actor-slot subregion + a 20B unswept gap, 21 category entries of 8B each
        // (category u32, value i32). spec: 4-1 yaml (TableB layout: 3840 + 20 gap + 168 category + 16 target).
        const int categoryGap = 20; // 20B unswept gap before the category entries. spec: 4-1 yaml (TableB layout)
        const int categoryCount = 21; // 21 × 8B category entries (168 bytes). spec: 4-1 yaml (TableB layout)
        const int categoryStride = 8; // category u32 + value i32. spec: 4-1 yaml (TableB category entry 8B)
        var categoryBase = SmsgGameStateTick.TableBActorSlotsBytes + categoryGap;

        var categories = ImmutableArray.CreateBuilder<SceneCategoryEntry>(categoryCount);
        for (var i = 0; i < categoryCount; i++)
        {
            var entry = table.Slice(categoryBase + i * categoryStride, categoryStride);
            var category = BinaryPrimitives.ReadUInt32LittleEndian(entry[..sizeof(uint)]); // +0. spec: 4-1 yaml
            var value = BinaryPrimitives.ReadInt32LittleEndian(entry.Slice(sizeof(uint),
                sizeof(int))); // +4. spec: 4-1 yaml
            categories.Add(new SceneCategoryEntry(category, value));
        }

        _eventBus.Publish(new SceneEntitySnapshotEvent(slots.ToImmutable(), categories.ToImmutable()));
    }

    /// <summary>
    ///     4/1 HOTBAR (HotbarSlots @ <see cref="SmsgGameStateTick.HotbarOffset" />): walks
    ///     <see cref="SmsgGameStateTick.HotbarSlotCount" /> slots of stride 8 and collects the non-empty
    ///     (EntryKey != 0) slots. Per slot: EntryKey u32@+0, Count u16@+4. There is NO inline type byte —
    ///     skill-vs-item is data-driven (category value 5 = skill via the skill catalogue), which is NOT
    ///     injected into Application here, so the raw EntryKey+Count are published with a category-pending
    ///     note. spec: Docs/RE/specs/world_entry.md (hotbar init);
    ///     Docs/RE/packets/4-1_game_state_tick.yaml (HotbarSlots note).
    /// </summary>
    private void PublishHotbarSnapshot(ReadOnlySpan<byte> payload)
    {
        var block = payload.Slice(SmsgGameStateTick.HotbarOffset, SmsgGameStateTick.HotbarSize);

        var slots = ImmutableArray.CreateBuilder<HotbarSlotEntry>(SmsgGameStateTick.HotbarSlotCount);
        for (var i = 0; i < SmsgGameStateTick.HotbarSlotCount; i++)
        {
            var slot = block.Slice(i * SmsgGameStateTick.HotbarSlotStride, SmsgGameStateTick.HotbarSlotStride);

            var entryKey = BinaryPrimitives.ReadUInt32LittleEndian(
                slot.Slice(SmsgGameStateTick.HotbarSlotEntryKeyOffset,
                    sizeof(uint))); // +0. spec: 4-1 yaml (HotbarSlots)
            if (entryKey == 0u) continue; // empty slot. spec: 4-1 yaml (HotbarSlots; EntryKey 0 = empty)

            var count = BinaryPrimitives.ReadUInt16LittleEndian(
                slot.Slice(SmsgGameStateTick.HotbarSlotCountOffset,
                    sizeof(ushort))); // +4. spec: 4-1 yaml (HotbarSlots)

            // No inline type byte — skill-vs-item is resolved by a catalogue lookup (category value 5 = skill).
            // No skill-category resolver is injected into Application, so the slot is published raw with a
            // category-pending note. spec: 4-1 yaml (HotbarSlots; category value 5 = skill, no inline type byte).
            slots.Add(new HotbarSlotEntry(i, entryKey, count));
        }

        _eventBus.Publish(new HotbarInitializedEvent(slots.ToImmutable()));
    }

    // -------------------------------------------------------------------------
    // 4/4 — area entity snapshot (17-byte header + tag loop)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     4/4 — area entity snapshot. Reinterprets the fixed 17-byte area header, then walks the variable
    ///     tag loop from payload[<see cref="SmsgAreaEntitySnapshot.HeaderSize" />..]: each iteration reads one
    ///     tag u8 (tag == 0 ends the loop) and the tag-specific record. Tags 1/2/3 carry a 892-byte actor
    ///     record (= 8-byte prefix + 880-byte SpawnDescriptor core + 4-byte trailer per §21), with the sort
    ///     carried by the tag (1 = PC, 2 = mob, 3 = NPC) and the actor lookup key at record +0. The prefix
    ///     KindByte u8@+0x04: when it is 5 AND the actor already exists, this is a VISUAL-ONLY refresh
    ///     (weapon/joint) — it publishes <see cref="ActorVisualRefreshedEvent" /> and does NOT rebuild;
    ///     otherwise it spawns and registers an actor and publishes <see cref="ActorSpawnedEvent" /> exactly
    ///     like 5/3. spec: Docs/RE/structs/actor.md (4/4 record: KindByte == 5 visual-only refresh). Tags 4/6/9
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

        var cursor = SmsgAreaEntitySnapshot.HeaderSize;
        const int maxIterations = 256; // bound the loop; tag == 0 normally terminates. spec: §10 (loop ends on tag 0).

        // CA2014: visible-gear scratch hoisted out of the tag loop (no stackalloc-in-loop). Reuse is safe —
        // ReadVisibleGearGids fully overwrites all 6 entries each call and ImmutableArray.Create snapshots
        // them before the next iteration overwrites the buffer. spec: spawn_descriptor.md (+0x58 equip table).
        Span<uint> gearScratch = stackalloc uint[SpawnDescriptorReader.VisibleGearSlots.Length];

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

                    // Entity id-key u32 is in the prefix at record +0; the sort is the tag (NO Sort dword in
                    // the prefix — composite key = (ActorId@+0, tag-byte sort)). spec: structs/actor.md (4/4 record).
                    var actorId = BinaryPrimitives.ReadUInt32LittleEndian(
                        actorRecord.Slice(SmsgAreaEntitySnapshot.ActorIdOffset, sizeof(uint)));
                    var key = new ActorKey(actorId, ToEntitySort(tag));

                    // KindByte u8@+0x04 and RelationVisual u8@+0x05 sit INSIDE the tag-1/2/3 prefix (NOT a
                    // separate tag). KindByte == 5 gates a VISUAL-ONLY refresh of an already-spawned actor
                    // (weapon/joint refresh) — NOT a new spawn. spec: structs/actor.md (4/4 record: KindByte
                    // u8@+0x04, value 5 = visual-only refresh; RelationVisual u8@+0x05).
                    var kindByte = actorRecord[SmsgAreaEntitySnapshot.KindByteOffset];
                    var relationVisual = actorRecord[SmsgAreaEntitySnapshot.RelationVisualOffset];

                    if (kindByte == SmsgAreaEntitySnapshot.KindByteVisualRefresh &&
                        _world.TryGet(key, out _))
                    {
                        // Visual-only refresh: do NOT Remove/Add/rebuild — the actor stays put; only its
                        // weapon/joint visual is refreshed by layer 05 off this event. spec: structs/actor.md
                        // (KindByte == 5 => visual-only refresh of an existing actor).
                        _eventBus.Publish(new ActorVisualRefreshedEvent(key, relationVisual));
                        break;
                    }

                    // The 880-byte SpawnDescriptor core follows the prefix at +0x08. spec: structs/actor.md (4/4 record).
                    var descriptorBytes =
                        actorRecord.Slice(SmsgAreaEntitySnapshot.DescriptorOffset, SpawnDescriptorReader.Size);
                    var reader = new SpawnDescriptorReader(descriptorBytes);

                    var name = reader.ReadName();
                    var level = reader.ReadLevel();
                    // HP-qword correction: HP is ONE int64 @ +0x3C (clamped to u32); the single
                    // MP/stamina-class vital is @ +0x44. +0x40 is HP-HIGH, NOT MP. Both lower vital slots
                    // are seeded from the one vital_b (MP-vs-stamina pending), not a fabricated stamina.
                    // spec: Docs/RE/structs/spawn_descriptor.md (HP-qword correction).
                    var currentHp = reader.ReadCurrentHpClamped();
                    var vitalB = reader.ReadVitalB();
                    var serverClass = reader.ReadServerClass();

                    // FIX 8 — surface the descriptor-derived APPEARANCE IDENTITY the live skinned-avatar
                    // factory needs (ActorManager_SpawnActorFromDescriptor @0x423fe9, mode 1 = player →
                    // Appearance_ResolveKey @0x422631): internal_class (+0x34), appearance_variant (+0x2C),
                    // and the six visible-gear gids ({3,4,6,2,11,14}) from the +0x58 equip table. Only PC
                    // actors (sort tag 1) carry a meaningful PLAYER class/variant — for mob/NPC (tags 2/3)
                    // Appearance_ResolveKey takes a DIFFERENT branch (mob: MobInfo_LookupByMobId), so the raw
                    // fields are surfaced but layer 05 gates the skinned-PLAYER build on sort ==
                    // PlayerCharacter. The empty default is kept for kinds that genuinely lack the field
                    // (logged + skipped, never fabricated). spec: Docs/RE/structs/spawn_descriptor.md
                    // (+0x34 / +0x2C / +0x58); ActorManager_SpawnActorFromDescriptor @0x423fe9;
                    // Appearance_ResolveKey @0x422631.
                    var internalClass = reader.ReadInternalClass();
                    var appearanceVariant = reader.ReadAppearanceVariant();
                    reader.ReadVisibleGearGids(gearScratch);
                    var equipGids = ImmutableArray.Create(gearScratch);

                    // Float -> fixed at the boundary; world Y forced to 0. spec: spawn_descriptor.md (coords float, Y = 0).
                    var position =
                        Vector3Fixed.FromFloat(reader.ReadWorldX(), 0f, reader.ReadWorldZ());

                    var spawnInfo = new SpawnInfo(key, level, currentHp, vitalB, vitalB, serverClass);
                    var vitals = VitalsResolver(spawnInfo);

                    var actor = new Actor(key, level, vitals, currentHp, vitalB, vitalB, position);
                    _world.Add(actor);

                    _eventBus.Publish(new ActorSpawnedEvent(
                        key, name, level, actor.Position, actor.CurrentHp, actor.MaxHp, serverClass,
                        internalClass, appearanceVariant, equipGids));
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