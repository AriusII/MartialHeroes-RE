using System.Buffers.Binary;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Client.Domain.Progression.Progression;
using MartialHeroes.Network.Protocol.Packets.World.Packets;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    public void Handle(in SmsgStatUpdate packet)
    {
        const byte applied = 1;
        if (packet.ResultOk != applied) return;

        var key = _world.LocalActorKey ?? new ActorKey(packet.Handle, EntitySort.PlayerCharacter);

        _eventBus.Publish(new ActorStatsChangedEvent(
            key, packet.Stat0, packet.Stat1, packet.Stat2, packet.Stat3, packet.Stat4,
            packet.RemainingStatPoints));
    }


    public void Handle(in SmsgLevelUp packet)
    {
        var key = new ActorKey(packet.ActorId, ToEntitySort(packet.Sort));

        var currentHp = unchecked((uint)(packet.HpMpPacked & 0xFFFF_FFFF));
        var currentMp = unchecked((uint)((packet.HpMpPacked >> 32) & 0xFFFF_FFFF));
        var currentStamina = unchecked((uint)packet.Stamina);

        if (_world.TryGet(key, out var actor))
        {
            actor.SetLevel(packet.NewLevel);
            actor.SetCurrentHp(currentHp);
            actor.SetCurrentMp(currentMp);
            actor.SetCurrentStamina(currentStamina);
        }

        _eventBus.Publish(new ActorLeveledUpEvent(
            key, packet.NewLevel, currentHp, currentMp, currentStamina, packet.RemainingStatPoints));
    }


    private void HandleSkillPointUpdate(in SmsgSkillPointUpdateHeader packet)
    {
        const byte valid = 1;
        if (packet.Valid != valid)
        {
            _unhandled.Record();
            return;
        }

        const uint levelUpMode = 2;
        if (packet.Mode == levelUpMode
            && _world.LocalActor is { } local
            && packet.Value <= ushort.MaxValue)
        {
            local.SetLevel((ushort)packet.Value);
            RecomputeCombatStats();
        }

        _eventBus.Publish(new SkillPointUpdateEvent(packet.Mode, packet.Value));
    }


    private bool HandleStatsUpdate(ReadOnlySpan<byte> payload)
    {
        const int minSize = 36;
        if (payload.Length < minSize) return false;

        var sort = payload[0x00];
        var actorId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x04, 4));
        var stat0 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x08, 4));
        var stat2 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x0C, 4));
        var currentXp = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(0x10, 8));
        var stat6 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x18, 4));
        var stat4 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x1C, 4));
        var stat5 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x20, 4));

        var key = new ActorKey(actorId, ToEntitySort(sort));

        _eventBus.Publish(new ActorStatSyncEvent(key, stat0, stat2, stat4, stat5, stat6, currentXp));
        return true;
    }


    private bool HandleExpGain(ReadOnlySpan<byte> payload)
    {
        const int minSize = 32;
        if (payload.Length < minSize) return false;

        var sort = payload[0x00];
        var actorId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x04, 4));
        var sourceSort = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x08, 4));
        var amount = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(0x10, 8));

        var key = new ActorKey(actorId, ToEntitySort(sort));
        if (_world.LocalActorKey is { } localKey &&
            localKey != key) return true;

        Progression = Progression.AddExperience(amount);

        if ((byte)sourceSort == 2)
        {
            var ratePercent = XpBonusRatePercentResolver?.Invoke() ?? 0L;
            _ = ExperienceModel.SplitBonus(amount, ratePercent);
        }

        ProgressionRefresh?.Invoke(Progression);
        return true;
    }


    private bool HandleRankXpGain(ReadOnlySpan<byte> payload)
    {
        const int minSize = 20;
        if (payload.Length < minSize) return false;

        var actorId = BinaryPrimitives.ReadUInt32LittleEndian(payload[..4]);
        var sort = payload[0x04];
        var amount = unchecked((long)BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(0x08, 8)));
        var mode = payload[0x10];

        var key = new ActorKey(actorId, ToEntitySort(sort));
        if (_world.LocalActorKey is { } localKey && localKey != key) return true;

        int levelCache = _world.LocalActor?.Level ?? 0;

        try
        {
            Progression = Progression.AddRankXp(amount, mode, levelCache, RankXpDivisorTable, RankXpCapTable);
        }
        catch (LevelTableException ex)
        {
            LevelTableErrorSink?.Invoke(ex.LevelIndex);
            return true;
        }

        ProgressionRefresh?.Invoke(Progression);
        return true;
    }
}