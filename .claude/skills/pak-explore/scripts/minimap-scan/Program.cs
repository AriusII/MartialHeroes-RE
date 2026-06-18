// minimap-scan: THROWAWAY harness for Martial Heroes minimap/worldmap asset RE.
// NOT a solution member. Run: dotnet run --project .claude/skills/vfs-inspect/scripts/minimap-scan
//
// Investigates:
//   1. data/script/mapsetting.scr -- binary zone/area config, embedded CP949 names
//   2. data/ui/map/map1.dds       -- minimap texture dimensions (DDS header parse)
//   3. data/ui/map_userpoint.tga  -- TGA dimensions
//   4. data/ui/broodwarmap.dds    -- PvP zone map dimensions
//   5. data/ui/direction.dds      -- compass/direction icon
//   6. Per-area thumbnail scans under data/mapXXX/
//   7. msg.xdb zone name string ranges
// All offsets are hypotheses unless marked SAMPLE-VERIFIED.

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

// Build a list of all entry names once for repeated scanning
var allEntries = vfs.GetEntries().ToArray();

// =====================================================================
// 1. Parse mapsetting.scr -- binary zone config
// =====================================================================
Console.WriteLine("\n=== 1. mapsetting.scr ===");
ParseMapSetting(vfs, cp949);

// =====================================================================
// 2. DDS/TGA dimension scan for all known map art candidates
// =====================================================================
Console.WriteLine("\n=== 2. Map art candidates -- DDS/TGA headers ===");
string[] mapArtCandidates =
[
    "data/ui/map/map1.dds",
    "data/ui/broodwarmap.dds",
    "data/ui/direction.dds",
    "data/ui/map_userpoint.tga",
];
foreach (var path in mapArtCandidates)
{
    PrintImageDimensions(vfs, path, cp949);
}

// =====================================================================
// 3. Scan all data/mapXXX/ directories for thumbnail/overview art
// =====================================================================
Console.WriteLine("\n=== 3. Per-area map art scan (data/mapXXX/) ===");
ScanPerAreaMapArt(allEntries, vfs, cp949);

// =====================================================================
// 4. Scan data/ui/map/ subdirectory for all map tiles
// =====================================================================
Console.WriteLine("\n=== 4. data/ui/map/ entries ===");
ScanUiMapDir(allEntries, vfs, cp949);

// =====================================================================
// 5. msg.xdb zone name ranges
// =====================================================================
Console.WriteLine("\n=== 5. msg.xdb zone/area name candidates ===");
ScanMsgXdbRanges(vfs, cp949);

// =====================================================================
// 6. ui_map_listing -- any data/ui entry with "map" or "world" in name
// =====================================================================
Console.WriteLine("\n=== 6. All data/ui entries containing 'map' or 'world' ===");
foreach (var e in allEntries)
{
    string nl = e.Name.ToLowerInvariant();
    if (!nl.StartsWith("data/ui/")) continue;
    if (!nl.Contains("map") && !nl.Contains("world")) continue;
    Console.WriteLine($"  {e.Name}  size={e.DataSize}");
}

// =====================================================================
// 7. regiontable*.bin -- sub-area name + world-coord records
// =====================================================================
Console.WriteLine("\n=== 7. regiontableNNN.bin -- per-area sub-region name+coord records ===");
ParseRegionTable(vfs, cp949, "data/map001/regiontable001.bin");
ParseRegionTable(vfs, cp949, "data/map002/regiontable002.bin");
ParseRegionTable(vfs, cp949, "data/map003/regiontable003.bin");

return 0;

// -----------------------------------------------------------------------

static void ParseMapSetting(MappedVfsArchive vfs, Encoding cp949)
{
    const string path = "data/script/mapsetting.scr";
    ReadOnlyMemory<byte> raw;
    try { raw = vfs.GetFileContent(path); }
    catch { Console.WriteLine($"  MISSING: {path}"); return; }
    var data = raw.Span;

    Console.WriteLine($"  Size: {data.Length} bytes");
    // Stride 84 (0x54): 4368/84=52 exactly. SAMPLE-VERIFIED.
    int stride = 84;

    // CORRECTED layout — stride = 84 bytes (0x54), verified: 4368 / 84 = 52 records remainder 0.
    // At stride-84 boundaries, IDs read as 1, 2, 3... (SAMPLE-VERIFIED on first 3 records).
    // Name field is 36 bytes (0x24 bytes), giving the first coord field at +0x28.
    //
    // Hypothesis layout per record (84 bytes = 0x54):
    // [+0x00] int32   record_id (LE)                 -- SAMPLE-VERIFIED (1,2,3...)
    // [+0x04] char[36] CP949 name null-terminated    -- SAMPLE-VERIFIED (하왕관, 염무진, 사해주...)
    // [+0x28] int32   MinX (world coord LE signed)   -- PLAUSIBLE (rec0=-10240, rec1=7168, rec2=256)
    // [+0x2C] int32   MinY (world coord LE signed)   -- PLAUSIBLE (rec0=-7168, rec1=-3072, rec2=-1024)
    // [+0x30] int32   MaxX (world coord LE signed)   -- PLAUSIBLE (rec0=5120, rec1=15360, rec2=11264)
    // [+0x34] int32   MaxY (world coord LE signed)   -- PLAUSIBLE (rec0=10240, rec1=11264, rec2=10240)
    // [+0x38] int32   flags_a (area type?)           -- UNKNOWN; rec0=0x012C0001
    // [+0x3C] int32   flags_b                        -- UNKNOWN; rec0=0x00000001
    // [+0x40] float32 fog_density (1.7f in recs 0-2) -- PLAUSIBLE
    // [+0x44] int32   unknown_0x44                   -- UNKNOWN; rec0=1
    // [+0x48] int32   unknown_0x48                   -- UNKNOWN; rec0=0
    // [+0x4C] int32   unknown_0x4C                   -- UNKNOWN; rec0=0x64000007
    // [+0x50] int32   unknown_0x50                   -- UNKNOWN; rec0=0

    stride = 84; // CORRECTED: was 80
    int recCount = data.Length / stride;
    Console.WriteLine($"\n  data.Length / 84 = {recCount} records, remainder {data.Length % stride}  [SAMPLE-VERIFIED: stride=84]");
    Console.WriteLine($"\n  Stride-84 walkthrough ({recCount} records):");
    Console.WriteLine("  Rec#  ID   Name(CP949/36b)          MinX      MinY      MaxX      MaxY      fA         fB         fogF    u44 u48 u4C");
    for (int i = 0; i < recCount; i++)
    {
        int off = i * stride;
        if (off + stride > data.Length) break;
        var rec = data.Slice(off, stride);

        int id      = BinaryPrimitives.ReadInt32LittleEndian(rec[0x00..]);
        // name: 36 bytes at +4 (CP949 null-terminated)
        string name = cp949.GetString(rec.Slice(4, 36)).TrimEnd('\0');
        int minX    = BinaryPrimitives.ReadInt32LittleEndian(rec[0x28..]);
        int minY    = BinaryPrimitives.ReadInt32LittleEndian(rec[0x2C..]);
        int maxX    = BinaryPrimitives.ReadInt32LittleEndian(rec[0x30..]);
        int maxY    = BinaryPrimitives.ReadInt32LittleEndian(rec[0x34..]);
        int fA      = BinaryPrimitives.ReadInt32LittleEndian(rec[0x38..]);
        int fB      = BinaryPrimitives.ReadInt32LittleEndian(rec[0x3C..]);
        float flt40 = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(rec[0x40..]));
        int u44     = BinaryPrimitives.ReadInt32LittleEndian(rec[0x44..]);
        int u48     = BinaryPrimitives.ReadInt32LittleEndian(rec[0x48..]);
        int u4C     = BinaryPrimitives.ReadInt32LittleEndian(rec[0x4C..]);
        int u50     = BinaryPrimitives.ReadInt32LittleEndian(rec[0x50..]);
        Console.WriteLine($"  [{i,2}] id={id,3} \"{name,-22}\" minX={minX,8} minY={minY,8} maxX={maxX,8} maxY={maxY,8} fA=0x{fA:X8} fB=0x{fB:X8} fog={flt40:F2} u44={u44} u48={u48} u4C=0x{u4C:X8} u50={u50}");
    }

    // Print raw hex for first 3 records to aid cross-checking
    Console.WriteLine("\n  Raw hex per record (first 3), stride=84:");
    for (int i = 0; i < Math.Min(3, recCount); i++)
    {
        int off = i * stride;
        var rec = data.Slice(off, stride);
        Console.Write($"  Rec{i}: ");
        for (int j = 0; j < stride; j++) Console.Write($"{rec[j]:X2} ");
        Console.WriteLine();
    }
}

static void PrintImageDimensions(MappedVfsArchive vfs, string path, Encoding cp949)
{
    ReadOnlyMemory<byte> raw;
    try { raw = vfs.GetFileContent(path); }
    catch { Console.WriteLine($"  MISSING: {path}"); return; }
    var data = raw.Span;

    if (data.Length < 4) { Console.WriteLine($"  {path}: too small"); return; }

    string ext = Path.GetExtension(path).ToLowerInvariant();
    if (ext == ".dds")
    {
        if (data.Length < 128) { Console.WriteLine($"  {path}: DDS too small"); return; }
        // DDS header SAMPLE-VERIFIED offsets: magic[4], size[4], flags[4],
        // height[4@0xC], width[4@0x10], pitchOrLinear[4@0x14], depth[4@0x18], mipCount[4@0x1C]
        // pixelformat @ 0x4C: pfSize[4], pfFlags[4@0x50], fourCC[4@0x54]
        // caps @ 0x6C
        uint height  = BinaryPrimitives.ReadUInt32LittleEndian(data[0x0C..]);
        uint width   = BinaryPrimitives.ReadUInt32LittleEndian(data[0x10..]);
        uint mips    = BinaryPrimitives.ReadUInt32LittleEndian(data[0x1C..]);
        uint pfFlags = BinaryPrimitives.ReadUInt32LittleEndian(data[0x50..]);
        string fourCC = Encoding.ASCII.GetString(data.Slice(0x54, 4));
        string fmt = (pfFlags & 0x4) != 0 ? $"FourCC={fourCC}" : $"uncompressed(pfFlags=0x{pfFlags:X})";
        Console.WriteLine($"  {path}: {width}x{height}px mips={mips} fmt={fmt} fileSize={raw.Length}");
    }
    else if (ext == ".tga")
    {
        if (data.Length < 18) { Console.WriteLine($"  {path}: TGA too small"); return; }
        // TGA header: id_len[0], cmap_type[1], img_type[2], cmap_spec[5],
        // x_origin[2@0x8], y_origin[2@0xA], width[2@0xC], height[2@0xE], bpp[1@0x10]
        ushort width  = BinaryPrimitives.ReadUInt16LittleEndian(data[0x0C..]);
        ushort height = BinaryPrimitives.ReadUInt16LittleEndian(data[0x0E..]);
        byte bpp      = data[0x10];
        byte imgType  = data[2];
        Console.WriteLine($"  {path}: {width}x{height}px bpp={bpp} imgType={imgType} fileSize={raw.Length}");
    }
    else
    {
        Console.WriteLine($"  {path}: unknown ext {ext}, size={raw.Length}");
    }
}

static void ScanPerAreaMapArt(VfsEntry[] allEntries, MappedVfsArchive vfs, Encoding cp949)
{
    // Per-area images: anything in data/mapXXX/ that is .dds/.bmp/.png/.tga
    // but NOT in a /texture/ subfolder (those are terrain textures)
    var mapFolderArt = allEntries
        .Where(e =>
        {
            string nl = e.Name.ToLowerInvariant();
            if (!nl.StartsWith("data/map")) return false;
            string extL = Path.GetExtension(nl);
            if (extL is not (".dds" or ".bmp" or ".png" or ".tga")) return false;
            if (nl.Contains("/texture/")) return false;
            return true;
        })
        .ToList();

    Console.WriteLine($"  Non-terrain images under data/mapXXX/: {mapFolderArt.Count}");
    foreach (var e in mapFolderArt.Take(100))
        Console.WriteLine($"    {e.Name}  size={e.DataSize}");

    // Keyword search across entire VFS for minimap-like names
    var keywordHits = allEntries
        .Where(e =>
        {
            string nl = e.Name.ToLowerInvariant();
            return nl.Contains("mini") || nl.Contains("thumb") || nl.Contains("navi") ||
                   nl.Contains("overview") || nl.Contains("radar") || nl.Contains("worldmap") ||
                   nl.Contains("minimap");
        })
        .ToList();
    Console.WriteLine($"\n  Keyword hits (mini/thumb/navi/overview/radar/worldmap): {keywordHits.Count}");
    foreach (var e in keywordHits)
        Console.WriteLine($"    {e.Name}  size={e.DataSize}");

    // Check if the soundtable files under map000 are the only non-image/non-terrain per-area extras
    var mapMiscFiles = allEntries
        .Where(e =>
        {
            string nl = e.Name.ToLowerInvariant();
            if (!nl.StartsWith("data/map")) return false;
            string extL = Path.GetExtension(nl);
            return extL is ".txt" or ".lst" or ".bin" or ".ini" or ".cfg" or ".lua" or ".sc" or ".scr";
        })
        .ToList();
    Console.WriteLine($"\n  Text/config files under data/mapXXX/: {mapMiscFiles.Count}");
    foreach (var e in mapMiscFiles.Take(30))
        Console.WriteLine($"    {e.Name}  size={e.DataSize}");
}

static void ScanUiMapDir(VfsEntry[] allEntries, MappedVfsArchive vfs, Encoding cp949)
{
    var entries = allEntries
        .Where(e => e.Name.StartsWith("data/ui/map", StringComparison.OrdinalIgnoreCase))
        .ToArray();
    Console.WriteLine($"  Entries under data/ui/map*: {entries.Length}");
    foreach (var e in entries)
    {
        Console.WriteLine($"    {e.Name}  size={e.DataSize}");
        PrintImageDimensions(vfs, e.Name, cp949);
    }
}

static void ScanMsgXdbRanges(MappedVfsArchive vfs, Encoding cp949)
{
    const string xdbPath = "data/script/msg.xdb";
    ReadOnlyMemory<byte> raw;
    try { raw = vfs.GetFileContent(xdbPath); }
    catch { Console.WriteLine($"  MISSING: {xdbPath}"); return; }
    var data = raw.Span;

    // msg.xdb format: stride 516 (4 bytes int32 ID + 512 bytes CP949 string)
    // SAMPLE-VERIFIED via dump-msgxdb subcommand output above.
    const int stride = 516;
    int recordCount = data.Length / stride;
    Console.WriteLine($"  {xdbPath}: {data.Length} bytes, {recordCount} records at stride {stride}");

    // Print all records with IDs in ranges of interest
    var rangesOfInterest = new[] { (5001, 5100), (73000, 73100), (29000, 29050) };
    foreach (var (lo, hi) in rangesOfInterest)
    {
        Console.WriteLine($"\n  IDs {lo}..{hi}:");
        for (int i = 0; i < recordCount; i++)
        {
            int off = i * stride;
            if (off + stride > data.Length) break;
            int id = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(off, 4));
            if (id < lo || id > hi) continue;
            int strLen = 0;
            for (int k = 4; k < stride && data[off + k] != 0; k++) strLen++;
            string val = cp949.GetString(data.Slice(off + 4, strLen));
            Console.WriteLine($"    {id,6}: {val}");
        }
    }
}

static void ParseRegionTable(MappedVfsArchive vfs, Encoding cp949, string path)
{
    ReadOnlyMemory<byte> raw;
    try { raw = vfs.GetFileContent(path); }
    catch { Console.WriteLine($"  MISSING: {path}"); return; }
    var data = raw.Span;

    // Hypothesis layout for regiontableNNN.bin:
    // Observed from head hex of regiontable001.bin:
    //   Each record appears to be 32 bytes.
    //   1664 / 32 = 52 records.
    // Rec 0 (offset 0x00): [0x00..0x07] = 8 bytes (floats? XZ world coords?)
    //                      [0x08..0x0F] = 8 bytes (more coords or flags?)
    //                      [0x10..0x1F] = 16 bytes name CP949 null-terminated
    // But reg000 has "캐릭터선택창" at offset 0, and the subsequent records in reg001
    // show floats at bytes 0-8 and names at 0x30? Let's probe stride-32.
    Console.WriteLine($"\n  {path}: {data.Length} bytes");
    int[] strides = [32, 48, 64];
    foreach (int s in strides)
        Console.WriteLine($"    {data.Length} / {s} = {data.Length / s} records, rem {data.Length % s}");

    // Try stride 32 (1664/32=52)
    int stride = 32;
    Console.WriteLine($"\n  Stride-32 walkthrough (hypothesis):");
    int recCount = data.Length / stride;
    for (int i = 0; i < Math.Min(recCount, 30); i++)
    {
        int off = i * stride;
        if (off + stride > data.Length) break;
        var rec = data.Slice(off, stride);
        float f0 = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(rec[0x00..]));
        float f4 = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(rec[0x04..]));
        float f8 = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(rec[0x08..]));
        float fC = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(rec[0x0C..]));
        // name field: guess 16 bytes at offset 0x10
        int nameLen = 0;
        for (int k = 0x10; k < stride && rec[k] != 0; k++) nameLen++;
        string name = nameLen > 0 ? cp949.GetString(rec.Slice(0x10, nameLen)) : "";
        // Also try name at offset 0 (for region000 which starts with name)
        int nameLen0 = 0;
        for (int k = 0; k < 0x10 && rec[k] != 0; k++) nameLen0++;
        string name0 = nameLen0 > 0 ? cp949.GetString(rec.Slice(0, nameLen0)) : "";
        Console.WriteLine($"    [{i,2}] f0={f0,12:F2} f4={f4,12:F2} f8={f8,12:F2} fC={fC,12:F2} name@10=\"{name}\" name@0=\"{name0}\"");
    }

    // Also dump raw hex of first 3 records
    Console.WriteLine($"\n  Raw hex first 3 records (stride-32):");
    for (int i = 0; i < Math.Min(3, recCount); i++)
    {
        int off = i * stride;
        var rec = data.Slice(off, stride);
        Console.Write($"  [{i}]: ");
        for (int j = 0; j < stride; j++) Console.Write($"{rec[j]:X2} ");
        Console.WriteLine();
    }
}
