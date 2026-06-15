using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="SodBlobParser"/>.
/// All fixtures are synthetic in-memory byte buffers built to the spec.
/// spec: Docs/RE/formats/terrain.md §11. Collision solid blob — .sod
/// </summary>
public sealed class SodBlobParserTests
{
    // SolidRecord stride: 108 bytes (0x6C). CONFIRMED.
    // spec: Docs/RE/formats/terrain.md §11.2 — "108 bytes (0x6C)": CONFIRMED.
    private const int SolidStride = 108;

    // QuadRecord stride: 48 bytes (0x30). CONFIRMED.
    // spec: Docs/RE/formats/terrain.md §11.3 — "48 bytes (0x30)": CONFIRMED.
    private const int QuadStride = 48;

    // ── Fixture helpers ───────────────────────────────────────────────────────

    private static void WriteF32(byte[] buf, int off, float v) =>
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(off, 4), v);

    private static void WriteU32(byte[] buf, int off, uint v) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(off, 4), v);

    /// <summary>
    /// Builds a minimal .sod file with the given solid AABB and one quad.
    /// Trailing fields (+016..+107) of the SolidRecord are left zero.
    /// spec: Docs/RE/formats/terrain.md §11.
    /// </summary>
    private static byte[] BuildMinimalSod(
        float aabbXMin, float aabbZMin, float aabbXMax, float aabbZMax,
        float qX0 = 0f, float qZ0 = 0f,
        float qX1 = 10f, float qZ1 = 0f,
        float qX2 = 10f, float qZ2 = 10f,
        float qX3 = 0f, float qZ3 = 10f,
        float edgeSlope = 0f, float edgePad0 = 0f,
        float edgeIntercept = 0f, float edgePad1 = 0f)
    {
        // Layout: solidCount u32 | SolidRecord[1] (108B) | quadCount u32 | QuadRecord[1] (48B)
        // Total: 4 + 108 + 4 + 48 = 164 bytes
        int totalSize = 4 + SolidStride + 4 + QuadStride;
        var buf = new byte[totalSize];
        int pos = 0;

        // solidCount u32le = 1. spec: terrain.md §11.1 — "solidCount u32le @ offset 0: CONFIRMED".
        WriteU32(buf, pos, 1); pos += 4;

        // SolidRecord 108 bytes.
        // AABB +0..+15 (VERIFIED). spec: terrain.md §11.2.
        WriteF32(buf, pos + 0, aabbXMin);   // aabb_xmin f32 @ +000: VERIFIED
        WriteF32(buf, pos + 4, aabbZMin);   // aabb_zmin f32 @ +004: VERIFIED
        WriteF32(buf, pos + 8, aabbXMax);   // aabb_xmax f32 @ +008: VERIFIED
        WriteF32(buf, pos + 12, aabbZMax);  // aabb_zmax f32 @ +012: VERIFIED
        // +016..+059: _reserved_a — all zero (spec: terrain.md §11.2 — all-zero VERIFIED).
        // +060: quad_count_embedded u32 — 1 (matches stream count). spec: §11.2 — VERIFIED.
        WriteU32(buf, pos + 60, 1);
        // +064: _authoring_ptr — stale pointer, leave 0. spec: §11.2 — VERIFIED.
        // +068..+107: _reserved_b — all zero. spec: §11.2 — VERIFIED.
        // Note: +036 is PADDING per spec (VFS-DEEP-II correction 2026-06-14).
        // spec: Docs/RE/formats/terrain.md §11.2 — "_reserved_a +016..+059 all-zero: VERIFIED".
        pos += SolidStride;

        // quadCount u32le = 1. spec: terrain.md §11.1 — "quadCount u32le (stream copy) after flat SolidRecord array: CONFIRMED".
        WriteU32(buf, pos, 1); pos += 4;

        // QuadRecord 48 bytes. Four XZ corners +0..+31 (VERIFIED).
        // spec: terrain.md §11.3 — four XZ corners VERIFIED; trailing scalars +032..+047 VERIFIED NOT READ.
        WriteF32(buf, pos + 0, qX0);            // x0 f32 @ +000: VERIFIED
        WriteF32(buf, pos + 4, qZ0);            // z0 f32 @ +004: VERIFIED
        WriteF32(buf, pos + 8, qX1);            // x1 f32 @ +008: VERIFIED
        WriteF32(buf, pos + 12, qZ1);           // z1 f32 @ +012: VERIFIED
        WriteF32(buf, pos + 16, qX2);           // x2 f32 @ +016: VERIFIED
        WriteF32(buf, pos + 20, qZ2);           // z2 f32 @ +020: VERIFIED
        WriteF32(buf, pos + 24, qX3);           // x3 f32 @ +024: VERIFIED
        WriteF32(buf, pos + 28, qZ3);           // z3 f32 @ +028: VERIFIED
        // Dead 2D edge-line cache +032..+047. spec: terrain.md §11.3 — VERIFIED NOT READ.
        WriteF32(buf, pos + 32, edgeSlope);     // edge_slope @ +032: VERIFIED NOT READ
        WriteF32(buf, pos + 36, edgePad0);      // edge_pad0 @ +036: VERIFIED (always 0)
        WriteF32(buf, pos + 40, edgeIntercept); // edge_intercept @ +040: VERIFIED NOT READ
        WriteF32(buf, pos + 44, edgePad1);      // edge_pad1 @ +044: VERIFIED (always 0)

        return buf;
    }

    /// <summary>
    /// Builds a .sod file with solidCount = 0 (no solids, no quads).
    /// spec: terrain.md §11.1 — solidCount=0 is a valid empty file.
    /// </summary>
    private static byte[] BuildEmptySod()
    {
        var buf = new byte[4];
        // solidCount u32le = 0.
        WriteU32(buf, 0, 0);
        return buf;
    }

    // ── Empty file tests ──────────────────────────────────────────────────────

    [Fact]
    public void Parse_ZeroSolids_ReturnsEmptyBlob()
    {
        // A .sod with solidCount=0 must parse cleanly with no solids or quads.
        // spec: terrain.md §11.1 — solidCount=0 is valid.
        byte[] buf = BuildEmptySod();
        SodBlob blob = SodBlobParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(0u, blob.SolidCount);
        Assert.Empty(blob.Solids);
    }

    // ── solidCount tests ──────────────────────────────────────────────────────

    [Fact]
    public void Parse_OneSolid_SolidCountOne()
    {
        // spec: terrain.md §11.1 — "solidCount u32le @ offset 0: CONFIRMED".
        byte[] buf = BuildMinimalSod(0f, 0f, 100f, 100f);
        SodBlob blob = SodBlobParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(1u, blob.SolidCount);
        Assert.Single(blob.Solids);
    }

    // ── SolidRecord AABB tests ────────────────────────────────────────────────

    [Fact]
    public void Parse_SolidRecord_AabbXMin_RoundTrip()
    {
        // spec: terrain.md §11.2 — "aabb_xmin f32 @ +000: VERIFIED".
        byte[] buf = BuildMinimalSod(aabbXMin: -512f, aabbZMin: 0f, aabbXMax: 512f, aabbZMax: 100f);
        SodBlob blob = SodBlobParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(-512f, blob.Solids[0].AabbXMin, precision: 5);
    }

    [Fact]
    public void Parse_SolidRecord_AabbZMin_RoundTrip()
    {
        // spec: terrain.md §11.2 — "aabb_zmin f32 @ +004: VERIFIED".
        byte[] buf = BuildMinimalSod(aabbXMin: 0f, aabbZMin: -1024f, aabbXMax: 100f, aabbZMax: 1024f);
        SodBlob blob = SodBlobParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(-1024f, blob.Solids[0].AabbZMin, precision: 5);
    }

    [Fact]
    public void Parse_SolidRecord_AabbXMax_RoundTrip()
    {
        // spec: terrain.md §11.2 — "aabb_xmax f32 @ +008: VERIFIED".
        byte[] buf = BuildMinimalSod(aabbXMin: 0f, aabbZMin: 0f, aabbXMax: 888f, aabbZMax: 100f);
        SodBlob blob = SodBlobParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(888f, blob.Solids[0].AabbXMax, precision: 5);
    }

    [Fact]
    public void Parse_SolidRecord_AabbZMax_RoundTrip()
    {
        // spec: terrain.md §11.2 — "aabb_zmax f32 @ +012: VERIFIED".
        byte[] buf = BuildMinimalSod(aabbXMin: 0f, aabbZMin: 0f, aabbXMax: 100f, aabbZMax: 777f);
        SodBlob blob = SodBlobParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(777f, blob.Solids[0].AabbZMax, precision: 5);
    }

    // ── QuadRecord corner tests ───────────────────────────────────────────────

    [Fact]
    public void Parse_QuadCorners_RoundTrip()
    {
        // spec: terrain.md §11.3 — "x0/z0..x3/z3 f32 @ +000..+028: VERIFIED".
        byte[] buf = BuildMinimalSod(
            aabbXMin: 0f, aabbZMin: 0f, aabbXMax: 50f, aabbZMax: 50f,
            qX0: 1.1f, qZ0: 2.2f,
            qX1: 3.3f, qZ1: 4.4f,
            qX2: 5.5f, qZ2: 6.6f,
            qX3: 7.7f, qZ3: 8.8f);

        SodBlob blob = SodBlobParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Single(blob.Solids[0].Quads);
        CollisionQuad q = blob.Solids[0].Quads[0];
        Assert.Equal(1.1f, q.X0, precision: 5);
        Assert.Equal(2.2f, q.Z0, precision: 5);
        Assert.Equal(3.3f, q.X1, precision: 5);
        Assert.Equal(4.4f, q.Z1, precision: 5);
        Assert.Equal(5.5f, q.X2, precision: 5);
        Assert.Equal(6.6f, q.Z2, precision: 5);
        Assert.Equal(7.7f, q.X3, precision: 5);
        Assert.Equal(8.8f, q.Z3, precision: 5);
    }

    [Fact]
    public void Parse_DeadEdgeCache_IsRetained()
    {
        // The dead 2D edge-line cache (+032..+047) must be decoded and stored in the model,
        // even though no runtime code reads them.
        // spec: terrain.md §11.3 — edge_slope/edge_pad0/edge_intercept/edge_pad1: VERIFIED NOT READ.
        // NOTE: +036 is confirmed padding (always 0), not a meaningful field.
        // spec: terrain.md §11.3 — "edge_pad0 f32 @ +036: VERIFIED (always 0)".
        byte[] buf = BuildMinimalSod(
            aabbXMin: 0f, aabbZMin: 0f, aabbXMax: 10f, aabbZMax: 10f,
            edgeSlope: 1.5f, edgePad0: 0f, edgeIntercept: 2.5f, edgePad1: 0f);

        SodBlob blob = SodBlobParser.Parse(new ReadOnlyMemory<byte>(buf));
        CollisionQuad q = blob.Solids[0].Quads[0];

        // Spec says these are NOT read at runtime; we only assert decode fidelity.
        Assert.Equal(1.5f, q.EdgeSlope, precision: 5);
        Assert.Equal(0f, q.EdgePad0, precision: 5);     // always 0 per spec
        Assert.Equal(2.5f, q.EdgeIntercept, precision: 5);
        Assert.Equal(0f, q.EdgePad1, precision: 5);     // always 0 per spec
    }

    // ── Raw record backward-compat tests ─────────────────────────────────────

    [Fact]
    public void Parse_RawSolidRecord_Is108Bytes()
    {
        // RawRecord must be exactly 108 bytes (one full SolidRecord stride).
        // spec: terrain.md §11.2 — "stride 108 bytes (0x6C): CONFIRMED".
        byte[] buf = BuildMinimalSod(0f, 0f, 100f, 100f);
        SodBlob blob = SodBlobParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(108, blob.Solids[0].RawRecord.Length);
        Assert.Equal(108, blob.RawSolidRecords[0].Length);
    }

    [Fact]
    public void Parse_RawTriangleData_Is48BytesPerQuad()
    {
        // RawTriangleData for one quad must be exactly 48 bytes (one QuadRecord stride).
        // spec: terrain.md §11.3 — "stride 48 bytes (0x30): CONFIRMED".
        byte[] buf = BuildMinimalSod(0f, 0f, 100f, 100f);
        SodBlob blob = SodBlobParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(48, blob.RawTriangleData[0].Length);
    }

    // ── Truncation / error tests ──────────────────────────────────────────────

    [Fact]
    public void Parse_TruncatedAfterSolidCount_Throws()
    {
        // A 4-byte buffer declaring solidCount=1 but providing no solid data must throw.
        // spec: terrain.md §11.1 — parser throws InvalidDataException on truncation.
        var buf = new byte[4];
        WriteU32(buf, 0, 1); // solidCount = 1 but no solid data follows

        Assert.Throws<InvalidDataException>(() =>
            SodBlobParser.Parse(new ReadOnlyMemory<byte>(buf)));
    }

    [Fact]
    public void Parse_TruncatedQuadData_Throws()
    {
        // Build a valid solid header but truncate the QuadRecord to 10 bytes.
        // spec: terrain.md §11.3 — parser throws InvalidDataException when quad data is truncated.
        int partialSize = 4 + SolidStride + 4 + 10; // partial quad
        var buf = new byte[partialSize];
        WriteU32(buf, 0, 1); // solidCount=1
        // SolidRecord fields are zero (no AABB needed to trigger the truncation throw)
        WriteU32(buf, 4 + 60, 1); // quad_count_embedded
        WriteU32(buf, 4 + SolidStride, 1); // quadCount stream copy = 1

        Assert.Throws<InvalidDataException>(() =>
            SodBlobParser.Parse(new ReadOnlyMemory<byte>(buf)));
    }
}
