using System.Globalization;
using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parses <c>data/char/actormotion.txt</c> into a typed <see cref="ActormotionCatalogue"/>.
/// </summary>
/// <remarks>
/// <para>
/// spec: Docs/RE/formats/actormotion.md — full format description.
/// </para>
/// <para>
/// The file is a whitespace-delimited (tab-separated) text table, CP949/EUC-KR encoding.
/// The first line is a single integer giving the record count that follows.
/// Each record line contains exactly 33 tab-delimited columns (indices 0..32), read in order:
/// </para>
/// <para>
/// <b>Column layout (0-based text positions):</b>
/// <list type="table">
/// <item><term>[0]</term><description>col0 — category/direction selector (key input, not stored as standalone field)</description></item>
/// <item><term>[1]</term><description>col1 — intra-category offset (key input; equals mob_id for mob entries)</description></item>
/// <item><term>[2]</term><description>col2 — int_a → int field @ record offset 0x04</description></item>
/// <item><term>[3]</term><description>col3 — rate_src_x → float field @ 0x08</description></item>
/// <item><term>[4]</term><description>col4 — divisor_x → int field @ 0x28 (interleaved early in stream)</description></item>
/// <item><term>[5]</term><description>col5 — rate_src_y → float field @ 0x0C</description></item>
/// <item><term>[6]</term><description>col6 — divisor_y → int field @ 0x2C (interleaved early)</description></item>
/// <item><term>[7]</term><description>col7 — int_b → int field @ 0x10</description></item>
/// <item><term>[8]</term><description>col8 — float_c → float @ 0x14</description></item>
/// <item><term>[9]</term><description>col9 — float_d → float @ 0x18</description></item>
/// <item><term>[10]</term><description>col10 — float_e → float @ 0x1C</description></item>
/// <item><term>[11]</term><description>col11 — float_f → float @ 0x20</description></item>
/// <item><term>[12]</term><description>col12 — float_g → float @ 0x24</description></item>
/// <item><term>[13]</term><description>col13 — float_h → float @ 0x38</description></item>
/// <item><term>[14]</term><description>col14 — float_i → float @ 0x3C</description></item>
/// <item><term>[15..23]</term><description>motion_ids_a[0..8] → i32[9] @ 0x40</description></item>
/// <item><term>[24..32]</term><description>motion_ids_b[0..8] → i32[9] @ 0x64</description></item>
/// </list>
/// </para>
/// <para>
/// spec: Docs/RE/formats/actormotion.md §Per-record layout — "the read order interleaves two fields
/// (the two divisors at +0x28 / +0x2C) earlier in the column stream than their record-offset position."
/// Divisor_x is col4 in the spec notation (text position [4]); divisor_y is col6.
/// </para>
/// <para>
/// The computed <c>motion_key = col1 + base_table[(uint8)(col0 + 1)]</c> requires an external
/// per-category base table. When none is supplied, the raw col1 value is used as the key
/// (base contribution = 0).
/// spec: Docs/RE/formats/actormotion.md §Computed lookup key.
/// </para>
/// <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class ActormotionParser
{
    // ----------------------------------------------------------------
    // Column layout constants
    // spec: Docs/RE/formats/actormotion.md §Per-record layout
    // ----------------------------------------------------------------

    private const int ColCategory = 0; // col0 — key input
    private const int ColIntraOffset = 1; // col1 — key input
    private const int ColIntA = 2; // col2 → 0x04
    private const int ColRateSrcX = 3; // col3 → 0x08 (f32)
    private const int ColDivisorX = 4; // col4 → 0x28 (i32, interleaved early) spec: §Per-record layout
    private const int ColRateSrcY = 5; // col5 → 0x0C (f32)
    private const int ColDivisorY = 6; // col6 → 0x2C (i32, interleaved early) spec: §Per-record layout
    private const int ColIntB = 7; // col7 → 0x10
    private const int ColFloatC = 8; // col8 → 0x14
    private const int ColFloatD = 9; // col9 → 0x18
    private const int ColFloatE = 10; // col10 → 0x1C
    private const int ColFloatF = 11; // col11 → 0x20
    private const int ColFloatG = 12; // col12 → 0x24
    private const int ColFloatH = 13; // col13 → 0x38
    private const int ColFloatI = 14; // col14 → 0x3C
    private const int ColMotionIdsAStart = 15; // motion_ids_a[0..8] → 0x40
    private const int ColMotionIdsBStart = 24; // motion_ids_b[0..8] → 0x64
    private const int TotalColumns = 33; // total text columns per record

    /// <summary>
    /// Number of elements in each motion-id sub-array.
    /// The per-direction interpretation of the 9 slots is proposed, not proven.
    /// spec: Docs/RE/formats/actormotion.md §The two 9-element motion-id sub-arrays (0x40, 0x64).
    /// </summary>
    private const int MotionIdArrayCount = 9; // spec: Docs/RE/formats/actormotion.md §motion-id sub-arrays

    /// <summary>
    /// Per-frame rate base constant (frames per second).
    /// spec: Docs/RE/formats/actormotion.md §Per-frame rate fields (0x30, 0x34) — CONFIRMED math.
    /// </summary>
    private const float FpsBase = 15.0f; // spec: Docs/RE/formats/actormotion.md §Per-frame rate fields

    // ----------------------------------------------------------------
    // Public API
    // ----------------------------------------------------------------

    /// <inheritdoc cref="Parse(ReadOnlySpan{byte},ReadOnlySpan{uint})"/>
    public static ActormotionCatalogue Parse(ReadOnlyMemory<byte> fileBytes)
        => Parse(fileBytes.Span, ReadOnlySpan<uint>.Empty);

    /// <summary>
    /// Compatibility overload: parses <c>data/char/actormotion.txt</c> and returns a
    /// <see cref="Dictionary{TKey,TValue}"/> keyed by <see cref="ActormotionEntry.ActorClassId"/>
    /// (= col1 = mob_id) for O(1) mob-id lookup.
    /// </summary>
    /// <remarks>
    /// This overload preserves the pre-refactor API surface used by the Godot presentation layer.
    /// Prefer <see cref="Parse(ReadOnlyMemory{byte})"/> + <see cref="ActormotionCatalogue.GetByIntraOffset"/>
    /// for new code.
    /// </remarks>
    public static Dictionary<int, ActormotionEntry> ParseAsLookup(ReadOnlyMemory<byte> fileBytes)
    {
        var catalogue = Parse(fileBytes);
        var dict = new Dictionary<int, ActormotionEntry>(catalogue.Count);
        foreach (var entry in catalogue.AllEntries)
            dict.TryAdd(entry.ActorClassId, entry); // first occurrence wins
        return dict;
    }

    /// <inheritdoc cref="Parse(ReadOnlySpan{byte},ReadOnlySpan{uint})"/>
    public static ActormotionCatalogue Parse(ReadOnlyMemory<byte> fileBytes, ReadOnlySpan<uint> baseTable)
        => Parse(fileBytes.Span, baseTable);

    /// <summary>
    /// Parses the raw CP949 bytes of <c>data/char/actormotion.txt</c> into a
    /// <see cref="ActormotionCatalogue"/> keyed by the computed motion key.
    /// </summary>
    /// <param name="fileBytes">
    /// Raw bytes of the file (CP949/EUC-KR), typically obtained via
    /// <c>MappedVfsArchive.GetFileContent("data/char/actormotion.txt")</c>.
    /// </param>
    /// <param name="baseTable">
    /// Optional per-category base-offset table indexed by <c>(uint8)(col0 + 1)</c>.
    /// spec: Docs/RE/formats/actormotion.md §Computed lookup key.
    /// When empty or too short, the base contribution for that category is 0
    /// (i.e. <c>motion_key = col1</c>).
    /// </param>
    /// <returns>
    /// A <see cref="ActormotionCatalogue"/> containing all successfully parsed records.
    /// Lines with fewer than <see cref="TotalColumns"/> columns or un-parseable key columns
    /// are skipped (they are header/footer artefacts).
    /// </returns>
    public static ActormotionCatalogue Parse(ReadOnlySpan<byte> fileBytes, ReadOnlySpan<uint> baseTable)
    {
        // spec: Docs/RE/formats/actormotion.md §Identification — encoding CP949 (Korean).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949); // CP949/EUC-KR — spec: Docs/RE/formats/actormotion.md §Identification

        string text = cp949.GetString(fileBytes);
        return ParseText(text, baseTable);
    }

    /// <summary>
    /// Overload accepting pre-decoded text (for testing and diagnostics).
    /// </summary>
    public static ActormotionCatalogue ParseText(string text)
        => ParseText(text, ReadOnlySpan<uint>.Empty);

    /// <summary>
    /// Overload accepting pre-decoded text and an optional base-table (for testing).
    /// </summary>
    public static ActormotionCatalogue ParseText(string text, ReadOnlySpan<uint> baseTable)
    {
        string[] lines = text.Split('\n');

        // First line is the record count.
        // spec: Docs/RE/formats/actormotion.md §File structure — leading integer count.
        int capacity = lines.Length;
        if (lines.Length > 0 && int.TryParse(lines[0].Trim('\r').Trim(), out int declaredCount))
            capacity = declaredCount;

        var byKey = new Dictionary<uint, ActormotionEntry>(capacity);

        for (int lineIdx = 1; lineIdx < lines.Length; lineIdx++)
        {
            string raw = lines[lineIdx];
            if (raw.Length == 0) continue;

            // Strip CR for CRLF line endings.
            ReadOnlySpan<char> trimmed = raw.AsSpan().TrimEnd('\r');
            if (trimmed.IsEmpty) continue;

            string[] cols = raw.TrimEnd('\r').Split('\t');

            // Require the minimum column count to avoid reading out of bounds.
            // spec: Docs/RE/formats/actormotion.md — 33 columns per record.
            if (cols.Length < TotalColumns) continue;

            // --- Key-input columns ---
            // spec: Docs/RE/formats/actormotion.md §Per-record layout — key-input columns
            if (!int.TryParse(cols[ColCategory].Trim(), out int col0)) continue;
            if (!int.TryParse(cols[ColIntraOffset].Trim(), out int col1)) continue;

            // Compute motion_key = col1 + base_table[(uint8)(col0 + 1)]
            // spec: Docs/RE/formats/actormotion.md §Computed lookup key
            uint baseContrib = 0;
            int baseIdx =
                (byte)(col0 + 1); // spec: Docs/RE/formats/actormotion.md §Computed lookup key — (uint8)(col0+1)
            if (baseIdx < baseTable.Length)
                baseContrib = baseTable[baseIdx];

            uint motionKey = (uint)(col1 + (int)baseContrib);

            // --- Stored fields ---
            // spec: Docs/RE/formats/actormotion.md §Per-record layout

            int.TryParse(cols[ColIntA].Trim(), out int intA); // col2 → 0x04
            float.TryParse(cols[ColRateSrcX].Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float rateSrcX); // col3 → 0x08
            int.TryParse(cols[ColDivisorX].Trim(), out int divisorX); // col4 → 0x28 (interleaved)
            float.TryParse(cols[ColRateSrcY].Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float rateSrcY); // col5 → 0x0C
            int.TryParse(cols[ColIntB].Trim(), out int intB); // col7 → 0x10
            float.TryParse(cols[ColFloatC].Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float floatC); // col7 → 0x14
            float.TryParse(cols[ColFloatD].Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float floatD); // col8 → 0x18
            float.TryParse(cols[ColFloatE].Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float floatE); // col9 → 0x1C
            float.TryParse(cols[ColFloatF].Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float floatF); // col10 → 0x20
            float.TryParse(cols[ColFloatG].Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float floatG); // col11 → 0x24
            float.TryParse(cols[ColFloatH].Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float floatH); // col12 → 0x38
            float.TryParse(cols[ColFloatI].Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float floatI); // col13 → 0x3C
            int.TryParse(cols[ColDivisorY].Trim(), out int divisorY); // col6 → 0x2C

            // Divide-by-zero guard: force divisors to 1 when parsed as 0.
            // spec: Docs/RE/formats/actormotion.md — divisor_x / divisor_y forced to 1 if read as 0.
            if (divisorX == 0) divisorX = 1;
            if (divisorY == 0) divisorY = 1;

            // Computed per-frame rates.
            // spec: Docs/RE/formats/actormotion.md §Per-frame rate fields (0x30, 0x34) — CONFIRMED math.
            // rate_x = 15.0 * rate_src_x / divisor_x
            // rate_y = 15.0 * rate_src_y / divisor_y
            float rateX = FpsBase * rateSrcX / divisorX; // spec: Docs/RE/formats/actormotion.md §rate_x @ 0x30
            float rateY = FpsBase * rateSrcY / divisorY; // spec: Docs/RE/formats/actormotion.md §rate_y @ 0x34

            // Motion-id sub-arrays: 9 consecutive integer columns each.
            // spec: Docs/RE/formats/actormotion.md §The two 9-element motion-id sub-arrays (0x40, 0x64)
            var dirArray1 = new int[MotionIdArrayCount]; // motion_ids_a → record 0x40
            var dirArray2 = new int[MotionIdArrayCount]; // motion_ids_b → record 0x64
            for (int d = 0; d < MotionIdArrayCount; d++)
            {
                int.TryParse(cols[ColMotionIdsAStart + d].Trim(), out dirArray1[d]); // col[15+d] → 0x40+d*4
                int.TryParse(cols[ColMotionIdsBStart + d].Trim(), out dirArray2[d]); // col[24+d] → 0x64+d*4
            }

            var entry = new ActormotionEntry
            {
                MotionKey = motionKey,
                Col0Category = col0,
                Col1RawOffset = col1,
                IntA = intA,
                RateSrcX = rateSrcX,
                RateSrcY = rateSrcY,
                IntB = intB,
                FloatC = floatC,
                FloatD = floatD,
                FloatE = floatE,
                FloatF = floatF,
                FloatG = floatG,
                DivisorX = divisorX,
                DivisorY = divisorY,
                RateX = rateX,
                RateY = rateY,
                FloatH = floatH,
                FloatI = floatI,
                DirArray1 = dirArray1,
                DirArray2 = dirArray2,
            };

            // Insert into ordered map keyed by motion_key (first occurrence wins for duplicates).
            // spec: Docs/RE/formats/actormotion.md §File structure — records are inserted into
            // an ordered map keyed by the computed motion_key field.
            byKey.TryAdd(motionKey, entry);
        }

        return new ActormotionCatalogue(byKey);
    }
}