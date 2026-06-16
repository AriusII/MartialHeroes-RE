using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="NpcSpawnParser"/> and <see cref="NpcSpawnRecord"/>.
/// All fixtures are synthetic in-memory byte buffers; no real VFS is required.
/// spec: Docs/RE/formats/npc_spawns.md §Record layout — 28-byte stride, confirmed fields.
/// </summary>
/// <remarks>
/// Key invariants under test:
/// <list type="bullet">
///   <item>Record stride: 28 bytes (floor(file_size/28) records, trailing bytes ignored).</item>
///   <item>mob_id u16 @ +0, field_02 u16 @ +2 (inert), world_x f32 @ +4, world_z f32 @ +8.</item>
///   <item>facing f32 @ +12 (raw), spawn_type u32 @ +16, field_20/24 u32 (inert).</item>
///   <item>AppliedFacing = π/2 − Facing (NOT π/2 + Facing).</item>
/// </list>
/// </remarks>
public sealed class NpcSpawnParserTests
{
    // Record stride: 28 bytes (0x1C).
    // spec: Docs/RE/formats/npc_spawns.md §Record layout — 28 bytes per record: CONFIRMED.
    private const int RecordStride = 28;

    // ── Fixture builder ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds one 28-byte npc.arr record from explicit field values.
    /// spec: Docs/RE/formats/npc_spawns.md §Record layout.
    /// </summary>
    private static byte[] BuildRecord(
        ushort mobId,
        ushort field02Inert,
        float worldX,
        float worldZ,
        float facing,
        uint spawnType,
        uint field20Inert = 0,
        uint field24Inert = 0)
    {
        var buf = new byte[RecordStride];
        // mob_id u16le @ +0. spec: npc_spawns.md — mob_id u16 @ +0: CONFIRMED.
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0, 2), mobId);
        // field_02 u16le @ +2. spec: npc_spawns.md — field_02 u16 @ +2: CONFIRMED inert.
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(2, 2), field02Inert);
        // world_x f32le @ +4. spec: npc_spawns.md — world_x f32 @ +4: CONFIRMED.
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(4, 4), worldX);
        // world_z f32le @ +8. spec: npc_spawns.md — world_z f32 @ +8: CONFIRMED.
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(8, 4), worldZ);
        // facing f32le @ +12. spec: npc_spawns.md — facing f32 @ +12: CONFIRMED.
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(12, 4), facing);
        // spawn_type u32le @ +16. spec: npc_spawns.md — spawn_type u32 @ +16: CONFIRMED.
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(16, 4), spawnType);
        // field_20 u32le @ +20. spec: npc_spawns.md — field_20 u32 @ +20: CONFIRMED inert.
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(20, 4), field20Inert);
        // field_24 u32le @ +24. spec: npc_spawns.md — field_24 u32 @ +24: CONFIRMED inert.
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(24, 4), field24Inert);
        return buf;
    }

    private static byte[] Concat(params byte[][] arrays)
    {
        int total = 0;
        foreach (var a in arrays) total += a.Length;
        var result = new byte[total];
        int pos = 0;
        foreach (var a in arrays)
        {
            a.CopyTo(result, pos);
            pos += a.Length;
        }

        return result;
    }

    // ── Empty / trivial ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyBuffer_ReturnsEmpty()
    {
        // An empty span has zero complete records.
        // spec: npc_spawns.md §Container structure — floor(0/28) = 0.
        NpcSpawnArray result = NpcSpawnParser.Parse(ReadOnlySpan<byte>.Empty);
        Assert.Empty(result.Records);
    }

    [Fact]
    public void Parse_LessThanOneRecordStride_ReturnsEmpty()
    {
        // A 27-byte buffer cannot contain even one complete 28-byte record.
        // spec: npc_spawns.md §Container structure — floor(27/28) = 0.
        var buf = new byte[RecordStride - 1];
        NpcSpawnArray result = NpcSpawnParser.Parse(buf.AsSpan());
        Assert.Empty(result.Records);
    }

    // ── Record stride guard ────────────────────────────────────────────────────

    [Fact]
    public void Parse_ExactlyOneRecord_ReturnsOneRecord()
    {
        // A 28-byte buffer contains exactly one record.
        // spec: npc_spawns.md §Container structure — floor(28/28) = 1.
        byte[] buf = BuildRecord(mobId: 42, field02Inert: 0, worldX: 0f, worldZ: 0f, facing: 0f, spawnType: 0);
        NpcSpawnArray result = NpcSpawnParser.Parse(buf.AsSpan());
        Assert.Single(result.Records);
    }

    [Fact]
    public void Parse_TwoRecords_ReturnsTwoRecords()
    {
        // spec: npc_spawns.md §Container structure — floor(56/28) = 2.
        byte[] r0 = BuildRecord(mobId: 1, field02Inert: 0, worldX: 0f, worldZ: 0f, facing: 0f, spawnType: 0);
        byte[] r1 = BuildRecord(mobId: 2, field02Inert: 0, worldX: 0f, worldZ: 0f, facing: 0f, spawnType: 0);
        NpcSpawnArray result = NpcSpawnParser.Parse(Concat(r0, r1).AsSpan());
        Assert.Equal(2, result.Records.Length);
    }

    // ── Field decode round-trips ───────────────────────────────────────────────

    [Fact]
    public void Parse_MobId_RoundTrip()
    {
        // spec: npc_spawns.md — mob_id u16 @ +0: CONFIRMED.
        byte[] buf = BuildRecord(mobId: 0x1234, field02Inert: 0, worldX: 0f, worldZ: 0f, facing: 0f, spawnType: 0);
        NpcSpawnArray result = NpcSpawnParser.Parse(buf.AsSpan());
        Assert.Equal((ushort)0x1234, result.Records[0].MobId);
    }

    [Fact]
    public void Parse_WorldX_RoundTrip()
    {
        // spec: npc_spawns.md — world_x f32 @ +4: CONFIRMED.
        byte[] buf = BuildRecord(mobId: 1, field02Inert: 0, worldX: 12345.6f, worldZ: 0f, facing: 0f, spawnType: 0);
        NpcSpawnArray result = NpcSpawnParser.Parse(buf.AsSpan());
        Assert.Equal(12345.6f, result.Records[0].WorldX, precision: 3);
    }

    [Fact]
    public void Parse_WorldZ_RoundTrip()
    {
        // spec: npc_spawns.md — world_z f32 @ +8: CONFIRMED.
        byte[] buf = BuildRecord(mobId: 1, field02Inert: 0, worldX: 0f, worldZ: -9876.5f, facing: 0f, spawnType: 0);
        NpcSpawnArray result = NpcSpawnParser.Parse(buf.AsSpan());
        Assert.Equal(-9876.5f, result.Records[0].WorldZ, precision: 3);
    }

    [Fact]
    public void Parse_Facing_RawIsStoredValue()
    {
        // The raw Facing field stores the on-disk value unmodified.
        // The runtime transform π/2 − Facing is applied via AppliedFacing, NOT here.
        // spec: npc_spawns.md — facing f32 @ +12: CONFIRMED; "runtime applies π/2 − value".
        const float stored = 1.0f;
        byte[] buf = BuildRecord(mobId: 1, field02Inert: 0, worldX: 0f, worldZ: 0f, facing: stored, spawnType: 0);
        NpcSpawnArray result = NpcSpawnParser.Parse(buf.AsSpan());
        Assert.Equal(stored, result.Records[0].Facing, precision: 6);
    }

    [Fact]
    public void Parse_SpawnType_RoundTrip()
    {
        // spec: npc_spawns.md — spawn_type u32 @ +16: CONFIRMED.
        byte[] buf = BuildRecord(mobId: 1, field02Inert: 0, worldX: 0f, worldZ: 0f, facing: 0f, spawnType: 7);
        NpcSpawnArray result = NpcSpawnParser.Parse(buf.AsSpan());
        Assert.Equal(7u, result.Records[0].SpawnType);
    }

    [Fact]
    public void Parse_Field02Inert_RoundTrip()
    {
        // spec: npc_spawns.md — field_02 u16 @ +2: CONFIRMED inert (retained for stride fidelity).
        byte[] buf = BuildRecord(mobId: 1, field02Inert: 0xBEEF, worldX: 0f, worldZ: 0f, facing: 0f, spawnType: 0);
        NpcSpawnArray result = NpcSpawnParser.Parse(buf.AsSpan());
        Assert.Equal((ushort)0xBEEF, result.Records[0].Field02Inert);
    }

    [Fact]
    public void Parse_Field20Inert_RoundTrip()
    {
        // spec: npc_spawns.md — field_20 u32 @ +20: CONFIRMED inert.
        byte[] buf = BuildRecord(mobId: 1, field02Inert: 0, worldX: 0f, worldZ: 0f, facing: 0f, spawnType: 0, field20Inert: 0xDEAD1234u);
        NpcSpawnArray result = NpcSpawnParser.Parse(buf.AsSpan());
        Assert.Equal(0xDEAD1234u, result.Records[0].Field20Inert);
    }

    [Fact]
    public void Parse_Field24Inert_RoundTrip()
    {
        // spec: npc_spawns.md — field_24 u32 @ +24: CONFIRMED inert.
        byte[] buf = BuildRecord(mobId: 1, field02Inert: 0, worldX: 0f, worldZ: 0f, facing: 0f, spawnType: 0, field24Inert: 0xCAFEBABEu);
        NpcSpawnArray result = NpcSpawnParser.Parse(buf.AsSpan());
        Assert.Equal(0xCAFEBABEu, result.Records[0].Field24Inert);
    }

    // ── Trailing-byte truncation ───────────────────────────────────────────────

    [Fact]
    public void Parse_TrailingBytesIgnored()
    {
        // A buffer of 2×28 + 5 = 61 bytes → 2 complete records, 5 trailing bytes ignored.
        // spec: npc_spawns.md §Container structure — floor(61/28) = 2.
        byte[] r0 = BuildRecord(mobId: 11, field02Inert: 0, worldX: 1f, worldZ: 2f, facing: 0f, spawnType: 0);
        byte[] r1 = BuildRecord(mobId: 22, field02Inert: 0, worldX: 3f, worldZ: 4f, facing: 0f, spawnType: 0);
        byte[] trailing = new byte[5] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE };
        byte[] buf = Concat(r0, r1, trailing);

        NpcSpawnArray result = NpcSpawnParser.Parse(buf.AsSpan());

        Assert.Equal(2, result.Records.Length);
        Assert.Equal((ushort)11, result.Records[0].MobId);
        Assert.Equal((ushort)22, result.Records[1].MobId);
    }

    [Fact]
    public void Parse_16ByteBuffer_ReturnsNoRecords()
    {
        // A 16-byte buffer (less than one 28-byte record) returns 0 records.
        // Regression: non-multiple-of-28 buffer must not throw; trailing bytes are silently dropped.
        // spec: npc_spawns.md §Container structure — floor(16/28) = 0.
        byte[] buf = new byte[16];
        NpcSpawnArray result = NpcSpawnParser.Parse(buf.AsSpan());
        Assert.Empty(result.Records);
    }

    // ── ReadOnlyMemory overload ────────────────────────────────────────────────

    [Fact]
    public void Parse_ReadOnlyMemory_Overload_Works()
    {
        byte[] buf = BuildRecord(mobId: 77, field02Inert: 0, worldX: 5f, worldZ: 6f, facing: 0f, spawnType: 0);
        NpcSpawnArray result = NpcSpawnParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Single(result.Records);
        Assert.Equal((ushort)77, result.Records[0].MobId);
    }

    // ── AppliedFacing correctness ──────────────────────────────────────────────

    [Fact]
    public void AppliedFacing_IsHalfPiMinusFacing()
    {
        // The runtime applies π/2 − Facing (NOT π/2 + Facing).
        // spec: npc_spawns.md §Record layout — "runtime applies π/2 − value": sample-verified.
        const float storedFacing = 1.0f;
        byte[] buf = BuildRecord(mobId: 1, field02Inert: 0, worldX: 0f, worldZ: 0f, facing: storedFacing, spawnType: 0);
        NpcSpawnArray result = NpcSpawnParser.Parse(buf.AsSpan());
        float applied = result.Records[0].AppliedFacing;
        float expected = MathF.PI / 2f - storedFacing;
        Assert.Equal(expected, applied, precision: 6);
    }

    [Fact]
    public void AppliedFacing_DiffersFrom_PiOverTwoPlusFacing()
    {
        // AppliedFacing must NOT equal π/2 + Facing; using the wrong sign mirrors the orientation.
        // spec: npc_spawns.md §Record layout — "simply adding π/2 would mirror the orientation".
        const float storedFacing = 0.785f; // ~45 degrees
        byte[] buf = BuildRecord(mobId: 1, field02Inert: 0, worldX: 0f, worldZ: 0f, facing: storedFacing, spawnType: 0);
        NpcSpawnArray result = NpcSpawnParser.Parse(buf.AsSpan());
        float applied = result.Records[0].AppliedFacing;
        float wrongValue = MathF.PI / 2f + storedFacing;
        Assert.NotEqual(wrongValue, applied);
    }

    [Fact]
    public void AppliedFacing_ZeroFacing_IsHalfPi()
    {
        // When stored facing = 0, AppliedFacing = π/2.
        // spec: npc_spawns.md — AppliedFacing = π/2 − 0 = π/2.
        byte[] buf = BuildRecord(mobId: 1, field02Inert: 0, worldX: 0f, worldZ: 0f, facing: 0f, spawnType: 0);
        NpcSpawnArray result = NpcSpawnParser.Parse(buf.AsSpan());
        Assert.Equal(MathF.PI / 2f, result.Records[0].AppliedFacing, precision: 6);
    }

    [Fact]
    public void AppliedFacing_NegativeFloorCase_ComputesCorrectly()
    {
        // When stored facing = π/2 (maximum aligned), AppliedFacing = 0.
        // spec: npc_spawns.md — "floor −1.5708" (π/2 ≈ 1.5708).
        float storedFacing = MathF.PI / 2f;
        byte[] buf = BuildRecord(mobId: 1, field02Inert: 0, worldX: 0f, worldZ: 0f, facing: storedFacing, spawnType: 0);
        NpcSpawnArray result = NpcSpawnParser.Parse(buf.AsSpan());
        Assert.Equal(0f, result.Records[0].AppliedFacing, precision: 5);
    }
}
