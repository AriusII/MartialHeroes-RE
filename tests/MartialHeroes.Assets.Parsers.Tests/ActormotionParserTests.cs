using System.Globalization;
using Xunit;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// In-memory fixture tests for <see cref="ActormotionParser"/> / <see cref="ActormotionCatalogue"/>.
/// All fixture rows are hand-built to match the 33-column tab-delimited layout described in the spec.
/// spec: Docs/RE/formats/actormotion.md — full record layout.
/// </summary>
public sealed class ActormotionParserTests
{
    // ----------------------------------------------------------------
    // Fixture helpers
    // ----------------------------------------------------------------

    /// <summary>
    /// Builds a minimal valid 33-column tab-delimited actormotion row.
    /// Column assignments follow the spec exactly.
    /// spec: Docs/RE/formats/actormotion.md §Per-record layout
    /// </summary>
    /// <param name="col0">Category selector (key input).</param>
    /// <param name="col1">Intra-category offset / mob_id (key input).</param>
    /// <param name="col2">int_a → record 0x04.</param>
    /// <param name="col3">rate_src_x → record 0x08 (f32).</param>
    /// <param name="col4">divisor_x → record 0x28 (i32, interleaved early).</param>
    /// <param name="col5">rate_src_y → record 0x0C (f32).</param>
    /// <param name="col6">int_b → record 0x10.</param>
    /// <param name="col14">divisor_y (paired) → record 0x2C.</param>
    /// <param name="dir1">9 values for dir_array_1 → record 0x40.</param>
    /// <param name="dir2">9 values for dir_array_2 → record 0x64.</param>
    private static string MakeRow(
        int col0, int col1,
        int col2,
        float col3, int col4, float col5,
        int col6,
        float col7 = 0f, float col8 = 0f, float col9 = 0f,
        float col10 = 0f, float col11 = 0f,
        float col12 = 0f, float col13 = 0f,
        int col14 = 1,
        int[]? dir1 = null, int[]? dir2 = null)
    {
        // spec: Docs/RE/formats/actormotion.md — 33 columns, tab-delimited.
        var parts = new string[33];
        parts[0] = col0.ToString(CultureInfo.InvariantCulture);
        parts[1] = col1.ToString(CultureInfo.InvariantCulture);
        parts[2] = col2.ToString(CultureInfo.InvariantCulture);
        parts[3] = col3.ToString("G", CultureInfo.InvariantCulture);
        parts[4] = col4.ToString(CultureInfo.InvariantCulture);
        parts[5] = col5.ToString("G", CultureInfo.InvariantCulture);
        parts[6] = col6.ToString(CultureInfo.InvariantCulture);
        parts[7] = col7.ToString("G", CultureInfo.InvariantCulture);
        parts[8] = col8.ToString("G", CultureInfo.InvariantCulture);
        parts[9] = col9.ToString("G", CultureInfo.InvariantCulture);
        parts[10] = col10.ToString("G", CultureInfo.InvariantCulture);
        parts[11] = col11.ToString("G", CultureInfo.InvariantCulture);
        parts[12] = col12.ToString("G", CultureInfo.InvariantCulture);
        parts[13] = col13.ToString("G", CultureInfo.InvariantCulture);
        parts[14] = col14.ToString(CultureInfo.InvariantCulture);
        for (int d = 0; d < 9; d++)
            parts[15 + d] = (dir1 != null && d < dir1.Length ? dir1[d] : 0).ToString(CultureInfo.InvariantCulture);
        for (int d = 0; d < 9; d++)
            parts[24 + d] = (dir2 != null && d < dir2.Length ? dir2[d] : 0).ToString(CultureInfo.InvariantCulture);
        return string.Join('\t', parts);
    }

    private static string MakeFile(params string[] rows)
        => rows.Length + "\n" + string.Join("\n", rows) + "\n";

    // ----------------------------------------------------------------
    // Tests
    // ----------------------------------------------------------------

    [Fact]
    public void Parses_declared_count_and_single_record()
    {
        // spec: Docs/RE/formats/actormotion.md §File structure — leading integer count.
        string txt = MakeFile(MakeRow(col0: 0, col1: 1, col2: 42, col3: 7.402f, col4: 16, col5: 16.282f, col6: 11));
        var cat = ActormotionParser.ParseText(txt);
        Assert.Equal(1, cat.Count);
    }

    [Fact]
    public void Motion_key_without_base_table_equals_col1()
    {
        // Without a base table, motion_key = col1 + 0 = col1.
        // spec: Docs/RE/formats/actormotion.md §Computed lookup key — base contribution = 0 when table absent.
        string txt = MakeFile(MakeRow(col0: 0, col1: 7, col2: 1, col3: 1f, col4: 1, col5: 1f, col6: 1));
        var cat = ActormotionParser.ParseText(txt);
        var entry = cat.GetByMotionKey(7);
        Assert.NotNull(entry);
        Assert.Equal(7u, entry.MotionKey);
        Assert.Equal(7, entry.Col1RawOffset);
    }

    [Fact]
    public void Motion_key_with_base_table_adds_base_contribution()
    {
        // motion_key = col1 + base_table[(uint8)(col0 + 1)]
        // spec: Docs/RE/formats/actormotion.md §Computed lookup key
        // col0=2 → index = (byte)(2+1) = 3; base_table[3] = 100; col1=5 → key = 105
        uint[] baseTable = [0, 0, 0, 100, 0]; // index 3 = 100
        string txt = MakeFile(MakeRow(col0: 2, col1: 5, col2: 1, col3: 1f, col4: 1, col5: 1f, col6: 1));
        var cat = ActormotionParser.ParseText(txt, baseTable);
        var entry = cat.GetByMotionKey(105);
        Assert.NotNull(entry);
        Assert.Equal(105u, entry.MotionKey);
    }

    [Fact]
    public void Rate_fields_computed_with_15fps_base()
    {
        // rate_x = 15.0 * rate_src_x / divisor_x
        // rate_y = 15.0 * rate_src_y / divisor_y
        // spec: Docs/RE/formats/actormotion.md §Per-frame rate fields (0x30, 0x34) — CONFIRMED math.
        // RateSrcX=7.402, DivisorX=16, RateSrcY=16.282, DivisorY=1
        string txt = MakeFile(MakeRow(col0: 0, col1: 1, col2: 1, col3: 7.402f, col4: 16, col5: 16.282f, col6: 11,
            col14: 1));
        var cat = ActormotionParser.ParseText(txt);
        var e = cat.GetByMotionKey(1);
        Assert.NotNull(e);
        Assert.Equal(7.402f, e.RateSrcX, precision: 3);
        Assert.Equal(16.282f, e.RateSrcY, precision: 3);
        Assert.Equal(16, e.DivisorX);
        Assert.Equal(1, e.DivisorY);
        // rate_x = 15.0 * 7.402 / 16 ≈ 6.939
        Assert.Equal(15.0f * 7.402f / 16.0f, e.RateX, precision: 3);
        // rate_y = 15.0 * 16.282 / 1 = 244.23
        Assert.Equal(15.0f * 16.282f / 1.0f, e.RateY, precision: 3);
    }

    [Fact]
    public void Divisor_zero_is_forced_to_one()
    {
        // spec: Docs/RE/formats/actormotion.md — divisor_x / divisor_y forced to 1 if read as 0.
        string txt = MakeFile(MakeRow(col0: 0, col1: 1, col2: 1, col3: 5f, col4: 0, col5: 3f, col6: 0, col14: 0));
        var cat = ActormotionParser.ParseText(txt);
        var e = cat.GetByMotionKey(1);
        Assert.NotNull(e);
        Assert.Equal(1, e.DivisorX); // forced from 0 to 1
        Assert.Equal(1, e.DivisorY); // forced from 0 to 1
        // Rates should not be NaN/Inf
        Assert.Equal(15.0f * 5f / 1f, e.RateX);
        Assert.Equal(15.0f * 3f / 1f, e.RateY);
    }

    [Fact]
    public void Directional_arrays_round_trip()
    {
        // spec: Docs/RE/formats/actormotion.md §The two 9-element directional sub-arrays (0x40, 0x64).
        // 9 directions: 8 compass + 1 neutral = 9 elements each.
        int[] d1 = [101100001, 111100010, 111100020, 111100030, 121100060, 121100090, 121100010, 0, 0];
        int[] d2 = [811100001, 811100002, 821100005, 0, 0, 0, 0, 0, 0];
        string txt = MakeFile(MakeRow(col0: 0, col1: 1, col2: 1, col3: 7.402f, col4: 16, col5: 16.282f, col6: 11,
            dir1: d1, dir2: d2));
        var cat = ActormotionParser.ParseText(txt);
        var e = cat.GetByMotionKey(1);
        Assert.NotNull(e);
        Assert.Equal(9, e.DirArray1.Length); // spec: Docs/RE/formats/actormotion.md — DirArrayCount = 9
        Assert.Equal(9, e.DirArray2.Length);
        Assert.Equal(101100001, e.DirArray1[0]);
        Assert.Equal(111100010, e.DirArray1[1]);
        Assert.Equal(121100010, e.DirArray1[6]);
        Assert.Equal(0, e.DirArray1[7]); // last 2 are zero
        Assert.Equal(0, e.DirArray1[8]);
        Assert.Equal(811100001, e.DirArray2[0]);
        Assert.Equal(821100005, e.DirArray2[2]);
        Assert.Equal(0, e.DirArray2[3]);
    }

    [Fact]
    public void Secondary_index_by_intra_offset_resolves_mob_id()
    {
        // spec: Docs/RE/formats/actormotion.md — col1 = mob_id for mob/NPC entries.
        string txt = MakeFile(
            MakeRow(col0: 0, col1: 11, col2: 3, col3: 7f, col4: 16, col5: 7f, col6: 0),
            MakeRow(col0: 0, col1: 12, col2: 5, col3: 7f, col4: 16, col5: 7f, col6: 0));
        var cat = ActormotionParser.ParseText(txt);
        Assert.Equal(2, cat.Count);

        var e11 = cat.GetByIntraOffset(11);
        var e12 = cat.GetByIntraOffset(12);
        Assert.NotNull(e11);
        Assert.NotNull(e12);
        Assert.Equal(3, e11!.IntA);
        Assert.Equal(5, e12!.IntA);
    }

    [Fact]
    public void Short_rows_are_skipped_not_thrown()
    {
        // Lines with fewer than 33 columns must be skipped silently.
        // spec: Docs/RE/formats/actormotion.md §File structure — fixed column count.
        string txt = "2\n0\t1\t1\t7\t16\t16\t11\n" // only 7 cols — too short, skipped
                     + MakeRow(col0: 0, col1: 2, col2: 99, col3: 1f, col4: 1, col5: 1f, col6: 0) + "\n";
        var cat = ActormotionParser.ParseText(txt);
        Assert.Equal(1, cat.Count);
        var e = cat.GetByMotionKey(2);
        Assert.NotNull(e);
        Assert.Equal(99, e.IntA);
    }

    [Fact]
    public void Blank_and_crlf_lines_are_tolerated()
    {
        // Empty lines and CRLF endings must not crash the parser.
        string txt = "1\r\n"
                     + MakeRow(col0: 0, col1: 3, col2: 7, col3: 1f, col4: 2, col5: 1f, col6: 0) + "\r\n"
                     + "\r\n";
        var cat = ActormotionParser.ParseText(txt);
        Assert.Equal(1, cat.Count);
    }

    [Fact]
    public void Multiple_records_are_all_parsed()
    {
        // Parse three records, verify all are reachable by motion_key.
        string txt = MakeFile(
            MakeRow(col0: 0, col1: 1, col2: 10, col3: 1f, col4: 1, col5: 1f, col6: 0),
            MakeRow(col0: 0, col1: 2, col2: 20, col3: 1f, col4: 1, col5: 1f, col6: 0),
            MakeRow(col0: 0, col1: 3, col2: 30, col3: 1f, col4: 1, col5: 1f, col6: 0));
        var cat = ActormotionParser.ParseText(txt);
        Assert.Equal(3, cat.Count);
        Assert.Equal(10, cat.GetByMotionKey(1)!.IntA);
        Assert.Equal(20, cat.GetByMotionKey(2)!.IntA);
        Assert.Equal(30, cat.GetByMotionKey(3)!.IntA);
    }

    [Fact]
    public void Duplicate_motion_key_first_occurrence_wins()
    {
        // When two records compute the same motion_key, the first one is kept.
        // spec: Docs/RE/formats/actormotion.md §File structure — insertion into ordered map.
        string txt = MakeFile(
            MakeRow(col0: 0, col1: 5, col2: 111, col3: 1f, col4: 1, col5: 1f, col6: 0),
            MakeRow(col0: 0, col1: 5, col2: 222, col3: 1f, col4: 1, col5: 1f, col6: 0));
        var cat = ActormotionParser.ParseText(txt);
        Assert.Equal(1, cat.Count);
        Assert.Equal(111, cat.GetByMotionKey(5)!.IntA);
    }

    [Fact]
    public void Unknown_key_returns_null()
    {
        var cat = ActormotionParser.ParseText(MakeFile(
            MakeRow(col0: 0, col1: 1, col2: 1, col3: 1f, col4: 1, col5: 1f, col6: 0)));
        Assert.Null(cat.GetByMotionKey(999));
        Assert.Null(cat.GetByIntraOffset(999));
    }

    [Fact]
    public void Raw_bytes_overload_decodes_via_cp949()
    {
        // Smoke-test the ReadOnlyMemory<byte> path: encode a minimal table as CP949 and parse it.
        // spec: Docs/RE/formats/actormotion.md §Identification — encoding CP949 (Korean).
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var cp949 = System.Text.Encoding.GetEncoding(949);
        string txt = MakeFile(MakeRow(col0: 0, col1: 42, col2: 7, col3: 1f, col4: 1, col5: 1f, col6: 0));
        byte[] bytes = cp949.GetBytes(txt);
        var cat = ActormotionParser.Parse(bytes.AsMemory());
        Assert.Equal(1, cat.Count);
        Assert.Equal(42, cat.GetByMotionKey(42)!.Col1RawOffset);
    }

    [Fact]
    public void Observed_first_row_values_match_vfs_preview()
    {
        // Validates the column mapping against the first real data row observed from
        // the VFS head preview (vfs-inspect --head data/char/actormotion.txt):
        // col0=0 col1=1 col2=1 col3=7.402 col4=16 col5=16.282 col6=11 col14=1
        // dir_array_1=[101100001,111100010,111100020,111100030,121100060,121100090,121100010,0,0]
        // dir_array_2=[811100001,811100002,821100005,0,0,0,0,0,0]
        // No base table => motion_key = 1.
        // spec: Docs/RE/formats/actormotion.md — all field positions.
        int[] d1 = [101100001, 111100010, 111100020, 111100030, 121100060, 121100090, 121100010, 0, 0];
        int[] d2 = [811100001, 811100002, 821100005, 0, 0, 0, 0, 0, 0];
        string txt = MakeFile(MakeRow(col0: 0, col1: 1, col2: 1, col3: 7.402f, col4: 16, col5: 16.282f,
            col6: 11, col7: 0f, col8: 4f, col9: 5f, col10: 3f, col11: 1f,
            col12: 8f, col13: 4f, col14: 1, dir1: d1, dir2: d2));
        var cat = ActormotionParser.ParseText(txt);
        var e = cat.GetByMotionKey(1);
        Assert.NotNull(e);
        Assert.Equal(0, e.Col0Category);
        Assert.Equal(1, e.Col1RawOffset);
        Assert.Equal(1, e.IntA);
        Assert.Equal(7.402f, e.RateSrcX, precision: 3);
        Assert.Equal(16, e.DivisorX);
        Assert.Equal(16.282f, e.RateSrcY, precision: 3);
        Assert.Equal(11, e.IntB);
        Assert.Equal(0f, e.FloatC);
        Assert.Equal(4f, e.FloatD);
        Assert.Equal(5f, e.FloatE);
        Assert.Equal(3f, e.FloatF);
        Assert.Equal(1f, e.FloatG);
        Assert.Equal(8f, e.FloatH);
        Assert.Equal(4f, e.FloatI);
        Assert.Equal(1, e.DivisorY);
        Assert.Equal(101100001, e.DirArray1[0]);
        Assert.Equal(811100001, e.DirArray2[0]);
    }
}