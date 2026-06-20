// spec: Docs/RE/specs/handlers.md §10 + §21 — opcode 4/4 (0x40004), variable-length.
// spec: Docs/RE/packets/4-4_ground_item_tag4.yaml — tag-4 ground-item sub-record (24 bytes).
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Routing + the 17-byte area header structure + per-tag record sizes are STRUCTURE-HIGH
// (control-flow-confirmed per handlers.md §21 / §10). The three leading header values
// (HeaderFlag, ViewerEntityId, AreaGrid) are read-then-discarded by the client; only the
// two f32 coordinates (AreaCentreZ, AreaCentreX) are consumed. Field VALUE SEMANTICS
// remain capture/debugger-pending — do not over-read "discarded" as confirmed on-wire.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 4/4 — area entity snapshot. Variable-length S2C message: a fixed 17-byte area header
/// followed by a tag loop (each iteration reads one tag u8 then a tag-specific record).
/// The loop terminates when tag == 0.
/// <para>
/// This struct models ONLY the fixed 17-byte area header.  The tag loop is variable and
/// must be consumed by the handler from the raw payload span starting at offset
/// <see cref="HeaderSize"/>.
/// </para>
/// spec: Docs/RE/specs/handlers.md §10 (area header + tag-loop table) + §21 (892-byte framing
/// reconciliation). CAPTURE-UNVERIFIED layout (header structure STRUCTURE-HIGH; field value
/// semantics capture/debugger-pending).
/// </summary>
[PacketOpcode(4, 4)] // spec: Docs/RE/specs/handlers.md §10 (opcode 4/4 = 0x40004)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgAreaEntitySnapshot
{
    /// <summary>
    /// Packed opcode 0x40004 (4/4). spec: Docs/RE/specs/handlers.md §10.
    /// </summary>
    public const uint OpcodeId = Opcodes.Opcodes.SmsgAreaEntitySnapshot;

    /// <summary>
    /// Fixed area-header size in bytes. This is the variable-length packet's fixed prefix;
    /// the tag loop follows at payload offset <c>HeaderSize</c>.
    /// spec: Docs/RE/specs/handlers.md §10 (17-byte area header) + §21.
    /// </summary>
    public const int HeaderSize = 17; // spec: Docs/RE/specs/handlers.md §10 + §21

    // -------------------------------------------------------------------------
    // Per-tag record sizes exposed for use by the handler's tag loop.
    // The handler reads one tag u8, then reads the corresponding number of bytes
    // as the record body. tag == 0 terminates the loop.
    // spec: Docs/RE/specs/handlers.md §10 (tag-loop table) + §21 (892 reconciliation).
    // -------------------------------------------------------------------------

    /// <summary>
    /// Tag 1/2/3 actor record size (892 bytes = 0x37C).
    /// Tag 1 = player character actor (sort 1); tag 2 = mob (sort 2); tag 3 = NPC (sort 3).
    /// <para>
    /// RECONCILIATION-PENDING: 892 (this on-wire size) vs 880 (SpawnDescriptor core in
    /// structs/spawn_descriptor.md) vs 908 (5/3 CharSpawn framing) vs 912 (5/1
    /// ActorSpawnExtended framing) — §21 confirms the split is 8 (prefix) + 880
    /// (descriptor core) + 4 (trailer) = 892 for 4/4 only; the other handlers have their
    /// own prefix/trailer framing around the same 880-byte core. Do not conflate these.
    /// spec: Docs/RE/specs/handlers.md §10 + §21.
    /// </para>
    /// </summary>
    public const int ActorRecordSize = 892; // spec: Docs/RE/specs/handlers.md §21 (0x37C, binary-confirmed)

    /// <summary>
    /// Tag 4 ground-item record size (24 bytes).
    /// spec: Docs/RE/specs/handlers.md §10 + Docs/RE/packets/4-4_ground_item_tag4.yaml (size: 24).
    /// </summary>
    public const int GroundItemRecordSize = 24; // spec: Docs/RE/specs/handlers.md §10 + Docs/RE/packets/4-4_ground_item_tag4.yaml

    /// <summary>
    /// Tag 6 guild record size (36 bytes): entity key u32 at +0; guild name CP949 asciiz at +5.
    /// spec: Docs/RE/specs/handlers.md §10.
    /// </summary>
    public const int GuildRecordSize = 36; // spec: Docs/RE/specs/handlers.md §10

    /// <summary>
    /// Tag 9 title record size (24 bytes): entity key u32 at +0; state u8 at +4; sub-flag u8 at +5;
    /// name CP949 asciiz at +6. State values 2 and 4 are special.
    /// spec: Docs/RE/specs/handlers.md §10.
    /// </summary>
    public const int TitleRecordSize = 24; // spec: Docs/RE/specs/handlers.md §10

    // -------------------------------------------------------------------------
    // Fixed 17-byte area header fields (Pack=1, sequential). Offsets are payload-relative.
    // spec: Docs/RE/specs/handlers.md §10 (area header table).
    // -------------------------------------------------------------------------

    /// <summary>
    /// +0x00 (u8) Header flag — read then discarded by the client (does not gate rendering).
    /// spec: Docs/RE/specs/handlers.md §10.
    /// </summary>
    public readonly byte HeaderFlag; // spec: Docs/RE/specs/handlers.md §10 (+0x00, u8, discarded)

    /// <summary>
    /// +0x01 (u32) Viewer entity id — read then discarded.
    /// spec: Docs/RE/specs/handlers.md §10.
    /// </summary>
    public readonly uint ViewerEntityId; // spec: Docs/RE/specs/handlers.md §10 (+0x01, u32, discarded)

    /// <summary>
    /// +0x05 (u32) Area grid id — read then discarded.
    /// spec: Docs/RE/specs/handlers.md §10.
    /// </summary>
    public readonly uint AreaGrid; // spec: Docs/RE/specs/handlers.md §10 (+0x05, u32, discarded)

    /// <summary>
    /// +0x09 (f32) Area centre Z — used to recenter the actor grid.
    /// spec: Docs/RE/specs/handlers.md §10.
    /// </summary>
    public readonly float AreaCentreZ; // spec: Docs/RE/specs/handlers.md §10 (+0x09, f32, consumed)

    /// <summary>
    /// +0x0D (f32) Area centre X — used to recenter the actor grid.
    /// spec: Docs/RE/specs/handlers.md §10.
    /// </summary>
    public readonly float AreaCentreX; // spec: Docs/RE/specs/handlers.md §10 (+0x0D, f32, consumed)
}
