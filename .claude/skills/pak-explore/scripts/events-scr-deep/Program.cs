// events-scr-deep pass-2 — Deep drill into anomalous records + flag semantics
// Analyses:
//   A: reserved_a @0x08 — decode the 5 non-zero records as structured fields
//   B: flag_b/flag_c — arithmetic progression pattern
//   C: reserved_b @0x62 bit +2 — is it the same 5 records?
//   D: record_trailer {01 00 01 00 01 00 00 00} — decode as 3×u16 or 2×u32
//   E: ids_array_a sub-ranges — try to identify namespace buckets
//   F: event_id 10551-31704 gap analysis and the "step 10" claim
// THROWAWAY — not in solution.

using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

string infPath = "D:/MartialHeroesClient/data.inf";
string vfsPath = "D:/MartialHeroesClient/data/data.vfs";

string[] candidateRoots =
{
    Environment.GetEnvironmentVariable("MH_CLIENT_DIR") ?? "",
    @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata",
    @"D:\MartialHeroesClient",
};

foreach (string root in candidateRoots)
{
    if (string.IsNullOrEmpty(root)) continue;
    string ci = Path.Combine(root, "data.inf");
    string cv = Path.Combine(root, "data", "data.vfs");
    if (File.Exists(ci) && File.Exists(cv)) { infPath = ci; vfsPath = cv; break; }
}

using MappedVfsArchive archive = MappedVfsArchive.Open(infPath, vfsPath);
ReadOnlyMemory<byte> raw = archive.GetFileContent("data/script/events.scr");

const int Stride = 520;
int recordCount = raw.Length / Stride;

Console.WriteLine("=== PASS 2: Deep drill ===");
Console.WriteLine();

// ============================================================
// A: The 5 anomalous records (reserved_a non-zero)
//    Records 276-280: event_id 30133-30137
// ============================================================
Console.WriteLine("=== A: Anomalous records 276-280 full layout ===");

for (int recIdx = 276; recIdx <= 280; recIdx++)
{
    ReadOnlySpan<byte> rec = raw.Span.Slice(recIdx * Stride, Stride);

    uint eventId   = BinaryPrimitives.ReadUInt32LittleEndian(rec[0x00..]);
    ushort evType  = BinaryPrimitives.ReadUInt16LittleEndian(rec[0x04..]);
    ushort dayCount= BinaryPrimitives.ReadUInt16LittleEndian(rec[0x06..]);

    // reserved_a @0x08 — interpret as structured fields
    // From pass-1 output: non-zero at +0,+1,+2,+4,+8,+10,+14,+16,+18,+20,+22
    // Pattern: bytes @0x08: 40 42 0F 00 14/32/64/96/C8 00 00 00 01/24 00 90 00 ...
    // These look like u32 LE values:
    //   @0x08 u32: 40 42 0F 00 = 1,000,000 (dec) = 0x000F4240
    //   @0x0C u32: 14 00 00 00 = 20, or 32=50, 64=100, 96=150, C8=200
    //   @0x10 u32: 01 00 90 00 = 0x00900001 = 9437185? or 24 00 90 00
    // Let's decode all 68 bytes as 17×u32

    Console.WriteLine($"  rec#{recIdx} event_id={eventId} evType={evType} dayCount={dayCount}");

    // Decode reserved_a as u32 array
    Console.Write("    reserved_a as 17×u32 @0x08: ");
    for (int i = 0; i < 17; i++)
    {
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(0x08 + i * 4, 4));
        if (v != 0) Console.Write($"[{i}]={v} ");
    }
    Console.WriteLine();

    // Decode reserved_a as bytes for precise mapping
    Console.Write("    reserved_a hex @0x08..0x4B: ");
    Console.WriteLine(Convert.ToHexString(rec[0x08..0x4C]));

    uint levelMin = BinaryPrimitives.ReadUInt32LittleEndian(rec[0x4C..]);
    uint levelMax = BinaryPrimitives.ReadUInt32LittleEndian(rec[0x50..]);
    ushort flagB  = BinaryPrimitives.ReadUInt16LittleEndian(rec[0x54..]);
    ushort flagC  = BinaryPrimitives.ReadUInt16LittleEndian(rec[0x56..]);
    ushort flagD  = BinaryPrimitives.ReadUInt16LittleEndian(rec[0x58..]);
    ushort flagE  = BinaryPrimitives.ReadUInt16LittleEndian(rec[0x5A..]);
    ushort flagF  = BinaryPrimitives.ReadUInt16LittleEndian(rec[0x5C..]);
    ushort flagG  = BinaryPrimitives.ReadUInt16LittleEndian(rec[0x5E..]);
    ushort flagH  = BinaryPrimitives.ReadUInt16LittleEndian(rec[0x60..]);

    // reserved_b @0x62 (6 bytes)
    Console.Write($"    hex @0x62..0x67: ");
    Console.WriteLine(Convert.ToHexString(rec[0x62..0x68]));

    Console.WriteLine($"    level_min={levelMin} level_max={levelMax}");
    Console.WriteLine($"    flagB={flagB} flagC={flagC} flagD={flagD} flagE={flagE} flagF={flagF} flagG={flagG} flagH={flagH}");

    // ids_array_a: first 10 non-zero
    Console.Write("    ids_array_a (first 10 non-zero): ");
    int shown = 0;
    for (int slot = 0; slot < 50 && shown < 10; slot++)
    {
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(0x68 + slot * 4, 4));
        if (v != 0) { Console.Write($"{v} "); shown++; }
    }
    Console.WriteLine();

    // ids_array_b: first 10 non-zero
    Console.Write("    ids_array_b (first 10 non-zero): ");
    shown = 0;
    for (int slot = 0; slot < 52 && shown < 10; slot++)
    {
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(0x130 + slot * 4, 4));
        if (v != 0) { Console.Write($"{v} "); shown++; }
    }
    Console.WriteLine();

    // trailer
    Console.Write("    trailer @0x200: ");
    Console.WriteLine(Convert.ToHexString(rec[0x200..0x208]));
    Console.WriteLine();
}

// ============================================================
// B: flag_b/flag_c arithmetic progression
// ============================================================
Console.WriteLine("=== B: flag_b and flag_c arithmetic pattern ===");

// From pass-1: flag_b has values 0,1,13,25,37,49,61,73,85,97,109,121,133 each ×12 (except 0,1)
// flag_c has values 0,12,24,36,48,60,72,84,96,108,120,132,144 each ×12 (except 144)
// This looks like flag_b is the "day start" offset and flag_c is the "day end" offset or vice versa
// Let's look at flag_b + flag_c pairs per record

var pairDist = new SortedDictionary<string, int>();
for (int recIdx = 0; recIdx < recordCount; recIdx++)
{
    ReadOnlySpan<byte> rec = raw.Span.Slice(recIdx * Stride, Stride);
    uint id = BinaryPrimitives.ReadUInt32LittleEndian(rec[..4]);
    if (id == 0) continue;

    ushort fb = BinaryPrimitives.ReadUInt16LittleEndian(rec[0x54..]);
    ushort fc = BinaryPrimitives.ReadUInt16LittleEndian(rec[0x56..]);

    // Check: does fc = fb + 12 - 1? Or fc = fb - 1?
    string pair = $"b={fb,3} c={fc,3} (diff={fc - fb})";
    pairDist.TryGetValue(pair, out int cnt);
    pairDist[pair] = cnt + 1;
}

Console.WriteLine("  flag_b / flag_c pairs and their frequency:");
foreach ((string pair, int cnt) in pairDist.OrderBy(p => p.Key))
    Console.WriteLine($"    {pair} × {cnt}");

// ============================================================
// C: reserved_b @0x62 byte +2 = is it always the same 5 records?
// ============================================================
Console.WriteLine();
Console.WriteLine("=== C: reserved_b @0x62 byte +2 non-zero ===");

for (int recIdx = 0; recIdx < recordCount; recIdx++)
{
    ReadOnlySpan<byte> rec = raw.Span.Slice(recIdx * Stride, Stride);
    byte b = rec[0x64]; // @0x62 + 2 = 0x64
    if (b != 0)
    {
        uint id = BinaryPrimitives.ReadUInt32LittleEndian(rec[..4]);
        Console.WriteLine($"  rec#{recIdx} event_id={id}: @0x64 = 0x{b:X2} = {b}");
        Console.Write("    full reserved_b @0x62..0x67: ");
        Console.WriteLine(Convert.ToHexString(rec[0x62..0x68]));
    }
}

// ============================================================
// D: record_trailer — interpret as structured
// ============================================================
Console.WriteLine();
Console.WriteLine("=== D: record_trailer @0x200 dominant value ===");
Console.WriteLine("  Dominant: [01 00 01 00 01 00 00 00]");
Console.WriteLine("  As 4×u16 LE: 1, 1, 1, 0");
Console.WriteLine("  As 2×u32 LE: 65537 (0x00010001), 1 (0x00000001)... wait:");
{
    byte[] b = [0x01, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00];
    uint w0 = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(0, 4));
    uint w1 = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(4, 4));
    ushort s0 = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(0, 2));
    ushort s1 = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(2, 2));
    ushort s2 = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(4, 2));
    ushort s3 = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(6, 2));
    Console.WriteLine($"  As 2×u32 LE: w0={w0} (0x{w0:X8}), w1={w1} (0x{w1:X8})");
    Console.WriteLine($"  As 4×u16 LE: s0={s0}, s1={s1}, s2={s2}, s3={s3}");
    Console.WriteLine("  Interpretation: likely 3 separate u16 flags each=1, then u16 pad=0");
    Console.WriteLine("  Or: 2 u32s: first=0x00010001 (two u16 each=1), second=0x00000001");
}
// Which records have the zero trailer — check if they're the anomalous 5
Console.WriteLine("  Zero-trailer records:");
for (int recIdx = 0; recIdx < recordCount; recIdx++)
{
    ReadOnlySpan<byte> rec = raw.Span.Slice(recIdx * Stride, Stride);
    ReadOnlySpan<byte> trailer = rec[0x200..0x208];
    bool allZero = true;
    for (int b = 0; b < 8; b++) if (trailer[b] != 0) { allZero = false; break; }
    if (allZero)
    {
        uint id = BinaryPrimitives.ReadUInt32LittleEndian(rec[..4]);
        Console.WriteLine($"    rec#{recIdx} event_id={id}");
    }
}

// ============================================================
// E: ids_array_a sub-ranges — try to identify namespace
// ============================================================
Console.WriteLine();
Console.WriteLine("=== E: ids_array_a namespace sub-ranges ===");

// Pass-1 shows values mostly 500k-1M range, with some small integers (1, 2, 10, 70)
// and 1,000,000 itself. Let's look at the small-integer cluster more carefully.
Console.WriteLine("  Values < 1000:");
var smallVals = new SortedDictionary<uint, int>();
for (int recIdx = 0; recIdx < recordCount; recIdx++)
{
    ReadOnlySpan<byte> rec = raw.Span.Slice(recIdx * Stride, Stride);
    for (int slot = 0; slot < 50; slot++)
    {
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(0x68 + slot * 4, 4));
        if (v > 0 && v < 1000)
        {
            smallVals.TryGetValue(v, out int c);
            smallVals[v] = c + 1;
        }
    }
}
foreach ((uint v, int c) in smallVals)
    Console.WriteLine($"    {v,6} × {c}");

Console.WriteLine();
Console.WriteLine("  Values in 100k-500k range (first 30 distinct):");
var midVals = new SortedDictionary<uint, int>();
for (int recIdx = 0; recIdx < recordCount; recIdx++)
{
    ReadOnlySpan<byte> rec = raw.Span.Slice(recIdx * Stride, Stride);
    for (int slot = 0; slot < 50; slot++)
    {
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(0x68 + slot * 4, 4));
        if (v >= 100000 && v < 500000)
        {
            midVals.TryGetValue(v, out int c);
            midVals[v] = c + 1;
        }
    }
}
int shownMid = 0;
foreach ((uint v, int c) in midVals.OrderBy(p => p.Key))
{
    Console.WriteLine($"    {v,8} × {c}");
    if (++shownMid >= 30) { Console.WriteLine("    …"); break; }
}

Console.WriteLine();
Console.WriteLine("  Values in 500k-1M range — sample (every 10th distinct):");
var highVals = new SortedDictionary<uint, int>();
for (int recIdx = 0; recIdx < recordCount; recIdx++)
{
    ReadOnlySpan<byte> rec = raw.Span.Slice(recIdx * Stride, Stride);
    for (int slot = 0; slot < 50; slot++)
    {
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(0x68 + slot * 4, 4));
        if (v >= 500000 && v < 1000000)
        {
            highVals.TryGetValue(v, out int c);
            highVals[v] = c + 1;
        }
    }
}
int n = 0;
foreach ((uint v, int c) in highVals.OrderBy(p => p.Key))
{
    if (n++ % 10 == 0)
        Console.WriteLine($"    {v,8} × {c}");
}
Console.WriteLine($"  Total distinct in 500k-1M: {highVals.Count}");

// Check if ids_array_a values appear in ids_array_b of ANY record (cross-array check)
Console.WriteLine();
Console.WriteLine("  Cross-check: do any ids_array_a values appear in ids_array_b?");
var allIdsB = new HashSet<uint>();
for (int recIdx = 0; recIdx < recordCount; recIdx++)
{
    ReadOnlySpan<byte> rec = raw.Span.Slice(recIdx * Stride, Stride);
    for (int slot = 0; slot < 52; slot++)
    {
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(0x130 + slot * 4, 4));
        if (v != 0) allIdsB.Add(v);
    }
}

var allIdsA = new HashSet<uint>();
for (int recIdx = 0; recIdx < recordCount; recIdx++)
{
    ReadOnlySpan<byte> rec = raw.Span.Slice(recIdx * Stride, Stride);
    for (int slot = 0; slot < 50; slot++)
    {
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(0x68 + slot * 4, 4));
        if (v != 0) allIdsA.Add(v);
    }
}

var intersection = new HashSet<uint>(allIdsA);
intersection.IntersectWith(allIdsB);
Console.WriteLine($"  ids_array_a distinct: {allIdsA.Count}, ids_array_b distinct: {allIdsB.Count}");
Console.WriteLine($"  Intersection (same value in both arrays): {intersection.Count}");
if (intersection.Count > 0 && intersection.Count <= 20)
{
    Console.WriteLine($"  Overlapping values: {string.Join(", ", intersection.OrderBy(v => v))}");
}

// ============================================================
// F: event_id gap analysis
// ============================================================
Console.WriteLine();
Console.WriteLine("=== F: event_id gap analysis ===");

// Pass-1 showed gap=1 x1703, gap=10 x143, gap=18020 x1
// The claim was "step of 10" but actually step of 1 is dominant
// Let's see if groups of 10-step events cluster together with group of 1-step events

var gapToEventIds = new Dictionary<uint, List<(uint from, uint to)>>();
uint prevEventId = 0;
for (int recIdx = 0; recIdx < recordCount; recIdx++)
{
    ReadOnlySpan<byte> rec = raw.Span.Slice(recIdx * Stride, Stride);
    uint id = BinaryPrimitives.ReadUInt32LittleEndian(rec[..4]);
    if (recIdx > 0 && id != 0 && prevEventId != 0)
    {
        uint gap = id > prevEventId ? id - prevEventId : 0;
        if (gap > 0 && gap != 1)
        {
            if (!gapToEventIds.ContainsKey(gap)) gapToEventIds[gap] = new();
            gapToEventIds[gap].Add((prevEventId, id));
        }
    }
    prevEventId = id;
}

Console.WriteLine("  Non-unit gaps (gap > 1):");
foreach ((uint gap, var pairs) in gapToEventIds.OrderBy(p => p.Key))
{
    Console.Write($"    gap={gap}: ");
    Console.WriteLine(string.Join(", ", pairs.Take(5).Select(p => $"{p.from}→{p.to}")));
}

// Show first 20 records to understand the event_id sequencing
Console.WriteLine();
Console.WriteLine("  First 30 records: event_id, event_type, day_count, level_min, flag_e, ids_a_count:");
for (int recIdx = 0; recIdx < 30; recIdx++)
{
    ReadOnlySpan<byte> rec = raw.Span.Slice(recIdx * Stride, Stride);
    uint id = BinaryPrimitives.ReadUInt32LittleEndian(rec[0x00..]);
    ushort evType = BinaryPrimitives.ReadUInt16LittleEndian(rec[0x04..]);
    ushort dayCount = BinaryPrimitives.ReadUInt16LittleEndian(rec[0x06..]);
    uint levelMin = BinaryPrimitives.ReadUInt32LittleEndian(rec[0x4C..]);
    uint levelMax = BinaryPrimitives.ReadUInt32LittleEndian(rec[0x50..]);
    ushort flagB = BinaryPrimitives.ReadUInt16LittleEndian(rec[0x54..]);
    ushort flagC = BinaryPrimitives.ReadUInt16LittleEndian(rec[0x56..]);
    ushort flagE = BinaryPrimitives.ReadUInt16LittleEndian(rec[0x5A..]);

    int idsACount = 0;
    for (int slot = 0; slot < 50; slot++)
    {
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(0x68 + slot * 4, 4));
        if (v != 0) idsACount++;
    }

    Console.WriteLine($"    rec#{recIdx,4}: id={id,6} type={evType} days={dayCount} lv={levelMin}-{levelMax} fb={flagB,3} fc={flagC,3} fe={flagE} idsA={idsACount}");
}

Console.WriteLine();
Console.WriteLine("=== DONE PASS 2 ===");
return 0;
