using System.Buffers.Binary;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Client.Domain.Progression.Progression;
using MartialHeroes.Network.Protocol.Core.Opcodes;
using MartialHeroes.Network.Protocol.Packets.World.Packets;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    // -------------------------------------------------------------------------
    // 4/29 — stat update
    // -------------------------------------------------------------------------

    /// <summary>
    ///     4/29 — stat-allocation ack. Applied only when ResultOk == 1; emits
    ///     <see cref="ActorStatsChangedEvent" /> with the five echoed absolute stats and remaining points.
    ///     The five stat values are wire echoes (Domain owns no stat-allocation mutator yet), so this
    ///     handler publishes the snapshot without re-deriving anything. spec:
    ///     Docs/RE/packets/4-29_stat_update.yaml.
    /// </summary>
    public void Handle(in SmsgStatUpdate packet)
    {
        const byte applied = 1; // ResultOk == 1 applies the update. spec: 4-29.
        if (packet.ResultOk != applied) return;

        // The stat update targets the local player; key it on the known local actor when present.
        var key = _world.LocalActorKey ?? new ActorKey(packet.Handle, EntitySort.PlayerCharacter);

        _eventBus.Publish(new ActorStatsChangedEvent(
            key, packet.Stat0, packet.Stat1, packet.Stat2, packet.Stat3, packet.Stat4,
            packet.RemainingStatPoints));
    }

    // -------------------------------------------------------------------------
    // 5/32 — level up
    // -------------------------------------------------------------------------

    /// <summary>
    ///     5/32 — level up. Updates the actor's level and refreshed vitals, then emits
    ///     <see cref="ActorLeveledUpEvent" />. HP/MP are packed as two i32 halves in one i64 (HP = low,
    ///     MP = high). spec: Docs/RE/packets/5-32_level_up.yaml (HpMpPacked, HIGH CONFIDENCE core).
    /// </summary>
    public void Handle(in SmsgLevelUp packet)
    {
        var key = new ActorKey(packet.ActorId, ToEntitySort(packet.Sort));

        // HP = low i32 half, MP = high i32 half of the packed value. spec: 5-32 (HpMpPacked).
        var currentHp = unchecked((uint)(packet.HpMpPacked & 0xFFFF_FFFF)); // 0x14 low
        var currentMp = unchecked((uint)((packet.HpMpPacked >> 32) & 0xFFFF_FFFF)); // 0x14 high
        var currentStamina = unchecked((uint)packet.Stamina); // 0x1c

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

    // -------------------------------------------------------------------------
    // 4/150 — skill-point / level update
    // -------------------------------------------------------------------------

    /// <summary>
    ///     4/150 — skill-point update (fixed 16-byte header). Mode 1 sets the total skill-point pool; mode 2
    ///     is a level-up notice (Value = new level), which also updates the local actor's level. The 255
    ///     display cap is UI-only; the wire value is not clamped. spec: Docs/RE/specs/handlers.md §13 Group F
    ///     (4/150); Docs/RE/structs/skill.md.
    /// </summary>
    private void HandleSkillPointUpdate(in SmsgSkillPointUpdateHeader packet)
    {
        const byte valid = 1; // +0 must equal 1. spec: structs/skill.md (valid).
        if (packet.Valid != valid)
        {
            _unhandled.Record(Opcodes.SmsgSkillPointUpdate, SmsgSkillPointUpdateHeader.HeaderSize);
            return;
        }

        const uint levelUpMode = 2; // mode 2 = level-up notice; Value = new level. spec: structs/skill.md (mode).
        if (packet.Mode == levelUpMode
            && _world.LocalActor is { } local
            && packet.Value <= ushort.MaxValue)
        {
            local.SetLevel((ushort)packet.Value);
            RecomputeCombatStats(); // level changed -> recompose. spec: combat.md §2.
        }

        _eventBus.Publish(new SkillPointUpdateEvent(packet.Mode, packet.Value));
    }

    // -------------------------------------------------------------------------
    // 5/67 — world-entry stat sync
    // -------------------------------------------------------------------------

    /// <summary>
    ///     5/67 — world-entry stat sync. Writes the neutral stat slots and current XP onto the actor's level
    ///     (XP only) and publishes the snapshot. The neutral slot numbering (stat0/2/4/5/6) is preserved
    ///     pending a named-stat mapping; no game-rule remap happens here. spec: Docs/RE/specs/handlers.md §4
    ///     (5/67).
    /// </summary>
    private bool HandleStatsUpdate(ReadOnlySpan<byte> payload)
    {
        const int minSize = 36; // Min fixed payload 36 (0x24). spec: handlers.md §4 (5/67).
        if (payload.Length < minSize) return false;

        // sort@+0; id@+4; stat0@+8; stat2@+12; current-XP i64@+16; stat6@+24; stat4@+28; stat5@+32.
        // spec: handlers.md §4 (5/67 fields).
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

    // -------------------------------------------------------------------------
    // 5/9 — experience gain
    // -------------------------------------------------------------------------

    /// <summary>
    ///     5/9 — experience gain. Adds the 64-bit amount to BOTH the current-XP and lifetime-XP accumulators
    ///     (add-with-carry) for the local player, then fires the XP-bar refresh seam. The §3.1 display split
    ///     (base/bonus, gated on the source-mode low byte == 2) is a presentation-only transform applied with
    ///     the server-set bonus rate — it does not change what accumulates; the rate is injected DATA
    ///     (capture-pending per §12 Q6), so it is 0 (no bonus) unless the composition root supplies it.
    ///     The two trailing proficiency/mastery slots (+24/+28) are not progression state (a separate
    ///     stat-channel writer per §3.3) and are out of scope here.
    ///     spec: Docs/RE/specs/progression.md §3 / §3.1 / §3.4 / §11.
    /// </summary>
    private bool HandleExpGain(ReadOnlySpan<byte> payload)
    {
        const int minSize = 32; // 5/9 payload is 32 bytes. spec: progression.md §3.4.
        if (payload.Length < minSize) return false;

        // sort@+0; id@+4; source-sort@+8 (low byte == 2 enables the §3.1 split); src-id@+12; amount i64@+16.
        // spec: progression.md §3.4.
        var sort = payload[0x00];
        var actorId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x04, 4));
        var sourceSort = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x08, 4));
        var amount = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(0x10, 8));

        // Progression is local-player state only; ignore XP gain reported for any other actor.
        // spec: progression.md §1 (all five S2C channels gated to the local player).
        var key = new ActorKey(actorId, ToEntitySort(sort));
        if (_world.LocalActorKey is { } localKey &&
            localKey != key) return true; // decoded and consumed; just not the local player.

        Progression = Progression.AddExperience(amount); // spec: progression.md §3 (add to both accumulators).

        // §3.1 display split — the floating "<base> + <bonus>" text only fires when source-mode == 2.
        // The split value is informational; the FULL amount already accumulated above. spec: progression.md §3.1.
        if ((byte)sourceSort == 2)
        {
            var ratePercent = XpBonusRatePercentResolver?.Invoke() ?? 0L; // server DATA; 0 = no bonus. spec: §12 Q6.
            _ = ExperienceModel.SplitBonus(amount, ratePercent);
        }

        ProgressionRefresh?.Invoke(Progression); // refresh the XP bar. spec: progression.md §3.
        return true;
    }

    // -------------------------------------------------------------------------
    // 5/11 — rank / honor XP gain
    // -------------------------------------------------------------------------

    /// <summary>
    ///     5/11 — rank/honor XP gain. A separate progression channel (no HP/MP/level math): routes the amount
    ///     through the Domain rank-XP model for the local player — mode 2 adds directly to the rank accumulator,
    ///     any other mode runs the §4 per-level table routine (capped at 25) keyed by the local-player level
    ///     cache. The per-level divisor/cap tables are server/config DATA (capture-pending per §12 Q6), injected
    ///     empty so nothing is invented; a 0 divisor surfaces the "leveltable error" diagnostic.
    ///     spec: Docs/RE/specs/progression.md §4 / §4.1 / §11.
    /// </summary>
    private bool HandleRankXpGain(ReadOnlySpan<byte> payload)
    {
        const int minSize = 20; // 5/11 payload is 20 bytes. spec: progression.md §4.1.
        if (payload.Length < minSize) return false;

        // id@+0; sort@+4; amount u64@+8; mode u8@+16 (2 = direct add). spec: progression.md §4.1.
        var actorId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x00, 4));
        var sort = payload[0x04];
        var amount = unchecked((long)BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(0x08, 8)));
        var mode = payload[0x10];

        // Local-player only. spec: progression.md §1 / §4 (applied to the local player only).
        var key = new ActorKey(actorId, ToEntitySort(sort));
        if (_world.LocalActorKey is { } localKey && localKey != key) return true;

        // The §4 table index / cap special-case is the local-player level cache. spec: progression.md §4.
        int levelCache = _world.LocalActor?.Level ?? 0;

        try
        {
            Progression = Progression.AddRankXp(amount, mode, levelCache, RankXpDivisorTable, RankXpCapTable);
        }
        catch (LevelTableException ex)
        {
            // "leveltable error" — a 0 divisor for the active level. Log and leave state unchanged.
            // spec: progression.md §4.
            LevelTableErrorSink?.Invoke(ex.LevelIndex);
            return true;
        }

        ProgressionRefresh?.Invoke(Progression); // refresh the rank bar. spec: progression.md §4.
        return true;
    }
}