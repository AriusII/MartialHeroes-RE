// do-census — THROWAWAY harness: full statistical census of all 12 per-class stance .do files.
// spec: Docs/RE/formats/ui_manifests.md §2.7
// Stride = 116 (0x74). Ground truth: iconSrcX @+0x18 and iconSrcY @+0x1C must be in [0..489]
// and multiples of 23 (mostly), cell size 23, sheet 512x512.
// Reads every offset 0x00..0x73 as u8/u16/u32/i16/float and produces per-field stats.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Encoding.GetEncoding(949); // register CP949

// --- VFS mount ---
string infPath = "D:/MartialHeroesClient/data.inf", vfsPath = "D:/MartialHeroesClient/data/data.vfs";
foreach (string root in DefaultClientRoots())
{
    string ci = Path.Combine(root, "data.inf"), cv = Path.Combine(root, "data", "data.vfs");
    if (File.Exists(ci) && File.Exists(cv)) { infPath = ci; vfsPath = cv; break; }
}
using MappedVfsArchive archive = MappedVfsArchive.Open(infPath, vfsPath);
Console.WriteLine($"Mounted {archive.GetEntries().Length:N0} entries");

// --- The 12 per-class stance .do files ---
string[] doFiles = {
    "data/script/musajung.do",
    "data/script/musasa.do",
    "data/script/musama.do",
    "data/script/assasinjung.do",
    "data/script/assasinsa.do",
    "data/script/assasinma.do",
    "data/script/wizardjung.do",
    "data/script/wizardsa.do",
    "data/script/wizardma.do",
    "data/script/monkjung.do",
    "data/script/monksa.do",
    "data/script/monkma.do",
};

const int Stride = 0x74; // 116 bytes per record
const int Cell = 23, Sheet = 512, MaxCoord = Sheet - Cell; // 489
const int NumOffsets = Stride; // 0x00..0x73

// Per-dword-offset (0,4,8,...,112) stats across all records across all 12 files
// We'll track u32, and also interpret as i16/u16 at each 2-byte aligned offset, and float at 4-byte aligned
// Key offsets we know:
//   +0x00 u32 instanceKey (large, sequential)
//   +0x04 u32 groupSubIndex (small 0..N)
//   +0x08 u32 slotIndex (sequential)
//   +0x0C u32 classStanceRef (1001/1002/1003 or similar)
//   +0x10 u32 groupId
//   +0x14 u16 (secondary X variant)
//   +0x16 u16 (unknown)
//   +0x18 i16 iconSrcX (0..489, multiples of 23 mostly)
//   +0x1A u16 (padding?)
//   +0x1C i16 iconSrcY
//   +0x1E u16 (padding?)
//   +0x20..0x73 unmapped

// We collect all 116 bytes per record for multi-interpretation

int totalRecords = 0;
int totalFiles = 0;

// Stats per byte-offset: for u8, u16, u32, i16, float
// u8 stats
long[] sumU8 = new long[NumOffsets];
int[] minU8 = new int[NumOffsets], maxU8 = new int[NumOffsets];
long[] zeroU8 = new long[NumOffsets];
long[] recCountU8 = new long[NumOffsets];
var distinctU8 = new HashSet<byte>[NumOffsets];
for (int i = 0; i < NumOffsets; i++) { distinctU8[i] = new HashSet<byte>(); minU8[i] = 255; maxU8[i] = 0; }

// u16 at each 2-byte aligned offset (58 offsets: 0,2,4,...,114)
const int NumU16 = Stride / 2; // 58
long[] sumU16 = new long[NumU16];
int[] minU16 = new int[NumU16], maxU16 = new int[NumU16];
long[] zeroU16 = new long[NumU16];
var distinctU16 = new HashSet<ushort>[NumU16];
for (int i = 0; i < NumU16; i++) { distinctU16[i] = new HashSet<ushort>(); minU16[i] = 65535; maxU16[i] = 0; }

// i16 at each 2-byte aligned offset
int[] miniI16 = new int[NumU16], maxiI16 = new int[NumU16];
for (int i = 0; i < NumU16; i++) { miniI16[i] = 32767; maxiI16[i] = -32768; }

// u32 at each 4-byte aligned offset (29 offsets: 0,4,8,...,112)
const int NumU32 = Stride / 4; // 29
long[] sumU32 = new long[NumU32];
uint[] minU32 = new uint[NumU32], maxU32 = new uint[NumU32];
long[] zeroU32 = new long[NumU32];
var distinctU32 = new HashSet<uint>[NumU32];
for (int i = 0; i < NumU32; i++) { distinctU32[i] = new HashSet<uint>(); minU32[i] = uint.MaxValue; maxU32[i] = 0; }

// float at each 4-byte aligned offset
float[] minF = new float[NumU32], maxF = new float[NumU32];
bool[] floatValid = new bool[NumU32]; // has any valid non-NaN/Inf float?
for (int i = 0; i < NumU32; i++) { minF[i] = float.MaxValue; maxF[i] = float.MinValue; }

// per-file record counts
var fileRecordCounts = new List<(string path, int size, int records, int tail)>();

// collect all records
var allRecords = new List<byte[]>();

Console.WriteLine("\n=== Loading all 12 .do files ===");
foreach (string path in doFiles)
{
    if (!archive.Contains(path)) { Console.WriteLine($"  MISSING: {path}"); continue; }
    ReadOnlyMemory<byte> mem = archive.GetFileContent(path);
    ReadOnlySpan<byte> raw = mem.Span;
    int nrec = raw.Length / Stride;
    int tail = raw.Length % Stride;
    fileRecordCounts.Add((path, raw.Length, nrec, tail));
    Console.WriteLine($"  {path}: {raw.Length:N0} bytes, {nrec} records, tail={tail}");
    totalFiles++;
    totalRecords += nrec;

    for (int i = 0; i < nrec; i++)
    {
        ReadOnlySpan<byte> r = raw.Slice(i * Stride, Stride);
        byte[] copy = r.ToArray();
        allRecords.Add(copy);

        // u8
        for (int o = 0; o < NumOffsets; o++)
        {
            byte v = copy[o];
            sumU8[o] += v;
            if (v < minU8[o]) minU8[o] = v;
            if (v > maxU8[o]) maxU8[o] = v;
            if (v == 0) zeroU8[o]++;
            recCountU8[o]++;
            distinctU8[o].Add(v);
        }

        // u16 / i16
        for (int w = 0; w < NumU16; w++)
        {
            int o = w * 2;
            ushort u = BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(o, 2));
            short s = BinaryPrimitives.ReadInt16LittleEndian(r.Slice(o, 2));
            sumU16[w] += u;
            if (u < minU16[w]) minU16[w] = u;
            if (u > maxU16[w]) maxU16[w] = u;
            if (u == 0) zeroU16[w]++;
            distinctU16[w].Add(u);
            if (s < miniI16[w]) miniI16[w] = s;
            if (s > maxiI16[w]) maxiI16[w] = s;
        }

        // u32 / float
        for (int d = 0; d < NumU32; d++)
        {
            int o = d * 4;
            uint u = BinaryPrimitives.ReadUInt32LittleEndian(r.Slice(o, 4));
            float f = BitConverter.Int32BitsToSingle((int)u);
            sumU32[d] += u;
            if (u < minU32[d]) minU32[d] = u;
            if (u > maxU32[d]) maxU32[d] = u;
            if (u == 0) zeroU32[d]++;
            distinctU32[d].Add(u);
            if (!float.IsNaN(f) && !float.IsInfinity(f))
            {
                if (f < minF[d]) minF[d] = f;
                if (f > maxF[d]) maxF[d] = f;
                floatValid[d] = true;
            }
        }
    }
}

Console.WriteLine($"\nTotal: {totalFiles} files, {totalRecords} records");

// === XDB cross-check ranges ===
// msg.xdb IDs: 1..74509
const int XdbMin = 1, XdbMax = 74509;
// level range guess: 1..120
const int LevelMax = 120;
// MP cost guess: 0..10000
const int MpMax = 10000;
// range/cooldown guess: 0..100000

Console.WriteLine("\n=== PER-OFFSET STATS (u32 at 4-byte boundaries) ===");
Console.WriteLine($"{"off":>5} {"hex":>6} {"#dist":>6} {"min":>12} {"max":>12} {"zero%":>7} {"avg":>12}  {"best-type":>12}  floatRange  hypothesis");
Console.WriteLine(new string('-', 130));

// Build per-dword summary
for (int d = 0; d < NumU32; d++)
{
    int o = d * 4;
    long n = totalRecords;
    if (n == 0) continue;

    uint mn = minU32[d], mx = maxU32[d];
    double avg = sumU32[d] / (double)n;
    double zp = 100.0 * zeroU32[d] / n;
    int ndist = distinctU32[d].Count;

    // Interpret as i16 pair
    int w = d * 2;
    int mn16a = miniI16[w], mx16a = maxiI16[w];
    int mn16b = miniI16[w + 1], mx16b = maxiI16[w + 1];

    // Best-type heuristic
    string bestType, hyp;
    if (o == 0x00) { bestType = "u32-large"; hyp = "instanceKey (primary map key)"; }
    else if (o == 0x04) { bestType = "u32-small"; hyp = "groupSubIndex (0..N)"; }
    else if (o == 0x08) { bestType = "u32-seq"; hyp = "slotIndex (secondary map key)"; }
    else if (o == 0x0C) { bestType = "u32-enum"; hyp = "classStanceRef (1001..1012)"; }
    else if (o == 0x10) { bestType = "u32-med"; hyp = "groupId (skill family)"; }
    else if (o == 0x14) { bestType = "u16-pair"; hyp = "+0x14 u16 secXvar / +0x16 u16 unknown"; }
    else if (o == 0x18) { bestType = "i16-pair"; hyp = "iconSrcX @+0x18 (CONFIRMED) / +0x1A padding"; }
    else if (o == 0x1C) { bestType = "i16-pair"; hyp = "iconSrcY @+0x1C (CONFIRMED) / +0x1E padding"; }
    else if (o == 0x20) { bestType = "u16-pair"; hyp = "secondarySpriteX @+0x20 / +0x22 unknown"; }
    else if (o == 0x24) { bestType = "u16-pair"; hyp = "secondarySpriteY @+0x24 / +0x26 unknown"; }
    else
    {
        // Heuristic: small distinct count = enum; range 1..120 = level; large sequential = ID
        if (ndist <= 8 && mx <= 20) bestType = "enum";
        else if (mn >= 1 && mx <= LevelMax && ndist <= LevelMax) bestType = "u8-level?";
        else if (mn <= MpMax && mx <= MpMax) bestType = "u16-cost?";
        else if (mn32IsFloatLike(mn, mx)) bestType = "float?";
        else bestType = "u32-unk";
        hyp = InferHypothesis(o, d, mn, mx, ndist, zp, avg, mn16a, mx16a, mn16b, mx16b, XdbMin, XdbMax);
    }

    // float range string
    string frange = floatValid[d] ? $"{minF[d]:F2}..{maxF[d]:F2}" : "no-valid-float";

    Console.WriteLine($"{o,5:X2}h {o,4} {ndist,6} {mn,12} {mx,12} {zp,6:F1}% {avg,12:F1}  {bestType,12}  {frange,-20} {hyp}");
}

// === PER-WORD STATS (u16 at 2-byte boundaries) ===
Console.WriteLine("\n=== PER-WORD STATS (u16 at 2-byte boundaries, odd-aligned too) ===");
Console.WriteLine($"{"byteOff":>7} {"#dist":>6} {"min(u16)":>10} {"max(u16)":>10} {"min(i16)":>10} {"max(i16)":>10} {"zero%":>7}  notes");
Console.WriteLine(new string('-', 100));
for (int w = 0; w < NumU16; w++)
{
    int o = w * 2;
    long n = totalRecords;
    double zp = 100.0 * zeroU16[w] / n;
    int ndist = distinctU16[w].Count;
    string notes = "";
    if (o == 0x14) notes = "secX-variant (SAMPLE-VERIFIED)";
    else if (o == 0x16) notes = "unknown u16";
    else if (o == 0x18) notes = "** iconSrcX (CODE-CONFIRMED) **";
    else if (o == 0x1A) notes = "padding after iconSrcX?";
    else if (o == 0x1C) notes = "** iconSrcY (CODE-CONFIRMED) **";
    else if (o == 0x1E) notes = "padding after iconSrcY?";
    else if (o == 0x20) notes = "secondarySpriteX";
    else if (o == 0x22) notes = "padding?";
    else if (o == 0x24) notes = "secondarySpriteY";
    Console.WriteLine($"  0x{o:X2} {o,3}  {ndist,6} {minU16[w],10} {maxU16[w],10} {miniI16[w],10} {maxiI16[w],10} {zp,6:F1}%  {notes}");
}

// === PER-BYTE STATS ===
Console.WriteLine("\n=== PER-BYTE ZERO RATIOS (highlight near-100% zero = likely padding) ===");
for (int o = 0; o < NumOffsets; o++)
{
    long n = totalRecords;
    double zp = 100.0 * zeroU8[o] / n;
    int ndist = distinctU8[o].Count;
    if (zp >= 90.0)
        Console.WriteLine($"  byte 0x{o:X2} ({o,3}): zero={zp:F1}%  ndist={ndist}  min={minU8[o]}  max={maxU8[o]}  => likely padding/unused");
}

// === DETAILED BREAKDOWN OF KNOWN REGIONS (0x00..0x27) ===
Console.WriteLine("\n=== KNOWN-REGION DETAIL (0x00..0x27) — per-file breakdown ===");
Console.WriteLine("Verifying iconSrcX/Y range and grid alignment across all 12 files...");

int totalValidIcon = 0;
int totalIconRecords = 0;
var xdbHitCounts = new Dictionary<int, int>(); // offset -> hit count of XDB-range values

foreach (string path in doFiles)
{
    if (!archive.Contains(path)) continue;
    ReadOnlyMemory<byte> mem = archive.GetFileContent(path);
    ReadOnlySpan<byte> raw = mem.Span;
    int nrec = raw.Length / Stride;
    var xSet = new SortedSet<int>();
    var ySet = new SortedSet<int>();
    int validIcon = 0;
    var d3set = new SortedSet<uint>();

    for (int i = 0; i < nrec; i++)
    {
        ReadOnlySpan<byte> r = raw.Slice(i * Stride, Stride);
        short sx = BinaryPrimitives.ReadInt16LittleEndian(r.Slice(0x18, 2));
        short sy = BinaryPrimitives.ReadInt16LittleEndian(r.Slice(0x1C, 2));
        uint d3 = BinaryPrimitives.ReadUInt32LittleEndian(r.Slice(0x0C, 4));
        d3set.Add(d3);
        if (sx >= 0 && sx <= MaxCoord && sy >= 0 && sy <= MaxCoord) { xSet.Add(sx); ySet.Add(sy); validIcon++; }
    }
    totalValidIcon += validIcon;
    totalIconRecords += nrec;

    string d3str = string.Join(",", d3set.Take(5)) + (d3set.Count > 5 ? "..." : "");
    Console.WriteLine($"  {Path.GetFileName(path),-20}: {nrec,4} rec  iconValid={validIcon,4}  classStanceRef(d3)=[{d3str}]  xVals={xSet.Count}  yVals={ySet.Count}");
    if (xSet.Count > 0)
    {
        Console.WriteLine($"    xSet: {string.Join(",", xSet.Take(22))}");
        Console.WriteLine($"    ySet: {string.Join(",", ySet.Take(22))}");
        Console.WriteLine($"    allX%23==0: {xSet.All(v => v % 23 == 0),-5}  allY%23==0: {ySet.All(v => v % 23 == 0)}");
    }
}
Console.WriteLine($"\n  TOTAL: {totalIconRecords} records, {totalValidIcon} with valid iconXY ({100.0*totalValidIcon/totalIconRecords:F1}%)");

// === SCAN UNMAPPED REGION 0x28..0x73 for XDB-range hits ===
Console.WriteLine("\n=== UNMAPPED REGION 0x28..0x73: scan for XDB-range u32 values ===");
Console.WriteLine($"XDB range: {XdbMin}..{XdbMax}");
Console.WriteLine($"{"offset":>6} {"hex":>5} {"#dist":>6} {"min-u32":>10} {"max-u32":>10} {"zero%":>7}  {"xdb-range-hits%":>15}  hypothesis");
Console.WriteLine(new string('-', 110));

for (int d = 10; d < NumU32; d++) // 0x28 = offset 40 = dword index 10
{
    int o = d * 4;
    if (o < 0x28) continue;
    long n = totalRecords;
    uint mn = minU32[d], mx = maxU32[d];
    double zp = 100.0 * zeroU32[d] / n;
    int ndist = distinctU32[d].Count;

    // Count XDB-range hits
    int xdbHits = 0;
    foreach (uint v in distinctU32[d])
        if (v >= XdbMin && v <= XdbMax) xdbHits += (int)Math.Min(n, distinctU32[d].Count); // approx
    // Better: count records in range
    // We need per-record data: re-scan
    // Actually we stored distinctU32 not per-record counts; recompute from allRecords
    int xdbHitRecs = 0;
    foreach (byte[] rec in allRecords)
    {
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(rec.AsSpan(o, 4));
        if (v >= XdbMin && v <= (uint)XdbMax) xdbHitRecs++;
    }
    double xdbPct = 100.0 * xdbHitRecs / n;

    // Heuristic hypothesis
    string hyp;
    if (zp > 95.0 && ndist <= 3) hyp = "likely padding/unused";
    else if (xdbPct > 80.0) hyp = "LIKELY xdb string id";
    else if (mx <= LevelMax && mn >= 0) hyp = "u8-small: level/count/rank?";
    else if (mx <= MpMax) hyp = "u16-cost: MP/HP cost?";
    else if (mn == mx && ndist == 1) hyp = "constant";
    else if (zp > 50.0 && ndist <= 10) hyp = "sparse enum / optional flag";
    else hyp = "unknown";

    Console.WriteLine($"  0x{o:X2} {o,4} {ndist,6} {mn,10} {mx,10} {zp,6:F1}%  {xdbPct,14:F1}%  {hyp}");
}

// === u16 scan of unmapped region ===
Console.WriteLine("\n=== UNMAPPED REGION 0x28..0x73: u16 pairs ===");
Console.WriteLine($"{"off":>5} {"#dist":>6} {"min-u16":>8} {"max-u16":>8} {"min-i16":>8} {"max-i16":>8} {"zero%":>7}  notes");
Console.WriteLine(new string('-', 85));
for (int w = 0x28 / 2; w < NumU16; w++)
{
    int o = w * 2;
    long n = totalRecords;
    double zp = 100.0 * zeroU16[w] / n;
    int ndist = distinctU16[w].Count;
    string note = "";
    if (ndist <= 5 && maxU16[w] <= 20) note = "likely enum/flag";
    else if (minU16[w] >= 1 && maxU16[w] <= LevelMax && ndist <= LevelMax) note = "level-range?";
    else if (maxU16[w] <= MpMax) note = "cost-range?";
    else if (zp > 95.0) note = "mostly-zero";
    Console.WriteLine($"  0x{o:X2} {o,3}  {ndist,6} {minU16[w],8} {maxU16[w],8} {miniI16[w],8} {maxiI16[w],8} {zp,6:F1}%  {note}");
}

// === FLOAT SCAN ===
Console.WriteLine("\n=== FLOAT-PLAUSIBLE DWORDS (non-NaN, non-Inf, finite range) ===");
for (int d = 0; d < NumU32; d++)
{
    int o = d * 4;
    if (!floatValid[d]) continue;
    // Only print if the float range looks game-plausible (e.g. 0..10000)
    if (minF[d] < -1e6f || maxF[d] > 1e8f) continue;
    Console.WriteLine($"  0x{o:X2} ({o,3}): float range {minF[d]:F3}..{maxF[d]:F3}  (u32 range {minU32[d]}..{maxU32[d]})");
}

// === RECORD FIRST-16 SAMPLE (musajung.do, first 16 records) ===
Console.WriteLine("\n=== SAMPLE: musajung.do first 16 records, full 116-byte hex dump ===");
{
    ReadOnlyMemory<byte> mem = archive.GetFileContent("data/script/musajung.do");
    ReadOnlySpan<byte> raw = mem.Span;
    int nshow = Math.Min(16, raw.Length / Stride);
    for (int i = 0; i < nshow; i++)
    {
        ReadOnlySpan<byte> r = raw.Slice(i * Stride, Stride);
        Console.Write($"  rec{i,3}: ");
        for (int b = 0; b < Stride; b++)
        {
            if (b > 0 && b % 16 == 0) Console.Write("\n         ");
            Console.Write($"{r[b]:X2} ");
        }
        Console.WriteLine();
        // Print parsed known fields
        uint d0 = BinaryPrimitives.ReadUInt32LittleEndian(r[0x00..0x04]);
        uint d1 = BinaryPrimitives.ReadUInt32LittleEndian(r[0x04..0x08]);
        uint d2 = BinaryPrimitives.ReadUInt32LittleEndian(r[0x08..0x0C]);
        uint d3 = BinaryPrimitives.ReadUInt32LittleEndian(r[0x0C..0x10]);
        uint d4 = BinaryPrimitives.ReadUInt32LittleEndian(r[0x10..0x14]);
        short sx = BinaryPrimitives.ReadInt16LittleEndian(r.Slice(0x18, 2));
        short sy = BinaryPrimitives.ReadInt16LittleEndian(r.Slice(0x1C, 2));
        Console.WriteLine($"         -> instanceKey=0x{d0:X8}({d0}) groupSub={d1} slotIdx={d2} classRef={d3} grp={d4} iconX={sx} iconY={sy}");
    }
}

// === SCAN u32 0x28..0x73 for all values across first 80 records of musajung, full detail ===
Console.WriteLine("\n=== musajung.do dword-scan 0x28..0x73, first 40 records ===");
{
    ReadOnlyMemory<byte> mem = archive.GetFileContent("data/script/musajung.do");
    ReadOnlySpan<byte> raw = mem.Span;
    int nshow = Math.Min(40, raw.Length / Stride);
    // Header
    Console.Write($"  {"rec",3}");
    for (int d = 10; d < NumU32; d++) Console.Write($" {$"0x{d*4:X2}",6}");
    Console.WriteLine();
    for (int i = 0; i < nshow; i++)
    {
        ReadOnlySpan<byte> r = raw.Slice(i * Stride, Stride);
        Console.Write($"  {i,3}");
        for (int d = 10; d < NumU32; d++)
        {
            int o = d * 4;
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(r.Slice(o, 4));
            Console.Write($" {v,6}");
        }
        Console.WriteLine();
    }
}

// === CROSS-CHECK: scan u16 values 0x28..0x73 for small-enum and XDB-range values ===
Console.WriteLine("\n=== u16 scan of 0x28..0x73 across ALL files: small-value histogram ===");
// For each u16 position, show value frequency top-10
for (int w = 0x28 / 2; w < NumU16; w++)
{
    int o = w * 2;
    if (distinctU16[w].Count > 200) continue; // skip high-cardinality
    if (minU16[w] > 500 && maxU16[w] > 500) continue; // skip purely large
    // Build frequency histogram from allRecords
    var freq = new Dictionary<ushort, int>();
    foreach (byte[] rec in allRecords)
    {
        ushort v = BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(o, 2));
        freq.TryGetValue(v, out int cnt);
        freq[v] = cnt + 1;
    }
    var top = freq.OrderByDescending(kv => kv.Value).Take(10).ToList();
    Console.WriteLine($"  0x{o:X2} ({o,3}): {distinctU16[w],4} distinct  top vals: {string.Join(", ", top.Select(kv => $"{kv.Key}:{kv.Value}"))}");
}

Console.WriteLine("\nDone.");
return 0;

// ---- helpers ----
static bool mn32IsFloatLike(uint mn, uint mx)
{
    float f1 = BitConverter.Int32BitsToSingle((int)mn);
    float f2 = BitConverter.Int32BitsToSingle((int)mx);
    return !float.IsNaN(f1) && !float.IsInfinity(f1) && !float.IsNaN(f2) && !float.IsInfinity(f2)
        && f1 >= -1e6f && f2 <= 1e8f;
}

static string InferHypothesis(int o, int d, uint mn, uint mx, int ndist, double zp, double avg,
    int mn16a, int mx16a, int mn16b, int mx16b, int xdbMin, int xdbMax)
{
    if (zp > 98.0 && ndist <= 2) return "unused / padding";
    if (ndist == 1) return $"constant={mn}";
    if (mn >= (uint)xdbMin && mx <= (uint)xdbMax && ndist > 10) return "xdb string id candidate";
    if (mx <= 120 && mn >= 0 && ndist <= 120) return "level/rank/count (u8 range)";
    if (mx <= 10000 && zp < 80.0) return "cost/stat (u16 range)";
    if (mx <= 1000000 && zp < 50.0) return "timer/cooldown? (u32 range)";
    return "unknown";
}

static IEnumerable<string> DefaultClientRoots()
{
    string? env = Environment.GetEnvironmentVariable("MH_CLIENT_DIR");
    if (!string.IsNullOrWhiteSpace(env)) yield return env;
    for (DirectoryInfo? d = new(AppContext.BaseDirectory); d != null; d = d.Parent)
        yield return Path.Combine(d.FullName, "05.Presentation", "MartialHeroes.Client.Godot", "clientdata");
    yield return "D:/MartialHeroesClient";
}
