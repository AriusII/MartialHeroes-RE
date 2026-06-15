// THROWAWAY harness — never commit, never add to MartialHeroes.slnx
// V14: char text tables validation
// Stats only — no raw text dump, no full file content.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var cp949 = Encoding.GetEncoding(949);

var inf = args.Length > 0 ? args[0] : "D:/MartialHeroesClient/data.inf";
var vfs = args.Length > 1 ? args[1] : "D:/MartialHeroesClient/data/data.vfs";

Console.WriteLine($"Mounting VFS: {inf}");
using var archive = MappedVfsArchive.Open(inf, vfs);
Console.WriteLine($"Mounted {archive.GetEntries().Length} entries");
Console.WriteLine();

// ===== SKIN.TXT =====
AnalyzeSkinTxt(archive, cp949);

// ===== ACTORMOTION.TXT =====
AnalyzeActormotion(archive, cp949);

// ===== BINDLIST.TXT =====
AnalyzeBindlist(archive, cp949);

// ===== BGTEXTURE.TXT (both instances) =====
AnalyzeBgtexture(archive, cp949, "data/map000/texture/bgtexture.txt");
AnalyzeBgtexture(archive, cp949, "data/effect/texture/bgtexture.txt");

// ===== BGTEXTURE.LST =====
AnalyzeBgtextureLst(archive, "data/map000/texture/bgtexture.lst");
AnalyzeBgtextureLst(archive, "data/effect/texture/bgtexture.lst");

return;

// -----------------------------------------------------------------------

static string[] SplitTabRow(string line) => line.Split('\t');

static void PrintFieldStats(string label, IEnumerable<string> values)
{
    var vals = values.ToList();
    int total = vals.Count;
    int empty = vals.Count(v => string.IsNullOrEmpty(v) || v == "0");
    var distinct = vals.GroupBy(v => v).OrderByDescending(g => g.Count()).ToList();
    string modal = distinct.FirstOrDefault()?.Key ?? "(none)";
    int modalFreq = distinct.FirstOrDefault()?.Count() ?? 0;
    bool allInt = vals.All(v => long.TryParse(v, out _));
    long min = 0, max = 0;
    if (allInt && vals.Count > 0)
    {
        min = vals.Min(v => long.Parse(v));
        max = vals.Max(v => long.Parse(v));
    }
    Console.WriteLine($"  {label}: total={total} distinct={distinct.Count} modal=\"{modal}\"({modalFreq}) zero/empty={empty} allInt={allInt}" +
        (allInt ? $" min={min} max={max}" : ""));
}

// -----------------------------------------------------------------------

static void AnalyzeSkinTxt(MappedVfsArchive archive, Encoding cp949)
{
    Console.WriteLine("=== SKIN.TXT ===");
    var path = "data/char/skin.txt";
    if (!archive.Contains(path)) { Console.WriteLine("NOT FOUND"); return; }
    var bytes = archive.GetFileContent(path);
    var text = cp949.GetString(bytes.Span);
    var rawLines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                       .Select(l => l.TrimEnd('\r')).ToArray();

    Console.WriteLine($"File size: {bytes.Length} bytes, {rawLines.Length} raw lines");

    // Line 0 = count
    int declaredCount = int.Parse(rawLines[0].Trim());
    Console.WriteLine($"Declared count (line 0): {declaredCount}");

    var dataLines = rawLines.Skip(1).ToArray();
    Console.WriteLine($"Data lines: {dataLines.Length}");

    // Column count distribution
    var colCounts = dataLines.Select(l => SplitTabRow(l).Length).GroupBy(n => n)
                             .OrderByDescending(g => g.Count()).ToList();
    Console.WriteLine("Column count distribution:");
    foreach (var g in colCounts)
        Console.WriteLine($"  {g.Key} cols: {g.Count()} rows");

    // For each column width family, separate analysis
    // The spec referenced in text_tables.md says skin.txt is documented in bgtexture_lst.md (skin chain)
    // From head observation: the data has rows with 3 cols and rows with 6 cols
    // Rows with 6 cols: col0=IdX, col1=IdA, col2=IdB, col3=unknown, col4=tex_id_512, col5=tex_id_1024
    // Rows with 3 cols: col0=IdX, col1=IdA, col2=IdB (no tex_id — null/absent entries)

    // Collect per-column
    var allRows = dataLines.Select(l => SplitTabRow(l)).ToList();
    var rows6 = allRows.Where(r => r.Length == 6).ToList();
    var rows3 = allRows.Where(r => r.Length == 3).ToList();
    var rows6plus = allRows.Where(r => r.Length > 3).ToList();
    var rowsShort = allRows.Where(r => r.Length <= 3).ToList();

    Console.WriteLine($"Rows with >=4 cols (have tex_ids): {rows6plus.Count}");
    Console.WriteLine($"Rows with <=3 cols (no tex_id): {rowsShort.Count}");
    Console.WriteLine($"Rows with exactly 6 cols: {rows6.Count}");
    Console.WriteLine($"Rows with exactly 3 cols: {rows3.Count}");
    Console.WriteLine($"Other column counts: {allRows.Count - rows6.Count - rows3.Count}");

    // Verify declared count
    Console.WriteLine($"Count match: declared={declaredCount} actual={dataLines.Length} => {(declaredCount == dataLines.Length ? "MATCH" : "MISMATCH")}");

    // col0 stats (present in all rows)
    Console.WriteLine("\nPer-column stats (all rows):");
    PrintFieldStats("col0", allRows.Select(r => r.Length > 0 ? r[0] : ""));
    PrintFieldStats("col1 (IdA)", allRows.Select(r => r.Length > 1 ? r[1] : ""));
    PrintFieldStats("col2 (IdB)", allRows.Select(r => r.Length > 2 ? r[2] : ""));

    // For rows with 6 cols:
    if (rows6.Count > 0)
    {
        Console.WriteLine($"\nFor 6-col rows ({rows6.Count} rows):");
        PrintFieldStats("col3", rows6.Select(r => r[3]));
        PrintFieldStats("col4 (tex_id_512?)", rows6.Select(r => r[4]));
        PrintFieldStats("col5 (tex_id_1024?)", rows6.Select(r => r[5]));

        // Validate IdB in {1,11,16,26} for skeleton chain
        var idbVals = allRows.Where(r => r.Length > 2).Select(r => r[2]).Distinct().OrderBy(v => v).ToList();
        Console.WriteLine($"\nDistinct IdB values ({idbVals.Count}): {string.Join(",", idbVals.Take(20))}{(idbVals.Count > 20 ? "..." : "")}");
        var idBSet = new HashSet<string> { "1", "11", "16", "26" };
        var idBInSet = idbVals.Where(v => idBSet.Contains(v)).ToList();
        var idBOutSet = idbVals.Where(v => !idBSet.Contains(v)).ToList();
        Console.WriteLine($"IdB in {{1,11,16,26}}: {idBInSet.Count} values => {string.Join(",", idBInSet)}");
        Console.WriteLine($"IdB outside {{1,11,16,26}}: {idBOutSet.Count} values => {string.Join(",", idBOutSet.Take(30))}");
    }

    // col4 tex_id patterns: check if they look like 9-digit numeric ids
    if (rows6.Count > 0)
    {
        var col4Vals = rows6.Select(r => r[4]).Where(v => v != "0").ToList();
        bool all9digit = col4Vals.All(v => v.Length == 9 && long.TryParse(v, out _));
        Console.WriteLine($"\ncol4 non-zero vals: {col4Vals.Count}, all 9-digit: {all9digit}");
        var col5Vals = rows6.Select(r => r[5]).Where(v => v != "0").ToList();
        bool col5all9 = col5Vals.All(v => v.Length == 9 && long.TryParse(v, out _));
        Console.WriteLine($"col5 non-zero vals: {col5Vals.Count}, all 9-digit: {col5all9}");
    }
    // Deep dive: col2 (IdB) distribution for rows where col4 is non-zero (active skins)
    var activeRows = allRows.Where(r => r.Length == 6 && r[4] != "0").ToList();
    var idBDistActive = activeRows.Select(r => r[2]).GroupBy(v => v).OrderByDescending(g => g.Count())
                                  .Select(g => $"{g.Key}({g.Count()})").ToList();
    Console.WriteLine($"\ncol2 (IdB) distribution for ACTIVE rows (col4!=0, n={activeRows.Count}):");
    Console.WriteLine($"  {string.Join(", ", idBDistActive)}");

    // col1 (IdA) sample for cross-chain validation
    var col1Dist = allRows.Select(r => r.Length > 1 ? r[1] : "").GroupBy(v => v)
                          .OrderByDescending(g => g.Count()).Take(5).Select(g => $"{g.Key}({g.Count()})").ToList();
    Console.WriteLine($"\ncol1 (IdA) top-5: {string.Join(", ", col1Dist)}");

    // col0 meaning: looking at the head, col0 is always 0,1,2 — likely a skin family/class group tag
    // 0=common, 1=player, 2=item/special?
    var col0Dist = allRows.Select(r => r.Length > 0 ? r[0] : "").GroupBy(v => v)
                          .OrderByDescending(g => g.Count()).Select(g => $"{g.Key}({g.Count()})").ToList();
    Console.WriteLine($"\ncol0 distribution: {string.Join(", ", col0Dist)}");

    Console.WriteLine();
}

static void AnalyzeActormotion(MappedVfsArchive archive, Encoding cp949)
{
    Console.WriteLine("=== ACTORMOTION.TXT ===");
    var path = "data/char/actormotion.txt";
    if (!archive.Contains(path)) { Console.WriteLine("NOT FOUND"); return; }
    var bytes = archive.GetFileContent(path);
    var text = cp949.GetString(bytes.Span);
    var rawLines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                       .Select(l => l.TrimEnd('\r')).ToArray();

    Console.WriteLine($"File size: {bytes.Length} bytes, {rawLines.Length} raw lines");
    int declaredCount = int.Parse(rawLines[0].Trim());
    Console.WriteLine($"Declared count (line 0): {declaredCount}");

    var dataLines = rawLines.Skip(1).ToArray();
    Console.WriteLine($"Data lines: {dataLines.Length}");
    Console.WriteLine($"Count match: {(declaredCount == dataLines.Length ? "MATCH" : $"MISMATCH declared={declaredCount} actual={dataLines.Length}")}");

    // Count raw lines before filtering
    var rawDataLines2 = text.Split('\n').Skip(1).ToArray();
    int blankLines = rawDataLines2.Count(l => string.IsNullOrWhiteSpace(l.TrimEnd('\r')));
    int nonBlankLines = rawDataLines2.Count(l => !string.IsNullOrWhiteSpace(l.TrimEnd('\r')));
    Console.WriteLine($"Raw lines after line0: {rawDataLines2.Length} (blank={blankLines}, non-blank={nonBlankLines})");
    // If file ends without \r\n, the last split element could be empty
    bool lastEmpty = rawDataLines2.Length > 0 && string.IsNullOrEmpty(rawDataLines2[^1].TrimEnd('\r'));
    Console.WriteLine($"Last raw line empty (no trailing newline): {lastEmpty}");

    var allRows = dataLines.Select(l => l.Split('\t')).ToList();
    var colCounts = allRows.Select(r => r.Length).GroupBy(n => n).OrderByDescending(g => g.Count()).ToList();
    Console.WriteLine("Column count distribution:");
    foreach (var g in colCounts)
        Console.WriteLine($"  {g.Key} cols: {g.Count()} rows");

    // Spec says 34 columns per row (col0..col33 = 2 key + 12 data + 9+9 dir arrays = 32 data cols)
    // Let's count: col0,col1 + then the rest
    // From head: row looks like ~34 tab-separated tokens
    // Count actual: 0.1.1.7.402.16.16.282.11.0.4.5.3.1.8.4.1.{9 vals}.{9 vals} = 2+13+2+9+9 = ?
    // Let's just count observed
    int expectedCols = 34; // from spec analysis: 2 key + col2..col13 (12) + 9 + 9 + 2 (computed) = but text has raw cols
    // Actually from text: 0	1	1	7.402	16	16.282	11	0	4	5	3	1	8	4	1	{9 ids}	{9 ids} = ~34 cols

    var modal = colCounts.First().Key;
    Console.WriteLine($"Modal column count: {modal}");

    // Anomaly rows
    var anomalies = allRows.Where(r => r.Length != modal).ToList();
    Console.WriteLine($"Anomaly rows (not {modal} cols): {anomalies.Count}");

    // col0 = category, col1 = intra-category offset, col2 = int_a
    Console.WriteLine("\nPer-column stats:");
    PrintFieldStats("col0 (category)", allRows.Where(r => r.Length > 0).Select(r => r[0]));
    PrintFieldStats("col1 (intra-cat offset)", allRows.Where(r => r.Length > 1).Select(r => r[1]));
    PrintFieldStats("col2 (int_a / skin_class)", allRows.Where(r => r.Length > 2).Select(r => r[2]));

    // col2 distinct values — CLAUDE.md says col2 == IdB for actormotion
    // The spec says col2 = int_a (animation/motion id, likely) stored at 0x04
    // But CLAUDE.md + frontend_scenes.md says actormotion col2 == IdB -> col16 = mot id
    // Let's see what col2 distinct values look like:
    var col2vals = allRows.Where(r => r.Length > 2).Select(r => r[2]).Distinct().OrderBy(v => v).ToList();
    Console.WriteLine($"Distinct col2 values ({col2vals.Count}): {string.Join(",", col2vals.Take(30))}{(col2vals.Count>30?"...":"")}");

    // Check if IdB {1,11,16,26} appear in col2
    var idBSet = new HashSet<string> {"1","11","16","26"};
    var foundInIdBSet = col2vals.Where(v => idBSet.Contains(v)).ToList();
    Console.WriteLine($"col2 values in {{1,11,16,26}}: {string.Join(",", foundInIdBSet)}");

    // col16 = dir_array_1[0] according to spec layout — let's see what col16 holds for IdB-matching rows
    // actually spec layout: the TEXT columns are col0..col1 (key), then col2..col13 (12 fields + 2 divisors),
    // then 9 cols for dir_array_1, then 9 cols for dir_array_2 = total 2+12+9+9 = 32 text cols?
    // But from head we see: 0	1	1	7.402	16	16.282	11	0	4	5	3	1	8	4	1	{9 big ids}	...
    // That is col0=0, col1=1, col2=1, col3=7.402, col4=16, col5=16.282, col6=11, col7=0, col8=4, col9=5
    // col10=3, col11=1, col12=8, col13=4, col14=1, then 9 animation IDs, then 9 more = total ~34 cols
    // col14 = divisor_x (stored at 0x28) — in spec this is col4!
    // The spec says col4 = divisor_x but read order interleaves. Let's just count:
    // col0..col1 key, col2=int_a, col3=rate_src_x, col4=divisor_x, col5=rate_src_y, col6=int_b,
    // col7..col13 = float_c..float_i(7), col14=? actually spec is confusing.
    // Let's just check col16 for the rows where col2 is in {1,11,16,26}:
    if (modal >= 34)
    {
        var idBRows = allRows.Where(r => r.Length >= 17 && idBSet.Contains(r[2])).ToList();
        Console.WriteLine($"\nRows where col2 in {{1,11,16,26}}: {idBRows.Count}");
        if (idBRows.Count > 0)
        {
            var col16vals = idBRows.Select(r => r[16]).Distinct().Take(20).ToList();
            Console.WriteLine($"col16 vals for those rows (first 20 distinct): {string.Join(",", col16vals)}");
        }
    }

    Console.WriteLine();
}

static void AnalyzeBindlist(MappedVfsArchive archive, Encoding cp949)
{
    Console.WriteLine("=== BINDLIST.TXT ===");
    var path = "data/char/bindlist.txt";
    if (!archive.Contains(path)) { Console.WriteLine("NOT FOUND"); return; }
    var bytes = archive.GetFileContent(path);
    var text = cp949.GetString(bytes.Span);

    // Split by CRLF (or LF) — spec says CRLF, final line no terminator
    var lines = text.Split('\n').Select(l => l.TrimEnd('\r'))
                    .Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

    Console.WriteLine($"File size: {bytes.Length} bytes");
    Console.WriteLine($"Entry count: {lines.Length}");
    Console.WriteLine($"Spec says: 349 entries => {(lines.Length == 349 ? "MATCH" : $"MISMATCH (actual={lines.Length})")}");

    // Verify all entries are g<N>.bnd
    bool allGPattern = lines.All(l => l.StartsWith("g") && l.EndsWith(".bnd"));
    Console.WriteLine($"All entries match g<N>.bnd pattern: {allGPattern}");

    // Extract N values
    var nVals = lines.Select(l => l[1..^4]).ToList(); // strip leading 'g' and trailing '.bnd'
    bool allNumeric = nVals.All(v => int.TryParse(v, out _));
    Console.WriteLine($"All N values numeric: {allNumeric}");

    if (allNumeric)
    {
        var nums = nVals.Select(int.Parse).OrderBy(v => v).ToList();
        Console.WriteLine($"N range: {nums.First()} .. {nums.Last()}");
        // Check non-contiguity
        int gaps = 0;
        for (int i = 1; i < nums.Count; i++)
            if (nums[i] != nums[i-1] + 1) gaps++;
        Console.WriteLine($"Gaps (non-contiguous): {gaps}");

        // Check if g1, g2, g3, g4 are present (CLAUDE.md says only g1..g4 — basic class skeletons)
        var basicSet = new[] {1,2,3,4};
        foreach (var v in basicSet)
            Console.WriteLine($"  g{v}.bnd present: {nums.Contains(v)}");

        // Distinct N for IdB chain: {1, 11, 16, 26}
        var idBNums = new[] {1, 11, 16, 26};
        Console.WriteLine($"IdB skeleton candidates in bindlist:");
        foreach (var v in idBNums)
            Console.WriteLine($"  g{v}.bnd present: {nums.Contains(v)}");
    }
    Console.WriteLine();
}

static void AnalyzeBgtexture(MappedVfsArchive archive, Encoding cp949, string path)
{
    Console.WriteLine($"=== BGTEXTURE.TXT [{path}] ===");
    if (!archive.Contains(path)) { Console.WriteLine("NOT FOUND"); return; }
    var bytes = archive.GetFileContent(path);
    var text = cp949.GetString(bytes.Span);
    var rawLines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                       .Select(l => l.TrimEnd('\r')).ToArray();

    Console.WriteLine($"File size: {bytes.Length} bytes, {rawLines.Length} rows");

    // Spec: no header row, 3 columns: col0=index(0-based), col1=kind, col2=relpath
    var allRows = rawLines.Select(l => l.Split('\t')).ToList();
    var colCounts = allRows.Select(r => r.Length).GroupBy(n => n).OrderByDescending(g => g.Count()).ToList();
    Console.WriteLine("Column count distribution:");
    foreach (var g in colCounts) Console.WriteLine($"  {g.Key} cols: {g.Count()} rows");

    // Verify col0 is sequential 0-based
    var rows3 = allRows.Where(r => r.Length == 3).ToList();
    bool col0Sequential = true;
    for (int i = 0; i < rows3.Count; i++)
    {
        if (!int.TryParse(rows3[i][0], out int idx) || idx != i)
        { col0Sequential = false; break; }
    }
    Console.WriteLine($"col0 sequential 0-based: {col0Sequential}");

    // col1 = kind byte distribution
    PrintFieldStats("col1 (kind)", rows3.Select(r => r[1]));
    var kindDist = rows3.Select(r => r[1]).GroupBy(v => v).OrderByDescending(g => g.Count())
                        .Select(g => $"{g.Key}({g.Count()})").Take(10).ToList();
    Console.WriteLine($"  kind distribution: {string.Join(", ", kindDist)}");

    // col2 relpath: check non-empty, check ends with no extension
    var relpaths = rows3.Select(r => r[2]).ToList();
    int emptyPath = relpaths.Count(v => string.IsNullOrEmpty(v));
    int hasDds = relpaths.Count(v => v.EndsWith(".dds", StringComparison.OrdinalIgnoreCase));
    Console.WriteLine($"col2 relpaths: empty={emptyPath}, has .dds ext={hasDds} (should be 0)");
    Console.WriteLine($"col2 sample paths (first 3): {string.Join(" | ", relpaths.Take(3))}");
    Console.WriteLine();
}

static void AnalyzeBgtextureLst(MappedVfsArchive archive, string path)
{
    Console.WriteLine($"=== BGTEXTURE.LST [{path}] ===");
    if (!archive.Contains(path)) { Console.WriteLine("NOT FOUND"); return; }
    var bytes = archive.GetFileContent(path);
    var span = bytes.Span;

    if (span.Length < 4) { Console.WriteLine("Too short"); return; }
    uint recordCount = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span[..4]);
    int expectedSize = 4 + (int)recordCount * 48;
    Console.WriteLine($"File size: {bytes.Length} bytes, record_count={recordCount}, expected_size={expectedSize}");
    Console.WriteLine($"Size formula match: {(bytes.Length == expectedSize ? "EXACT" : $"MISMATCH (actual={bytes.Length})")}");

    // Scan all records for kind byte distribution
    var kindCounts = new Dictionary<byte, int>();
    int emptyRelpath = 0;
    int hasDdsInRelpath = 0;
    for (int i = 0; i < (int)recordCount; i++)
    {
        int off = 4 + i * 48;
        byte kind = span[off];
        if (!kindCounts.TryGetValue(kind, out int cnt)) kindCounts[kind] = 0;
        kindCounts[kind]++;

        // Read relpath (47 bytes null-terminated)
        var relpathBytes = span.Slice(off + 1, 47);
        int nullPos = relpathBytes.IndexOf((byte)0);
        var relpath = System.Text.Encoding.ASCII.GetString(nullPos >= 0 ? relpathBytes[..nullPos] : relpathBytes);
        if (string.IsNullOrEmpty(relpath)) emptyRelpath++;
        if (relpath.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) hasDdsInRelpath++;
    }

    Console.WriteLine($"Distinct kind values: {kindCounts.Count}");
    foreach (var kv in kindCounts.OrderBy(k => k.Key))
        Console.WriteLine($"  kind=0x{kv.Key:X2}({kv.Key}): {kv.Value} records");
    Console.WriteLine($"Empty relpaths: {emptyRelpath}, relpath has .dds: {hasDdsInRelpath}");
    Console.WriteLine();
}
