using System.Collections.Immutable;
using System.Runtime.InteropServices;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Network.Protocol.Core.Opcodes;
using MartialHeroes.Network.Protocol.Packets.Login.Packets;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    // -------------------------------------------------------------------------
    // 3/5 — enter-game ack
    // -------------------------------------------------------------------------

    /// <summary>
    ///     3/5 — enter-world acknowledgement / post-login account-ack. Drives the scene spine to Load
    ///     (state 2) and seeds the account character-count authoritatively from char_count@40. 3/5 is
    ///     state-agnostic and UNSOLICITED — it is processed regardless of any prior 1/9 request. spec:
    ///     Docs/RE/specs/client_runtime.md §7.5.2; Docs/RE/packets/3-5_enter_game_response.yaml.
    /// </summary>
    public void Handle(in SmsgEnterGameAck packet)
    {
        _ = packet.BillingFlag; // available for a future use case; billing behavior is not invented here.
        _sceneStateMachine?.OnEnterGameAck();

        // Seed the account char-count authoritatively from char_count@40. Set() clamps 0..5, so passing the
        // raw u32 is safe; the int cast is guarded against overflow. 3/5 is unsolicited (NOT gated on a prior
        // 1/9). spec: Docs/RE/specs/login_flow.md §3.4 / §5.2 (3/5 char_count@40 seeds the account char-count);
        // §1 step 7 (3/5 is unsolicited, NOT gated on a prior 1/9).
        _accountCharacters?.Set((int)Math.Min(packet.CharacterCount, int.MaxValue));
    }

    // -------------------------------------------------------------------------
    // 3/14 — char-spawn result (the actual local-player spawn)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     3/14 — enter-game spawn result. On Result != 0 the client materializes the local player from the
    ///     slot descriptor cached at select time (Section 3.5) and publishes <see cref="LocalPlayerSpawnedEvent" />;
    ///     on Result == 0 it publishes <see cref="LocalPlayerSpawnFailedEvent" /> (a timed failure message).
    ///     The local player is registered as the controlled actor (<see cref="ClientWorld.LocalActorKey" />),
    ///     so the move/skill use cases can source its position. spec: Docs/RE/specs/login_flow.md §3.5 / §5.3.
    /// </summary>
    public void Handle(in SmsgCharSpawnResult packet)
    {
        // 3/14 is a char-mgmt result handler: clear the single in-flight latch.
        // spec: Docs/RE/specs/net_contracts.md §1.3 (CLEARED by 3/14).
        _inFlightLatch?.Clear();

        // Result 0 = failure (a timed message is shown). spec: login_flow.md §5.3.
        if (packet.Result == 0)
        {
            _eventBus.Publish(new LocalPlayerSpawnFailedEvent(packet.Slot));
            return;
        }

        // Success: materialize the local player from the CACHED slot descriptor (Section 3.5). Without a
        // cache (no store wired, or no slot confirmed) there is nothing to spawn from; record and bail.
        var cached = _characterSelection?.Chosen;
        if (cached is null)
        {
            _unhandled.Record(Opcodes.SmsgCharSpawnResult, SmsgCharSpawnResult.WireSize);
            return;
        }

        // The local player's actor id is not carried by the 16-byte 3/14 block (only result + slot + 3
        // opaque spawn-param u32s; their meaning is UNVERIFIED — spec §5.3). Key the local player on the
        // PlayerCharacter sort with the unassigned-id sentinel until a self-spawn (5/3) supplies the real
        // id. spec: Docs/RE/structs/actor.md (id initialised to 0xFFFFFFFF before spawn).
        var key = new ActorKey(ActorKey.UnassignedRawId, EntitySort.PlayerCharacter);

        // Float -> fixed at the boundary; world Y forced to 0. spec: actor.md (coords float, Y = 0).
        var position = Vector3Fixed.FromFloat(cached.WorldX, 0f, cached.WorldZ);

        var spawnInfo = new SpawnInfo(
            key, cached.Level, cached.CurrentHp, cached.CurrentMp, cached.CurrentStamina, cached.ServerClass);
        var vitals = VitalsResolver(spawnInfo);

        var actor = new Actor(
            key, cached.Level, vitals, cached.CurrentHp, cached.CurrentMp, cached.CurrentStamina, position);
        _world.Add(actor);
        _world.LocalActorKey = key; // mark the controlled actor for the move/skill use cases.

        _eventBus.Publish(new LocalPlayerSpawnedEvent(
            key, packet.Slot, cached.Name, cached.Level, actor.Position, actor.CurrentHp, actor.MaxHp,
            cached.ServerClass));
    }

    // -------------------------------------------------------------------------
    // 3/7 — char manage / delete result (8-byte block)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     3/7 — character manage / delete result. Classifies the subtype (subtype 2 = delete-confirm,
    ///     which decrements the account char count) and forwards the ReadyTime so the presentation can
    ///     format a "wait HH:MM" delete-cooldown message on the blocked path. spec:
    ///     Docs/RE/specs/login_flow.md §5.5; Docs/RE/opcodes.md (3/7 SmsgCharManageResult).
    /// </summary>
    public void Handle(in SmsgCharManageResult packet)
    {
        // 3/7 is a char-mgmt result handler: clear the single in-flight latch.
        // spec: Docs/RE/specs/net_contracts.md §1.3 (CLEARED by 3/7).
        _inFlightLatch?.Clear();

        const byte success = 1; // result 1 = success path. spec: §5.5.
        const byte deleteConfirmSubtype = 2; // subtype 2 = delete-confirm. spec: §5.5.
        var ok = packet.Result == success;

        var subtype = packet.Subtype switch
        {
            0 => CharManageSubtype.GenericRefresh, // spec: §5.5 (semantics UNVERIFIED)
            1 => CharManageSubtype.RenameApplied, // spec: §5.5 (semantics UNVERIFIED)
            2 => CharManageSubtype.DeleteConfirm, // spec: §5.5 (delete-confirm)
            _ => CharManageSubtype.Other
        };

        // A successful delete-confirm decrements the account char count. spec: §5.5.
        var charCount = _accountCharacters?.CharacterCount ?? 0;
        if (ok && packet.Subtype == deleteConfirmSubtype && _accountCharacters is not null)
            charCount = _accountCharacters.Decrement();

        // 3/7 writes NO scene state — the table-driven transition is 3/100 (HandleCharActionResult).
        // spec: Docs/RE/specs/client_runtime.md §7.5.2; Docs/RE/opcodes.md (3/7 vs 3/100).
        _eventBus.Publish(new CharManageResultEvent(
            ok, subtype, packet.Subtype, packet.ReadyTime, charCount));
    }

    // -------------------------------------------------------------------------
    // 3/6 — rename-character result
    // -------------------------------------------------------------------------

    /// <summary>
    ///     3/6 — rename-character result. A 12-byte block: result code, error code, padding, slot index, and an unverified
    ///     dword.
    ///     spec: Docs/RE/packets/3-6_rename_char_result.yaml.
    /// </summary>
    public void Handle(in SmsgRenameCharResult packet)
    {
        // 3/6 is a char-mgmt result handler: clear the single in-flight latch.
        // spec: Docs/RE/specs/net_contracts.md §1.3 (CLEARED by 3/6).
        _inFlightLatch?.Clear();

        var ok = packet.Result != 0;

        if (ok)
        {
            // The packet doesn't carry the name; the client updates via 3/4 refresh
            _eventBus.Publish(new CharRenameResultEvent(true, string.Empty, 0));
            return;
        }

        // Failure: ErrorCode maps to a UI error string by the presentation.
        _eventBus.Publish(new CharRenameResultEvent(false, string.Empty, packet.ErrorCode));
    }

    // -------------------------------------------------------------------------
    // 3/23 — character-create result
    // -------------------------------------------------------------------------

    /// <summary>
    ///     3/23 — character-create result. Pairs with the CreateCharacterRequestedEvent the select use-case
    ///     emits for a blank slot. On success the Code byte is the assigned slot id and the account char
    ///     count is incremented; on failure Code is an error code (0xC8..0xD4). spec:
    ///     Docs/RE/specs/login_flow.md §5.4; Docs/RE/packets/SmsgCharCreateResult.
    /// </summary>
    public void Handle(in SmsgCharCreateResult packet)
    {
        const byte success = 1; // result 1 = success. spec: §5.4.
        var ok = packet.Result == success;

        // On success the account char count is incremented. spec: §5.4.
        var charCount = _accountCharacters?.CharacterCount ?? 0;
        if (ok && _accountCharacters is not null) charCount = _accountCharacters.Increment();

        // Code is the assigned slot id on success, or the error code on failure. spec: §5.4.
        var assignedSlotId = ok ? packet.Code : (byte)0;
        var errorCode = ok ? (byte)0 : packet.Code;

        _eventBus.Publish(new CharCreateResultEvent(
            ok, assignedSlotId, errorCode, packet.Value1, packet.Value2, charCount));
    }

    /// <summary>
    ///     3/100 — generic character-management action/result code. Feeds the Campaign-15 scene spine with
    ///     the exact result-code table (0, 1..4/7, 202/203/232, out-of-range). spec:
    ///     Docs/RE/opcodes.md; Docs/RE/specs/client_runtime.md §7.5.2.
    /// </summary>
    public void Handle(in SmsgCharActionResult packet)
    {
        var result = packet.Result > int.MaxValue ? int.MaxValue : (int)packet.Result;
        _sceneStateMachine?.OnCharActionResult(result, _world.LocalActor is not null);
    }

    // -------------------------------------------------------------------------
    // 3/1 — character-select list
    // -------------------------------------------------------------------------

    /// <summary>
    ///     3/1 — character-select list. Decodes the 3-byte header, then one 981-byte per-slot record for each
    ///     set bit in the slot mask (LSB-first, exactly 5 slots (indices 0..4)), pulling the name/level/class/HP out of each
    ///     record's embedded 880-byte SpawnDescriptor. Forces a Select re-entry and emits the list snapshot.
    ///     spec: Docs/RE/packets/3-1_character_list.yaml; Docs/RE/specs/handlers.md §2 / §17.1.
    /// </summary>
    private bool HandleCharacterList(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgCharacterListHeader.HeaderSize) return false;

        // 3/1 is a char-mgmt result handler: clear the single in-flight latch.
        // spec: Docs/RE/specs/net_contracts.md §1.3 (CLEARED by 3/1).
        _inFlightLatch?.Clear();

        ref readonly var header =
            ref MemoryMarshal.AsRef<SmsgCharacterListHeader>(payload);

        // A fresh list replaces the prior roster (and any stale chosen-slot cache). spec: login_flow.md §3.2.
        _characterSelection?.Reset();

        // 3/1 and 3/4 both reach this same 3 + N×981 roster decode. spec: login_flow.md §1 step 7 / §5.1.
        var slots =
            DecodeAndRetainRoster(in header, payload);

        // 3/1 CharacterList FORCES a Select (state 4) re-entry, accepted from Load/Select. This is the
        // 3/1-only behaviour; 3/4 does NOT force a scene change. spec: client_runtime.md §7.5.2; login_flow.md §1 step 7.
        _sceneStateMachine?.OnCharacterListReceived();

        _eventBus.Publish(new CharacterListEvent(header.ServerId, header.ChannelId, slots));
        return true;
    }

    /// <summary>
    ///     Shared roster decode for the 3+N×981 character list, reached by BOTH <c>3/1 SmsgCharacterList</c>
    ///     and <c>3/4 SmsgSceneEntityUpdate</c> (the in-place refill). Walks the slot mask over exactly 5
    ///     slots (indices 0..4), decodes each set slot's embedded 880-byte SpawnDescriptor into a
    ///     <see cref="CharacterListSlot" />, and retains each RAW per-slot record into the
    ///     <see cref="_characterSelection" /> store. The caller owns the <c>Reset()</c>, the scene transition
    ///     (3/1 only), and the <see cref="CharacterListEvent" /> publish. spec: Docs/RE/specs/login_flow.md
    ///     §1 step 7 / §5.1 / §3.2; Docs/RE/packets/3-1_character_list.yaml; Docs/RE/structs/spawn_descriptor.md.
    /// </summary>
    private ImmutableArray<CharacterListSlot> DecodeAndRetainRoster(
        in SmsgCharacterListHeader header, ReadOnlySpan<byte> payload)
    {
        var builder = ImmutableArray.CreateBuilder<CharacterListSlot>();
        var cursor = SmsgCharacterListHeader.HeaderSize;

        // Hard, bounded iteration of exactly 5 slots (indices 0..4); the list never references a slot
        // beyond 4. spec: login_flow.md §3.2 / §7 (Char-list maximum slots = 5).
        for (var slot = 0; slot < CharacterSelectionStore.MaxSlots; slot++)
        {
            if ((header.SlotMask & (1 << slot)) == 0) continue; // bit clear -> no record for this slot. spec: 3-1.

            // Each set bit consumes one 981-byte record = 880 descriptor + 96 stats + 1 flag + 4 timestamp.
            // spec: 3-1_character_list.yaml (SlotRecordSize). A short/truncated frame ends the loop.
            if (cursor + SmsgCharacterListHeader.SlotRecordSize > payload.Length) break;

            var record = payload.Slice(cursor, SmsgCharacterListHeader.SlotRecordSize);
            cursor += SmsgCharacterListHeader.SlotRecordSize;

            // The descriptor is the first 880 bytes of the record. spec: 3-1 / spawn_descriptor.md.
            var reader = new SpawnDescriptorReader(record[..SpawnDescriptorReader.Size]);
            // CONFLICT (committed-spec disagreement, debugger-pending): frontend_scenes.md §3.2 lists the
            // select-row position at +0xA0/+0xA4; structs/actor.md (this reader's source) pins
            // world_x/world_z at +0x4C/+0x50. Reusing the actor.md offsets for decode consistency; the
            // §3.2 offset is unconfirmed. The X/Z (vs X/Y) axis pairing is itself flagged debugger-pending
            // in §3.2. spec: Docs/RE/structs/actor.md / frontend_scenes.md §3.2
            builder.Add(new CharacterListSlot(
                slot, reader.ReadName(), reader.ReadLevel(), reader.ReadServerClass(), reader.ReadCurrentHp(),
                reader.ReadWorldX(), reader.ReadWorldZ()));

            // Retain the RAW per-slot record (880 descriptor + 96 stats + 1 flag byte) so SelectCharacterAsync
            // can detect "@BLANK@", and the 3/14 handler can materialize the local player. spec: login_flow.md §3.5.
            // The 880 + 96 = 976-byte descriptor+stats span; the flag byte is at record +976. spec: §3.2.
            const int descriptorAndStatsSize = SpawnDescriptorReader.Size + 96; // 976
            var slotFlag = record.Length > descriptorAndStatsSize ? record[descriptorAndStatsSize] : (byte)0;
            _characterSelection?.Retain(
                new CharacterSlotRecord(slot, record[..descriptorAndStatsSize], slotFlag));
        }

        return builder.ToImmutable();
    }

    // -------------------------------------------------------------------------
    // 3/4 — scene-entity update / in-place roster refill (gated by form_byte0 == 1)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     3/4 — the in-place character-roster refill. The 3-byte header
    ///     <c>[form_byte0][channel_byte1][slot_mask_byte2]</c> is byte-identical to the 3/1 header, so it is
    ///     read through <see cref="SmsgCharacterListHeader" />; <c>form_byte0</c> is the header's first byte.
    ///     Per spec, 3/4 decodes the roster ONLY when <c>form_byte0 == 1</c> (the in-place refill form);
    ///     other forms are consumed as a no-op. Unlike 3/1, 3/4 does NOT force a scene change. On the refill
    ///     form it resets the selection store, runs the shared 3+N×981 roster decode, and republishes the
    ///     <see cref="CharacterListEvent" /> so the char-select screen repopulates in place. spec:
    ///     Docs/RE/specs/login_flow.md §5.1, §1 step 7.
    /// </summary>
    private bool HandleSceneEntityUpdate(ReadOnlySpan<byte> payload)
    {
        // The 3/4 header is byte-identical to the 3/1 header (form, channel, slot mask). spec: §1 step 7 / §5.1.
        if (payload.Length < SmsgCharacterListHeader.HeaderSize) return false;

        // 3/4 is a char-mgmt result handler: clear the single in-flight latch.
        // spec: Docs/RE/specs/net_contracts.md §1.3 (CLEARED by 3/4).
        _inFlightLatch?.Clear();

        ref readonly var header =
            ref MemoryMarshal.AsRef<SmsgCharacterListHeader>(payload);

        // GATE: form_byte0 is the header's first byte. 3/4 is the in-place refill gated on form_byte0 == 1;
        // any other form is consumed as a no-op refill. spec: Docs/RE/specs/login_flow.md §5.1, §1 step 7.
        const byte refillForm = 1;
        if (header.ServerId !=
            refillForm)
            return true; // consumed; non-form-1 is a no-op refill (no scene change, no decode). spec: §1 step 7.

        // In-place refill: replace the prior roster, run the SAME roster decode 3/1 uses, and republish so
        // the char-select screen repopulates. NO forced scene change (3/4 != 3/1). spec: §5.1, §1 step 7.
        _characterSelection?.Reset();
        var slots =
            DecodeAndRetainRoster(in header, payload);
        _eventBus.Publish(new CharacterListEvent(header.ServerId, header.ChannelId, slots));
        return true;
    }

    /// <summary>
    ///     Creates the local player for the 4/1 world-entry form from the descriptor cached during
    ///     character select, overriding descriptor X/Z with the 4/1 spawn seed. spec:
    ///     Docs/RE/opcodes.md (4/1 is the local-player world spawn); Docs/RE/specs/client_runtime.md
    ///     §9.1 steps 5–7; Docs/RE/structs/actor.md (Y forced to 0 and local-player pointer is side state).
    /// </summary>
    private bool TryCreateLocalPlayerFromCachedDescriptor(
        Vector3Fixed spawnPosition,
        out Actor? actor,
        out int slotIndex,
        out string name,
        out ushort serverClass)
    {
        actor = null;
        slotIndex = -1;
        name = string.Empty;
        serverClass = 0;

        var cached = _characterSelection?.Chosen;
        if (cached is null || cached.RawDescriptor.Length < SpawnDescriptorReader.Size) return false;

        var key = new ActorKey(ActorKey.UnassignedRawId, EntitySort.PlayerCharacter);
        var reader = new SpawnDescriptorReader(cached.RawDescriptor.Span[..SpawnDescriptorReader.Size]);
        var level = reader.ReadLevel();
        var currentHp = reader.ReadCurrentHp();
        var currentMp = reader.ReadCurrentMp();
        var currentStamina = reader.ReadCurrentStamina();
        serverClass = reader.ReadServerClass();

        var spawnInfo = new SpawnInfo(key, level, currentHp, currentMp, currentStamina, serverClass);
        var vitals = VitalsResolver(spawnInfo);

        actor = new Actor(key, level, vitals, currentHp, currentMp, currentStamina, spawnPosition);
        _world.Add(actor);
        _world.LocalActorKey = key;
        slotIndex = cached.SlotIndex;
        name = cached.Name;
        return true;
    }
}