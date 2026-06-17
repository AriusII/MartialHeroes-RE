using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="MobSpawnParser"/>.
/// All fixtures are synthetic in-memory byte buffers; no real VFS is required.
/// spec: Docs/RE/formats/npc_spawns.md §Companion formats — mob.arr (20-byte record).
/// NOTE: mob.arr is a content-tool / editor format with no client loader at runtime.
///       The client reads mob data from data/script/mobs.scr instead. These tests
///       cover mechanical decode only.
///       spec: Docs/RE/formats/npc_spawns.md §114 — "mob.arr (20-byte) … NO CLIENT LOADER".
/// </summary>
public sealed class MobSpawnParserTests
{
    // Record stride: 20 bytes (0x14).
    // spec: Docs/RE/formats/npc_spawns.md §Companion formats — "mob.arr: 20-byte record": CONFIRMED.
    private const int RecordStride = 20;

    // ── Fixture helpers ───────────────────────────────────────────────────────

    private static byte[] BuildRecord(
        ushort mobId,
        ushort pad,
        float worldX,
        float worldZ,
        float fieldC,
        float field10)
    {
        var buf = new byte[RecordStride];
        // MobId u16le @ +0. spec: npc_spawns.md §mob.arr — u16 MobId @0.
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0, 2), mobId);
        // Pad u16le @ +2. spec: npc_spawns.md §mob.arr — u16 pad @2: CONFIRMED inert.
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(2, 2), pad);
        // WorldX f32le @ +4. spec: npc_spawns.md §mob.arr — f32 WorldX @4: CONFIRMED.
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(4, 4), worldX);
        // WorldZ f32le @ +8. spec: npc_spawns.md §mob.arr — f32 WorldZ @8: CONFIRMED.
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(8, 4), worldZ);
        // FieldC f32le @ +12. spec: npc_spawns.md §mob.arr — f32 FieldC @12: CONFIRMED inert.
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(12, 4), fieldC);
        // Field10 f32le @ +16. spec: npc_spawns.md §mob.arr — f32 Field10 @16: CONFIRMED inert.
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(16, 4), field10);
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

    // ── Empty / trivial tests ─────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyBuffer_ReturnsEmpty()
    {
        // An empty span has zero complete records.
        MobSpawnRecord[] records = MobSpawnParser.Parse(ReadOnlySpan<byte>.Empty);
        Assert.Empty(records);
    }

    [Fact]
    public void Parse_LessThanOneRecordStride_ReturnsEmpty()
    {
        // A 19-byte buffer cannot contain even one complete 20-byte record.
        // spec: npc_spawns.md — "record_count = floor(file_size / 20)".
        var buf = new byte[RecordStride - 1];
        MobSpawnRecord[] records = MobSpawnParser.Parse(buf.AsSpan());
        Assert.Empty(records);
    }

    [Fact]
    public void Parse_RecordStride_IsCorrect()
    {
        // The parser should decode exactly 1 record from a 20-byte buffer.
        // spec: npc_spawns.md §mob.arr — "Fixed 20-byte records: CONFIRMED".
        byte[] buf = BuildRecord(mobId: 101, pad: 0, worldX: 100f, worldZ: 200f, fieldC: 0f, field10: 0f);
        MobSpawnRecord[] records = MobSpawnParser.Parse(buf.AsSpan());
        Assert.Single(records);
    }

    // ── MobId zero is preserved (mechanical decoder; no runtime semantics) ─────

    [Fact]
    public void Parse_MobIdZero_RecordPreserved()
    {
        // The 20-byte mob.arr format has no client loader; the decoder preserves every complete record.
        // spec: Docs/RE/formats/npc_spawns.md §Companion formats — semantics out-of-client-scope.
        byte[] buf = BuildRecord(mobId: 0, pad: 0, worldX: 100f, worldZ: 200f, fieldC: 0f, field10: 0f);
        MobSpawnRecord[] records = MobSpawnParser.Parse(buf.AsSpan());
        Assert.Single(records);
        Assert.Equal((ushort)0, records[0].MobId);
    }

    [Fact]
    public void Parse_MixedZeroAndNonZeroMobId_PreservesBoth()
    {
        // Record 0 has MobId=0; record 1 has MobId=201. Both are preserved in file order.
        byte[] rec0 = BuildRecord(mobId: 0, pad: 0, worldX: 100f, worldZ: 100f, fieldC: 0f, field10: 0f);
        byte[] rec1 = BuildRecord(mobId: 201, pad: 0, worldX: 500f, worldZ: 600f, fieldC: 0f, field10: 0f);
        byte[] buf = Concat(rec0, rec1);

        MobSpawnRecord[] records = MobSpawnParser.Parse(buf.AsSpan());

        Assert.Equal(2, records.Length);
        Assert.Equal((ushort)0, records[0].MobId);
        Assert.Equal((ushort)201, records[1].MobId);
    }

    // ── Field decode round-trips ──────────────────────────────────────────────

    [Fact]
    public void Parse_MobId_RoundTrip()
    {
        // spec: npc_spawns.md §mob.arr — "u16 MobId @0": field value round-trips.
        byte[] buf = BuildRecord(mobId: 512, pad: 0, worldX: 0f, worldZ: 0f, fieldC: 0f, field10: 0f);
        MobSpawnRecord[] records = MobSpawnParser.Parse(buf.AsSpan());
        Assert.Single(records);
        Assert.Equal((ushort)512, records[0].MobId);
    }

    [Fact]
    public void Parse_WorldX_RoundTrip()
    {
        // spec: npc_spawns.md §mob.arr — "f32 WorldX @4; world_x: CONFIRMED".
        byte[] buf = BuildRecord(mobId: 42, pad: 0, worldX: 12345.6f, worldZ: 0f, fieldC: 0f, field10: 0f);
        MobSpawnRecord[] records = MobSpawnParser.Parse(buf.AsSpan());
        Assert.Single(records);
        Assert.Equal(12345.6f, records[0].WorldX, precision: 3);
    }

    [Fact]
    public void Parse_WorldZ_RoundTrip()
    {
        // spec: npc_spawns.md §mob.arr — "f32 WorldZ @8; world_z: CONFIRMED".
        byte[] buf = BuildRecord(mobId: 43, pad: 0, worldX: 0f, worldZ: -9876.5f, fieldC: 0f, field10: 0f);
        MobSpawnRecord[] records = MobSpawnParser.Parse(buf.AsSpan());
        Assert.Single(records);
        Assert.Equal(-9876.5f, records[0].WorldZ, precision: 3);
    }

    [Fact]
    public void Parse_FieldC_IsRetained()
    {
        // spec: npc_spawns.md §mob.arr — "f32 FieldC @12: CONFIRMED inert (semantic UNVERIFIED)".
        // The parser decodes FieldC but does not interpret it.
        byte[] buf = BuildRecord(mobId: 5, pad: 0, worldX: 0f, worldZ: 0f, fieldC: 3.14f, field10: 0f);
        MobSpawnRecord[] records = MobSpawnParser.Parse(buf.AsSpan());
        Assert.Single(records);
        Assert.Equal(3.14f, records[0].FieldC, precision: 5);
    }

    [Fact]
    public void Parse_Field10_IsRetained()
    {
        // spec: npc_spawns.md §mob.arr — "f32 Field10 @16: CONFIRMED inert (semantic UNVERIFIED)".
        byte[] buf = BuildRecord(mobId: 7, pad: 0, worldX: 0f, worldZ: 0f, fieldC: 0f, field10: 99.9f);
        MobSpawnRecord[] records = MobSpawnParser.Parse(buf.AsSpan());
        Assert.Single(records);
        Assert.Equal(99.9f, records[0].Field10, precision: 4);
    }

    [Fact]
    public void Parse_Pad_IsRetained()
    {
        // spec: npc_spawns.md §mob.arr — "u16 field_02 @2: CONFIRMED inert".
        // The pad field is decoded but its semantic is unused.
        byte[] buf = BuildRecord(mobId: 3, pad: 0xBEEF, worldX: 0f, worldZ: 0f, fieldC: 0f, field10: 0f);
        MobSpawnRecord[] records = MobSpawnParser.Parse(buf.AsSpan());
        Assert.Single(records);
        Assert.Equal((ushort)0xBEEF, records[0].Pad);
    }

    // ── Trailing-byte truncation test ─────────────────────────────────────────

    [Fact]
    public void Parse_TrailingBytesIgnored()
    {
        // A buffer of 45 bytes = 2 complete 20-byte records + 5 trailing bytes.
        // Trailing bytes must be silently ignored (not throw).
        // spec: npc_spawns.md — "Any trailing bytes that do not fill a complete 20-byte record are silently ignored."
        byte[] rec0 = BuildRecord(mobId: 11, pad: 0, worldX: 1f, worldZ: 2f, fieldC: 0f, field10: 0f);
        byte[] rec1 = BuildRecord(mobId: 22, pad: 0, worldX: 3f, worldZ: 4f, fieldC: 0f, field10: 0f);
        byte[] trailing = new byte[5] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE };
        byte[] buf = Concat(rec0, rec1, trailing);

        MobSpawnRecord[] records = MobSpawnParser.Parse(buf.AsSpan());

        Assert.Equal(2, records.Length);
        Assert.Equal((ushort)11, records[0].MobId);
        Assert.Equal((ushort)22, records[1].MobId);
    }

    // ── No de-duplication ─────────────────────────────────────────────────────

    [Fact]
    public void Parse_DuplicateMobIdXZ_PreservesBoth()
    {
        // Two records with identical (MobId, WorldX, WorldZ) are still two complete records.
        // spec: Docs/RE/formats/npc_spawns.md §Companion formats — record_count = floor(size / 20).
        byte[] rec0 = BuildRecord(mobId: 99, pad: 0, worldX: 10f, worldZ: 20f, fieldC: 0f, field10: 0f);
        byte[] rec1 = BuildRecord(mobId: 99, pad: 0, worldX: 10f, worldZ: 20f, fieldC: 1f, field10: 1f);
        byte[] buf = Concat(rec0, rec1);

        MobSpawnRecord[] records = MobSpawnParser.Parse(buf.AsSpan());

        Assert.Equal(2, records.Length);
        Assert.Equal((ushort)99, records[0].MobId);
        Assert.Equal(1f, records[1].FieldC);
    }

    [Fact]
    public void Parse_SameMobIdDifferentPosition_BothKept()
    {
        // Same MobId but different (WorldX, WorldZ) — both must appear.
        byte[] rec0 = BuildRecord(mobId: 50, pad: 0, worldX: 100f, worldZ: 200f, fieldC: 0f, field10: 0f);
        byte[] rec1 = BuildRecord(mobId: 50, pad: 0, worldX: 101f, worldZ: 200f, fieldC: 0f, field10: 0f);
        byte[] buf = Concat(rec0, rec1);

        MobSpawnRecord[] records = MobSpawnParser.Parse(buf.AsSpan());

        Assert.Equal(2, records.Length);
    }

    // ── ReadOnlyMemory overload ───────────────────────────────────────────────

    [Fact]
    public void Parse_ReadOnlyMemory_Overload_Works()
    {
        byte[] buf = BuildRecord(mobId: 77, pad: 0, worldX: 5f, worldZ: 6f, fieldC: 0f, field10: 0f);
        MobSpawnRecord[] records = MobSpawnParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Single(records);
        Assert.Equal((ushort)77, records[0].MobId);
    }
}