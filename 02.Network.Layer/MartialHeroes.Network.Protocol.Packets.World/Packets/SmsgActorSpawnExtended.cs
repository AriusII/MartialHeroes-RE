// spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml — opcode 5/1 (0x50001), 912-byte fixed block.
// SpawnDescriptor sub-field layout: Docs/RE/structs/spawn_descriptor.md / structs/actor.md
// (decode is a Domain/Application concern; here it is an opaque 880-byte buffer).
//
// CONTROL-FLOW CONFIRMED (static IDA, IDB SHA 263bd994): the fixed 912-byte body, the 12-byte prefix
// + 880-byte SpawnDescriptor + 20-byte trailer split, and the player-branch HP qword are all confirmed.
// Descriptor interior VALUES and trailer value-semantics are capture/debugger-pending (non-blocking).
// spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml.
//
// CYCLE 11 (2026-06-22): HP qword wire offset added for Sort==1 (player) branch:
//   descriptor +0x3C (HP-low dword) / +0x40 (HP-high dword) → WIRE +0x48 / +0x4C.
//   This is ONE 64-bit HP qword — NOT two independent vitals. See PlayerHpQword() accessor.
//   Σ check: 12 (prefix) + 880 (descriptor) + 20 (trailer) = 912 ✓
// World-pos landmark: descriptor +0x4C (world_x) / +0x50 (world_z) → WIRE +0x58 / +0x5C.
//   (12-byte prefix shifts these by 4 vs the 5/3 8-byte-prefix opcode which lands at +0x54/+0x58.)
//   spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml (WORLD-POSITION WIRE OFFSET note).

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;
using MartialHeroes.Network.Protocol.Core.Packets;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

/// <summary>
///     5/1 — server pushes an extended actor spawn (player, mob/NPC, or ground item): a 12-byte prefix,
///     an embedded 880-byte SpawnDescriptor record, and a 20-byte trailer of visual/combat flags.
///     Fixed 912-byte payload block. Σ = 12 (prefix) + 880 (SpawnDescriptor) + 20 (Trailer) = 912.
///     spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml. CONTROL-FLOW CONFIRMED (IDB SHA 263bd994).
/// </summary>
/// <remarks>
///     The 880-byte <see cref="SpawnDescriptor" /> is intentionally opaque on this layer: its internal
///     fields (name, vitals, world X/Z, class, etc.) are documented in Docs/RE/structs/spawn_descriptor.md
///     and are decoded by the Domain/Application layer, not by Network.Protocol.
///     <para>
///         <b>World-position landmark (CONFIRMED):</b> world_x sits at descriptor +0x4C → WIRE +0x58;
///         world_z at descriptor +0x50 → WIRE +0x5C (12-byte prefix). Do NOT use the 5/3 rule (+0x54/+0x58)
///         here — the 4-byte-larger prefix is exactly what shifts them. spec: packets/5-1_actor_spawn_extended.yaml.
///     </para>
///     <para>
///         <b>Player-branch HP qword (CYCLE 11, CONFIRMED):</b> on Sort==1 (player) the handler reads ONE
///         64-bit HP value from descriptor +0x3C (HP-low dword) / +0x40 (HP-high dword) → WIRE +0x48 / +0x4C.
///         This is NOT two independent vitals. Use <see cref="PlayerHpQword" /> on Sort==1 only.
///         spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml (HP QWORD WIRE OFFSET, CYCLE 11).
///     </para>
/// </remarks>
[PacketOpcode(5, 1)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgActorSpawnExtended
{
    /// <summary>Packed opcode 0x50001 (5/1). spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml.</summary>
    public const uint OpcodeId = Opcodes.SmsgActorSpawnExtended;

    /// <summary>
    ///     Declared wire size in bytes = 12 (prefix) + 880 (SpawnDescriptor) + 20 (Trailer) = 912.
    ///     spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml (size: 912). Σ-verified.
    /// </summary>
    public const int WireSize = 912; // spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml — Σ 12+880+20=912

    // --- Descriptor-relative offsets for HP qword accessor (player branch only) ---
    // spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml (HP QWORD WIRE OFFSET, CYCLE 11).

    /// <summary>
    ///     Descriptor-relative offset of HP-low dword (+0x3C). With descriptor at frame +0x0C,
    ///     WIRE +0x48 (HP-low). spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml.
    /// </summary>
    private const int
        SdHpLowOffset = 0x3C; // spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml (HP-low dword @ desc+0x3C)

    /// <summary>
    ///     Descriptor-relative offset of HP-high dword (+0x40). With descriptor at frame +0x0C,
    ///     WIRE +0x4C (HP-high). spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml.
    /// </summary>
    private const int
        SdHpHighOffset = 0x40; // spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml (HP-high dword @ desc+0x40)

    // --- Wire fields (Pack=1 sequential; offsets are packet-payload-relative) ---

    /// <summary>
    ///     0x00 — actor sort: 1=player, 2=mob/NPC, 3=ground item. HIGH CONFIDENCE. CONFIRMED.
    ///     spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml.
    /// </summary>
    public readonly byte Sort;

    /// <summary>
    ///     0x01 — alignment padding to the next dword (3 bytes). spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml (Pad0).
    /// </summary>
    private readonly byte _pad0_0;

    private readonly byte _pad0_1;
    private readonly byte _pad0_2;

    /// <summary>0x04 — actor id (LE u32). spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml.</summary>
    public readonly uint ActorId;

    /// <summary>0x08 — title / state byte. spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml.</summary>
    public readonly byte TitleState;

    /// <summary>0x09 — title slot / secondary flag. spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml.</summary>
    public readonly byte TitleSlot;

    /// <summary>0x0A — guild / relation flag. spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml.</summary>
    public readonly byte RelationFlag;

    /// <summary>0x0B — padding byte. spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml (Pad1).</summary>
    private readonly byte _pad1;

    /// <summary>
    ///     0x0C — embedded 880-byte SpawnDescriptor (opaque here; decode per Docs/RE/structs/spawn_descriptor.md).
    ///     World X/Z landmarks: descriptor +0x4C (world_x) → WIRE +0x58; +0x50 (world_z) → WIRE +0x5C.
    ///     Player HP qword: descriptor +0x3C (HP-low) → WIRE +0x48; +0x40 (HP-high) → WIRE +0x4C.
    ///     spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml.
    /// </summary>
    public readonly SpawnDescriptorBuffer SpawnDescriptor;

    /// <summary>
    ///     0x37C — 20-byte trailer: combat flag + visual/stealth bytes (per-byte meanings capture-pending).
    ///     spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml (Trailer: bytes[20]).
    /// </summary>
    public readonly TrailerBuffer Trailer;

    // --- Typed accessors ---

    /// <summary>
    ///     Typed view over <see cref="Sort" />. 1=PlayerCharacter, 2=Mob, 3=GroundItem.
    ///     spec: Docs/RE/structs/actor.md; Docs/RE/packets/5-1_actor_spawn_extended.yaml.
    /// </summary>
    public ActorSort SortKind => (ActorSort)Sort; // spec: Docs/RE/structs/actor.md (sort low byte)

    /// <summary>
    ///     Returns <see langword="true" /> when <see cref="Sort" /> indicates a player character (Sort==1).
    ///     Only call <see cref="PlayerHpQword" /> on the player branch.
    ///     spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml (Sort low-byte branch: 1=player).
    /// </summary>
    public bool IsPlayerBranch => Sort == 1; // spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml (Sort==1 player)

    /// <summary>
    ///     PLAYER BRANCH ONLY (Sort==1). Reads the single 64-bit HP qword from the SpawnDescriptor:
    ///     descriptor +0x3C (HP-low dword) combined with +0x40 (HP-high dword) → WIRE +0x48 / +0x4C.
    ///     This is ONE i64 HP — not two independent vitals. Do NOT call this on Sort==2 or Sort==3
    ///     (mob/NPC / ground item) branches — the bytes mean something different there.
    ///     spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml (HP QWORD WIRE OFFSET, CYCLE 11 CONFIRMED).
    /// </summary>
    public readonly long PlayerHpQword
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            // spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml — HP qword at descriptor +0x3C (low) / +0x40 (high).
            // Descriptor sits at frame +0x0C → HP-low WIRE +0x48, HP-high WIRE +0x4C. Read as one LE i64.
            // Zero-copy: read directly from the SpawnDescriptor inline buffer.
            ref readonly var descBase =
                ref Unsafe.As<SpawnDescriptorBuffer, byte>(ref Unsafe.AsRef(in SpawnDescriptor));
            var span = MemoryMarshal.CreateReadOnlySpan(in descBase, 880);
            return BinaryPrimitives.ReadInt64LittleEndian(span[SdHpLowOffset..]);
        }
    }

    /// <summary>
    ///     0x0C — opaque 880-byte (0x370) SpawnDescriptor record. Sub-fields per
    ///     Docs/RE/structs/spawn_descriptor.md. spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml.
    /// </summary>
    [InlineArray(880)]
    public struct SpawnDescriptorBuffer
    {
        private byte _element0;
    }

    /// <summary>
    ///     0x37C — 20-byte trailer; per-byte meanings capture-pending.
    ///     spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml (Trailer: bytes[20]).
    /// </summary>
    [InlineArray(20)]
    public struct TrailerBuffer
    {
        private byte _element0;
    }
}