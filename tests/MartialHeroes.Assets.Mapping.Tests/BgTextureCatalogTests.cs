using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Mapping;
using Xunit;

namespace MartialHeroes.Assets.Mapping.Tests;

/// <summary>
/// Fixture-based tests for <see cref="BgTextureCatalog"/> — the runtime, <c>.lst</c>-backed
/// terrain/effect texture pool. All buffers are built in-memory; no real game file is required.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/bgtexture_lst.md (binary layout + cross-file join — IDA-corrected 263bd994:
///       the <c>.map</c> intTexId is the 0-based pool slot used DIRECTLY, NO -1; pool accessor reads
///       <c>pool[0]+stride*intTexId</c> at IDA 0x445833 / 0x44a46d / store 0x44b267),
///       Docs/RE/specs/asset_pipeline.md §3 chain B (runtime path + index-keyed pool).
/// </remarks>
public sealed class BgTextureCatalogTests
{
    // ─── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a syntactically correct bgtexture.lst buffer with the given records.
    /// Layout: u32LE record_count @ 0, then record_count × 48-byte records;
    /// each record = u8 kind @ +0 + char[47] relpath (NUL-terminated, zero-padded) @ +1.
    /// spec: Docs/RE/formats/bgtexture_lst.md §Header layout + §Record / body layout: CONFIRMED.
    /// </summary>
    private static byte[] BuildLst(params (byte kind, string relPath)[] entries)
    {
        int count = entries.Length;
        byte[] buf = new byte[4 + count * 48];

        // spec: Docs/RE/formats/bgtexture_lst.md §Header layout — record_count u32LE @ 0: CONFIRMED.
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), (uint)count);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);

        for (int i = 0; i < count; i++)
        {
            int recBase = 4 + i * 48;
            // spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — kind u8 @ +0: CONFIRMED.
            buf[recBase] = entries[i].kind;

            // spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout —
            //   rel_path char[47] @ +1, NUL-terminated, zero-padded: CONFIRMED.
            byte[] pathBytes = cp949.GetBytes(entries[i].relPath);
            int copyLen = Math.Min(pathBytes.Length, 46); // leave at least one NUL byte
            pathBytes.AsSpan(0, copyLen).CopyTo(buf.AsSpan(recBase + 1, 47));
        }

        return buf;
    }

    // =========================================================================
    // 1. Index-keyed resolution: intTexId is the 0-based pool slot, used DIRECTLY (NO -1).
    //    spec (IDA-corrected 263bd994): the pool accessor reads pool[0]+stride*intTexId — the only -1
    //    in the terrain chain is on the .ted byte (perCellTexList[byte-1]), NOT on the intTexId.
    // =========================================================================

    [Fact]
    public void FromLst_ResolveRelativePath_IntTexId_IsDirectZeroBasedSlot()
    {
        // spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join (IDA 0x445833 / 0x44a46d) —
        //   the .map intTexId IS the 0-based .lst record index, used directly with NO subtraction.
        byte[] lst = BuildLst(
            (0x01, "terrain/first"),
            (0x01, "terrain/second"),
            (0x02, "water/river"));

        var cat = BgTextureCatalog.FromLst(new ReadOnlyMemory<byte>(lst));

        // intTexId 0 → slot 0, intTexId 2 → slot 2 (direct, no -1).
        Assert.Equal("terrain/first", cat.ResolveRelativePath(0));
        Assert.Equal("terrain/second", cat.ResolveRelativePath(1));
        Assert.Equal("water/river", cat.ResolveRelativePath(2));
    }

    [Fact]
    public void FromLst_SlotCount_EqualsRecordCount()
    {
        // spec: Docs/RE/formats/bgtexture_lst.md §Header layout — every record occupies a slot;
        //   the pool size equals record_count: CONFIRMED.
        byte[] lst = BuildLst(
            (0x01, "a"),
            (0x01, "b"),
            (0x01, "c"));

        var cat = BgTextureCatalog.FromLst(new ReadOnlyMemory<byte>(lst));

        Assert.Equal(3, cat.SlotCount);
    }

    [Fact]
    public void FromLst_ResolveRelativePath_OutOfRange_ReturnsNull()
    {
        byte[] lst = BuildLst((0x01, "terrain/only"));
        var cat = BgTextureCatalog.FromLst(new ReadOnlyMemory<byte>(lst));

        // Slot 0 is the only valid slot (direct). Negative and >=count are out of range.
        Assert.Equal("terrain/only", cat.ResolveRelativePath(0));
        Assert.Null(cat.ResolveRelativePath(-1));  // negative slot
        Assert.Null(cat.ResolveRelativePath(1));   // beyond the single record (slot 1)
        Assert.Null(cat.ResolveRelativePath(999));
    }

    // =========================================================================
    // 2. Full .dds path construction (prefix + rel + .dds, added at runtime)
    // =========================================================================

    [Fact]
    public void FromLst_ResolveTexturePath_TerrainPrefixAndDdsExtension()
    {
        // spec: Docs/RE/specs/asset_pipeline.md §3 chain B — runtime path
        //   "data/map000/texture/<rel>.dds" (prefix + extension added at runtime): CONFIRMED.
        byte[] lst = BuildLst((0x01, "terrain/g3"));
        var cat = BgTextureCatalog.FromLst(new ReadOnlyMemory<byte>(lst));

        Assert.Equal(
            "data/map000/texture/terrain/g3.dds",
            cat.ResolveTexturePath(0));
        Assert.Equal("data/map000/texture/", BgTextureCatalog.TerrainTextureDir);
    }

    [Fact]
    public void FromLst_ResolveTexturePath_EffectPrefix()
    {
        // spec: Docs/RE/formats/bgtexture_lst.md §Identification — the effect instance resolves
        //   under "data/effect/texture/": CONFIRMED.
        byte[] lst = BuildLst((0x01, "spark/_glow"));
        var cat = BgTextureCatalog.FromLst(new ReadOnlyMemory<byte>(lst));

        Assert.Equal(
            "data/effect/texture/spark/_glow.dds",
            cat.ResolveTexturePath(0, BgTextureCatalog.EffectTextureDir));
        Assert.Equal("data/effect/texture/", BgTextureCatalog.EffectTextureDir);
    }

    [Fact]
    public void FromLst_ResolveTexturePath_OutOfRange_ReturnsNull()
    {
        byte[] lst = BuildLst((0x01, "terrain/only"));
        var cat = BgTextureCatalog.FromLst(new ReadOnlyMemory<byte>(lst));

        Assert.Null(cat.ResolveTexturePath(1)); // only slot 0 exists
    }

    // =========================================================================
    // 3. kind == 0 → slot skipped (no element built) but the index is preserved
    // =========================================================================

    [Fact]
    public void FromLst_KindZeroRecord_LeavesSlotEmpty_ButPreservesLaterIndices()
    {
        // spec: Docs/RE/specs/asset_pipeline.md §3 chain B —
        //   "Kind selector: … 0 ⇒ slot skipped (no element built)". The loader steps one pool
        //   element per record regardless of kind, so a kind-0 record holds its slot index and
        //   later records keep their positions: CONFIRMED.
        byte[] lst = BuildLst(
            (0x01, "terrain/before"),
            (0x00, "ignored/skipped"), // kind 0: no element built → empty slot, but slot 1 reserved
            (0x01, "terrain/after"));

        var cat = BgTextureCatalog.FromLst(new ReadOnlyMemory<byte>(lst));

        Assert.Equal(3, cat.SlotCount); // every record occupies a slot
        Assert.Equal("terrain/before", cat.ResolveRelativePath(0)); // slot 0
        Assert.Null(cat.ResolveRelativePath(1)); // slot 1 = kind-0 → empty
        Assert.Equal("terrain/after", cat.ResolveRelativePath(2)); // slot 2 NOT shifted up
    }

    // =========================================================================
    // 4. kind byte does not change the resolved relpath (render-mode tag only)
    // =========================================================================

    [Theory]
    [InlineData((byte)0x01)] // KIND_STATIC
    [InlineData((byte)0x02)] // KIND_SCROLL
    [InlineData((byte)0x0A)] // KIND_GRASS
    [InlineData((byte)0x14)] // KIND_FOLIAGE
    public void FromLst_NonZeroKind_AlwaysResolves(byte kind)
    {
        // spec: Docs/RE/formats/bgtexture_lst.md §Enumerations — the kind byte selects a
        //   render/pool path (== 0x01 vs != 0x01) but does NOT change the slot index or relpath;
        //   any non-zero kind builds an element: CONFIRMED.
        byte[] lst = BuildLst((kind, "terrain/tile"));
        var cat = BgTextureCatalog.FromLst(new ReadOnlyMemory<byte>(lst));

        Assert.Equal("terrain/tile", cat.ResolveRelativePath(0));
    }

    // =========================================================================
    // 5. NUL-terminated relpath within the 47-byte field
    // =========================================================================

    [Fact]
    public void FromLst_RelPath_StopsAtFirstNul()
    {
        // spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout —
        //   "null-terminated, zero-padded to the full 47 bytes": CONFIRMED.
        byte[] lst = BuildLst((0x01, "short"));
        var cat = BgTextureCatalog.FromLst(new ReadOnlyMemory<byte>(lst));

        Assert.Equal("short", cat.ResolveRelativePath(0));
    }

    // =========================================================================
    // 6. Malformed .lst surfaces the parser's InvalidDataException
    // =========================================================================

    [Fact]
    public void FromLst_Truncated_ThrowsInvalidDataException()
    {
        // Header claims 10 records but only one record's worth of body follows.
        // spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — size formula enforced.
        byte[] buf = new byte[4 + 48];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), 10u);

        Assert.Throws<InvalidDataException>(() => BgTextureCatalog.FromLst(new ReadOnlyMemory<byte>(buf)));
    }

    // =========================================================================
    // 7. .txt mirror fallback — same DIRECT 0-based index API (dev / loose-tree only)
    // =========================================================================

    [Fact]
    public void FromTxt_TextMirror_ResolvesByZeroBasedIndexColumn()
    {
        // spec: Docs/RE/formats/bgtexture_lst.md §Text mirror — TAB-separated, no header,
        //   col0 = 0-based record index (== the direct pool slot), col1 = kind, col2 = relpath (no .dds).
        // Row indices intentionally out of order to prove col0 (not line position) keys the slot.
        const string txt = "2\t1\tterrain/c\r\n0\t1\tterrain/a\r\n1\t2\twater/b\r\n";
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        byte[] bytes = Encoding.GetEncoding(949).GetBytes(txt);

        var cat = BgTextureCatalog.FromTxt(new ReadOnlyMemory<byte>(bytes));

        Assert.Equal(3, cat.SlotCount);
        // intTexId 0 → slot 0 (col0 == 0), etc. (direct, no -1).
        Assert.Equal("terrain/a", cat.ResolveRelativePath(0));
        Assert.Equal("water/b", cat.ResolveRelativePath(1));
        Assert.Equal("terrain/c", cat.ResolveRelativePath(2));
        Assert.Equal(
            "data/map000/texture/terrain/a.dds",
            cat.ResolveTexturePath(0));
    }

    [Fact]
    public void FromTxt_TextMirror_GapIndices_ResolveNull()
    {
        // A mirror that skips index 1 leaves that slot empty; the array is sized to maxIndex+1.
        // spec: Docs/RE/formats/bgtexture_lst.md §Text mirror — col0 is the 0-based slot index.
        const string txt = "0\t1\tterrain/a\r\n2\t1\tterrain/c\r\n";
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        byte[] bytes = Encoding.GetEncoding(949).GetBytes(txt);

        var cat = BgTextureCatalog.FromTxt(new ReadOnlyMemory<byte>(bytes));

        Assert.Equal(3, cat.SlotCount);
        Assert.Equal("terrain/a", cat.ResolveRelativePath(0)); // slot 0
        Assert.Null(cat.ResolveRelativePath(1)); // slot 1 absent
        Assert.Equal("terrain/c", cat.ResolveRelativePath(2)); // slot 2
    }
}
