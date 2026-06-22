using System.Buffers.Binary;
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
    ///     state-agnostic and UNSOLICITED — it forces Load regardless of any prior 1/9 request.
    ///     <para>
    ///         <b>Enter-ladder discrimination.</b> 3/5 is overloaded: it is BOTH the tail of the enter
    ///         ladder (1/9 → 3/5 → 4/1) AND an unsolicited post-login account-ack the replica pushes right
    ///         after the 3/4 roster (login_flow.md §1 step 7). Only the enter-ladder 3/5 may arm the
    ///         "enter-world confirmed" latch that lets the following load terminate at InGame; the
    ///         post-login 3/5 must leave it disarmed so the load lands at Select via the roster — otherwise
    ///         the loading-grace timer would carry the client into the world with no 1/9 ever sent (the live
    ///         enter-world fidelity gap). The discriminator is the single in-flight latch: 1/9 ARMS it and
    ///         4/1 (not 3/5) CLEARS it (net_contracts.md §1.3), so an armed latch at 3/5 means an enter is
    ///         genuinely pending. We DELIBERATELY do not clear the latch here (only 4/1 clears it).
    ///     </para>
    ///     spec: Docs/RE/specs/client_runtime.md §7.5.2 / §7.9.4; Docs/RE/specs/login_flow.md §1 step 7 /
    ///     step 9 / §3.4 / §3.5; Docs/RE/specs/net_contracts.md §1.3 (1/9 arms / 4/1 clears; 3/5 does NOT);
    ///     Docs/RE/packets/3-5_enter_game_response.yaml.
    /// </summary>
    public void Handle(in SmsgEnterGameAck packet)
    {
        _ = packet.BillingFlag; // available for a future use case; billing behavior is not invented here.

        // Arm the enter-world latch only when an enter request (1/9) is genuinely in flight. spec:
        // net_contracts.md §1.3 (the in-flight latch is the enter-ladder pending primitive).
        var enterRequestPending = _inFlightLatch?.IsArmed ?? false;
        _sceneStateMachine?.OnEnterGameAck(enterRequestPending);

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
    ///     3/14 — SmsgCharSpawnResponse, the ENTER-LADDER TRIGGER (NOT the spawn). CORRECTED (CYCLE 12 Phase 2):
    ///     this 16-byte reply is the server's answer to the C2S 1/7 mode-1 play-confirm; it PRECEDES and
    ///     TRIGGERS the 1/9 enter send rather than responding to 1/9. On <c>Result != 0</c> (the leading flag
    ///     byte = "go") the client emits the 1/9 CmsgEnterGameRequest FROM INSIDE this handler (server-triggered)
    ///     via the injected <see cref="_enterWorldEmitter" /> seam — which ARMS the in-flight latch — and does
    ///     NOT spawn here. On <c>Result == 0</c> the server has armed a timeout: the client emits NO 1/9 and
    ///     surfaces the existing <see cref="LocalPlayerSpawnFailedEvent" /> (the C# scene machine does not model
    ///     that timer). The ACTUAL local-player spawn happens EXACTLY ONCE, later, on 4/1 SmsgGameStateTick
    ///     (HandleGameStateTick / TryCreateLocalPlayerFromCachedDescriptor) — 3/14 no longer materializes the
    ///     player. Ladder: 1/7(mode 1) → 3/14(flag≠0) → 1/9 → 3/5 → 4/1(spawn). spec:
    ///     Docs/RE/specs/frontend_scenes.md §7 (1/9 emitted from the 3/14 handler; spawn driver is 4/1);
    ///     Docs/RE/packets/3-14_char_spawn_response.yaml (Result flag = enter-ladder trigger);
    ///     Docs/RE/packets/cmsg_char_enter.yaml (ENTER SEQUENCE).
    /// </summary>
    public void Handle(in SmsgCharSpawnResult packet)
    {
        // Result 0 = failure (the server armed a timeout; no 1/9). spec: 3-14_char_spawn_response.yaml.
        if (packet.Result == 0)
        {
            // Zero-flag path: no enter is pending — clear the single in-flight latch (a faithful char-mgmt
            // result-handler clear, per the census) and surface the existing failure event. No 1/9 is emitted.
            // spec: Docs/RE/specs/net_contracts.md §1.3 (3/14 among the latch-clearers — applies on the
            // zero-flag path only; the positive path re-arms via the 1/9 emit below).
            _inFlightLatch?.Clear();
            _eventBus.Publish(new LocalPlayerSpawnFailedEvent(packet.Slot));
            return;
        }

        // Positive flag = "go": EMIT 1/9 from inside the 3/14 handler (server-triggered enter). The
        // emitter (ApplicationUseCases.EmitEnterWorldRequest) builds the 40-byte 1/9 AND arms the in-flight
        // latch, so the subsequent 4/1 is correctly recognized as the enter confirmation and clears it.
        //
        // LATCH RECONCILIATION (deliberate, NOT a regression of the census). net_contracts.md §1.3 lists
        // 3/14 among the latch-clearers, but under the corrected ladder the 3/14 handler immediately
        // EMITS 1/9 which RE-ARMS the latch. So on the positive-flag path we DO NOT clear-then-leave-cleared:
        // we let the 1/9 emit Arm() the latch. Net result: after a positive 3/14 the latch is ARMED (a 1/9
        // is in flight), exactly as the 4/1 enter-confirmation read expects. The spawn is NOT done here.
        // spec: Docs/RE/specs/net_contracts.md §1.3 (3/14-emits-1/9-which-arms; the latch is the
        // enter-in-flight primitive that 4/1 clears); Docs/RE/specs/frontend_scenes.md §7.
        if (_enterWorldEmitter is not null)
        {
            // Fire-and-forget the server-triggered 1/9 (a low-rate enter send). The slot to enter is the
            // 3/14 body's Slot field. The emitter arms the latch internally. spec: cmsg_char_enter.yaml.
            _ = _enterWorldEmitter(packet.Slot, CancellationToken.None);
            return;
        }

        // Fallback (emitter unwired — e.g. core tests with no use-case facade): nothing can send 1/9, so
        // record the opcode rather than crash. The WIRED path (composition root passes
        // ApplicationUseCases.EmitEnterWorldRequest) is the real ladder. spec: frontend_scenes.md §7.
        _unhandled.Record(Opcodes.SmsgCharSpawnResult, SmsgCharSpawnResult.WireSize);
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
    ///     3/6 — rename-character result. A 12-byte block: result code, error code, padding, then two IEEE float
    ///     placement values (binary-confirmed CYCLE 8 Phase 2.1; the prior "slot index + unverified dword" is refuted).
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
    // 3/23 — select-screen character level and status update (SmsgCharStatusBytesByName)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     3/23 — select-screen character level and status update (28-byte body, by-name roster patch).
    ///     Binary-confirmed (Phase 2b, build 263bd994): this is NOT a 12-byte char-create result.
    ///     When HasCustomText is non-zero the handler matches CharacterName in the lobby roster and
    ///     updates StatusValue and Level. When HasCustomText is 0 it switches on StatusCode to show a
    ///     timed status notice. If the local player is instantiated it updates the global status flag and
    ///     the local player level global. Create is acked via 3/7 SmsgCharManageResult + a refreshed 3/4
    ///     char list — NOT via a 3/23 create-result handler.
    ///     spec: Docs/RE/packets/3-23_char_select_status_update.yaml;
    ///     Docs/RE/specs/net_contracts.md §2.2.
    /// </summary>
    public void Handle(in SmsgCharStatusBytesByName packet)
    {
        // When HasCustomText is non-zero: by-name roster patch — update the matching slot's
        // StatusValue (character status / PK / faction byte) and Level fields.
        // When HasCustomText is 0: code-based timed status notice (shown by presentation).
        // The application layer surfaces these as a roster-status event for the presentation to act on.
        // spec: Docs/RE/packets/3-23_char_select_status_update.yaml (handler behavior).
        _eventBus.Publish(new CharStatusBytesByNameEvent(
            packet.HasCustomText != 0,
            packet.StatusCode,
            packet.StatusValue,
            packet.Level));
    }

    /// <summary>
    ///     3/100 — generic character-management action/result code. Feeds the scene spine with the exact
    ///     result-code table (0, 1..4/7, 202/203/232, out-of-range) AND publishes the decoded 4-byte code
    ///     so the presentation can surface the reason (the live log left this code undecoded). When a 3/100
    ///     arrives during the enter phase (a 1/9 was sent and the server answered 3/100 instead of the 4/1
    ///     world tick), the scene machine treats it as a REJECTION and returns to char-select rather than
    ///     entering the world. spec: Docs/RE/opcodes.md; Docs/RE/specs/client_runtime.md §7.5.2;
    ///     Docs/RE/specs/login_flow.md §1 step 6 (coded outcome) / step 9 (enter only on 3/5→4/1).
    /// </summary>
    public void Handle(in SmsgCharActionResult packet)
    {
        // Surface the raw 4-byte action-result code so the rejection reason is VISIBLE (it was not decoded
        // in the live log). Code 0 = success; any non-zero code is a rejection per the §7.5.2 table.
        // spec: Docs/RE/specs/login_flow.md §1 step 6 (a coded outcome shows a message); §7.5.2.
        _eventBus.Publish(new CharActionResultEvent(packet.Result, packet.Result != 0));

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

        // CA2014: scratch buffer hoisted out of the per-slot loop (stackalloc-in-loop). Reuse is safe
        // because ReadVisibleGearGids fully overwrites all 6 entries each call and ImmutableArray.Create
        // snapshots them before the next iteration overwrites the buffer.
        Span<uint> gearScratch = stackalloc uint[SpawnDescriptorReader.VisibleGearSlots.Length];

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
            // Decode the appearance-driver fields the layer-05 preview row renders the REAL character
            // from (internal_class is the skeleton driver, NOT server_class). The six visible-gear gids
            // are the overlay slots {3,4,6,2,11,14} read out of the +0x58 equip table.
            // spec: Docs/RE/packets/3-1_character_list.yaml (Sub-block 1 + APPEARANCE DRIVER).
            reader.ReadVisibleGearGids(gearScratch);
            var equipGids = ImmutableArray.Create(gearScratch);

            // Sub-block 3 (record +0x3D0, 1 byte): the server-supplied per-slot occupied/selectable flag — the
            // byte the select window mirrors at its +0x148C field (gates the 1/7 select/manage click). Sub-block
            // 4 (record +0x3D1, LE u32): the per-slot FLAGS WORD (bit 0 = billing/premium). Both are surfaced on
            // the slot so layer 05 has the server flags end-to-end. spec: Docs/RE/packets/3-1_character_list.yaml
            // (sub-blocks 3/4); Docs/RE/specs/frontend_scenes.md §3.4 (+0x148C = server-supplied per-slot flag).
            const int descriptorAndStatsSize = SpawnDescriptorReader.Size + 96; // 976 = SlotFlag offset (+0x3D0)
            const int flagsWordOffset = descriptorAndStatsSize + 1; // 977 = FlagsWord offset (+0x3D1)
            var slotFlag = record.Length > descriptorAndStatsSize ? record[descriptorAndStatsSize] : (byte)0;
            var billingFlags = record.Length >= flagsWordOffset + sizeof(uint)
                ? BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(flagsWordOffset, sizeof(uint)))
                : 0u;

            // CurrentHp is the u32-clamped value of the SINGLE 64-bit HP qword (+0x3C); +0x40 is HP-HIGH,
            // not MP. spec: Docs/RE/structs/spawn_descriptor.md (HP-qword correction).
            builder.Add(new CharacterListSlot(
                slot, reader.ReadName(), reader.ReadLevel(), reader.ReadServerClass(),
                reader.ReadCurrentHpClamped(),
                reader.ReadWorldX(), reader.ReadWorldZ(),
                reader.ReadInternalClass(), reader.ReadAppearanceVariant(), reader.ReadFaceA(), equipGids,
                slotFlag, billingFlags));

            // Retain the RAW per-slot record (880 descriptor + 96 stats + 1 flag byte) so SelectCharacterAsync
            // can detect "@BLANK@", and the 3/14 handler can materialize the local player. spec: login_flow.md §3.5.
            // The 880 + 96 = 976-byte descriptor+stats span; the flag byte is at record +976. spec: §3.2.
            _characterSelection?.Retain(
                new CharacterSlotRecord(slot, record[..descriptorAndStatsSize], slotFlag, billingFlags));
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
        out ushort serverClass,
        out ImmutableArray<uint> equipGids)
    {
        actor = null;
        slotIndex = -1;
        name = string.Empty;
        serverClass = 0;
        equipGids = [];

        var cached = _characterSelection?.Chosen;
        if (cached is null || cached.RawDescriptor.Length < SpawnDescriptorReader.Size) return false;

        var key = new ActorKey(ActorKey.UnassignedRawId, EntitySort.PlayerCharacter);
        var reader = new SpawnDescriptorReader(cached.RawDescriptor.Span[..SpawnDescriptorReader.Size]);
        var level = reader.ReadLevel();
        // HP-qword correction: HP is ONE int64 @ +0x3C (clamped to u32); the single MP/stamina-class vital
        // is @ +0x44 (vital_b). The former +0x40 "MP" was the HP-HIGH dword. The 3-vital Actor shape is
        // seeded from (hp, vital_b, vital_b) — both lower vital slots take the single vital_b rather than a
        // fabricated stamina the descriptor no longer supplies. spec: Docs/RE/structs/spawn_descriptor.md.
        var currentHp = reader.ReadCurrentHpClamped();
        var vitalB = reader.ReadVitalB();
        serverClass = reader.ReadServerClass();

        var spawnInfo = new SpawnInfo(key, level, currentHp, vitalB, vitalB, serverClass);
        var vitals = VitalsResolver(spawnInfo);

        actor = new Actor(key, level, vitals, currentHp, vitalB, vitalB, spawnPosition);
        _world.Add(actor);
        _world.LocalActorKey = key;
        slotIndex = cached.SlotIndex;
        name = cached.Name;
        // The six visible-gear GIDs ({3,4,6,2,11,14}) from the cached descriptor's +0x58 equip table, so
        // the 4/1 world-entry spawn carries the same equip-overlay input the 3/14 path does. spec:
        // Docs/RE/structs/spawn_descriptor.md (+0x58 equip table).
        equipGids = cached.EquipGids;
        return true;
    }
}