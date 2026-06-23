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
    public void Handle(in SmsgGameStateTick packet)
    {
        var payload = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<SmsgGameStateTick.PayloadBuffer, byte>(
                ref Unsafe.AsRef(in packet.Payload)),
            SmsgGameStateTick.WireSize);
        HandleGameStateTick(payload);
    }

    private void HandleGameStateTick(ReadOnlySpan<byte> payload)
    {
        var enterRequestPending = inFlightLatch?.IsArmed ?? false;
        inFlightLatch?.Clear();

        if (enterRequestPending) sceneStateMachine?.OnWorldEntryConfirmed(true);

        if (!SmsgGameStateTick.TryReadWorldEntrySeed(payload, out var seed))
        {
            if (_world.LocalActor is null) sceneStateMachine?.OnGameStateTickNoLocalPlayer();

            return;
        }

        var position = Vector3Fixed.FromFloat(seed.SpawnX, 0f, seed.SpawnZ);

        if (_world.LocalActor is { } existing)
        {
            existing.SnapTo(position);
            worldEntry?.Record(seed.AreaId, position);
            _eventBus.Publish(new InGameWorldBootstrappedEvent(existing.Key, position, seed.AreaId));
            PublishInteriorSnapshot(payload);
            return;
        }

        if (!TryCreateLocalPlayerFromCachedDescriptor(
                position, out var actor, out var slotIndex, out var name, out var serverClass,
                out var equipGids))
        {
            sceneStateMachine?.OnGameStateTickNoLocalPlayer();
            return;
        }

        var localActor = actor!;
        _eventBus.Publish(new LocalPlayerSpawnedEvent(
            localActor.Key, slotIndex, name, localActor.Level, localActor.Position, localActor.CurrentHp,
            localActor.MaxHp,
            serverClass, equipGids));
        worldEntry?.Record(seed.AreaId, position);
        _eventBus.Publish(new InGameWorldBootstrappedEvent(localActor.Key, position, seed.AreaId));
        PublishInteriorSnapshot(payload);
    }

    private void PublishInteriorSnapshot(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgGameStateTick.WireSize) return;

        PublishRosterSnapshot(payload);
        PublishSceneEntitySnapshot(payload);
        PublishHotbarSnapshot(payload);
    }

    private void PublishRosterSnapshot(ReadOnlySpan<byte> payload)
    {
        var table = payload.Slice(SmsgGameStateTick.TableAOffset, SmsgGameStateTick.TableASize);

        var rows = ImmutableArray.CreateBuilder<RosterMember>(SmsgGameStateTick.TableASweepCount);
        for (var i = 0; i < SmsgGameStateTick.TableASweepCount; i++)
        {
            var rec = table.Slice(i * SmsgGameStateTick.TableARecordStride, SmsgGameStateTick.TableARecordStride);

            var actorId = BinaryPrimitives.ReadUInt32LittleEndian(
                rec.Slice(SmsgGameStateTick.TableARecordActorIdOffset, sizeof(uint)));
            if (actorId == 0u) continue;

            var keepGuard = BinaryPrimitives.ReadUInt32LittleEndian(
                rec.Slice(SmsgGameStateTick.TableARecordKeepGuardOffset, sizeof(uint)));
            var aux = BinaryPrimitives.ReadUInt32LittleEndian(
                rec.Slice(SmsgGameStateTick.TableARecordAuxOffset, sizeof(uint)));

            rows.Add(new RosterMember(actorId, keepGuard, aux));
        }

        _eventBus.Publish(new RosterSnapshotEvent(rows.ToImmutable()));
    }

    private void PublishSceneEntitySnapshot(ReadOnlySpan<byte> payload)
    {
        var table = payload.Slice(SmsgGameStateTick.TableBOffset, SmsgGameStateTick.TableBSize);

        var slots = ImmutableArray.CreateBuilder<RosterMember>(SmsgGameStateTick.TableBActorSlotCount);
        for (var i = 0; i < SmsgGameStateTick.TableBActorSlotCount; i++)
        {
            var rec = table.Slice(i * SmsgGameStateTick.TableARecordStride, SmsgGameStateTick.TableARecordStride);

            var actorId = BinaryPrimitives.ReadUInt32LittleEndian(
                rec.Slice(SmsgGameStateTick.TableARecordActorIdOffset, sizeof(uint)));
            if (actorId == 0u) continue;

            var keepGuard = BinaryPrimitives.ReadUInt32LittleEndian(
                rec.Slice(SmsgGameStateTick.TableARecordKeepGuardOffset, sizeof(uint)));
            var aux = BinaryPrimitives.ReadUInt32LittleEndian(
                rec.Slice(SmsgGameStateTick.TableARecordAuxOffset, sizeof(uint)));

            slots.Add(new RosterMember(actorId, keepGuard, aux));
        }

        const int categoryGap = 20;
        const int categoryCount = 21;
        const int categoryStride = 8;
        var categoryBase = SmsgGameStateTick.TableBActorSlotsBytes + categoryGap;

        var categories = ImmutableArray.CreateBuilder<SceneCategoryEntry>(categoryCount);
        for (var i = 0; i < categoryCount; i++)
        {
            var entry = table.Slice(categoryBase + i * categoryStride, categoryStride);
            var category = BinaryPrimitives.ReadUInt32LittleEndian(entry[..sizeof(uint)]);
            var value = BinaryPrimitives.ReadInt32LittleEndian(entry.Slice(sizeof(uint),
                sizeof(int)));
            categories.Add(new SceneCategoryEntry(category, value));
        }

        _eventBus.Publish(new SceneEntitySnapshotEvent(slots.ToImmutable(), categories.ToImmutable()));
    }

    private void PublishHotbarSnapshot(ReadOnlySpan<byte> payload)
    {
        var block = payload.Slice(SmsgGameStateTick.HotbarOffset, SmsgGameStateTick.HotbarSize);

        var slots = ImmutableArray.CreateBuilder<HotbarSlotEntry>(SmsgGameStateTick.HotbarSlotCount);
        for (var i = 0; i < SmsgGameStateTick.HotbarSlotCount; i++)
        {
            var slot = block.Slice(i * SmsgGameStateTick.HotbarSlotStride, SmsgGameStateTick.HotbarSlotStride);

            var entryKey = BinaryPrimitives.ReadUInt32LittleEndian(
                slot.Slice(SmsgGameStateTick.HotbarSlotEntryKeyOffset,
                    sizeof(uint)));
            if (entryKey == 0u) continue;

            var count = BinaryPrimitives.ReadUInt16LittleEndian(
                slot.Slice(SmsgGameStateTick.HotbarSlotCountOffset,
                    sizeof(ushort)));

            slots.Add(new HotbarSlotEntry(i, entryKey, count));
        }

        _eventBus.Publish(new HotbarInitializedEvent(slots.ToImmutable()));
    }


    private bool HandleAreaEntitySnapshot(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgAreaEntitySnapshot.HeaderSize) return false;

        ref readonly var header = ref MemoryMarshal.AsRef<SmsgAreaEntitySnapshot>(payload);
        var areaCentreX = header.AreaCentreX;
        var areaCentreZ = header.AreaCentreZ;

        var spawnedActorCount = 0;

        var cursor = SmsgAreaEntitySnapshot.HeaderSize;
        const int maxIterations = 256;

        Span<uint> gearScratch = stackalloc uint[SpawnDescriptorReader.VisibleGearSlots.Length];

        for (var i = 0; i < maxIterations; i++)
        {
            if (cursor >= payload.Length) break;

            var tag = payload[cursor];
            cursor++;

            if (tag == 0) break;

            switch (tag)
            {
                case 1:
                case 2:
                case 3:
                    if (cursor + SmsgAreaEntitySnapshot.ActorRecordSize >
                        payload.Length) return true;

                    var actorRecord =
                        payload.Slice(cursor, SmsgAreaEntitySnapshot.ActorRecordSize);
                    cursor += SmsgAreaEntitySnapshot.ActorRecordSize;

                    var actorId = BinaryPrimitives.ReadUInt32LittleEndian(
                        actorRecord.Slice(SmsgAreaEntitySnapshot.ActorIdOffset, sizeof(uint)));
                    var key = new ActorKey(actorId, ToEntitySort(tag));

                    var kindByte = actorRecord[SmsgAreaEntitySnapshot.KindByteOffset];
                    var relationVisual = actorRecord[SmsgAreaEntitySnapshot.RelationVisualOffset];

                    if (kindByte == SmsgAreaEntitySnapshot.KindByteVisualRefresh &&
                        _world.TryGet(key, out _))
                    {
                        _eventBus.Publish(new ActorVisualRefreshedEvent(key, relationVisual));
                        break;
                    }

                    var descriptorBytes =
                        actorRecord.Slice(SmsgAreaEntitySnapshot.DescriptorOffset, SpawnDescriptorReader.Size);
                    var reader = new SpawnDescriptorReader(descriptorBytes);

                    var name = reader.ReadName();
                    var level = reader.ReadLevel();
                    var currentHp = reader.ReadCurrentHpClamped();
                    var vitalB = reader.ReadVitalB();
                    var serverClass = reader.ReadServerClass();

                    var internalClass = reader.ReadInternalClass();
                    var appearanceVariant = reader.ReadAppearanceVariant();
                    reader.ReadVisibleGearGids(gearScratch);
                    var equipGids = ImmutableArray.Create(gearScratch);

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

                case 4:
                    if (cursor + SmsgAreaEntitySnapshot.GroundItemRecordSize > payload.Length)
                        return PublishAreaPopulated(areaCentreX, areaCentreZ, spawnedActorCount);

                    PublishGroundItem(payload.Slice(cursor, SmsgAreaEntitySnapshot.GroundItemRecordSize));
                    cursor += SmsgAreaEntitySnapshot.GroundItemRecordSize;
                    break;

                case 6:
                    if (cursor + SmsgAreaEntitySnapshot.GuildRecordSize > payload.Length)
                        return PublishAreaPopulated(areaCentreX, areaCentreZ, spawnedActorCount);

                    PublishGuildOverlay(payload.Slice(cursor, SmsgAreaEntitySnapshot.GuildRecordSize));
                    cursor += SmsgAreaEntitySnapshot.GuildRecordSize;
                    break;

                case 9:
                    if (cursor + SmsgAreaEntitySnapshot.TitleRecordSize > payload.Length)
                        return PublishAreaPopulated(areaCentreX, areaCentreZ, spawnedActorCount);

                    PublishTitleOverlay(payload.Slice(cursor, SmsgAreaEntitySnapshot.TitleRecordSize));
                    cursor += SmsgAreaEntitySnapshot.TitleRecordSize;
                    break;

                default:
                    return PublishAreaPopulated(areaCentreX, areaCentreZ, spawnedActorCount);
            }
        }

        return PublishAreaPopulated(areaCentreX, areaCentreZ, spawnedActorCount);
    }

    private bool PublishAreaPopulated(float areaCentreX, float areaCentreZ, int spawnedActorCount)
    {
        _eventBus.Publish(new AreaPopulatedEvent(areaCentreX, areaCentreZ, spawnedActorCount));
        return true;
    }

    private void PublishGroundItem(ReadOnlySpan<byte> record)
    {
        var key = BinaryPrimitives.ReadUInt32LittleEndian(record[..sizeof(uint)]);
        var templateId = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(0x04, sizeof(uint)));
        var worldX = BinaryPrimitives.ReadSingleLittleEndian(record.Slice(0x10, sizeof(float)));
        var worldZ = BinaryPrimitives.ReadSingleLittleEndian(record.Slice(0x14, sizeof(float)));

        var position = Vector3Fixed.FromFloat(worldX, 0f, worldZ);
        _eventBus.Publish(new GroundItemSpawnedEvent(key, templateId, position));
    }

    private void PublishGuildOverlay(ReadOnlySpan<byte> record)
    {
        var entityId = BinaryPrimitives.ReadUInt32LittleEndian(record[..sizeof(uint)]);
        var guildName = Cp949Text.Decode(record[0x05..]);
        _eventBus.Publish(new GuildOverlayEvent(entityId, guildName));
    }

    private void PublishTitleOverlay(ReadOnlySpan<byte> record)
    {
        var entityId = BinaryPrimitives.ReadUInt32LittleEndian(record[..sizeof(uint)]);
        var relationState = record[0x04];
        var overlaySubCode =
            record[0x05];
        var titleName = Cp949Text.Decode(record.Slice(0x06, 17));
        _eventBus.Publish(new TitleOverlayEvent(entityId, relationState, overlaySubCode, titleName));
    }


    private bool HandleCombatAttackUpdate(ReadOnlySpan<byte> payload)
    {
        const int minSize = 188;
        if (payload.Length < minSize) return false;

        var phase = payload[0x08];
        var subKind = unchecked((sbyte)payload[0x0A]);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x0C, 4));

        const byte startCharge = 3;
        const byte endCharge = 5;

        _eventBus.Publish(new CombatAttackUpdateEvent(
            phase, subKind, value, phase == startCharge, phase == endCharge));
        return true;
    }
}