// spec: Docs/RE/opcodes.md; Docs/RE/specs/handlers.md §4/1; Docs/RE/specs/client_runtime.md §9.1/§9.4;
// Docs/RE/packets/4-1_game_state_tick.yaml (Interior record strides block, CYCLE 12 / Phase 3).
// 4/1 is the 9100-byte world-state tick / world-entry payload. Routing and the fixed read size are
// confirmed; interior-block offsets/strides are control-flow-confirmed (CYCLE 12 / Phase 3);
// value-only semantics remain capture/debugger-pending.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

/// <summary>
///     4/1 — server game-state tick and world-entry snapshot. Fixed 9100-byte body in the recovered
///     handler, modelled as an opaque payload with accessor helpers for the pinned world-entry seed.
///     spec: Docs/RE/specs/handlers.md §4/1; Docs/RE/specs/client_runtime.md §9.1/§9.4.
/// </summary>
/// <remarks>
///     The handler branches on body byte +0; form <c>1</c> is the world-entry path. The only payload
///     fields this clean slice consumes are area id at +0x00C (absolute area index; 3-digit decimal
///     directory selects &lt;id&gt;.lst) and spawn X/Z at +0x2374/+0x2378.
///     World Y is not on the wire and is forced to zero by Application.
/// </remarks>
[PacketOpcode(4, 1)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgGameStateTick
{
    /// <summary>Packed opcode 0x40001 (4/1). spec: Docs/RE/opcodes.md.</summary>
    public const uint OpcodeId = Opcodes.SmsgGameStateTick;

    /// <summary>Fixed body read size: 9100 bytes (0x238C). spec: handlers.md §4/1.</summary>
    public const int WireSize = 0x238C;

    /// <summary>Minimum length needed to read form, scenario, and spawn X/Z. spec: client_runtime.md §9.4.</summary>
    public const int WorldEntrySeedSize = SpawnZOffset + sizeof(float);

    /// <summary>Body byte +0 selector; value 1 is the world-entry form. spec: handlers.md §4/1.</summary>
    public const int FormOffset = 0x0000;

    /// <summary>Body byte +0 value for the world-entry branch. spec: handlers.md §4/1.</summary>
    public const byte WorldEntryForm = 1;

    /// <summary>
    ///     Area id at body +0x00C — absolute area index; its 3-digit decimal directory form
    ///     (&lt;id&gt;.lst, e.g. id 6 → "006", id 12 → "012") selects the on-disk area.
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (field AreaId, offset 12);
    ///     Docs/RE/specs/handlers.md §4/1.
    /// </summary>
    public const int AreaIdOffset = 0x000C;

    /// <summary>Spawn X at body +0x2374. spec: client_runtime.md §9.1 step 5 / §9.4.</summary>
    public const int SpawnXOffset = 0x2374;

    /// <summary>Spawn Z at body +0x2378. spec: client_runtime.md §9.1 step 5 / §9.4.</summary>
    public const int SpawnZOffset = 0x2378;

    // =========================================================================
    // Interior block layout — control-flow-confirmed (CYCLE 12 / Phase 3, IDB SHA 263bd994).
    // Verified by contiguity: 24 + 3088 + 4044 + 1920 = 9076, then SpawnX @ 9076 ✓.
    // Consumers slice these regions off the PayloadBuffer span using these consts;
    // no new byte[] allocations — pass Payload as a ReadOnlySpan<byte> slice.
    // spec: Docs/RE/packets/4-1_game_state_tick.yaml ("Interior record strides" notes block).
    // =========================================================================

    // --- WorldEntryTableA ---

    /// <summary>
    ///     Body offset of WorldEntryTableA (the actor roster / label-grid table).
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (WorldEntryTableA offset 24).
    /// </summary>
    public const int TableAOffset = 24; // spec: 4-1_game_state_tick.yaml (WorldEntryTableA @ 24)

    /// <summary>
    ///     Byte size of WorldEntryTableA (3088 bytes).
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (WorldEntryTableA 3088 bytes).
    /// </summary>
    public const int TableASize = 3088; // spec: 4-1_game_state_tick.yaml (WorldEntryTableA size 3088)

    /// <summary>
    ///     Record stride in WorldEntryTableA: 16 bytes per record.
    ///     Per record: lead group bytes @+0..+3; ActorId u32@+4; KeepGuard u32@+8; Aux u32@+12.
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (TableA 16-byte record stride).
    /// </summary>
    public const int TableARecordStride = 16; // spec: 4-1_game_state_tick.yaml (TableA record stride 16)

    /// <summary>
    ///     Capacity of WorldEntryTableA: 193 slots (3088 / 16 = 193).
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (TableA capacity 193).
    /// </summary>
    public const int TableACapacity = 193; // spec: 4-1_game_state_tick.yaml (TableA capacity 193)

    /// <summary>
    ///     Number of records the stale-slot sweep walks in WorldEntryTableA: 120.
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (TableA sweep count 120).
    /// </summary>
    public const int TableASweepCount = 120; // spec: 4-1_game_state_tick.yaml (TableA sweep walks 120 records)

    /// <summary>
    ///     Per-record offset of the ActorId u32 within a WorldEntryTableA record.
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (TableA record: actor-id u32 @+4).
    /// </summary>
    public const int TableARecordActorIdOffset = 4; // spec: 4-1_game_state_tick.yaml (TableA record ActorId u32 @+4)

    /// <summary>
    ///     Per-record offset of the KeepGuard u32 within a WorldEntryTableA record.
    ///     Eviction gate: a slot is evicted only when KeepGuard is 0 (the keep-guard dword also
    ///     doubles as the displayed member number in the roster panel).
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (TableA record: keep-guard u32 @+8).
    /// </summary>
    public const int
        TableARecordKeepGuardOffset =
            8; // spec: 4-1_game_state_tick.yaml (TableA record KeepGuard u32 @+8; eviction gate)

    /// <summary>
    ///     Per-record offset of the Aux u32 within a WorldEntryTableA record.
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (TableA record: aux u32 @+12).
    /// </summary>
    public const int TableARecordAuxOffset = 12; // spec: 4-1_game_state_tick.yaml (TableA record Aux u32 @+12)

    // --- WorldEntryTableB ---

    /// <summary>
    ///     Body offset of WorldEntryTableB (scene tracked-entity / actor-slot table).
    ///     TableB is HETEROGENEOUS — not one flat array. Layout: 240×16B actor-slot records
    ///     (3840 bytes) + 20B unswept gap + 21×8B category entries (168 bytes) + 16B world-target
    ///     selection record. 3840 + 20 + 168 + 16 = 4044. ✓
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (WorldEntryTableB offset 3112).
    /// </summary>
    public const int TableBOffset = 3112; // spec: 4-1_game_state_tick.yaml (WorldEntryTableB @ 3112)

    /// <summary>
    ///     Byte size of WorldEntryTableB (4044 bytes).
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (WorldEntryTableB 4044 bytes).
    /// </summary>
    public const int TableBSize = 4044; // spec: 4-1_game_state_tick.yaml (WorldEntryTableB size 4044)

    /// <summary>
    ///     Number of 16-byte actor-slot records at the start of WorldEntryTableB: 240.
    ///     Record shape is identical to TableA: lead @+0..+3; ActorId u32@+4; KeepGuard u32@+8; Aux u32@+12.
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (TableB: 240×16B actor-slot records).
    /// </summary>
    public const int TableBActorSlotCount = 240; // spec: 4-1_game_state_tick.yaml (TableB 240 actor-slot records)

    /// <summary>
    ///     Byte length of the actor-slot subregion at the start of WorldEntryTableB (240 × 16 = 3840 bytes).
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (TableB actor slots bytes 3840).
    /// </summary>
    public const int TableBActorSlotsBytes = 3840; // spec: 4-1_game_state_tick.yaml (TableB 3840 actor-slot bytes)

    // --- HotbarSlots ---

    /// <summary>
    ///     Body offset of HotbarSlots block (1920 bytes; 240 slots × 8 bytes each).
    ///     Copied verbatim into the hotbar global. Verification: 24 + 3088 + 4044 = 7156. ✓
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (HotbarSlots offset 7156).
    /// </summary>
    public const int HotbarOffset = 7156; // spec: 4-1_game_state_tick.yaml (HotbarSlots @ 7156; 24+3088+4044=7156 ✓)

    /// <summary>
    ///     Byte size of HotbarSlots block (1920 bytes).
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (HotbarSlots 1920 bytes).
    /// </summary>
    public const int HotbarSize = 1920; // spec: 4-1_game_state_tick.yaml (HotbarSlots size 1920)

    /// <summary>
    ///     Number of hotbar slots: 240.
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (HotbarSlots 240 slots).
    /// </summary>
    public const int HotbarSlotCount = 240; // spec: 4-1_game_state_tick.yaml (HotbarSlots count 240)

    /// <summary>
    ///     Byte stride of each hotbar slot: 8 bytes.
    ///     Per slot: EntryKey u32@+0 (0 = empty); Count u16@+4 (quantity/charge); 2 unused bytes.
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (HotbarSlots slot stride 8).
    /// </summary>
    public const int HotbarSlotStride = 8; // spec: 4-1_game_state_tick.yaml (HotbarSlots slot 8 bytes)

    /// <summary>
    ///     Per-slot offset of the EntryKey u32 within a hotbar slot. 0 = empty slot.
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (HotbarSlots slot: EntryKey u32@+0).
    /// </summary>
    public const int
        HotbarSlotEntryKeyOffset = 0; // spec: 4-1_game_state_tick.yaml (HotbarSlots EntryKey u32 @+0; 0=empty)

    /// <summary>
    ///     Per-slot offset of the Count u16 (quantity / charge) within a hotbar slot.
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (HotbarSlots slot: Count u16@+4).
    /// </summary>
    public const int HotbarSlotCountOffset = 4; // spec: 4-1_game_state_tick.yaml (HotbarSlots Count u16 @+4)

    /// <summary>
    ///     Skill category value used to distinguish skill entries from item entries in a hotbar slot.
    ///     The catalogue record's category field value 5 = skill; other small values = non-skill families.
    ///     There is NO inline type byte in the slot itself — skill-vs-item is resolved by catalogue lookup.
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (HotbarSlots: category value 5 = skill).
    /// </summary>
    public const int
        HotbarSkillCategoryValue =
            5; // spec: 4-1_game_state_tick.yaml (HotbarSlots skill category = 5; no inline type byte)

    /// <summary>Opaque 9100-byte tick body. Interior sections are sliced by offset consts above.</summary>
    public readonly PayloadBuffer Payload;

    /// <summary>
    ///     Reads the pinned world-entry seed from a raw 4/1 payload. Returns false if the frame is too
    ///     short or is not the world-entry form.
    /// </summary>
    public static bool TryReadWorldEntrySeed(ReadOnlySpan<byte> payload, out SmsgGameStateTickSeed seed)
    {
        seed = default;

        if (payload.Length < WorldEntrySeedSize || payload[FormOffset] != WorldEntryForm) return false;

        seed = new SmsgGameStateTickSeed(
            payload[FormOffset],
            BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(AreaIdOffset, sizeof(int))),
            BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(SpawnXOffset, sizeof(float))),
            BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(SpawnZOffset, sizeof(float))));
        return true;
    }

    /// <summary>Opaque 9100-byte (0x238C) game-state tick body. spec: handlers.md §4/1.</summary>
    [InlineArray(WireSize)]
    public struct PayloadBuffer
    {
        private byte _element0;
    }
}

/// <summary>
///     The decoded 4/1 world-entry seed: form byte, area id, and X/Z spawn position. World Y is
///     not on the wire. spec: Docs/RE/specs/client_runtime.md §9.1/§9.4;
///     Docs/RE/packets/4-1_game_state_tick.yaml (field AreaId, offset 12).
/// </summary>
/// <param name="Form">Leading form-selector byte (value 1 = world-entry path).</param>
/// <param name="AreaId">
///     Absolute area index at body +0x00C. Its 3-digit decimal directory form (&lt;id&gt;.lst,
///     e.g. id 6 → "006", id 12 → "012") selects the on-disk area.
///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (field AreaId, offset 12).
/// </param>
/// <param name="SpawnX">Local-player spawn X; feeds position set and terrain cold-start.</param>
/// <param name="SpawnZ">Local-player spawn Z; feeds position set and terrain cold-start.</param>
public readonly record struct SmsgGameStateTickSeed(byte Form, int AreaId, float SpawnX, float SpawnZ);