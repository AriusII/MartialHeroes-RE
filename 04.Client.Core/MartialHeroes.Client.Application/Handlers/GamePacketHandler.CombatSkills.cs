using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Client.Domain.Skills.Skills;
using MartialHeroes.Network.Protocol.Core.Opcodes;
using MartialHeroes.Network.Protocol.Packets.World.Packets;
using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    // -------------------------------------------------------------------------
    // 4/102 — full skill/state-window snapshot (30 buff records)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     4/102 — full skill/state-window snapshot. Fixed 476-byte block; the 30 buff records are FIELDS
    ///     inside the struct (so this is a typed Handle, not a span loop). Rebuilds the 30-slot HUD buff bar:
    ///     each non-empty record (BuffXXId != 0) becomes a populated <see cref="BuffSlot" />; empty records
    ///     become <see cref="BuffSlot.EmptyBuffId" />. The per-record 12-byte param roles are
    ///     CAPTURE-UNVERIFIED (competing {id,X,Y} vs {id,?,duration,stack,flag}), so the duration is passed
    ///     as a live-pending candidate (null) rather than inventing duration/stack semantics. Published to
    ///     the HUD hub (when wired) via <see cref="IHudEventHub.PublishBuffState" />. spec:
    ///     Docs/RE/packets/4-102_buff_state.yaml.
    /// </summary>
    public void Handle(in SmsgSkillWindowStateUpdate packet)
    {
        if (_hudEventHub is null)
            return; // no HUD sink wired — nothing to publish (the state is server-owned, no Domain mutation).

        // Rebuild all 30 slots in wire order. spec: 4-102 (clear all 30, re-show active ones).
        var slots = ImmutableArray.CreateBuilder<BuffSlot>(
            SmsgSkillWindowStateUpdate.BuffRecordCount);

        // live-pending: the per-record 12-byte param roles (X/Y vs duration/stack/flag) are CAPTURE-UNVERIFIED.
        // Pass the id through; carry the duration as a null candidate (do NOT invent ms/s/stack semantics).
        // spec: 4-102_buff_state.yaml (competing {id,X,Y} vs {id,?,duration,stack,flag}).
        AddBuffSlot(slots, packet.Buff00Id);
        AddBuffSlot(slots, packet.Buff01Id);
        AddBuffSlot(slots, packet.Buff02Id);
        AddBuffSlot(slots, packet.Buff03Id);
        AddBuffSlot(slots, packet.Buff04Id);
        AddBuffSlot(slots, packet.Buff05Id);
        AddBuffSlot(slots, packet.Buff06Id);
        AddBuffSlot(slots, packet.Buff07Id);
        AddBuffSlot(slots, packet.Buff08Id);
        AddBuffSlot(slots, packet.Buff09Id);
        AddBuffSlot(slots, packet.Buff10Id);
        AddBuffSlot(slots, packet.Buff11Id);
        AddBuffSlot(slots, packet.Buff12Id);
        AddBuffSlot(slots, packet.Buff13Id);
        AddBuffSlot(slots, packet.Buff14Id);
        AddBuffSlot(slots, packet.Buff15Id);
        AddBuffSlot(slots, packet.Buff16Id);
        AddBuffSlot(slots, packet.Buff17Id);
        AddBuffSlot(slots, packet.Buff18Id);
        AddBuffSlot(slots, packet.Buff19Id);
        AddBuffSlot(slots, packet.Buff20Id);
        AddBuffSlot(slots, packet.Buff21Id);
        AddBuffSlot(slots, packet.Buff22Id);
        AddBuffSlot(slots, packet.Buff23Id);
        AddBuffSlot(slots, packet.Buff24Id);
        AddBuffSlot(slots, packet.Buff25Id);
        AddBuffSlot(slots, packet.Buff26Id);
        AddBuffSlot(slots, packet.Buff27Id);
        AddBuffSlot(slots, packet.Buff28Id);
        AddBuffSlot(slots, packet.Buff29Id);

        _hudEventHub.PublishBuffState(BuffStateEvent.FromSlots(slots.MoveToImmutable()));
    }

    // -------------------------------------------------------------------------
    // 5/33 — skill hotbar slot overwrite
    // -------------------------------------------------------------------------

    /// <summary>
    ///     5/33 — authoritative server overwrite of one skill-hotbar slot for the local player. Writes the
    ///     {skill, points} entry into the 240-slot hotbar (mirroring the cooldown duration), then emits the
    ///     snapshot. Ignored when no <see cref="LocalPlayerState" /> is wired. spec: Docs/RE/specs/handlers.md §4
    ///     (5/33); Docs/RE/structs/skill.md.
    /// </summary>
    public void Handle(in SmsgSkillHotbarSlotSet packet)
    {
        // HotbarSlot must be < 240. spec: structs/skill.md (hotbar_slot < 0xF0).
        if (packet.HotbarSlot >= SmsgSkillHotbarSlotSet.HotbarSlotCount)
        {
            _unhandled.Record(Opcodes.SmsgSkillHotbarSlotSet, SmsgSkillHotbarSlotSet.WireSize);
            return;
        }

        var skill = new SkillId(unchecked((uint)packet.SkillId));
        if (_localPlayer is not null)
        {
            var cooldownMs =
                CooldownDurationResolver?.Invoke(skill) ?? 0; // skills.scr lookup; 0 = ready. spec: skills.md §4.
            _localPlayer.SetHotbarSlot(packet.HotbarSlot, skill, packet.SkillPoints, cooldownMs);
        }

        _eventBus.Publish(new SkillHotbarSlotSetEvent(packet.HotbarSlot, skill, packet.SkillPoints));
    }

    // -------------------------------------------------------------------------
    // 4/41 — skill hotbar assign result
    // -------------------------------------------------------------------------

    /// <summary>
    ///     4/41 — result of a client-initiated hotbar assignment. spec: Docs/RE/specs/handlers.md §13 Group C
    ///     (4/41); Docs/RE/structs/skill.md.
    /// </summary>
    public void Handle(in SmsgSkillHotbarAssignResult packet)
    {
        const byte ok = 1; // gate 1 = apply/ok. spec: structs/skill.md (4/41 gate).
        var success = packet.Gate == ok;

        _eventBus.Publish(new SkillHotbarAssignResultEvent(
            success, packet.ResultCode, packet.HotbarSlotEcho,
            new SkillId(unchecked((uint)packet.SkillIdEcho)), packet.SkillPointPool));
    }
    // -------------------------------------------------------------------------
    // 5/52 — actor skill action (24-byte header + 36-byte target records)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     5/52 — actor skill action / combat result. Reinterprets the fixed 24-byte header, then loops
    ///     <see cref="SmsgActorSkillAction.TargetCount" /> records of stride
    ///     <see cref="SmsgActorSkillAction.TargetRecordStride" /> (36) from
    ///     payload[<see cref="SmsgActorSkillAction.HeaderSize" />..]. Per record it reads TargetSubKey @+0x00
    ///     (u8, spec-agreed) and TargetKey @+0x04 (u32, spec-agreed); the 64-bit visible-damage value offset
    ///     is AMBIGUOUS, so it reads BOTH candidate i64s raw and forwards them unmodified (no polarity/decode
    ///     chosen). For each target it publishes a <see cref="CombatTextEvent" /> on the HUD hub (when wired)
    ///     carrying the target key + skill id, with the raw damage candidates passed through. spec:
    ///     Docs/RE/packets/5-52_actor_skill_action.yaml; Docs/RE/specs/handlers.md §17.11 / §20.3.
    /// </summary>
    private bool HandleActorSkillAction(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgActorSkillAction.HeaderSize) return false;

        ref readonly var header = ref MemoryMarshal.AsRef<SmsgActorSkillAction>(payload);
        var skillId = header.SkillId; // header +0x0C (CONFIRMED). spec: 5-52 (SkillId @0x0C).

        var records = payload[SmsgActorSkillAction.HeaderSize..];

        // TargetCount is bounded (0, 0x28]; iterate as far as the buffer allows. spec: 5-52 (TargetCount @0x14).
        for (var t = 0; t < header.TargetCount; t++)
        {
            var recordStart = t * SmsgActorSkillAction.TargetRecordStride;
            if (recordStart + SmsgActorSkillAction.TargetRecordStride > records.Length) break; // short read — stop.

            var record = records.Slice(recordStart, SmsgActorSkillAction.TargetRecordStride);

            // Spec-agreed offsets: sub-key u8 @+0x00, key u32 @+0x04. spec: 5-52 (TargetSubKeyOffset/TargetKeyOffset).
            var targetSubKey = record[SmsgActorSkillAction.TargetSubKeyOffset];
            var targetKey = BinaryPrimitives.ReadUInt32LittleEndian(
                record.Slice(SmsgActorSkillAction.TargetKeyOffset, sizeof(uint)));

            // live-pending: damage offset ambiguous (handlers.md §17.11 +0x10/+0x14 vs 5-52.yaml +0x14/+0x18).
            // Read BOTH candidate i64s raw; do NOT pick a polarity or decode damage here.
            // spec: handlers.md §17.11 (polarity live-pending).
            var damageCandidateA =
                record.Length >= 0x10 + sizeof(long)
                    ? BinaryPrimitives.ReadInt64LittleEndian(record.Slice(0x10, sizeof(long)))
                    : 0L; // §17.11 reading: +0x10/+0x14.
            var damageCandidateB =
                record.Length >= 0x14 + sizeof(long)
                    ? BinaryPrimitives.ReadInt64LittleEndian(record.Slice(0x14, sizeof(long)))
                    : 0L; // 5-52.yaml reading: +0x14/+0x18.

            var key = new ActorKey(targetKey, ToEntitySort(targetSubKey));

            // No committed damage semantics: forward target key + skill id, raw candidates passed through.
            // Value left 0 (undecoded), Kind 0, IsCrit false until a capture pins the damage offset/polarity.
            _hudEventHub?.PublishCombatText(new CombatTextEvent(
                key,
                0,
                CombatTextEvent.MinKind,
                false,
                skillId,
                damageCandidateA,
                damageCandidateB));
        }

        return true;
    }

    /// <summary>
    ///     Appends one 4/102 buff record as a <see cref="BuffSlot" />: a populated slot when the catalog id is
    ///     non-zero, else the empty sentinel. The remaining-time candidate is null (live-pending — the
    ///     duration field role is CAPTURE-UNVERIFIED). spec: Docs/RE/packets/4-102_buff_state.yaml.
    /// </summary>
    private static void AddBuffSlot(
        ImmutableArray<BuffSlot>.Builder slots, uint buffId)
    {
        if (buffId == 0u)
        {
            slots.Add(new BuffSlot(BuffSlot.EmptyBuffId, null)); // empty slot. spec: 4-102.
            return;
        }

        // The catalog id is a u32 on the wire but the HUD slot keys it as u16; take the low word.
        // spec: 4-102_buff_state.yaml (buff id; HUD BuffSlot.BuffId is u16). live-pending: duration role.
        slots.Add(new BuffSlot(unchecked((ushort)buffId), null));
    }

    // -------------------------------------------------------------------------
    // 5/31 — buff/status slot update
    // -------------------------------------------------------------------------

    /// <summary>
    ///     5/31 — buff/status slot update. Writes the 12-byte status entry into the per-actor buff table; for
    ///     the local player it mirrors into <see cref="LocalPlayerState.Buffs" /> and recomputes combat stats
    ///     (a buff is a stat contribution). Only the per-actor table regime (slot 0..30) mutates Domain;
    ///     larger slot regimes are surfaced as events only. spec: Docs/RE/specs/handlers.md §4 (5/31);
    ///     Docs/RE/specs/skills.md §6.1.
    /// </summary>
    private bool HandleBuffSlotUpdate(ReadOnlySpan<byte> payload)
    {
        const int minSize = 56; // Min fixed payload 56 (0x38). spec: handlers.md §4 (5/31).
        if (payload.Length < minSize) return false;

        // (sort@+0, id@+4) actor key; slot@+8; effect code@+12; value/duration@+16; extra/param@+20.
        // spec: handlers.md §4 (5/31 fields).
        var sort = payload[0x00];
        var actorId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x04, 4));
        var slot = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x08, 4));
        var effectCode = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x0C, 4));
        var duration = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x10, 4));
        var extra = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x14, 4));

        var key = new ActorKey(actorId, ToEntitySort(sort));

        // Per-actor table regime: small slots (0..30) write the local mirror. The >30 / >=1,000,000 regimes
        // are global / per-actor-only and not modelled in this 31-slot local table. spec: skills.md §6.1.
        var isLocal = _world.LocalActorKey == key;
        if (isLocal && _localPlayer is not null && slot < BuffTable.SlotCount)
        {
            // Magnitude (the parallel secondary-table strength) is not in this 56-byte read; default 0.
            // spec: skills.md §6.1 (secondary magnitude table separate).
            _localPlayer.Buffs.Apply(
                (int)slot, unchecked((int)effectCode), unchecked((int)duration), unchecked((int)extra), 0);
            RecomputeCombatStats(); // a buff changed -> recompose. spec: combat.md §2.2.
        }

        _eventBus.Publish(new BuffSlotChangedEvent(
            key, unchecked((int)slot), unchecked((int)effectCode), unchecked((int)duration), unchecked((int)extra)));
        return true;
    }
}