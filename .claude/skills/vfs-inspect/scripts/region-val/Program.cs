// region-val: THROWAWAY harness — V13 lane, CAMPAIGN VFS-MASTERY Phase V
// Validates region*.bin, regiontable*.bin, mapsetting.scr against region_grid.md spec.
// NOT a solution member. Run: dotnet run -c Release --project <this dir>
// Writes stats ONLY — no raw payloads, no committed bytes.

using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var cp949 = Encoding.GetEncoding(949);

// --- Locate client ---
string infPath = "", vfsPath = "";
foreach (var root in new[]
{
    Environment.GetEnvironmentVariable("MH_CLIENT_DIR") ?? "",
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..",
        "05.Presentation", "MartialHeroes.Client.Godot", "clientdata"),
    @"D:\MartialHeroesClient",
})
{
    if (string.IsNullOrWhiteSpace(root)) continue;
    var ci = Path.GetFullPath(Path.Combine(root, "data.inf"));
    var cv = Path.GetFullPath(Path.Combine(root, "data", "data.vfs"));
    if (File.Exists(ci) && File.Exists(cv)) { infPath = ci; vfsPath = cv; break; }
}
if (infPath == "") { Console.Error.WriteLine("Client not found"); return 1; }

Console.WriteLine($"Mounted: {infPath}");
using var vfs = MappedVfsArchive.Open(infPath, vfsPath);
var allEntries = vfs.GetEntries().ToArray();

// =====================================================================
// SECTION 1: region*.bin — validate Layout A (spec: width/height/grid/originX/originZ)
// =====================================================================
Console.WriteLine("\n=== SECTION 1: region*.bin — Layout A validation ===");

var regionBins = allEntries
    .Where(e => System.Text.RegularExpressions.Regex.IsMatch(e.Name, @"data/map\d+/region\d+\.bin$"))
    .OrderBy(e => e.Name)
    .ToArray();

Console.WriteLine($"  Count: {regionBins.Length} region*.bin files");

// Global cell value histogram across all files
var globalCellHist = new Dictionary<byte, long>();
var sizeHistogram = new Dictionary<int, int>(); // size -> count
int layoutAMatches = 0;
int layoutAFails = 0;
var anomalies = new List<string>();

// Track grid dimensions
var widthSet = new HashSet<uint>();
var heightSet = new HashSet<uint>();
var originXSet = new HashSet<uint>();
var originZSet = new HashSet<uint>();

Console.WriteLine($"\n  Per-file analysis (spec Layout A = 8 + W*H + 8 = 16 + W*H):");
Console.WriteLine($"  {"File",-42} {"Size",7}  {"W",5} {"H",5} {"W*H",8}  {"computed",10}  {"match?",7}  {"distinctCellVals",20}  {"originX",10} {"originZ",10}");

foreach (var entry in regionBins)
{
    ReadOnlyMemory<byte> raw;
    try { raw = vfs.GetFileContent(entry.Name); }
    catch { anomalies.Add($"READ-FAIL: {entry.Name}"); continue; }

    var data = raw.Span;
    int fileSize = data.Length;

    if (!sizeHistogram.TryAdd(fileSize, 1)) sizeHistogram[fileSize]++;

    if (data.Length < 8)
    {
        anomalies.Add($"TOO-SMALL: {entry.Name} ({fileSize}B)");
        continue;
    }

    uint width  = BinaryPrimitives.ReadUInt32LittleEndian(data[0x00..]);
    uint height = BinaryPrimitives.ReadUInt32LittleEndian(data[0x04..]);
    long bodySize = (long)width * height;
    long expectedTotal = 8 + bodySize + 8; // Layout A

    widthSet.Add(width);
    heightSet.Add(height);

    bool layoutAOk = (fileSize == expectedTotal) && (8 + bodySize + 8 <= fileSize);

    uint originX = 0, originZ = 0;
    if (layoutAOk)
    {
        int bodyStart = 8;
        int tailOffset = bodyStart + (int)bodySize;
        if (tailOffset + 8 <= data.Length)
        {
            originX = BinaryPrimitives.ReadUInt32LittleEndian(data[tailOffset..]);
            originZ = BinaryPrimitives.ReadUInt32LittleEndian(data[(tailOffset + 4)..]);
            originXSet.Add(originX);
            originZSet.Add(originZ);
        }

        // Cell value histogram
        var localCellVals = new HashSet<byte>();
        for (long i = 0; i < bodySize && (8 + i) < data.Length; i++)
        {
            byte cell = data[(int)(8 + i)];
            localCellVals.Add(cell);
            if (!globalCellHist.TryAdd(cell, 1)) globalCellHist[cell]++;
        }

        layoutAMatches++;
        string distinctStr = string.Join(",", localCellVals.OrderBy(v => v).Select(v => v.ToString()));
        Console.WriteLine($"  {entry.Name,-42} {fileSize,7}  {width,5} {height,5} {bodySize,8}  {expectedTotal,10}  OK       [{distinctStr}]  {originX,10} {originZ,10}");
    }
    else
    {
        layoutAFails++;
        anomalies.Add($"LAYOUT-FAIL: {entry.Name} size={fileSize} expected={expectedTotal} W={width} H={height}");
        Console.WriteLine($"  {entry.Name,-42} {fileSize,7}  {width,5} {height,5} {bodySize,8}  {expectedTotal,10}  FAIL");
    }
}

Console.WriteLine($"\n  Layout A matches: {layoutAMatches}/{regionBins.Length}");
Console.WriteLine($"  Layout A fails:   {layoutAFails}/{regionBins.Length}");

Console.WriteLine($"\n  Size distribution (region*.bin):");
foreach (var kv in sizeHistogram.OrderBy(k => k.Key))
    Console.WriteLine($"    {kv.Key,8} bytes : {kv.Value} files");

Console.WriteLine($"\n  Width values observed: [{string.Join(", ", widthSet.OrderBy(v => v))}]");
Console.WriteLine($"  Height values observed: [{string.Join(", ", heightSet.OrderBy(v => v))}]");
Console.WriteLine($"  OriginX values (u32): [{string.Join(", ", originXSet.OrderBy(v => v))}]");
Console.WriteLine($"  OriginZ values (u32): [{string.Join(", ", originZSet.OrderBy(v => v))}]");

Console.WriteLine($"\n  Global cell value histogram (all {layoutAMatches} valid files):");
foreach (var kv in globalCellHist.OrderBy(k => k.Key))
{
    long total = globalCellHist.Values.Sum();
    double pct = total > 0 ? (double)kv.Value / total * 100.0 : 0;
    Console.WriteLine($"    cell={kv.Key,3} (0x{kv.Key:X2})  count={kv.Value,12}  {pct,6:F3}%");
}

long totalCells = globalCellHist.Values.Sum();
Console.WriteLine($"  Total cells across all files: {totalCells}");
Console.WriteLine($"  Distinct cell values: {globalCellHist.Count}  => [{string.Join(", ", globalCellHist.Keys.OrderBy(v => v))}]");

// =====================================================================
// SECTION 2: regiontable*.bin — stride analysis
// =====================================================================
Console.WriteLine("\n=== SECTION 2: regiontable*.bin — stride and record analysis ===");

var regionTables = allEntries
    .Where(e => System.Text.RegularExpressions.Regex.IsMatch(e.Name, @"data/map\d+/regiontable\d+\.bin$"))
    .OrderBy(e => e.Name)
    .ToArray();

Console.WriteLine($"  Count: {regionTables.Length} regiontable*.bin files");

var rtSizeHist = new Dictionary<int, int>();
var rtRecordCountHist = new Dictionary<int, int>(); // at stride 32
int rtStride32OK = 0, rtStride48OK = 0, rtStride32Only = 0;

// Global zoneType histogram for stride-32
var zoneTypeHist = new Dictionary<uint, int>();
var zoneNameLengths = new List<int>();
var tailDwordHist = new Dictionary<uint, int>();

Console.WriteLine($"\n  Per-file: size, stride-32 records, stride-48 records, stride-32 remainder");
Console.WriteLine($"  Probing stride-32 layout: [+0x00..+0x13=20B name?] [+0x14=u32 miniX] [+0x18=u32 miniY] [+0x1C=u32 zoneType?]");

foreach (var entry in regionTables)
{
    ReadOnlyMemory<byte> raw;
    try { raw = vfs.GetFileContent(entry.Name); }
    catch { anomalies.Add($"RT-READ-FAIL: {entry.Name}"); continue; }

    var data = raw.Span;
    int fileSize = data.Length;
    if (!rtSizeHist.TryAdd(fileSize, 1)) rtSizeHist[fileSize]++;

    int rem32 = fileSize % 32;
    int rem48 = fileSize % 48;
    int recs32 = fileSize / 32;
    int recs48 = fileSize / 48;

    if (rem32 == 0) rtStride32OK++;
    if (rem48 == 0) rtStride48OK++;
    if (rem32 == 0 && rem48 != 0) rtStride32Only++;

    if (!rtRecordCountHist.TryAdd(recs32, 1)) rtRecordCountHist[recs32]++;
}

Console.WriteLine($"\n  Total regiontable files: {regionTables.Length}");
Console.WriteLine($"  Files where size % 32 == 0: {rtStride32OK}/{regionTables.Length}");
Console.WriteLine($"  Files where size % 48 == 0: {rtStride48OK}/{regionTables.Length}");
Console.WriteLine($"  Files where ONLY stride 32 divides (not 48): {rtStride32Only}");

Console.WriteLine($"\n  Size distribution (regiontable*.bin):");
foreach (var kv in rtSizeHist.OrderBy(k => k.Key))
    Console.WriteLine($"    {kv.Key,8} bytes : {kv.Value} files  => at stride-32: {kv.Key/32} records");

Console.WriteLine($"\n  Record count histogram (at stride=32):");
foreach (var kv in rtRecordCountHist.OrderBy(k => k.Key))
    Console.WriteLine($"    {kv.Key} records: {kv.Value} files");

// Deep analysis of first 3 regiontable files to map the stride-32 layout
Console.WriteLine("\n  Deep stride-32 record analysis (first 3 files):");
foreach (var entry in regionTables.Take(3))
{
    ReadOnlyMemory<byte> raw;
    try { raw = vfs.GetFileContent(entry.Name); }
    catch { continue; }
    var data = raw.Span;
    int stride = 32;
    int recCount = data.Length / stride;
    Console.WriteLine($"\n  [{entry.Name}] {data.Length}B, {recCount} records at stride-32:");
    Console.WriteLine($"  Rec#  raw-hex (32B)");
    for (int i = 0; i < Math.Min(recCount, 52); i++)
    {
        int off = i * stride;
        var rec = data.Slice(off, stride);
        // Try to read as: miniX(f32@0), miniY(f32@4), miniZ(f32@8), miniW(f32@12), name16(+16), zoneType(u32@+28)?
        // Alternate: name at 0 (20B?), then coords, then type
        // From hex: rec001 row1 = [00 C0 C4 C4 00 A0 28 45 00 00 00 00 00 00 00 00 C6 F3 BE EE C3 CC 00 00 ...]
        // = f32: -1564.0, 2662.0, 0.0, 0.0 then "폐어촌" CP949 at +16
        // rec001 row3 = [00 00 80 80 00 00 00 00 ...] CP949 이름 at 0? No, -0 = neg zero float
        // Actually the clear pattern: odd records have floats at +0/+4, name at +16
        //                             even records (incl 0) are all-zero or name at +0?
        // Let's check: is rec 0 always zero?
        float f0 = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(rec[0x00..]));
        float f4 = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(rec[0x04..]));
        // name attempt at +0x10 (16B)
        int nlen16 = 0; for (int k = 0x10; k < stride && rec[k] != 0; k++) nlen16++;
        string name16 = nlen16 > 0 ? cp949.GetString(rec.Slice(0x10, nlen16)) : "(null)";
        // name attempt at +0x00 (20B)
        int nlen0 = 0; for (int k = 0; k < 0x14 && rec[k] != 0; k++) nlen0++;
        string name0 = nlen0 > 0 ? cp949.GetString(rec.Slice(0, nlen0)) : "(null)";
        // u32 at +28
        uint u28 = BinaryPrimitives.ReadUInt32LittleEndian(rec[0x1C..]);
        // Check if this is a "coord record" (f0, f4 look like world coords) or "name record"
        bool hasName16 = nlen16 > 0;
        bool hasName0  = nlen0 > 0;
        var recArr = rec.ToArray();
        string hexStr = string.Join(" ", recArr.Select(b => b.ToString("X2")));
        Console.WriteLine($"  [{i,2}] f0={f0,10:F1} f4={f4,10:F1} name@16=\"{name16,-14}\" name@0=\"{name0,-14}\" u28=0x{u28:X8}  hex:{hexStr}");
    }
}

// =====================================================================
// SECTION 3: mapsetting.scr — validate stride-84 layout (from minimap-scan findings)
// =====================================================================
Console.WriteLine("\n=== SECTION 3: mapsetting.scr — stride-84 layout validation ===");
{
    const string path = "data/script/mapsetting.scr";
    ReadOnlyMemory<byte> raw2;
    try { raw2 = vfs.GetFileContent(path); }
    catch { Console.WriteLine($"  MISSING: {path}"); goto skipMapsetting; }
    var data = raw2.Span;
    int stride = 84;
    int rem = data.Length % stride;
    int recCount = data.Length / stride;
    Console.WriteLine($"  {path}: {data.Length} bytes");
    Console.WriteLine($"  {data.Length} / 84 = {recCount} records, remainder {rem}  => stride-84 divides: {rem == 0}");

    // Field distribution analysis across all records
    var flagsAHist = new Dictionary<int, int>();
    var flagsBHist = new Dictionary<int, int>();
    var fogHist = new Dictionary<float, int>();
    var u44Hist = new Dictionary<int, int>();
    var u48Hist = new Dictionary<int, int>();
    var u4CHist = new Dictionary<int, int>();
    var u50Hist = new Dictionary<int, int>();
    var idSet = new SortedSet<int>();

    for (int i = 0; i < recCount; i++)
    {
        int off = i * stride;
        var rec = data.Slice(off, stride);
        int id    = BinaryPrimitives.ReadInt32LittleEndian(rec[0x00..]);
        int fA    = BinaryPrimitives.ReadInt32LittleEndian(rec[0x38..]);
        int fB    = BinaryPrimitives.ReadInt32LittleEndian(rec[0x3C..]);
        float fog = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(rec[0x40..]));
        int u44   = BinaryPrimitives.ReadInt32LittleEndian(rec[0x44..]);
        int u48   = BinaryPrimitives.ReadInt32LittleEndian(rec[0x48..]);
        int u4C   = BinaryPrimitives.ReadInt32LittleEndian(rec[0x4C..]);
        int u50   = BinaryPrimitives.ReadInt32LittleEndian(rec[0x50..]);
        idSet.Add(id);
        if (!flagsAHist.TryAdd(fA, 1)) flagsAHist[fA]++;
        if (!flagsBHist.TryAdd(fB, 1)) flagsBHist[fB]++;
        if (!fogHist.TryAdd(fog, 1)) fogHist[fog]++;
        if (!u44Hist.TryAdd(u44, 1)) u44Hist[u44]++;
        if (!u48Hist.TryAdd(u48, 1)) u48Hist[u48]++;
        if (!u4CHist.TryAdd(u4C, 1)) u4CHist[u4C]++;
        if (!u50Hist.TryAdd(u50, 1)) u50Hist[u50]++;
    }

    Console.WriteLine($"  Record count: {recCount}");
    Console.WriteLine($"  IDs present ({idSet.Count} distinct): [{string.Join(", ", idSet)}]");
    Console.WriteLine($"\n  Field distributions across {recCount} records:");
    Console.WriteLine($"  fA @+0x38 distinct={flagsAHist.Count}: " + string.Join(", ", flagsAHist.OrderByDescending(k=>k.Value).Select(k=>$"0x{k.Key:X8}x{k.Value}")));
    Console.WriteLine($"  fB @+0x3C distinct={flagsBHist.Count}: " + string.Join(", ", flagsBHist.OrderByDescending(k=>k.Value).Select(k=>$"0x{k.Key:X8}x{k.Value}")));
    Console.WriteLine($"  fog@+0x40 distinct={fogHist.Count}: " + string.Join(", ", fogHist.OrderByDescending(k=>k.Value).Select(k=>$"{k.Key:F2}x{k.Value}")));
    Console.WriteLine($"  u44@+0x44 distinct={u44Hist.Count}: " + string.Join(", ", u44Hist.OrderByDescending(k=>k.Value).Select(k=>$"{k.Key}x{k.Value}")));
    Console.WriteLine($"  u48@+0x48 distinct={u48Hist.Count}: " + string.Join(", ", u48Hist.OrderByDescending(k=>k.Value).Select(k=>$"{k.Key}x{k.Value}")));
    Console.WriteLine($"  u4C@+0x4C distinct={u4CHist.Count}: " + string.Join(", ", u4CHist.OrderByDescending(k=>k.Value).Select(k=>$"0x{k.Key:X8}x{k.Value}")));
    Console.WriteLine($"  u50@+0x50 distinct={u50Hist.Count}: " + string.Join(", ", u50Hist.OrderByDescending(k=>k.Value).Select(k=>$"{k.Key}x{k.Value}")));

    // Check name field width: does the 36-byte name (at +4..+39) overflow?
    // Spec: 4B id + 36B name + coords...
    // Let's check: for each record, find the actual NUL-terminated name length in the 36B window
    var nameLenHist = new Dictionary<int, int>();
    for (int i = 0; i < recCount; i++)
    {
        int off = i * stride;
        var rec = data.Slice(off, stride);
        int nameLen = 0;
        for (int k = 4; k < 40 && rec[k] != 0; k++) nameLen++;
        if (!nameLenHist.TryAdd(nameLen, 1)) nameLenHist[nameLen]++;
    }
    Console.WriteLine($"\n  Name length distribution (NUL-terminated within +4..+39, 36B window):");
    foreach (var kv in nameLenHist.OrderBy(k => k.Key))
        Console.WriteLine($"    {kv.Key,3} bytes: {kv.Value} records");
}
skipMapsetting:;

// =====================================================================
// ANOMALIES SUMMARY
// =====================================================================
Console.WriteLine("\n=== ANOMALIES ===");
if (anomalies.Count == 0)
    Console.WriteLine("  None.");
else
    foreach (var a in anomalies)
        Console.WriteLine($"  {a}");

Console.WriteLine("\nDone.");
return 0;
