// spec: Docs/RE/packets/4-4_area_entity_snapshot.yaml — opcode 4/4 (0x40004), variable-length.
// spec: Docs/RE/structs/actor.md — "4/4 area-entity-snapshot actor record" section (892-byte prefix/descriptor/trailer layout).
//
// Control-flow-confirmed on IDB SHA 263bd994 (CYCLE 12 / Phase 3): the 17-byte area header,
// the use of only the two center floats to recenter the actor grid, the 1-byte tag dispatch loop
// until a zero terminator, and the tag-record sizes {1/2/3 => 892, 4 => 24, 6 => 36, 9 => 24}
// are confirmed. Per-record VALUE semantics are capture/debugger-pending.
//
// ACTOR RECORD PREFIX CORRECTION (CYCLE 12 / Phase 3 — binary won):
//   The 4/4 actor record prefix is: ActorId u32@+0x00; KindByte u8@+0x04 (value 5 = visual-only
//   refresh); RelationVisual u8@+0x05; 2 pad@+0x06; then the 880-byte SpawnDescriptor @+0x08;
//   then 4-byte trailer @+0x378. There is NO Sort dword in the prefix — actor SORT is carried
//   out-of-band by the leading tag byte (tag 1 => sort 1 player; tags 2/3 carry the sort as the
//   tag value itself). Composite key = (ActorId@+0x00, tag-byte sort).
//   This DIFFERS from the 5/3 CharSpawn prefix (Sort@0/ActorId@4).
//   spec: Docs/RE/structs/actor.md (4/4 area-entity-snapshot actor record section).

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

/// <summary>
///     4/4 — area entity snapshot. Variable-length S2C message: a fixed 17-byte area header
///     followed by a tag loop (each iteration reads one tag u8 then a tag-specific record body).
///     The loop terminates when tag == 0.
///     <para>
///         This struct models ONLY the fixed 17-byte area header. The tag loop is variable and
///         must be consumed by the handler from the raw payload span starting at offset
///         <see cref="HeaderSize" />.
///     </para>
///     spec: Docs/RE/packets/4-4_area_entity_snapshot.yaml (framing, header, tag loop);
///     Docs/RE/structs/actor.md (4/4 area-actor record section — prefix/descriptor/trailer layout).
/// </summary>
[PacketOpcode(4, 4)] // spec: Docs/RE/packets/4-4_area_entity_snapshot.yaml (opcode 0x40004 = major 4, minor 4)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgAreaEntitySnapshot
{
    /// <summary>
    ///     Packed opcode 0x40004 (4/4).
    ///     spec: Docs/RE/packets/4-4_area_entity_snapshot.yaml.
    /// </summary>
    public const uint OpcodeId = Opcodes.SmsgAreaEntitySnapshot;

    /// <summary>
    ///     Fixed area-header size in bytes (17). This is the variable-length packet's fixed prefix;
    ///     the tag loop follows at payload offset <c>HeaderSize</c>.
    ///     spec: Docs/RE/packets/4-4_area_entity_snapshot.yaml (header: 17 bytes).
    /// </summary>
    public const int HeaderSize = 17; // spec: Docs/RE/packets/4-4_area_entity_snapshot.yaml (header block)

    // =========================================================================
    // Per-tag record sizes — used by the handler's tag loop.
    // The handler reads one tag u8, then reads the corresponding body byte count.
    // tag == 0 terminates the loop.
    // spec: Docs/RE/packets/4-4_area_entity_snapshot.yaml (tag_loop table).
    // =========================================================================

    /// <summary>
    ///     Tag 1/2/3 actor record size (892 bytes = 0x37C).
    ///     Tags 1, 2, and 3 all carry this exact 892-byte record.
    ///     <para>
    ///         Record layout: 8-byte prefix + 880-byte SpawnDescriptor + 4-byte trailer = 892.
    ///         The actor SORT is NOT in the prefix — it comes from the tag byte itself.
    ///         spec: Docs/RE/structs/actor.md (4/4 area-entity-snapshot actor record section).
    ///     </para>
    ///     spec: Docs/RE/packets/4-4_area_entity_snapshot.yaml (tag 1/2/3 body_size: 892).
    /// </summary>
    public const int
        ActorRecordSize =
            892; // spec: Docs/RE/packets/4-4_area_entity_snapshot.yaml + structs/actor.md (0x37C, confirmed)

    /// <summary>
    ///     Tag 4 ground-item record size (24 bytes).
    ///     spec: Docs/RE/packets/4-4_area_entity_snapshot.yaml (tag 4 body_size: 24);
    ///     Docs/RE/packets/4-4_ground_item_tag4.yaml.
    /// </summary>
    public const int
        GroundItemRecordSize =
            24; // spec: Docs/RE/packets/4-4_area_entity_snapshot.yaml tag 4 + 4-4_ground_item_tag4.yaml

    /// <summary>
    ///     Tag 6 area-name / secondary record size (36 bytes).
    ///     spec: Docs/RE/packets/4-4_area_entity_snapshot.yaml (tag 6 body_size: 36).
    /// </summary>
    public const int
        GuildRecordSize =
            36; // spec: Docs/RE/packets/4-4_area_entity_snapshot.yaml (tag 6; area-name / secondary update)

    /// <summary>
    ///     Tag 9 actor-state update record size (24 bytes).
    ///     NOTE: this is an actor-state update record (NOT the ground item). Tag 9 and tag 4 share
    ///     the same body size (24 bytes) but serve different roles; do NOT conflate them.
    ///     The exact field semantics of the tag-9 record are VALUE-PENDING — this const gives only
    ///     the confirmed body size.
    ///     spec: Docs/RE/packets/4-4_area_entity_snapshot.yaml (tag 9 body_size: 24 — "actor-state
    ///     update record (NOT the ground item)"; value-pending note).
    /// </summary>
    public const int
        TitleRecordSize =
            24; // spec: Docs/RE/packets/4-4_area_entity_snapshot.yaml (tag 9; actor-state update, 24 bytes, value-pending)

    // =========================================================================
    // Named offset/stride consts for the 4/4 actor record (892 bytes).
    // These allow the layer-04 handler to read fields by name from the raw record span.
    // All offsets are RECORD-relative (offset 0 = first byte of the 892-byte body).
    // spec: Docs/RE/structs/actor.md (4/4 area-entity-snapshot actor record section).
    // =========================================================================

    /// <summary>
    ///     Byte offset of ActorId within the 892-byte actor record.
    ///     ActorId is a u32 at record +0x00 — the id half of the (id, sort) composite key.
    ///     The sort half comes from the tag byte, NOT from this prefix.
    ///     spec: Docs/RE/structs/actor.md (4/4 record: ActorId u32@+0x00).
    /// </summary>
    public const int ActorIdOffset = 0; // spec: Docs/RE/structs/actor.md +0x00 (u32 ActorId)

    /// <summary>
    ///     Byte offset of KindByte within the 892-byte actor record.
    ///     KindByte is a u8 at record +0x04. Value 5 gates a visual-only refresh of an existing
    ///     actor (weapon/joint refresh) rather than a full spawn; VALUE-pending for other values.
    ///     spec: Docs/RE/structs/actor.md (4/4 record: KindByte u8@+0x04).
    /// </summary>
    public const int KindByteOffset = 4; // spec: Docs/RE/structs/actor.md +0x04 (u8 KindByte)

    /// <summary>
    ///     KindByte value that triggers a visual-only refresh of an existing actor (weapon/joint
    ///     refresh) rather than a full new-actor spawn.
    ///     spec: Docs/RE/structs/actor.md (4/4 record: KindByte value 5 = visual-only refresh).
    /// </summary>
    public const byte
        KindByteVisualRefresh = 5; // spec: Docs/RE/structs/actor.md (KindByte == 5 => visual-only refresh)

    /// <summary>
    ///     Byte offset of RelationVisual within the 892-byte actor record.
    ///     RelationVisual is a u8 at record +0x05; copied to the Actor relation/visual field.
    ///     VALUE-pending (role not yet confirmed by a capture).
    ///     spec: Docs/RE/structs/actor.md (4/4 record: RelationVisual u8@+0x05).
    /// </summary>
    public const int RelationVisualOffset = 5; // spec: Docs/RE/structs/actor.md +0x05 (u8 RelationVisual)

    /// <summary>
    ///     Byte offset of the embedded 880-byte SpawnDescriptor within the 892-byte actor record.
    ///     The descriptor is at record +0x08 and is copied wholesale into Actor +0x74 on spawn.
    ///     World X/Z land at record +0x08 + SD +0x4C / +0x50 = record +0x54 / +0x58.
    ///     spec: Docs/RE/structs/actor.md (4/4 record: SpawnDescriptor 880 bytes @+0x008).
    /// </summary>
    public const int DescriptorOffset = 8; // spec: Docs/RE/structs/actor.md +0x008 (880-byte SpawnDescriptor)

    /// <summary>
    ///     Byte offset of the 4-byte trailer within the 892-byte actor record.
    ///     Trailer layout: TrailerVisual u8@+0x378; pad@+0x379; CombatTimerFlag u8@+0x37A; pad@+0x37B.
    ///     Σ = 8 + 880 + 4 = 892 (0x37C). ✓
    ///     spec: Docs/RE/structs/actor.md (4/4 record: 4-byte trailer @+0x378).
    /// </summary>
    public const int
        TrailerOffset = 0x378; // spec: Docs/RE/structs/actor.md +0x378 (4-byte trailer; TrailerVisual/CombatTimerFlag)

    // =========================================================================
    // Fixed 17-byte area header fields (Pack=1, sequential). Offsets are payload-relative.
    // spec: Docs/RE/packets/4-4_area_entity_snapshot.yaml (header block).
    // =========================================================================

    /// <summary>
    ///     +0x00 (u8) Header flag — read then discarded by the client (does not gate rendering).
    ///     spec: Docs/RE/packets/4-4_area_entity_snapshot.yaml (Flag, offset 0).
    /// </summary>
    public readonly byte HeaderFlag; // spec: 4-4_area_entity_snapshot.yaml +0x00 (u8, discarded)

    /// <summary>
    ///     +0x01 (u32) Viewer entity id — read then discarded.
    ///     spec: Docs/RE/packets/4-4_area_entity_snapshot.yaml (ViewerId, offset 1).
    /// </summary>
    public readonly uint ViewerEntityId; // spec: 4-4_area_entity_snapshot.yaml +0x01 (u32, discarded)

    /// <summary>
    ///     +0x05 (u32) Area grid id — read then discarded.
    ///     spec: Docs/RE/packets/4-4_area_entity_snapshot.yaml (AreaGridId, offset 5).
    /// </summary>
    public readonly uint AreaGrid; // spec: 4-4_area_entity_snapshot.yaml +0x05 (u32, discarded)

    /// <summary>
    ///     +0x09 (f32) Area centre Z — used to recenter the local actor grid.
    ///     spec: Docs/RE/packets/4-4_area_entity_snapshot.yaml (CenterZ, offset 9).
    /// </summary>
    public readonly float
        AreaCentreZ; // spec: 4-4_area_entity_snapshot.yaml +0x09 (f32, consumed — recenter actor grid)

    /// <summary>
    ///     +0x0D (f32) Area centre X — used to recenter the local actor grid.
    ///     spec: Docs/RE/packets/4-4_area_entity_snapshot.yaml (CenterX, offset 13).
    /// </summary>
    public readonly float
        AreaCentreX; // spec: 4-4_area_entity_snapshot.yaml +0x0D (f32, consumed — recenter actor grid)
}