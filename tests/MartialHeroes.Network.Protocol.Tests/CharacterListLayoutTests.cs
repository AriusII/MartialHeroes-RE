// Layout + decode guards for the 3/1 character-select list per-slot record and its zero-alloc reader.
// Each struct's runtime size must equal its spec byte count (981 stride; 16-byte equip entry; Pack=1
// holds with no padding), and a SYNTHETIC in-memory frame (header + 2 set bits + two 981-byte records)
// must place records at their BIT-POSITION slots, decode the descriptor key fields, equip gids and
// StatBlock primaries, and leave the unset bit's slot empty.
// spec: Docs/RE/packets/3-1_character_list.yaml; Docs/RE/structs/actor.md. CAPTURE-UNVERIFIED layouts.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Packets;

namespace MartialHeroes.Network.Protocol.Tests;

public sealed class CharacterListLayoutTests
{
    // -------------------------------------------------------------------------
    // Size guards: 981-byte record stride; 16-byte equip entry; 3-byte header
    // -------------------------------------------------------------------------

    [Fact] // spec: Docs/RE/packets/3-1_character_list.yaml (880 + 96 + 1 + 4 = 981)
    public void SlotRecord_size_is_981()
    {
        Assert.Equal(981, Unsafe.SizeOf<CharacterListSlotRecord>());
        Assert.Equal(981, CharacterListSlotRecord.WireSize);
        // Sub-block geometry sums to the stride.
        Assert.Equal(
            981,
            CharacterListSlotRecord.DescriptorSize
            + CharacterListSlotRecord.StatBlockSize
            + CharacterListSlotRecord.SlotFlagSize
            + CharacterListSlotRecord.TimingSize);
    }

    [Fact] // spec: Docs/RE/packets/3-1_character_list.yaml (sub-block offsets 0x000 / 0x370 / 0x3D0 / 0x3D1)
    public void SlotRecord_subblock_offsets()
    {
        Assert.Equal(0x000, CharacterListSlotRecord.DescriptorOffset);
        Assert.Equal(0x370, CharacterListSlotRecord.StatBlockOffset);
        Assert.Equal(0x3D0, CharacterListSlotRecord.SlotFlagOffset);
        Assert.Equal(0x3D1, CharacterListSlotRecord.TimingOffset);
    }

    [Fact] // spec: Docs/RE/structs/actor.md (equip_ref_table: 20 entries x 16 bytes)
    public void EquipRefEntry_size_is_16()
    {
        Assert.Equal(16, Unsafe.SizeOf<EquipRefEntry>());
        Assert.Equal(16, EquipRefEntry.WireSize);
        Assert.Equal(0x00, (int)Marshal.OffsetOf<EquipRefEntry>(nameof(EquipRefEntry.PartGid)));
        Assert.Equal(20, CharacterListSlotRecord.EquipEntryCount);
    }

    [Fact] // spec: Docs/RE/packets/3-1_character_list.yaml (3-byte header: server u8, channel u8, slotMask u8)
    public void Header_size_is_3()
    {
        Assert.Equal(3, Unsafe.SizeOf<SmsgCharacterListHeader>());
        Assert.Equal(3, SmsgCharacterListHeader.HeaderSize);
    }

    // -------------------------------------------------------------------------
    // Appearance driver formula: model_class_id = 5*(class + 4*variant) - 24 -> {1,11,16,26}
    // -------------------------------------------------------------------------

    [Fact] // spec: Docs/RE/packets/3-1_character_list.yaml (APPEARANCE DRIVER); Docs/RE/structs/actor.md
    public void ModelClassId_matches_formula()
    {
        // model_class_id = 5*(internal_class + 4*variant) - 24, verified at a few (class, variant) points.
        Assert.Equal((5 * (1 + (4 * 1))) - 24,
            CharacterAppearance.ModelClassId(internalClass: 1, appearanceVariant: 1));
        Assert.Equal((5 * (4 + (4 * 0))) - 24,
            CharacterAppearance.ModelClassId(internalClass: 4, appearanceVariant: 0));
        Assert.Equal((5 * (2 + (4 * 3))) - 24,
            CharacterAppearance.ModelClassId(internalClass: 2, appearanceVariant: 3));
    }

    [Fact] // spec: Docs/RE/packets/3-1_character_list.yaml — the four starter classes yield IdB {1,11,16,26}.
    public void ModelClassId_starter_classes_yield_catalog_idb()
    {
        // The spec states the formula yields the catalog IdB set {1,11,16,26} for the four starter
        // classes. The (class, variant) inputs that produce those IdBs (variant 1):
        Assert.Equal(1, CharacterAppearance.ModelClassId(internalClass: 1, appearanceVariant: 1)); // 5*(1+4)-24 = 1
        Assert.Equal(11, CharacterAppearance.ModelClassId(internalClass: 3, appearanceVariant: 1)); // 5*(3+4)-24 = 11
        Assert.Equal(16, CharacterAppearance.ModelClassId(internalClass: 4, appearanceVariant: 1)); // 5*(4+4)-24 = 16
        Assert.Equal(26, CharacterAppearance.ModelClassId(internalClass: 6, appearanceVariant: 1)); // 5*(6+4)-24 = 26
    }

    // -------------------------------------------------------------------------
    // End-to-end: synthetic frame (header + slots 0 and 2 set; slot 1 empty) decodes correctly
    // -------------------------------------------------------------------------

    [Fact] // spec: Docs/RE/packets/3-1_character_list.yaml (BIT-POSITION placement); structs/actor.md
    public void Reader_places_records_at_bitposition_slots_and_decodes_fields()
    {
        // SlotMask = bits 0 and 2 set (0b0000_0101 = 0x05): slot 0 and slot 2 populated, slot 1 empty.
        const byte slotMask = 0b0000_0101;
        const int headerSize = SmsgCharacterListHeader.HeaderSize;
        const int recSize = CharacterListSlotRecord.WireSize;

        byte[] frame = new byte[headerSize + (2 * recSize)];
        frame[0x00] = 7; // ServerId
        frame[0x01] = 3; // ChannelId
        frame[0x02] = slotMask; // SlotMask: slots 0 and 2

        // Record A -> slot 0 (the first set bit, packed first).
        Span<byte> recA = frame.AsSpan(headerSize, recSize);
        WriteRecord(
            recA,
            name: "Musa\0",
            appearanceVariant: 1,
            internalClass: 1,
            stateByte: 0x0A,
            subLevelByte: 0x00,
            currentHp: 1234,
            currentMp: 567,
            currentStamina: 89,
            worldX: 100.5f,
            worldZ: -42.25f,
            serverClass: 0x1111,
            equipSlot3Gid: 301,
            equipSlot4Gid: 401,
            primary0: 10,
            primary1: 11,
            primary2: 12,
            primary3: 13,
            primary4: 14,
            remainingStatPoints: 5,
            slotFlag: 0x00,
            timing: 0xDEADBEEF);

        // Record B -> slot 2 (the second set bit, packed second).
        Span<byte> recB = frame.AsSpan(headerSize + recSize, recSize);
        WriteRecord(
            recB,
            name: "Dosa2\0",
            appearanceVariant: 1,
            internalClass: 3,
            stateByte: 0x14,
            subLevelByte: 0x00,
            currentHp: 9999,
            currentMp: 100,
            currentStamina: 50,
            worldX: -7.0f,
            worldZ: 3.5f,
            serverClass: 0x2222,
            equipSlot3Gid: 311,
            equipSlot4Gid: 411,
            primary0: 20,
            primary1: 21,
            primary2: 22,
            primary3: 23,
            primary4: 24,
            remainingStatPoints: 0,
            slotFlag: 0x01,
            timing: 0x12345678);

        var reader = new SmsgCharacterListReader(frame);

        // Header decodes.
        Assert.Equal((byte)7, reader.Header.ServerId);
        Assert.Equal((byte)3, reader.Header.ChannelId);
        Assert.Equal(slotMask, reader.SlotMask);
        Assert.Equal(2, reader.PopulatedCount);

        // Slot population follows the bitmask: 0 and 2 populated, 1 and 3 empty.
        Assert.True(reader.IsSlotPopulated(0));
        Assert.False(reader.IsSlotPopulated(1));
        Assert.True(reader.IsSlotPopulated(2));
        Assert.False(reader.IsSlotPopulated(3));

        // --- slot 0 = record A ---
        ref readonly CharacterListSlotRecord a = ref reader[0];
        Assert.Equal("Musa", DecodeName(a.NameField()));
        Assert.Equal((byte)1, a.AppearanceVariant);
        Assert.Equal((ushort)1, a.InternalClass);
        Assert.Equal((byte)0x0A, a.StateByte);
        Assert.Equal(1234u, a.CurrentHp);
        Assert.Equal(567u, a.CurrentMp);
        Assert.Equal(89u, a.CurrentStamina);
        Assert.Equal(100.5f, a.WorldX);
        Assert.Equal(-42.25f, a.WorldZ);
        Assert.Equal((ushort)0x1111, a.ServerClass);
        Assert.Equal(301u, a.EquipEntry(3).PartGid);
        Assert.Equal(401u, a.EquipEntry(4).PartGid);
        Assert.Equal(10u, a.PrimaryStat0);
        Assert.Equal(14u, a.PrimaryStat4);
        Assert.Equal(5u, a.RemainingStatPoints);
        Assert.Equal((byte)0x00, a.SlotFlag);
        Assert.Equal(0xDEADBEEFu, a.Timing);
        // Appearance driver from the decoded fields.
        Assert.Equal(
            CharacterAppearance.ModelClassId(1, 1),
            CharacterAppearance.ModelClassId(a.InternalClass, a.AppearanceVariant));

        // --- slot 2 = record B (the SECOND packed record lands in slot 2, NOT slot 1) ---
        ref readonly CharacterListSlotRecord b = ref reader[2];
        Assert.Equal("Dosa2", DecodeName(b.NameField()));
        Assert.Equal((ushort)3, b.InternalClass);
        Assert.Equal(9999u, b.CurrentHp);
        Assert.Equal(-7.0f, b.WorldX);
        Assert.Equal(3.5f, b.WorldZ);
        Assert.Equal((ushort)0x2222, b.ServerClass);
        Assert.Equal(311u, b.EquipEntry(3).PartGid);
        Assert.Equal(411u, b.EquipEntry(4).PartGid);
        Assert.Equal(20u, b.PrimaryStat0);
        Assert.Equal(24u, b.PrimaryStat4);
        Assert.Equal((byte)0x01, b.SlotFlag);
        Assert.Equal(0x12345678u, b.Timing);

        // Accessing an unset slot throws (ref struct can't be captured in a lambda -> explicit probe).
        bool threw = false;
        try
        {
            _ = reader[1].CurrentHp;
        }
        catch (ArgumentOutOfRangeException)
        {
            threw = true;
        }

        Assert.True(threw);
    }

    [Fact] // spec: Docs/RE/packets/3-1_character_list.yaml — empty mask => zero records, all slots empty.
    public void Reader_empty_mask_has_no_records()
    {
        byte[] frame = new byte[SmsgCharacterListHeader.HeaderSize];
        frame[0x02] = 0; // SlotMask = 0
        var reader = new SmsgCharacterListReader(frame);
        Assert.Equal(0, reader.PopulatedCount);
        Assert.False(reader.IsSlotPopulated(0));
    }

    [Fact] // spec: Docs/RE/packets/3-1_character_list.yaml — reader clamps to bytes actually present.
    public void Reader_clamps_count_to_available_record_bytes()
    {
        // Mask claims slots 0 and 1 (2 records) but only ONE record's worth of bytes follows.
        byte[] frame = new byte[SmsgCharacterListHeader.HeaderSize + CharacterListSlotRecord.WireSize];
        frame[0x02] = 0b0000_0011; // slots 0 and 1
        var reader = new SmsgCharacterListReader(frame);
        Assert.Equal(1, reader.PopulatedCount); // clamped to the one available record
    }

    // -------------------------------------------------------------------------
    // Helpers (synthetic fixture builders — NOT a real capture)
    // -------------------------------------------------------------------------

    private static string DecodeName(ReadOnlySpan<byte> field)
    {
        // CP949/EUC-KR registration (mirrors the project's shared decode convention). ASCII names here
        // are a subset of CP949, so this also validates the NUL-trim. The wire layer keeps the raw
        // bytes; higher layers do the actual CP949 decode.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        int nul = field.IndexOf((byte)0);
        ReadOnlySpan<byte> trimmed = nul >= 0 ? field[..nul] : field;
        return System.Text.Encoding.GetEncoding(949).GetString(trimmed);
    }

    private static void WriteRecord(
        Span<byte> rec,
        string name,
        byte appearanceVariant,
        ushort internalClass,
        byte stateByte,
        byte subLevelByte,
        uint currentHp,
        uint currentMp,
        uint currentStamina,
        float worldX,
        float worldZ,
        ushort serverClass,
        uint equipSlot3Gid,
        uint equipSlot4Gid,
        uint primary0,
        uint primary1,
        uint primary2,
        uint primary3,
        uint primary4,
        uint remainingStatPoints,
        byte slotFlag,
        uint timing)
    {
        // --- SpawnDescriptor (SD-relative offsets; SD starts at record +0). spec: structs/actor.md ---
        ReadOnlySpan<byte> nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
        nameBytes[..Math.Min(nameBytes.Length, 17)].CopyTo(rec[0x00..]); // name char[17]
        rec[0x2C] = appearanceVariant; // appearance_variant
        BinaryPrimitives.WriteUInt16LittleEndian(rec[0x34..], internalClass); // internal_class
        rec[0x38] = stateByte; // state_byte
        rec[0x39] = subLevelByte; // sub_level_byte
        BinaryPrimitives.WriteUInt32LittleEndian(rec[0x3C..], currentHp); // current_hp
        BinaryPrimitives.WriteUInt32LittleEndian(rec[0x40..], currentMp); // current_mp
        BinaryPrimitives.WriteUInt32LittleEndian(rec[0x44..], currentStamina); // current_stamina
        BinaryPrimitives.WriteSingleLittleEndian(rec[0x4C..], worldX); // world_x
        BinaryPrimitives.WriteSingleLittleEndian(rec[0x50..], worldZ); // world_z
        // equip_ref_table at SD +0x58, 16-byte entries: entry k leading dword = part gid.
        BinaryPrimitives.WriteUInt32LittleEndian(rec[(0x58 + (3 * 16))..], equipSlot3Gid); // slot 3
        BinaryPrimitives.WriteUInt32LittleEndian(rec[(0x58 + (4 * 16))..], equipSlot4Gid); // slot 4
        BinaryPrimitives.WriteUInt16LittleEndian(rec[0x74..],
            serverClass); // server_class (overlaps table; read at its offset)

        // --- StatBlock (StatBlock-relative; starts at record +0x370). spec: packets/3-1_character_list.yaml ---
        int sb = CharacterListSlotRecord.StatBlockOffset; // 0x370
        BinaryPrimitives.WriteUInt32LittleEndian(rec[(sb + 0x00)..], primary0);
        BinaryPrimitives.WriteUInt32LittleEndian(rec[(sb + 0x04)..], primary1);
        BinaryPrimitives.WriteUInt32LittleEndian(rec[(sb + 0x08)..], primary2);
        BinaryPrimitives.WriteUInt32LittleEndian(rec[(sb + 0x0C)..], primary3);
        BinaryPrimitives.WriteUInt32LittleEndian(rec[(sb + 0x10)..], primary4);
        BinaryPrimitives.WriteUInt32LittleEndian(rec[(sb + 0x30)..], remainingStatPoints);

        // --- SlotFlag (record +0x3D0) and Timing (record +0x3D1) ---
        rec[CharacterListSlotRecord.SlotFlagOffset] = slotFlag;
        BinaryPrimitives.WriteUInt32LittleEndian(rec[CharacterListSlotRecord.TimingOffset..], timing);
    }
}