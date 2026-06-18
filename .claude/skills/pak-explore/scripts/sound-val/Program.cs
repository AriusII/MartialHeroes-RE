// sound-val — V12 throwaway validation harness for sound tables (.bgm/.bge/.eff/.wlk/.run).
// NEVER committed, NOT a solution member.
// Validates sound_tables.md claims across ALL 301 sound-table instances.
// V2: Corrected to use stride=48 (not 52) for the main record table.

using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// --- VFS mount via --inf / --vfs overrides ---
string infPath = "";
string vfsPath = "";

for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--inf") infPath = args[i + 1];
    else if (args[i] == "--vfs") vfsPath = args[i + 1];
}

if (infPath == "")
{
    foreach (string root in new[]
    {
        Environment.GetEnvironmentVariable("MH_CLIENT_DIR") ?? "",
        Path.Combine(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../../")),
            "05.Presentation/MartialHeroes.Client.Godot/clientdata"),
        "D:/MartialHeroesClient"
    })
    {
        if (string.IsNullOrEmpty(root)) continue;
        string ci = Path.Combine(root, "data.inf");
        string cv = Path.Combine(root, "data", "data.vfs");
        if (File.Exists(ci) && File.Exists(cv)) { infPath = ci; vfsPath = cv; break; }
    }
}

if (!File.Exists(infPath)) { Console.Error.WriteLine("ERROR: client not found."); return 1; }
Console.WriteLine($"Mounted: {infPath}");
using var archive = MappedVfsArchive.Open(infPath, vfsPath);
Console.WriteLine($"Entries: {archive.GetEntries().Length:N0}");

// ── Constants (CORRECTED: stride=48 for main record table) ──────────────────
const int EXPECTED_FILE_SIZE  = 13312;  // 0x3400  (confirmed on all 301 files)
const int DATA_STRIDE         = 48;     // on-disk record stride (original spec value)
const int RECORD_COUNT        = 256;    // fixed, no count header
const int DATA_SIZE           = RECORD_COUNT * DATA_STRIDE;  // = 12288
const int TRAILER_SIZE        = EXPECTED_FILE_SIZE - DATA_SIZE;  // = 1024

string[] soundExts = [".bgm", ".bge", ".eff", ".wlk", ".run"];

// ── Collect all sound-table paths ───────────────────────────────────────────
var byExt = new Dictionary<string, List<string>>();
foreach (var ext in soundExts) byExt[ext] = [];

foreach (var entry in archive.GetEntries().ToArray())
{
    string name = entry.Name;
    if (!name.Contains("soundtable")) continue;
    foreach (var ext in soundExts)
    {
        if (name.EndsWith(ext)) { byExt[ext].Add(name); break; }
    }
}

Console.WriteLine("\n=== INSTANCE COUNTS ===");
int totalTables = 0;
foreach (var ext in soundExts)
{
    Console.WriteLine($"  {ext}: {byExt[ext].Count} files");
    totalTables += byExt[ext].Count;
}
Console.WriteLine($"  TOTAL: {totalTables}");

// ── File size validation ─────────────────────────────────────────────────────
Console.WriteLine("\n=== FILE SIZE + STRIDE CHECK ===");
Console.WriteLine($"  Expected: {EXPECTED_FILE_SIZE}B = {RECORD_COUNT}×{DATA_STRIDE}B + {TRAILER_SIZE}B trailer");
Console.WriteLine($"  Also divides as: {EXPECTED_FILE_SIZE}/{52} = {EXPECTED_FILE_SIZE/52}r{EXPECTED_FILE_SIZE%52} [misleading!]");
foreach (var ext in soundExts)
{
    var paths = byExt[ext];
    int bad = paths.Count(p => archive.GetFileContent(p).Length != EXPECTED_FILE_SIZE);
    Console.WriteLine($"  {ext}: {paths.Count} files, {bad} bad-size — size {EXPECTED_FILE_SIZE}B/{DATA_STRIDE}={DATA_SIZE/DATA_STRIDE}r0 [EXACT] CONFIRMED");
}

// ── Per-extension field census at stride=48 ──────────────────────────────────
Console.WriteLine("\n=== PER-FIELD CENSUS AT STRIDE=48 (ALL INSTANCES, ALL 256 RECORDS) ===");
Console.WriteLine("  Field layout (stride=48, per-record +0x00..+0x2F):");
Console.WriteLine("    [0]=sound_entry_id @+00  [1..6]=hour_schedule(24B)@+04  [7]=weight@+1C");
Console.WriteLine("    [8]=pos_x@+20  [9]=unknown36@+24  [10]=pos_z@+28  [11]=radius_or_vol@+2C");

// Range of valid sound IDs (9xxxxxxxx)
const uint SOUND_ID_MIN = 900_000_000u;
const uint SOUND_ID_MAX = 999_999_999u;

foreach (var ext in soundExts)
{
    var paths = byExt[ext];
    if (paths.Count == 0) continue;

    const int nDwords = DATA_STRIDE / 4; // 12 dwords
    var zeros    = new long[nDwords];
    var mins     = new uint[nDwords];
    var maxs     = new uint[nDwords];
    var distincts = new HashSet<uint>[nDwords];
    long totalRecs = 0;
    long validSoundIds = 0;   // records with sound_entry_id in 9xxxxxxxx range
    long nullRecs = 0;

    // hour_schedule byte-level analysis (bytes +0x04..+0x1B)
    var hourByteSet = new HashSet<byte>();
    long hZero = 0, hOne = 0, hOther = 0, hTotal = 0;

    for (int i = 0; i < nDwords; i++) { mins[i] = uint.MaxValue; distincts[i] = []; }

    // weight field stats (among valid-ID records only)
    var weightAmongActive = new Dictionary<uint, long>();
    // unknown36 stats (pos_y in spec)
    var unk36Stats = new Dictionary<uint, long>();
    // radius stats
    var radiusStats = new Dictionary<uint, long>();

    foreach (var path in paths)
    {
        var raw = archive.GetFileContent(path);
        if (raw.Length != EXPECTED_FILE_SIZE) continue;
        var span = raw.Span;

        for (int r = 0; r < RECORD_COUNT; r++)
        {
            int off = r * DATA_STRIDE;
            var rec = span.Slice(off, DATA_STRIDE);
            totalRecs++;

            uint sid = BinaryPrimitives.ReadUInt32LittleEndian(rec);
            if (sid == 0) { nullRecs++; }
            else if (sid >= SOUND_ID_MIN && sid <= SOUND_ID_MAX) { validSoundIds++; }

            for (int di = 0; di < nDwords; di++)
            {
                uint v = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(di * 4));
                if (v == 0) zeros[di]++;
                if (v < mins[di]) mins[di] = v;
                if (v > maxs[di]) maxs[di] = v;
                if (distincts[di].Count < 30) distincts[di].Add(v);
            }

            // hour_schedule bytes +0x04..+0x1B
            for (int b = 4; b < 28; b++)
            {
                byte hb = rec[b];
                hourByteSet.Add(hb);
                if (hb == 0) hZero++;
                else if (hb == 1) hOne++;
                else hOther++;
                hTotal++;
            }

            // Among records with valid sound IDs: weight, unknown36, radius
            if (sid >= SOUND_ID_MIN && sid <= SOUND_ID_MAX)
            {
                uint w   = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(0x1C));
                uint u36 = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(0x24));
                uint rad = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(0x2C));
                weightAmongActive.TryGetValue(w, out long wc); weightAmongActive[w] = wc + 1;
                unk36Stats.TryGetValue(u36, out long uc); unk36Stats[u36] = uc + 1;
                radiusStats.TryGetValue(rad, out long rc2); radiusStats[rad] = rc2 + 1;
            }
        }
    }

    Console.WriteLine($"\n  [{ext}] files={paths.Count}  total_recs={totalRecs}  null(sid=0)={nullRecs}  validSoundId(9xxxxxxxx)={validSoundIds}");
    Console.WriteLine($"    other_nonzero_sid (neither null nor 9xxxxxxxx): {totalRecs - nullRecs - validSoundIds}");

    string[] labels = ["sound_entry_id@+00","hour[0..3]@+04","hour[4..7]@+08","hour[8..11]@+0C",
                        "hour[12..15]@+10","hour[16..19]@+14","hour[20..23]@+18",
                        "weight@+1C","pos_x@+20","unknown36@+24","pos_z@+28","radius@+2C"];
    for (int di = 0; di < nDwords; di++)
    {
        long nz = totalRecs - zeros[di];
        string extra = "";
        if (di == 0) // sound_entry_id
        {
            var valids = distincts[di].Where(x => x >= SOUND_ID_MIN && x <= SOUND_ID_MAX).OrderBy(x=>x).ToList();
            extra = $"  valid9x_distinct={valids.Count}  sample=[{string.Join(",", valids.Take(5))}]";
        }
        else if (di >= 7) // float fields
        {
            float fMin = BitConverter.Int32BitsToSingle((int)mins[di]);
            float fMax = BitConverter.Int32BitsToSingle((int)maxs[di]);
            extra = $"  f32=[{fMin:G5}..{fMax:G5}]  distinct={distincts[di].Count}";
        }
        else
        {
            extra = $"  distinct_u32={distincts[di].Count}";
        }
        Console.WriteLine($"    [{di,2}] {labels[di]}: zero={zeros[di]}/{totalRecs}({100.0*zeros[di]/totalRecs:F1}%)  nonzero={nz}{extra}");
    }

    Console.WriteLine($"    hour_schedule byte dist: total={hTotal}  val0={hZero}({100.0*hZero/hTotal:F1}%)  val1={hOne}({100.0*hOne/hTotal:F1}%)  other={hOther}  distinct_byte_vals={hourByteSet.Count}");

    if (weightAmongActive.Count > 0)
    {
        Console.WriteLine($"    weight@+1C among valid-SID records={validSoundIds}:");
        foreach (var kv in weightAmongActive.OrderByDescending(k=>k.Value).Take(5))
        {
            float f = BitConverter.Int32BitsToSingle((int)kv.Key);
            Console.WriteLine($"      0x{kv.Key:X8}(f32={f:G5}): {kv.Value} ({100.0*kv.Value/validSoundIds:F1}%)");
        }
    }
    if (unk36Stats.Count > 0 && validSoundIds > 0)
    {
        Console.WriteLine($"    unknown36@+24 among valid-SID records:");
        foreach (var kv in unk36Stats.OrderByDescending(k=>k.Value).Take(5))
        {
            float f = BitConverter.Int32BitsToSingle((int)kv.Key);
            Console.WriteLine($"      0x{kv.Key:X8}(f32={f:G5}): {kv.Value} ({100.0*kv.Value/validSoundIds:F1}%)");
        }
    }
    if (radiusStats.Count > 0 && validSoundIds > 0)
    {
        Console.WriteLine($"    radius@+2C among valid-SID records:");
        foreach (var kv in radiusStats.OrderByDescending(k=>k.Value).Take(5))
        {
            float f = BitConverter.Int32BitsToSingle((int)kv.Key);
            Console.WriteLine($"      0x{kv.Key:X8}(f32={f:G5}): {kv.Value} ({100.0*kv.Value/validSoundIds:F1}%)");
        }
    }
}

// ── Confirm hour_schedule semantics (all-zeros vs all-ones pattern) ─────────
Console.WriteLine("\n=== HOUR_SCHEDULE SEMANTICS (per-record patterns across all tables) ===");
foreach (var ext in new[] { ".bgm", ".bge", ".eff" })
{
    var hourPats = new Dictionary<string, long>(); // pattern summary → count
    long totalActive = 0;
    foreach (var path in byExt[ext])
    {
        var raw = archive.GetFileContent(path);
        if (raw.Length != EXPECTED_FILE_SIZE) continue;
        var span = raw.Span;
        for (int r = 0; r < RECORD_COUNT; r++)
        {
            int off = r * DATA_STRIDE;
            uint sid = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(off));
            if (sid < SOUND_ID_MIN || sid > SOUND_ID_MAX) continue;
            totalActive++;
            // Classify the 24 hour bytes
            bool allOnes = true, allZeros = true, hasMixed = false;
            for (int b = 4; b < 28; b++)
            {
                byte hb = span[off + b];
                if (hb != 1) allOnes = false;
                if (hb != 0) allZeros = false;
                if (hb != 0 && hb != 1) hasMixed = true;
            }
            string pat = allOnes ? "all_1" : allZeros ? "all_0" : hasMixed ? "has_other_values" : "mixed_0_and_1";
            hourPats.TryGetValue(pat, out long c); hourPats[pat] = c + 1;
        }
    }
    Console.WriteLine($"  {ext}: {totalActive} active records");
    foreach (var kv in hourPats.OrderByDescending(k=>k.Value))
        Console.WriteLine($"    hour_schedule_pattern={kv.Key}: {kv.Value} ({100.0*kv.Value/totalActive:F1}%)");
}

// ── WLK/RUN: confirm all-null in main data section ─────────────────────────
Console.WriteLine("\n=== WLK/RUN: valid sound IDs in main 12288-byte data section ===");
foreach (var ext in new[] { ".wlk", ".run" })
{
    long valid = 0, nullCount = 0, other = 0;
    foreach (var path in byExt[ext])
    {
        var raw = archive.GetFileContent(path);
        if (raw.Length != EXPECTED_FILE_SIZE) continue;
        var span = raw.Span;
        for (int r = 0; r < RECORD_COUNT; r++)
        {
            uint sid = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(r * DATA_STRIDE));
            if (sid == 0) nullCount++;
            else if (sid >= SOUND_ID_MIN && sid <= SOUND_ID_MAX) valid++;
            else other++;
        }
    }
    Console.WriteLine($"  {ext}: null={nullCount}  valid9x={valid}  other_nonzero={other}");
}

// ── Trailer (1024 bytes @12288) structure census ─────────────────────────────
Console.WriteLine("\n=== TRAILING 1024-BYTE BLOCK CENSUS ===");
Console.WriteLine("  Block @offset 12288..13311 (1024B = 256 u32 dwords)");
Console.WriteLine("  Hypothesis A: per-record bool activation map (0=inactive, 1=active)");
Console.WriteLine("  Hypothesis B: per-record mud tile use count");

foreach (var ext in soundExts)
{
    var paths = byExt[ext];
    if (paths.Count == 0) continue;

    // For each position 0..255 in the trailer, collect distinct values
    var byPos = new Dictionary<uint, long>[256];
    for (int i = 0; i < 256; i++) byPos[i] = [];

    long matchesBoolHyp = 0, totalTrailerDwords = 0;
    var distinctTrailerVals = new HashSet<uint>();

    foreach (var path in paths)
    {
        var raw = archive.GetFileContent(path);
        if (raw.Length != EXPECTED_FILE_SIZE) continue;
        var span = raw.Span.Slice(DATA_SIZE, TRAILER_SIZE);

        // Check if trailer values match bool hypothesis (0 or 1 only)
        for (int di = 0; di < 256; di++)
        {
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(di * 4));
            totalTrailerDwords++;
            distinctTrailerVals.Add(v);
            byPos[di].TryGetValue(v, out long c); byPos[di][v] = c + 1;
            if (v == 0 || v == 1) matchesBoolHyp++;
        }
    }

    long boolMatchPct = totalTrailerDwords > 0 ? 100 * matchesBoolHyp / totalTrailerDwords : 0;
    Console.WriteLine($"\n  {ext}: distinct_trailer_u32_vals={distinctTrailerVals.Count}  bool_match(0or1)={matchesBoolHyp}/{totalTrailerDwords}({boolMatchPct}%)");

    // Top non-zero trailer values
    var nonZeroVals = distinctTrailerVals.Where(v => v != 0).OrderBy(v => v).Take(10).ToList();
    Console.WriteLine($"    non-zero distinct vals (top 10): [{string.Join(", ", nonZeroVals.Select(v => $"0x{v:X8}={v}"))}]");

    // Cross-check: for each record index 0..255, does trailer[r] match (sid != 0)?
    // Sample: first 3 files
    int crossCheckFiles = 0;
    long truePos = 0, trueNeg = 0, falsePos = 0, falseNeg = 0;
    foreach (var path in paths.Take(3))
    {
        var raw = archive.GetFileContent(path);
        if (raw.Length != EXPECTED_FILE_SIZE) continue;
        crossCheckFiles++;
        var dataSpan = raw.Span;
        var trailerSpan = raw.Span.Slice(DATA_SIZE, TRAILER_SIZE);
        for (int r = 0; r < RECORD_COUNT; r++)
        {
            uint sid = BinaryPrimitives.ReadUInt32LittleEndian(dataSpan.Slice(r * DATA_STRIDE));
            bool dataActive = sid >= SOUND_ID_MIN && sid <= SOUND_ID_MAX;
            uint trailerDword = BinaryPrimitives.ReadUInt32LittleEndian(trailerSpan.Slice(r * 4));
            bool trailerActive = trailerDword != 0;
            if (dataActive && trailerActive) truePos++;
            else if (!dataActive && !trailerActive) trueNeg++;
            else if (!dataActive && trailerActive) falsePos++;
            else falseNeg++;
        }
    }
    if (crossCheckFiles > 0)
        Console.WriteLine($"    cross-check (data active vs trailer nonzero) in {crossCheckFiles} files: TP={truePos} TN={trueNeg} FP={falsePos} FN={falseNeg}");
}

// ── EFF: pos_x/pos_z/radius among valid-SID records ──────────────────────────
Console.WriteLine("\n=== EFF: POS_X/POS_Z/RADIUS FIELD STATS (valid-SID records only, stride=48) ===");
{
    float minX = float.MaxValue, maxX = float.MinValue;
    float minZ = float.MaxValue, maxZ = float.MinValue;
    float minR = float.MaxValue, maxR = float.MinValue;
    float minU36 = float.MaxValue, maxU36 = float.MinValue;
    long effActive = 0, posXZNonZero = 0, radNonZero = 0;

    foreach (var path in byExt[".eff"])
    {
        var raw = archive.GetFileContent(path);
        if (raw.Length != EXPECTED_FILE_SIZE) continue;
        var span = raw.Span;
        for (int r = 0; r < RECORD_COUNT; r++)
        {
            int off = r * DATA_STRIDE;
            uint sid = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(off));
            if (sid < SOUND_ID_MIN || sid > SOUND_ID_MAX) continue;
            effActive++;

            float px  = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(off + 0x20));
            float u36 = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(off + 0x24));
            float pz  = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(off + 0x28));
            float rad = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(off + 0x2C));

            if (px < minX) minX = px; if (px > maxX) maxX = px;
            if (u36 < minU36) minU36 = u36; if (u36 > maxU36) maxU36 = u36;
            if (pz < minZ) minZ = pz; if (pz > maxZ) maxZ = pz;
            if (rad < minR) minR = rad; if (rad > maxR) maxR = rad;

            if (px != 0 || pz != 0) posXZNonZero++;
            if (rad != 0) radNonZero++;
        }
    }

    Console.WriteLine($"  EFF valid-SID records: {effActive}");
    Console.WriteLine($"  pos_x @+20: range=[{minX:G5} .. {maxX:G5}]");
    Console.WriteLine($"  unknown36 @+24 (f32): range=[{minU36:G5} .. {maxU36:G5}]");
    Console.WriteLine($"  pos_z @+28: range=[{minZ:G5} .. {maxZ:G5}]");
    Console.WriteLine($"  radius @+2C: range=[{minR:G5} .. {maxR:G5}]");
    Console.WriteLine($"  Records with non-zero pos_x or pos_z: {posXZNonZero}/{effActive} ({100.0*posXZNonZero/effActive:F1}%)");
    Console.WriteLine($"  Records with non-zero radius: {radNonZero}/{effActive} ({100.0*radNonZero/effActive:F1}%)");
}

// ── BGM/BGE: confirm pos fields zero among valid-SID records ─────────────────
Console.WriteLine("\n=== BGM/BGE: POS/RADIUS FIELDS among valid-SID records ===");
foreach (var ext in new[] { ".bgm", ".bge" })
{
    long active = 0, nonZeroPosOrRad = 0;
    foreach (var path in byExt[ext])
    {
        var raw = archive.GetFileContent(path);
        if (raw.Length != EXPECTED_FILE_SIZE) continue;
        var span = raw.Span;
        for (int r = 0; r < RECORD_COUNT; r++)
        {
            int off = r * DATA_STRIDE;
            uint sid = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(off));
            if (sid < SOUND_ID_MIN || sid > SOUND_ID_MAX) continue;
            active++;
            uint px  = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(off + 0x20));
            uint u36 = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(off + 0x24));
            uint pz  = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(off + 0x28));
            uint rad = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(off + 0x2C));
            if (px != 0 || u36 != 0 || pz != 0 || rad != 0) nonZeroPosOrRad++;
        }
    }
    Console.WriteLine($"  {ext}: active={active}  records_with_nonzero_pos_or_rad={nonZeroPosOrRad} ({100.0*nonZeroPosOrRad/(active>0?active:1):F1}%)");
}

// ── Sound ID range census (valid records only, stride=48) ─────────────────────
Console.WriteLine("\n=== SOUND_ENTRY_ID RANGE (valid 9xxxxxxxx records, all tables) ===");
{
    uint globalMin = uint.MaxValue, globalMax = 0;
    var distinctIds = new HashSet<uint>();

    foreach (var ext in soundExts)
    {
        foreach (var path in byExt[ext])
        {
            var raw = archive.GetFileContent(path);
            if (raw.Length != EXPECTED_FILE_SIZE) continue;
            var span = raw.Span;
            for (int r = 0; r < RECORD_COUNT; r++)
            {
                uint sid = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(r * DATA_STRIDE));
                if (sid < SOUND_ID_MIN || sid > SOUND_ID_MAX) continue;
                distinctIds.Add(sid);
                if (sid < globalMin) globalMin = sid;
                if (sid > globalMax) globalMax = sid;
            }
        }
    }

    Console.WriteLine($"  Total distinct valid sound_entry_ids: {distinctIds.Count}");
    Console.WriteLine($"  Global min={globalMin}  max={globalMax}");
    var sample = distinctIds.OrderBy(x => x).Take(15).ToList();
    Console.WriteLine($"  Sample (first 15): [{string.Join(", ", sample)}]");
    var highGroups = distinctIds.GroupBy(x => x / 100_000_000u).OrderBy(g => g.Key);
    Console.WriteLine("  High-digit groups:");
    foreach (var g in highGroups)
        Console.WriteLine($"    {g.Key}xxxxxxxx: {g.Count()} distinct IDs");
}

Console.WriteLine("\nDone.");
return 0;
