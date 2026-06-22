// spec: Docs/RE/packets/5-3_char_spawn.yaml — opcode 5/3 (0x50003), 908-byte fixed block.
// SpawnDescriptor sub-field layout: Docs/RE/structs/actor.md (decode is a Domain/Application
// concern; here it is an opaque 880-byte buffer).
//
// CONTROL-FLOW CONFIRMED (static IDA, IDB SHA 263bd994): the fixed 908-byte body (single 0x38C read),
// the 8-byte prefix + 880-byte SpawnDescriptor + 20-byte trailer split, and the world-coordinate wire
// offsets (world_x WIRE +0x54, world_z WIRE +0x58) are all confirmed. Descriptor interior VALUES and
// trailer value-semantics are capture/debugger-pending (non-blocking). spec: packets/5-3_char_spawn.yaml.
//
// CYCLE 11 (2026-06-22): trailer interior RESOLVED:
//   trailer[0]    clone/name-rebuild byte (signals name-display rebuild)
//   trailer[1..17] 17-byte secondary CP949 name region (NUL-terminated within)
//   trailer[18]   relation byte (guild/relation flag)
//   trailer[19]   tail byte (reserved / remaining flag)
// Σ check: 8 (prefix) + 880 (descriptor) + 20 (trailer) = 908 ✓
// World-pos landmark: world_x at descriptor +0x4C → WIRE +0x54; world_z at +0x50 → WIRE +0x58
//   (8-byte prefix shifts; differs from 5/1 which has a 12-byte prefix: WIRE +0x58/+0x5C there).
//   spec: packets/5-3_char_spawn.yaml (WORLD POSITION note).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;
using MartialHeroes.Network.Protocol.Core.Packets;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

/// <summary>
///     5/3 — server spawns an actor into the world; the payload carries an 8-byte prefix, an embedded
///     880-byte SpawnDescriptor record (at frame +0x008), and a 20-byte trailer. Fixed 908-byte payload.
///     Σ = 8 (prefix) + 880 (SpawnDescriptor) + 20 (Trailer) = 908. spec: Docs/RE/packets/5-3_char_spawn.yaml.
/// </summary>
/// <remarks>
///     The 880-byte <see cref="SpawnDescriptor" /> is intentionally opaque on this layer: its internal
///     fields (name, vitals, world X/Z, class, etc.) are documented in Docs/RE/structs/actor.md and
///     are decoded by the Domain/Application layer, not by Network.Protocol.
///     <para>
///         <b>World-position landmark (CONFIRMED):</b> world_x sits at descriptor +0x4C → WIRE +0x54;
///         world_z at descriptor +0x50 → WIRE +0x58 (8-byte prefix). Do NOT apply the 5/1 rule
///         (+0x58/+0x5C) here — that opcode has a 12-byte prefix. spec: packets/5-3_char_spawn.yaml.
///     </para>
///     <para>
///         <b>Trailer interior (CYCLE 11, RESOLVED):</b> trailer[0] = clone/name-rebuild byte;
///         trailer[1..17] = 17-byte secondary CP949 name region (NUL-terminated within);
///         trailer[18] = relation byte (guild/relation flag); trailer[19] = tail/reserved byte.
///         Value-semantics remain capture/debugger-pending (non-blocking).
///         spec: packets/5-3_char_spawn.yaml.
///     </para>
/// </remarks>
[PacketOpcode(5, 3)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgCharSpawn
{
    /// <summary>Packed opcode 0x50003 (5/3). spec: Docs/RE/packets/5-3_char_spawn.yaml.</summary>
    public const uint OpcodeId = Opcodes.SmsgCharSpawn;

    /// <summary>
    ///     Declared wire size in bytes = 8 (prefix) + 880 (SpawnDescriptor) + 20 (Trailer) = 908.
    ///     spec: Docs/RE/packets/5-3_char_spawn.yaml (size: 908). Σ-verified.
    /// </summary>
    public const int WireSize = 908; // spec: Docs/RE/packets/5-3_char_spawn.yaml — Σ 8+880+20=908

    // --- Record-relative byte offsets for landmark documentation only (not field selectors) ---

    /// <summary>
    ///     Frame byte offset of descriptor start (0x008). WIRE +0x54 = world_x; WIRE +0x58 = world_z.
    ///     spec: Docs/RE/packets/5-3_char_spawn.yaml (PREFIX 8 bytes, descriptor at frame +0x008).
    /// </summary>
    private const int DescriptorOffset = 0x008; // spec: Docs/RE/packets/5-3_char_spawn.yaml

    /// <summary>
    ///     Frame byte offset of trailer start = 0x000 + 0x008 + 0x370 = 0x378.
    ///     NOTE: the spec YAML incorrectly lists 0x37c in the field comment; the correct sum is
    ///     8+880=888=0x378. The struct arithmetic (Pack=1, sequential) is correct here.
    ///     spec: Docs/RE/packets/5-3_char_spawn.yaml (Trailer: bytes[20], Σ: 8+880+20=908).
    /// </summary>
    private const int TrailerOffset = DescriptorOffset + 0x370; // = 0x378

    // --- Trailer sub-offsets (relative to Trailer[0], CYCLE 11 RESOLVED) ---
    // spec: Docs/RE/packets/5-3_char_spawn.yaml (TRAILER 20 bytes, CYCLE 11).

    /// <summary>Trailer[0] — clone/name-rebuild byte. spec: Docs/RE/packets/5-3_char_spawn.yaml.</summary>
    public const int TrailerCloneByteOffset = 0; // spec: Docs/RE/packets/5-3_char_spawn.yaml trailer[0]

    /// <summary>Trailer[1..17] — 17-byte secondary CP949 name region (NUL-terminated within, up to 16+NUL). spec: same.</summary>
    public const int TrailerSecondaryNameOffset = 1; // spec: Docs/RE/packets/5-3_char_spawn.yaml trailer[1..17]

    /// <summary>Byte length of the secondary CP949 name region (17 = 16 bytes + NUL). spec: same.</summary>
    public const int TrailerSecondaryNameLength = 17; // spec: Docs/RE/packets/5-3_char_spawn.yaml trailer[1..17]

    /// <summary>Trailer[18] — relation byte (guild/relation flag). spec: same.</summary>
    public const int TrailerRelationByteOffset = 18; // spec: Docs/RE/packets/5-3_char_spawn.yaml trailer[18]

    /// <summary>Trailer[19] — tail/reserved byte. spec: same.</summary>
    public const int TrailerTailByteOffset = 19; // spec: Docs/RE/packets/5-3_char_spawn.yaml trailer[19]

    // --- Wire fields (Pack=1 sequential; offsets are packet-payload-relative) ---

    /// <summary>
    ///     0x000 — actor category (low byte is the real sort: 1=PC, 2=Mob/NPC). spec: Docs/RE/packets/5-3_char_spawn.yaml.
    /// </summary>
    public readonly uint Sort;

    /// <summary>0x004 — actor id (LE u32). spec: Docs/RE/packets/5-3_char_spawn.yaml.</summary>
    public readonly uint ActorId;

    /// <summary>
    ///     0x008 — embedded 880-byte SpawnDescriptor (opaque here; decode per Docs/RE/structs/actor.md).
    ///     World X/Z landmarks: descriptor +0x4C (world_x) / +0x50 (world_z) → WIRE +0x54 / +0x58.
    ///     spec: Docs/RE/packets/5-3_char_spawn.yaml.
    /// </summary>
    public readonly SpawnDescriptorBuffer SpawnDescriptor;

    /// <summary>
    ///     0x378 — 20-byte trailer. Interior (CYCLE 11 RESOLVED): [0]=clone-byte, [1..17]=17-byte secondary
    ///     CP949 name, [18]=relation byte, [19]=tail byte. Value-semantics capture/debugger-pending.
    ///     spec: Docs/RE/packets/5-3_char_spawn.yaml (Trailer: bytes[20]).
    /// </summary>
    public readonly TrailerBuffer Trailer;

    // --- Typed accessors ---

    /// <summary>Typed view over the low byte of <see cref="Sort" />. spec: Docs/RE/structs/actor.md.</summary>
    public ActorSort SortKind => (ActorSort)(byte)Sort; // spec: Docs/RE/structs/actor.md (sort low byte)

    /// <summary>
    ///     Trailer[0] — clone/name-rebuild byte. Signals the client to rebuild the display name using
    ///     the clone-discriminator. spec: Docs/RE/packets/5-3_char_spawn.yaml (trailer[0], CYCLE 11).
    ///     Value-semantics capture-pending.
    /// </summary>
    public byte TrailerCloneByte
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            // spec: Docs/RE/packets/5-3_char_spawn.yaml — trailer[0] clone/name-rebuild byte (CYCLE 11 RESOLVED).
            ref readonly var b = ref Unsafe.As<TrailerBuffer, byte>(ref Unsafe.AsRef(in Trailer));
            return Unsafe.Add(ref Unsafe.AsRef(in b), TrailerCloneByteOffset);
        }
    }

    /// <summary>
    ///     Trailer[1..17] — 17-byte secondary CP949 name region (NUL-terminated, up to 16 bytes + NUL).
    ///     Zero-copy span; no managed string. Decode with CP949 at a higher layer.
    ///     spec: Docs/RE/packets/5-3_char_spawn.yaml (trailer[1..17], CYCLE 11 RESOLVED).
    ///     Value-semantics capture-pending.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ReadOnlySpan<byte> TrailerSecondaryName()
    {
        // spec: Docs/RE/packets/5-3_char_spawn.yaml — trailer[1..17] = 17-byte secondary CP949 name (CYCLE 11).
        ref readonly var b = ref Unsafe.As<TrailerBuffer, byte>(ref Unsafe.AsRef(in Trailer));
        return MemoryMarshal.CreateReadOnlySpan(
            in Unsafe.Add(ref Unsafe.AsRef(in b), TrailerSecondaryNameOffset),
            TrailerSecondaryNameLength);
    }

    /// <summary>
    ///     Trailer[18] — relation byte (guild/relation flag for the spawned actor).
    ///     spec: Docs/RE/packets/5-3_char_spawn.yaml (trailer[18], CYCLE 11 RESOLVED). Value-semantics capture-pending.
    /// </summary>
    public byte TrailerRelationByte
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            // spec: Docs/RE/packets/5-3_char_spawn.yaml — trailer[18] relation byte (CYCLE 11 RESOLVED).
            ref readonly var b = ref Unsafe.As<TrailerBuffer, byte>(ref Unsafe.AsRef(in Trailer));
            return Unsafe.Add(ref Unsafe.AsRef(in b), TrailerRelationByteOffset);
        }
    }

    /// <summary>
    ///     Trailer[19] — tail/reserved byte (remaining flag).
    ///     spec: Docs/RE/packets/5-3_char_spawn.yaml (trailer[19], CYCLE 11 RESOLVED). Value-semantics capture-pending.
    /// </summary>
    public byte TrailerTailByte
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            // spec: Docs/RE/packets/5-3_char_spawn.yaml — trailer[19] tail byte (CYCLE 11 RESOLVED).
            ref readonly var b = ref Unsafe.As<TrailerBuffer, byte>(ref Unsafe.AsRef(in Trailer));
            return Unsafe.Add(ref Unsafe.AsRef(in b), TrailerTailByteOffset);
        }
    }

    /// <summary>
    ///     0x008 — opaque 880-byte (0x370) SpawnDescriptor record. Sub-fields per Docs/RE/structs/actor.md.
    ///     spec: Docs/RE/packets/5-3_char_spawn.yaml.
    /// </summary>
    [InlineArray(880)]
    public struct SpawnDescriptorBuffer
    {
        private byte _element0;
    }

    /// <summary>
    ///     0x378 — 20-byte trailer. Interior: [0]=clone-byte, [1..17]=secondary CP949 name (17B),
    ///     [18]=relation byte, [19]=tail byte. spec: Docs/RE/packets/5-3_char_spawn.yaml (Trailer: bytes[20]).
    /// </summary>
    [InlineArray(20)]
    public struct TrailerBuffer
    {
        private byte _element0;
    }
}