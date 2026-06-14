using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for the new campaign-4 xdb loaders:
/// <c>vehicle.xdb</c> (52-byte stride) and <c>creature_item.xdb</c> (48-byte stride).
/// All buffers are built in-memory; no real game file is required.
/// spec: Docs/RE/formats/xdb_tables.md §4 (vehicle) + §5 (creature_item)
/// </summary>
public sealed class XdbFormatLoaderTests
{
    // ─── binary helpers ────────────────────────────────────────────────────────

    private static void WriteU32LE(byte[] buf, int offset, uint v) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset, 4), v);

    private static void WriteF32LE(byte[] buf, int offset, float v) =>
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(offset, 4), v);

    // =========================================================================
    // vehicle.xdb — stride 52 bytes
    // =========================================================================
    // spec: Docs/RE/formats/xdb_tables.md §4 — "stride 52 bytes, 58 records (3,016 / 52 = 58, exact)": CONFIRMED.

    /// <summary>
    /// Builds a minimal vehicle.xdb fixture with the given (vehicleId, itemId) pairs.
    /// All UNVERIFIED bytes (unknown_8b and zero_region) are left as zero.
    /// spec: Docs/RE/formats/xdb_tables.md §4 — vehicle_id u32LE @ +0, item_id u32LE @ +4: CONFIRMED.
    /// </summary>
    private static byte[] BuildVehicleXdb(params (uint vehicleId, uint itemId)[] records)
    {
        byte[] buf = new byte[records.Length * 52];
        for (int i = 0; i < records.Length; i++)
        {
            int off = i * 52;
            // vehicle_id u32LE @ +0. CONFIRMED.
            // spec: Docs/RE/formats/xdb_tables.md §4 — vehicle_id u32LE @ +0: CONFIRMED.
            WriteU32LE(buf, off + 0, records[i].vehicleId);
            // item_id u32LE @ +4. CONFIRMED.
            // spec: Docs/RE/formats/xdb_tables.md §4 — item_id u32LE @ +4: CONFIRMED.
            WriteU32LE(buf, off + 4, records[i].itemId);
            // unknown_8b @ +8 (8 bytes) and zero_region @ +16 (36 bytes): left zero.
        }
        return buf;
    }

    [Fact]
    public void VehicleXdb_Stride52_RecordCountFromDivision()
    {
        // spec: Docs/RE/formats/xdb_tables.md §4 — "record count = file_size / 52": CONFIRMED.
        // Verified by: 3,016 / 52 = 58 (exact).
        Assert.Equal(0, 3016 % 52);
        Assert.Equal(58, 3016 / 52);
    }

    [Fact]
    public void VehicleXdb_VehicleId_And_ItemId_Decoded()
    {
        // spec: Docs/RE/formats/xdb_tables.md §4 — vehicle_id u32LE @ +0: CONFIRMED.
        // spec: Docs/RE/formats/xdb_tables.md §4 — item_id u32LE @ +4: CONFIRMED.
        byte[] buf = BuildVehicleXdb((1u, 3108u), (2u, 3109u));
        VehicleXdbRecord[] records = XdbParser.ParseVehicleXdb(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(2, records.Length);
        Assert.Equal(1u, records[0].VehicleId);
        Assert.Equal(3108u, records[0].ItemId);
        Assert.Equal(2u, records[1].VehicleId);
        Assert.Equal(3109u, records[1].ItemId);
    }

    [Fact]
    public void VehicleXdb_UnknownRegions_ExposedAsRawBytes()
    {
        // spec: Docs/RE/formats/xdb_tables.md §4 — unknown_8b @ +8: UNVERIFIED (carried through raw).
        // spec: Docs/RE/formats/xdb_tables.md §4 — zero_region @ +16: layout UNVERIFIED (carried through raw).
        byte[] buf = BuildVehicleXdb((7u, 3114u));
        VehicleXdbRecord[] records = XdbParser.ParseVehicleXdb(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(8, records[0].Unknown8b.Length);
        Assert.Equal(36, records[0].ZeroRegion.Length);
    }

    [Fact]
    public void VehicleXdb_NonMultiple_ThrowsInvalidDataException()
    {
        // A buffer that is not a multiple of 52 must be rejected.
        // spec: Docs/RE/formats/xdb_tables.md §4 — "record count = file_size / stride (must be exact)".
        byte[] bad = new byte[53]; // 53 is not a multiple of 52
        Assert.Throws<InvalidDataException>(
            () => XdbParser.ParseVehicleXdb(new ReadOnlyMemory<byte>(bad)));
    }

    [Fact]
    public void VehicleXdb_EmptyBuffer_YieldsZeroRecords()
    {
        VehicleXdbRecord[] records = XdbParser.ParseVehicleXdb(ReadOnlyMemory<byte>.Empty);
        Assert.Empty(records);
    }

    // =========================================================================
    // creature_item.xdb — stride 48 bytes
    // =========================================================================
    // spec: Docs/RE/formats/xdb_tables.md §5 — "stride 48 bytes, 921 records (44,208 / 48 = 921, exact)": CONFIRMED.

    /// <summary>
    /// Builds a minimal creature_item.xdb fixture record.
    /// spec: Docs/RE/formats/xdb_tables.md §5 field offsets: all per spec.
    /// The six attachment floats (+8..+28) are UNVERIFIED for axis; carried as raw values.
    /// </summary>
    private static byte[] BuildCreatureItemXdb(
        uint creatureKey, uint itemId,
        float f0, float f1, float f2, float f3, float f4, float f5,
        float scaleOrRadius,
        uint unknownU1,
        byte flag0, byte flag1, byte flag2, byte flag3,
        uint probability)
    {
        byte[] buf = new byte[48]; // single record
        // creature_key u32LE @ +0. spec: §5 CONFIRMED (pattern).
        WriteU32LE(buf, 0, creatureKey);
        // item_id u32LE @ +4. spec: §5 CONFIRMED.
        WriteU32LE(buf, 4, itemId);
        // attach_f0..attach_f5 f32LE @ +8..+28. spec: §5 CONFIRMED (present); axis UNVERIFIED.
        WriteF32LE(buf, 8, f0);
        WriteF32LE(buf, 12, f1);
        WriteF32LE(buf, 16, f2);
        WriteF32LE(buf, 20, f3);
        WriteF32LE(buf, 24, f4);
        WriteF32LE(buf, 28, f5);
        // scale_or_radius f32LE @ +32. spec: §5 CONFIRMED (present); semantic UNVERIFIED.
        WriteF32LE(buf, 32, scaleOrRadius);
        // unknown_u1 u32LE @ +36. spec: §5 CONFIRMED (value=0 in head); UNVERIFIED.
        WriteU32LE(buf, 36, unknownU1);
        // flag_0..flag_3 u8 @ +40..+43. spec: §5 UNVERIFIED.
        buf[40] = flag0;
        buf[41] = flag1;
        buf[42] = flag2;
        buf[43] = flag3;
        // probability u32LE @ +44. spec: §5 CONFIRMED (value=100 in head); semantic UNVERIFIED.
        WriteU32LE(buf, 44, probability);
        return buf;
    }

    [Fact]
    public void CreatureItemXdb_Stride48_RecordCountFromDivision()
    {
        // spec: Docs/RE/formats/xdb_tables.md §5 — "record count = file_size / 48": CONFIRMED.
        // Verified by: 44,208 / 48 = 921 (exact).
        Assert.Equal(0, 44208 % 48);
        Assert.Equal(921, 44208 / 48);
    }

    [Fact]
    public void CreatureItemXdb_CreatureKey_And_ItemId_Decoded()
    {
        // spec: Docs/RE/formats/xdb_tables.md §5 — creature_key u32LE @ +0: CONFIRMED (pattern).
        // spec: Docs/RE/formats/xdb_tables.md §5 — item_id u32LE @ +4: CONFIRMED.
        byte[] buf = BuildCreatureItemXdb(
            creatureKey: 100001u, itemId: 3001u,
            f0: -5.0f, f1: 0.0f, f2: 10.0f, f3: 0.0f, f4: 0.0f, f5: 0.0f,
            scaleOrRadius: 8.0f,
            unknownU1: 0u,
            flag0: 0, flag1: 1, flag2: 0, flag3: 0,
            probability: 100u);

        CreatureItemXdbRecord[] records = XdbParser.ParseCreatureItemXdb(new ReadOnlyMemory<byte>(buf));

        Assert.Single(records);
        Assert.Equal(100001u, records[0].CreatureKey);
        Assert.Equal(3001u, records[0].ItemId);
    }

    [Fact]
    public void CreatureItemXdb_AttachmentFloats_CarriedThrough_Unverified()
    {
        // The six attachment floats are carried through as raw values without axis interpretation.
        // spec: Docs/RE/formats/xdb_tables.md §5 Known unknowns —
        //   "axis mapping of the six attachment floats: UNVERIFIED".
        byte[] buf = BuildCreatureItemXdb(
            creatureKey: 200002u, itemId: 3002u,
            f0: -5.5f, f1: 1.0f, f2: 12.3f, f3: -0.5f, f4: 0.25f, f5: -9.9f,
            scaleOrRadius: 8.0f,
            unknownU1: 0u,
            flag0: 0, flag1: 1, flag2: 1, flag3: 0,
            probability: 100u);

        CreatureItemXdbRecord[] records = XdbParser.ParseCreatureItemXdb(new ReadOnlyMemory<byte>(buf));
        CreatureItemXdbRecord r = records[0];

        // Carry-through assertion: exact float values preserved (no axis transform applied).
        Assert.Equal(-5.5f, r.AttachF0, precision: 5);
        Assert.Equal(1.0f, r.AttachF1, precision: 5);
        Assert.Equal(12.3f, r.AttachF2, precision: 4); // float precision
        Assert.Equal(-0.5f, r.AttachF3, precision: 5);
        Assert.Equal(0.25f, r.AttachF4, precision: 5);
        Assert.Equal(-9.9f, r.AttachF5, precision: 4);
    }

    [Fact]
    public void CreatureItemXdb_ScaleOrRadius_CarriedThrough()
    {
        // spec: Docs/RE/formats/xdb_tables.md §5 — scale_or_radius f32LE @ +32:
        //   CONFIRMED (present); semantic UNVERIFIED — carried raw.
        byte[] buf = BuildCreatureItemXdb(
            100001u, 3001u,
            0f, 0f, 0f, 0f, 0f, 0f,
            scaleOrRadius: 8.0f,
            0u, 0, 1, 0, 0, 100u);

        CreatureItemXdbRecord[] records = XdbParser.ParseCreatureItemXdb(new ReadOnlyMemory<byte>(buf));
        Assert.Equal(8.0f, records[0].ScaleOrRadius, precision: 5);
    }

    [Fact]
    public void CreatureItemXdb_Flags_CarriedThrough()
    {
        // spec: Docs/RE/formats/xdb_tables.md §5 — flag_0..flag_3 u8 @ +40..+43: UNVERIFIED.
        // flag_1 = 1 in every head record; flag_2 alternates 0/1.
        byte[] buf = BuildCreatureItemXdb(
            100001u, 3001u,
            0f, 0f, 0f, 0f, 0f, 0f, 8.0f, 0u,
            flag0: 0, flag1: 1, flag2: 1, flag3: 0,
            probability: 100u);

        CreatureItemXdbRecord[] records = XdbParser.ParseCreatureItemXdb(new ReadOnlyMemory<byte>(buf));
        CreatureItemXdbRecord r = records[0];

        Assert.Equal(0, r.Flag0);
        Assert.Equal(1, r.Flag1);
        Assert.Equal(1, r.Flag2);
        Assert.Equal(0, r.Flag3);
    }

    [Fact]
    public void CreatureItemXdb_Probability100_CarriedThrough()
    {
        // spec: Docs/RE/formats/xdb_tables.md §5 — probability u32LE @ +44:
        //   CONFIRMED (value=100 in head); semantic UNVERIFIED — carried raw.
        byte[] buf = BuildCreatureItemXdb(
            100001u, 3001u,
            0f, 0f, 0f, 0f, 0f, 0f, 8.0f, 0u,
            0, 1, 0, 0, probability: 100u);

        CreatureItemXdbRecord[] records = XdbParser.ParseCreatureItemXdb(new ReadOnlyMemory<byte>(buf));
        Assert.Equal(100u, records[0].Probability);
    }

    [Fact]
    public void CreatureItemXdb_NonMultiple_ThrowsInvalidDataException()
    {
        // spec: Docs/RE/formats/xdb_tables.md §5 — "record count = file_size / stride (must be exact)".
        byte[] bad = new byte[49]; // 49 is not a multiple of 48
        Assert.Throws<InvalidDataException>(
            () => XdbParser.ParseCreatureItemXdb(new ReadOnlyMemory<byte>(bad)));
    }

    [Fact]
    public void CreatureItemXdb_TwoRecords_BothDecoded()
    {
        // Builds two back-to-back records to verify stride alignment is correct.
        byte[] rec0 = BuildCreatureItemXdb(1u, 3001u, 0f, 0f, 0f, 0f, 0f, 0f, 8.0f, 0u, 0, 1, 0, 0, 100u);
        byte[] rec1 = BuildCreatureItemXdb(2u, 3002u, 1f, 2f, 3f, 4f, 5f, 6f, 9.0f, 0u, 0, 1, 1, 0, 100u);
        byte[] buf = new byte[96]; // 2 × 48
        rec0.CopyTo(buf, 0);
        rec1.CopyTo(buf, 48);

        CreatureItemXdbRecord[] records = XdbParser.ParseCreatureItemXdb(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(2, records.Length);
        Assert.Equal(1u, records[0].CreatureKey);
        Assert.Equal(2u, records[1].CreatureKey);
        Assert.Equal(3002u, records[1].ItemId);
        Assert.Equal(1.0f, records[1].AttachF0, precision: 5);
        Assert.Equal(1, records[1].Flag2); // flag_2 alternates
    }
}
