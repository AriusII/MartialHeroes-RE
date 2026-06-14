// spec: Docs/RE/packets/3-1_character_list.yaml — opcode 3/1 (0x30001), VARIABLE-LENGTH packet.
// SpawnDescriptor sub-field offsets: Docs/RE/structs/actor.md ("Embedded SpawnDescriptor" table).
//
// This file models the VARIABLE PER-SLOT RECORD of the 3/1 character-select list and provides a
// zero-allocation reader that walks the 3-byte header's SlotMask and yields one 981-byte record per
// set bit at its BIT-POSITION slot. The fixed 3-byte header itself lives in SmsgCharacterListHeader.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in both specs). The
// (major:minor) routing is dispatch-table-confirmed; the field layout is a hypothesis until a live
// capture confirms it. Genuinely unresolved fields are flagged `TODO needs-capture` below.
//
// WHY THE DESCRIPTOR IS A BUFFER WITH ACCESSORS (not a flat sequential struct):
// Docs/RE/structs/actor.md is an OFFSET-ANNOTATION map over the 880-byte descriptor, not a flat
// field-by-field layout. Several documented points OVERLAP — e.g. `server_class` (SD +0x74) and the
// 600-byte `equip_stat_buff_block` (SD +0xD4) both sit INSIDE the 320-byte `equip_ref_table`
// (SD +0x58..+0x197). A single sequential struct cannot represent overlapping annotations. To stay
// byte-exact we model the descriptor as a fixed 880-byte buffer (the wire IS the buffer) and expose
// each named field via a typed accessor that reads it at its documented SD offset. The buffer is the
// layout; the accessors are the annotation. No managed strings, no copies.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// One 16-byte entry of the SpawnDescriptor's visible-gear / equipment reference table
/// (<c>equip_ref_table</c>, SD +0x58, 20 entries). The leading 4-byte dword is the part gid; the
/// renderer attaches overlay slots {3,4,6,2,11,14}, mapping each gid to <c>data/char/skin/g{gid}.skn</c>.
/// The remaining 12 bytes per entry are unverified. spec: Docs/RE/structs/actor.md (equip_ref_table);
/// Docs/RE/packets/3-1_character_list.yaml. CAPTURE-UNVERIFIED.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct EquipRefEntry
{
    /// <summary>Size of one equip-ref entry in bytes. spec: structs/actor.md (20 entries x 16 bytes).</summary>
    public const int WireSize = 16;

    /// <summary>
    /// +0x00 — visible-gear PART GID (LE u32). 0 ⇒ empty slot. The renderer overlays slots
    /// {3,4,6,2,11,14}. spec: Docs/RE/structs/actor.md (equip_ref_table leading dword).
    /// </summary>
    public readonly uint PartGid;

    /// <summary>+0x04 — remaining 12 bytes per entry; role unverified. TODO needs-capture. spec: structs/actor.md.</summary>
    public readonly EquipEntryTailBuffer Tail;

    /// <summary>+0x04 — 12 unverified bytes per equip entry. spec: Docs/RE/structs/actor.md.</summary>
    [InlineArray(12)]
    public struct EquipEntryTailBuffer
    {
        private byte _element0;
    }
}

/// <summary>
/// One 981-byte per-slot record of the 3/1 character-select list, as a single <c>Pack=1</c> wire
/// buffer. Layout = 880-byte SpawnDescriptor + 96-byte StatBlock + 1-byte SlotFlag + 4-byte Timing.
/// Named fields are exposed via accessors that read each value at its documented offset (see the file
/// header for why this is a buffer-with-accessors, not a flat struct).
/// spec: Docs/RE/packets/3-1_character_list.yaml; Docs/RE/structs/actor.md. CAPTURE-UNVERIFIED.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CharacterListSlotRecord
{
    // --- record geometry (spec: packets/3-1_character_list.yaml: 880 + 96 + 1 + 4 = 981) ---

    /// <summary>SpawnDescriptor size (0x370). spec: structs/actor.md; packets/3-1_character_list.yaml.</summary>
    public const int DescriptorSize = 880;

    /// <summary>StatBlock size (0x60). spec: packets/3-1_character_list.yaml (sub-block 2).</summary>
    public const int StatBlockSize = 96;

    /// <summary>SlotFlag size. spec: packets/3-1_character_list.yaml (sub-block 3).</summary>
    public const int SlotFlagSize = 1;

    /// <summary>Timing size. spec: packets/3-1_character_list.yaml (sub-block 4).</summary>
    public const int TimingSize = 4;

    /// <summary>Total per-slot record stride. spec: packets/3-1_character_list.yaml (880+96+1+4 = 981).</summary>
    public const int WireSize = DescriptorSize + StatBlockSize + SlotFlagSize + TimingSize; // 981

    // --- record-relative offsets of the four sub-blocks (spec: packets/3-1_character_list.yaml) ---

    /// <summary>Record-relative offset of the SpawnDescriptor sub-block (0x000). spec: same.</summary>
    public const int DescriptorOffset = 0;

    /// <summary>Record-relative offset of the StatBlock sub-block (0x370). spec: same.</summary>
    public const int StatBlockOffset = DescriptorSize; // 0x370

    /// <summary>Record-relative offset of the SlotFlag byte (0x3D0). spec: same.</summary>
    public const int SlotFlagOffset = StatBlockOffset + StatBlockSize; // 0x3D0

    /// <summary>Record-relative offset of the Timing dword (0x3D1). spec: same.</summary>
    public const int TimingOffset = SlotFlagOffset + SlotFlagSize; // 0x3D1

    // --- SpawnDescriptor (SD-relative) field offsets (spec: Docs/RE/structs/actor.md) ---

    private const int SdName = 0x00; // char[17] CP949, NUL-terminated
    private const int SdNameMaxBytes = 17;
    private const int SdAppearanceVariant = 0x2C; // u8
    private const int SdInternalClass = 0x34; // u16 {1,2,3,4}
    private const int SdStateByte = 0x38; // u8
    private const int SdSubLevelByte = 0x39; // u8
    private const int SdLevel = 0x3A; // u16 (boundary unresolved — see TODO)
    private const int SdCurrentHp = 0x3C; // u32
    private const int SdCurrentMp = 0x40; // u32
    private const int SdCurrentStamina = 0x44; // u32
    private const int SdWorldX = 0x4C; // f32
    private const int SdWorldZ = 0x50; // f32
    private const int SdEquipRefTable = 0x58; // 20 x 16 bytes
    private const int SdServerClass = 0x74; // u16
    private const int EquipRefEntryCount = 20;

    // --- StatBlock (StatBlock-relative) field offsets (spec: packets/3-1_character_list.yaml) ---

    private const int SbPrimary0 = 0x00; // u32
    private const int SbPrimary1 = 0x04; // u32
    private const int SbPrimary2 = 0x08; // u32
    private const int SbPrimary3 = 0x0C; // u32
    private const int SbPrimary4 = 0x10; // u32
    private const int SbVitalCurrent = 0x14; // u32
    private const int SbRankXp = 0x18; // i64
    private const int SbWithinRankXp = 0x20; // u32
    private const int SbRemainingStatPoints = 0x30; // u32

    /// <summary>
    /// The full 981 record bytes as a single <c>Pack=1</c> inline buffer. The descriptor occupies
    /// [0x000..0x370), the StatBlock [0x370..0x3D0), the SlotFlag byte at 0x3D0, and the Timing dword
    /// at [0x3D1..0x3D5). spec: Docs/RE/packets/3-1_character_list.yaml.
    /// </summary>
    public readonly RecordBuffer Raw;

    /// <summary>981-byte per-slot record buffer. spec: Docs/RE/packets/3-1_character_list.yaml.</summary>
    [InlineArray(WireSize)]
    public struct RecordBuffer
    {
        private byte _element0;
    }

    // ----------------------------------------------------------------------
    // SpawnDescriptor accessors (read at SD-relative offsets; SD starts at record +0)
    // spec: Docs/RE/structs/actor.md ("Embedded SpawnDescriptor" table)
    // ----------------------------------------------------------------------

    /// <summary>
    /// SD +0x00 — the raw 17-byte CP949 (EUC-KR) name field, NUL-terminated within. NO managed string:
    /// callers slice with <see cref="NameBytes"/> and decode with the shared CP949 decoder in a higher
    /// layer. spec: Docs/RE/structs/actor.md (name); Docs/RE/packets/3-1_character_list.yaml.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ReadOnlySpan<byte> NameField()
        => MemoryMarshal.CreateReadOnlySpan(in Unsafe.As<RecordBuffer, byte>(ref Unsafe.AsRef(in Raw)), WireSize)
            .Slice(SdName, SdNameMaxBytes);

    /// <summary>
    /// SD +0x2C — body/gender appearance variant; the <c>variant</c> arg of the model-class formula.
    /// spec: Docs/RE/structs/actor.md (appearance_variant); Docs/RE/packets/3-1_character_list.yaml.
    /// </summary>
    public readonly byte AppearanceVariant => ReadU8(SdAppearanceVariant);

    /// <summary>
    /// SD +0x34 — internal class {1=Musa,2=Salsu,3=Dosa,4=Monk}; the skeleton driver and the
    /// <c>class</c> arg of the model-class formula. spec: Docs/RE/structs/actor.md (internal_class).
    /// </summary>
    public readonly ushort InternalClass => ReadU16(SdInternalClass);

    /// <summary>SD +0x38 — first level/state byte (5/53-written). spec: Docs/RE/structs/actor.md (state_byte).</summary>
    public readonly byte StateByte => ReadU8(SdStateByte);

    /// <summary>SD +0x39 — second level/state byte (5/53-written). spec: Docs/RE/structs/actor.md (sub_level_byte).</summary>
    public readonly byte SubLevelByte => ReadU8(SdSubLevelByte);

    /// <summary>
    /// SD +0x3A — level as a u16. TODO needs-capture: the u16 here MAY straddle the two state bytes at
    /// SD +0x38/+0x39 (open question 1) — do NOT promote this as a clean level without a 5/53 vitals
    /// capture of a known-level character. Read the state bytes explicitly until then.
    /// spec: Docs/RE/structs/actor.md (level, draft); Docs/RE/packets/3-1_character_list.yaml (CONFLICT).
    /// </summary>
    public readonly ushort LevelRaw => ReadU16(SdLevel);

    /// <summary>SD +0x3C — current HP (u32). spec: Docs/RE/structs/actor.md (current_hp).</summary>
    public readonly uint CurrentHp => ReadU32(SdCurrentHp);

    /// <summary>SD +0x40 — current MP / ki (u32). spec: Docs/RE/structs/actor.md (current_mp).</summary>
    public readonly uint CurrentMp => ReadU32(SdCurrentMp);

    /// <summary>SD +0x44 — current stamina (u32). spec: Docs/RE/structs/actor.md (current_stamina).</summary>
    public readonly uint CurrentStamina => ReadU32(SdCurrentStamina);

    /// <summary>SD +0x4C — world X (f32). World Y is always 0. spec: Docs/RE/structs/actor.md (world_x).</summary>
    public readonly float WorldX => ReadF32(SdWorldX);

    /// <summary>SD +0x50 — world Z (f32). spec: Docs/RE/structs/actor.md (world_z).</summary>
    public readonly float WorldZ => ReadF32(SdWorldZ);

    /// <summary>
    /// SD +0x74 — server-assigned class id (martial-arts style), distinct from <see cref="InternalClass"/>.
    /// NOTE this offset sits INSIDE the equip-ref table's annotated span (see file header); it is read
    /// directly at SD +0x74. spec: Docs/RE/structs/actor.md (server_class).
    /// </summary>
    public readonly ushort ServerClass => ReadU16(SdServerClass);

    /// <summary>
    /// Returns an in-place <c>ref readonly</c> view of equip-ref entry <paramref name="index"/>
    /// (0..19), each entry's leading dword is the visible-gear part gid. Zero copy.
    /// spec: Docs/RE/structs/actor.md (equip_ref_table, SD +0x58, 20 x 16 bytes).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="index"/> is not in 0..19.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ref readonly EquipRefEntry EquipEntry(int index)
    {
        if ((uint)index >= (uint)EquipRefEntryCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Equip entry index out of range (0..19).");
        }

        ReadOnlySpan<byte> slot =
            AsSpan().Slice(SdEquipRefTable + (index * EquipRefEntry.WireSize), EquipRefEntry.WireSize);
        return ref MemoryMarshal.AsRef<EquipRefEntry>(slot);
    }

    /// <summary>Number of equip-ref entries (20). spec: Docs/RE/structs/actor.md (equip_ref_table).</summary>
    public static int EquipEntryCount => EquipRefEntryCount;

    // ----------------------------------------------------------------------
    // StatBlock accessors (read at StatBlock-relative offsets; StatBlock starts at record +0x370)
    // spec: Docs/RE/packets/3-1_character_list.yaml (sub-block 2)
    // ----------------------------------------------------------------------

    /// <summary>StatBlock +0x00 — 1st primary (STR-family, allocation order). spec: packets/3-1_character_list.yaml.</summary>
    public readonly uint PrimaryStat0 => ReadU32(StatBlockOffset + SbPrimary0);

    /// <summary>StatBlock +0x04 — 2nd primary. spec: packets/3-1_character_list.yaml.</summary>
    public readonly uint PrimaryStat1 => ReadU32(StatBlockOffset + SbPrimary1);

    /// <summary>StatBlock +0x08 — 3rd primary. spec: packets/3-1_character_list.yaml.</summary>
    public readonly uint PrimaryStat2 => ReadU32(StatBlockOffset + SbPrimary2);

    /// <summary>StatBlock +0x0C — 4th primary. spec: packets/3-1_character_list.yaml.</summary>
    public readonly uint PrimaryStat3 => ReadU32(StatBlockOffset + SbPrimary3);

    /// <summary>StatBlock +0x10 — 5th primary. spec: packets/3-1_character_list.yaml.</summary>
    public readonly uint PrimaryStat4 => ReadU32(StatBlockOffset + SbPrimary4);

    /// <summary>StatBlock +0x14 — a current vital value. spec: packets/3-1_character_list.yaml.</summary>
    public readonly uint VitalCurrent => ReadU32(StatBlockOffset + SbVitalCurrent);

    /// <summary>StatBlock +0x18 — rank/realm experience (i64). spec: packets/3-1_character_list.yaml.</summary>
    public readonly long RankXp => ReadI64(StatBlockOffset + SbRankXp);

    /// <summary>StatBlock +0x20 — xp progress within the current rank (u32). spec: packets/3-1_character_list.yaml.</summary>
    public readonly uint WithinRankXp => ReadU32(StatBlockOffset + SbWithinRankXp);

    /// <summary>StatBlock +0x30 — unspent allocation points (u32). spec: packets/3-1_character_list.yaml.</summary>
    public readonly uint RemainingStatPoints => ReadU32(StatBlockOffset + SbRemainingStatPoints);

    // StatBlock tail interior (+0x24, +0x34, +0x38..) is TODO needs-capture — left unmapped per spec
    // (packets/3-1_character_list.yaml: "REMAINING UNKNOWNS"). Do not invent accessors.

    // ----------------------------------------------------------------------
    // SlotFlag + Timing accessors (record-relative)
    // ----------------------------------------------------------------------

    /// <summary>
    /// Record +0x3D0 — per-slot availability/relation flag byte. The original clears it to 0 on a
    /// successful read. TODO needs-capture: exact relation semantics (locked / deletion-pending)
    /// UNRESOLVED. spec: Docs/RE/packets/3-1_character_list.yaml (sub-block 3).
    /// </summary>
    public readonly byte SlotFlag => ReadU8(SlotFlagOffset);

    /// <summary>
    /// Record +0x3D1 — a timing / timestamp value (LE u32), likely a delete/rename cooldown. TODO
    /// needs-capture: exact meaning UNRESOLVED. spec: Docs/RE/packets/3-1_character_list.yaml (sub-block 4).
    /// </summary>
    public readonly uint Timing => ReadU32(TimingOffset);

    // ----------------------------------------------------------------------
    // Zero-alloc primitives (read at record-relative byte offsets, little-endian)
    // ----------------------------------------------------------------------

    /// <summary>The whole 981-byte record as a read-only span — zero copy. spec: packets/3-1_character_list.yaml.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ReadOnlySpan<byte> AsSpan()
        => MemoryMarshal.CreateReadOnlySpan(in Unsafe.As<RecordBuffer, byte>(ref Unsafe.AsRef(in Raw)), WireSize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly byte ReadU8(int offset) => AsSpan()[offset];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly ushort ReadU16(int offset)
        => BinaryPrimitivesLe.ReadUInt16(AsSpan().Slice(offset, sizeof(ushort)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly uint ReadU32(int offset)
        => BinaryPrimitivesLe.ReadUInt32(AsSpan().Slice(offset, sizeof(uint)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly long ReadI64(int offset)
        => BinaryPrimitivesLe.ReadInt64(AsSpan().Slice(offset, sizeof(long)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly float ReadF32(int offset)
        => BitConverter.Int32BitsToSingle(BinaryPrimitivesLe.ReadInt32(AsSpan().Slice(offset, sizeof(int))));
}

/// <summary>
/// Helpers to read little-endian primitives from a span without depending on endianness assumptions.
/// spec: Docs/RE/structs/actor.md (LE wire) — the protocol is little-endian throughout.
/// </summary>
internal static class BinaryPrimitivesLe
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ushort ReadUInt16(ReadOnlySpan<byte> s)
        => System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(s);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static uint ReadUInt32(ReadOnlySpan<byte> s)
        => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(s);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int ReadInt32(ReadOnlySpan<byte> s)
        => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(s);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static long ReadInt64(ReadOnlySpan<byte> s)
        => System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(s);
}

/// <summary>
/// Pure helper for the recovered character-appearance driver. spec: Docs/RE/structs/actor.md (notes);
/// Docs/RE/packets/3-1_character_list.yaml (APPEARANCE DRIVER).
/// </summary>
public static class CharacterAppearance
{
    /// <summary>
    /// Derives the visual model-class id (the catalog IdB) from the descriptor's
    /// <paramref name="internalClass"/> (SD +0x34, {1,2,3,4}) and <paramref name="appearanceVariant"/>
    /// (SD +0x2C): <c>model_class_id = 5*(internal_class + 4*variant) - 24</c>, which yields an IdB in
    /// {1,11,16,26} for the four starter classes. That IdB selects the catalog skeleton (NOT a literal
    /// g{n}.bnd). spec: Docs/RE/packets/3-1_character_list.yaml (APPEARANCE DRIVER); Docs/RE/structs/actor.md.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ModelClassId(int internalClass, int appearanceVariant)
        => (5 * (internalClass + (4 * appearanceVariant))) - 24;
}

/// <summary>
/// Zero-allocation reader over a 3/1 character-select list frame. Parses the fixed 3-byte
/// <see cref="SmsgCharacterListHeader"/>, then walks <see cref="SmsgCharacterListHeader.SlotMask"/>
/// (LSB-first) and exposes one 981-byte <see cref="CharacterListSlotRecord"/> per SET bit at that
/// bit's slot index — a set bit consumes the next record; an UNSET bit leaves the slot empty (records
/// are NOT packed into the first N slots). No heap allocation, no LINQ, no closures.
/// spec: Docs/RE/packets/3-1_character_list.yaml (SLOT ORDERING — BIT-POSITION placement).
/// </summary>
public readonly ref struct SmsgCharacterListReader
{
    /// <summary>Number of slot bits the original loop scans (~8). spec: packets/3-1_character_list.yaml.</summary>
    public const int SlotCount = 8;

    private readonly ReadOnlySpan<byte> _records; // packed 981-byte records, one per set bit, in bit order

    /// <summary>The parsed 3-byte header at the head of the frame. spec: packets/3-1_character_list.yaml.</summary>
    public readonly SmsgCharacterListHeader Header;

    /// <summary>
    /// Parses the 3-byte header and bounds the trailing record region to the smaller of (popcount of
    /// <see cref="SmsgCharacterListHeader.SlotMask"/>) records and the bytes actually available after
    /// the header. <paramref name="frame"/> is the ALREADY-DECRYPTED, ALREADY-DECOMPRESSED 3/1 frame
    /// BODY (the 8-byte transport frame header is stripped by the transport layer).
    /// spec: Docs/RE/packets/3-1_character_list.yaml.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If the frame is shorter than the 3-byte header.</exception>
    public SmsgCharacterListReader(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < SmsgCharacterListHeader.HeaderSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frame), frame.Length,
                $"A 3/1 character-list frame requires at least {SmsgCharacterListHeader.HeaderSize} header bytes.");
        }

        // spec: packets/3-1_character_list.yaml — header is [server u8][channel u8][slotMask u8].
        Header = MemoryMarshal.Read<SmsgCharacterListHeader>(frame);

        ReadOnlySpan<byte> tail = frame[SmsgCharacterListHeader.HeaderSize..];
        int setBits = System.Numerics.BitOperations.PopCount((uint)(Header.SlotMask & ((1 << SlotCount) - 1)));
        int available = tail.Length / CharacterListSlotRecord.WireSize;
        int n = setBits <= available ? setBits : available;
        _records = tail[..(n * CharacterListSlotRecord.WireSize)];
    }

    /// <summary>The slot bitmask from the header (LSB-first). spec: packets/3-1_character_list.yaml.</summary>
    public readonly byte SlotMask => Header.SlotMask;

    /// <summary>
    /// The number of 981-byte records actually iterable (min of popcount(SlotMask) and the bytes
    /// available). spec: packets/3-1_character_list.yaml.
    /// </summary>
    public readonly int PopulatedCount => _records.Length / CharacterListSlotRecord.WireSize;

    /// <summary>True if slot <paramref name="slot"/> (0..7) is populated. spec: packets/3-1_character_list.yaml.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsSlotPopulated(int slot)
        => (uint)slot < (uint)SlotCount && (Header.SlotMask & (1 << slot)) != 0;

    /// <summary>
    /// Returns an in-place <c>ref readonly</c> view of the record occupying <paramref name="slot"/>
    /// (0..7). The record's position in the packed tail is the count of SET bits BELOW
    /// <paramref name="slot"/> (LSB-first bit-position placement). Zero copy.
    /// spec: Docs/RE/packets/3-1_character_list.yaml (SLOT ORDERING — BIT-POSITION placement).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// If <paramref name="slot"/> is out of range, the slot is empty, or its record bytes are absent.
    /// </exception>
    public readonly ref readonly CharacterListSlotRecord this[int slot]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (!IsSlotPopulated(slot))
            {
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "Slot is out of range or not populated.");
            }

            // Records are packed in LSB-first bit order: this slot's record index is the number of set
            // bits strictly below it. spec: packets/3-1_character_list.yaml (BIT-POSITION placement).
            int below = System.Numerics.BitOperations.PopCount((uint)(Header.SlotMask & ((1 << slot) - 1)));
            if (below >= PopulatedCount)
            {
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "Record bytes for this slot are missing.");
            }

            ReadOnlySpan<byte> rec = _records.Slice(below * CharacterListSlotRecord.WireSize,
                CharacterListSlotRecord.WireSize);
            return ref MemoryMarshal.AsRef<CharacterListSlotRecord>(rec);
        }
    }
}