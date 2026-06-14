// mud-sound-probe — THROWAWAY harness (NOT a solution member, never committed).
//
// PURPOSE:
//   1. Determine the on-disk structure of .bgm / .bge / .eff soundtable files.
//   2. Read real .mud tiles, extract the distinct non-zero sound-index bytes
//      (byte2=bgmZoneId, byte3/4=bgeAmbientId0/1, byte5/6/7=effId0/1/2).
//   3. Map those indices to entries in the corresponding soundtable file and
//      report the resolution chain: mud_index → soundtable row → sound_id → .ogg path.

using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// --- VFS mount ---
string infPath = "";
string vfsPath = "";
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
if (infPath == "") { Console.Error.WriteLine("ERROR: client not found"); return 1; }

Console.WriteLine($"Mounted: {infPath}");

using var archive = MappedVfsArchive.Open(infPath, vfsPath);

// ──────────────────────────────────────────────────────────────────────────────
// PART A — Analyse the soundtable binary format
// ──────────────────────────────────────────────────────────────────────────────

Console.WriteLine("\n=== PART A: soundtable binary structure ===");

// We look at area 001 as the reference. Three table types: .bgm .bge .eff
// .wlk and .run also exist in the census — include them for completeness.
string[] tableExts = [".bgm", ".bge", ".eff", ".wlk", ".run"];

foreach (string ext in tableExts)
{
    string path = $"data/map001/soundtable001{ext}";
    if (!archive.Contains(path)) { Console.WriteLine($"  MISSING: {path}"); continue; }
    var raw = archive.GetFileContent(path);
    int sz = raw.Length;
    Console.WriteLine($"\n{path}  ({sz} bytes)");

    // Hypothesis: each record is 48 bytes (reported stride). Check 48 and 52.
    foreach (int stride in new[] { 48, 52 })
    {
        if (sz % stride == 0)
            Console.WriteLine($"  stride {stride}: {sz / stride} records (exact)");
        else
            Console.WriteLine($"  stride {stride}: {sz / stride} records + {sz % stride} leftover");
    }

    // Print the first 5 records at stride 48 to characterise the layout.
    int s = 48;
    int nRecs = sz / s;
    Console.WriteLine($"  First records (stride {s}):");
    var span = raw.Span;
    for (int r = 0; r < Math.Min(5, nRecs); r++)
    {
        int off = r * s;
        var rec = span.Slice(off, s);
        // Print as groups of 4 little-endian u32 and floats
        Console.Write($"    rec[{r,3}] @{off:X4}:");
        for (int i = 0; i < s / 4; i++)
        {
            uint u = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(i * 4));
            float f = BinaryPrimitives.ReadSingleLittleEndian(rec.Slice(i * 4));
            Console.Write($"  [{i}]=0x{u:X8}/{f:G5}");
        }
        Console.WriteLine();
    }

    // Print the FIRST non-zero sound_id record
    Console.WriteLine($"  Non-zero entries (first 10 with sound_id != 0, stride {s}):");
    int printed = 0;
    for (int r = 0; r < nRecs && printed < 10; r++)
    {
        int off = r * s;
        // Based on the scan-sound output the sound_id is a u32 stored somewhere in the record.
        // Try offsets 0x14, 0x18, 0x1C for each 48-byte record.
        var rec = span.Slice(off, s);
        for (int fieldOff = 0; fieldOff < s - 3; fieldOff += 4)
        {
            uint candidate = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(fieldOff));
            // Sound IDs in this client are 9-digit numbers 91xxxxxxx or 92xxxxxxx
            if (candidate >= 900_000_000u && candidate <= 999_999_999u)
            {
                Console.WriteLine($"    rec[{r,3}]  @fieldOff={fieldOff:X2}  sound_id={candidate}  (1-based row_idx={r + 1})");
                printed++;
                break;
            }
        }
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// PART B — Determine exact record layout via a stride-48 field census
// ──────────────────────────────────────────────────────────────────────────────
Console.WriteLine("\n=== PART B: stride-48 field census for soundtable001.bgm ===");
{
    string path = "data/map001/soundtable001.bgm";
    var raw = archive.GetFileContent(path);
    var span = raw.Span;
    int s = 48;
    int nRecs = raw.Length / s;

    // For each dword offset, track: min/max/zero-count/nonzero-count/distinct values (up to 20)
    int nFields = s / 4;
    var zeros = new int[nFields];
    var mins = new uint[nFields];
    var maxs = new uint[nFields];
    var distincts = new HashSet<uint>[nFields];
    for (int i = 0; i < nFields; i++) { mins[i] = uint.MaxValue; distincts[i] = []; }

    for (int r = 0; r < nRecs; r++)
    {
        var rec = span.Slice(r * s, s);
        for (int i = 0; i < nFields; i++)
        {
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(i * 4));
            if (v == 0) zeros[i]++;
            if (v < mins[i]) mins[i] = v;
            if (v > maxs[i]) maxs[i] = v;
            if (distincts[i].Count < 30) distincts[i].Add(v);
        }
    }

    Console.WriteLine($"  {nRecs} records, {nFields} dword fields each:");
    for (int i = 0; i < nFields; i++)
    {
        string distinctStr = distincts[i].Count < 30
            ? string.Join(", ", distincts[i].OrderBy(x => x).Select(x => x.ToString()))
            : $"{distincts[i].Count}+ distinct";
        Console.WriteLine($"  field[{i}] @{i*4:X2}: zeros={zeros[i]}/{nRecs}  min=0x{mins[i]:X8}  max=0x{maxs[i]:X8}  vals=[{distinctStr}]");
    }
}

// Same for .bge and .eff
foreach (string ext in new[] { ".bge", ".eff" })
{
    string path = $"data/map001/soundtable001{ext}";
    if (!archive.Contains(path)) continue;
    var raw = archive.GetFileContent(path);
    var span = raw.Span;
    int s = 48;
    int nRecs = raw.Length / s;
    int nFields = s / 4;
    var zeros = new int[nFields];
    var mins = new uint[nFields];
    var maxs = new uint[nFields];
    var distincts = new HashSet<uint>[nFields];
    for (int i = 0; i < nFields; i++) { mins[i] = uint.MaxValue; distincts[i] = []; }

    for (int r = 0; r < nRecs; r++)
    {
        var rec = span.Slice(r * s, s);
        for (int i = 0; i < nFields; i++)
        {
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(i * 4));
            if (v == 0) zeros[i]++;
            if (v < mins[i]) mins[i] = v;
            if (v > maxs[i]) maxs[i] = v;
            if (distincts[i].Count < 30) distincts[i].Add(v);
        }
    }

    Console.WriteLine($"\n=== PART B (cont): stride-48 field census for soundtable001{ext} ===");
    Console.WriteLine($"  {nRecs} records, {nFields} dword fields each:");
    for (int i = 0; i < nFields; i++)
    {
        string distinctStr = distincts[i].Count < 30
            ? string.Join(", ", distincts[i].OrderBy(x => x).Select(x => x.ToString()))
            : $"{distincts[i].Count}+ distinct";
        Console.WriteLine($"  field[{i}] @{i*4:X2}: zeros={zeros[i]}/{nRecs}  min=0x{mins[i]:X8}  max=0x{maxs[i]:X8}  vals=[{distinctStr}]");
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// PART C — Read real .mud tiles, collect distinct non-zero sound indices
// ──────────────────────────────────────────────────────────────────────────────
Console.WriteLine("\n=== PART C: .mud tiles — distinct non-zero sound-index bytes ===");

// Collect all .mud paths for map001
var mudPaths = archive.GetEntries()
    .ToArray()
    .Where(e => e.Name.StartsWith("data/map001/dat/") && e.Name.EndsWith(".mud"))
    .Select(e => e.Name)
    .ToList();

Console.WriteLine($"  Map001 .mud files: {mudPaths.Count}");

// mud tile layout (known spec): 8 bytes per tile
//   byte0 = texture index (terrain)
//   byte1 = attribute/flags
//   byte2 = bgmZoneId
//   byte3 = bgeAmbientId0
//   byte4 = bgeAmbientId1
//   byte5 = effId0
//   byte6 = effId1
//   byte7 = effId2
// Each .mud = 32 768 bytes → 4096 tiles (64×64)

var bgmIds = new SortedSet<byte>();
var bge0Ids = new SortedSet<byte>();
var bge1Ids = new SortedSet<byte>();
var eff0Ids = new SortedSet<byte>();
var eff1Ids = new SortedSet<byte>();
var eff2Ids = new SortedSet<byte>();
int tilesScanned = 0;

foreach (string p in mudPaths)
{
    var raw = archive.GetFileContent(p);
    var s = raw.Span;
    if (s.Length % 8 != 0) { Console.WriteLine($"  WARN: {p} length {s.Length} not multiple of 8"); continue; }
    int nTiles = s.Length / 8;
    tilesScanned += nTiles;
    for (int t = 0; t < nTiles; t++)
    {
        int off = t * 8;
        byte bgm = s[off + 2];
        byte bge0 = s[off + 3];
        byte bge1 = s[off + 4];
        byte eff0 = s[off + 5];
        byte eff1 = s[off + 6];
        byte eff2 = s[off + 7];
        if (bgm != 0) bgmIds.Add(bgm);
        if (bge0 != 0) bge0Ids.Add(bge0);
        if (bge1 != 0) bge1Ids.Add(bge1);
        if (eff0 != 0) eff0Ids.Add(eff0);
        if (eff1 != 0) eff1Ids.Add(eff1);
        if (eff2 != 0) eff2Ids.Add(eff2);
    }
}

Console.WriteLine($"  Total tiles scanned: {tilesScanned:N0}");
Console.WriteLine($"  bgmZoneId   non-zero values: [{string.Join(", ", bgmIds)}]");
Console.WriteLine($"  bgeAmbId0   non-zero values: [{string.Join(", ", bge0Ids)}]");
Console.WriteLine($"  bgeAmbId1   non-zero values: [{string.Join(", ", bge1Ids)}]");
Console.WriteLine($"  effId0      non-zero values: [{string.Join(", ", eff0Ids)}]");
Console.WriteLine($"  effId1      non-zero values: [{string.Join(", ", eff1Ids)}]");
Console.WriteLine($"  effId2      non-zero values: [{string.Join(", ", eff2Ids)}]");

// Union of all BGE indices
var allBge = new SortedSet<byte>(bge0Ids.Concat(bge1Ids));
var allEff = new SortedSet<byte>(eff0Ids.Concat(eff1Ids).Concat(eff2Ids));
Console.WriteLine($"  BGE union: [{string.Join(", ", allBge)}]  max={(allBge.Count > 0 ? allBge.Max : 0)}");
Console.WriteLine($"  EFF union: [{string.Join(", ", allEff)}]  max={(allEff.Count > 0 ? allEff.Max : 0)}");

// ──────────────────────────────────────────────────────────────────────────────
// PART D — Cross-map index scan (all maps)
// ──────────────────────────────────────────────────────────────────────────────
Console.WriteLine("\n=== PART D: all maps — max sound-index values ===");
var allMudPaths = archive.GetEntries()
    .ToArray()
    .Where(e => e.Name.EndsWith(".mud"))
    .Select(e => e.Name)
    .ToList();
Console.WriteLine($"  Total .mud files: {allMudPaths.Count}");

byte globalMaxBgm = 0, globalMaxBge = 0, globalMaxEff = 0;
foreach (string p in allMudPaths)
{
    var raw = archive.GetFileContent(p);
    var s = raw.Span;
    if (s.Length % 8 != 0) continue;
    int nTiles = s.Length / 8;
    for (int t = 0; t < nTiles; t++)
    {
        int off = t * 8;
        if (s[off + 2] > globalMaxBgm) globalMaxBgm = s[off + 2];
        byte bge = Math.Max(s[off + 3], s[off + 4]);
        if (bge > globalMaxBge) globalMaxBge = bge;
        byte eff = Math.Max(s[off + 5], Math.Max(s[off + 6], s[off + 7]));
        if (eff > globalMaxEff) globalMaxEff = eff;
    }
}
Console.WriteLine($"  Global max bgmZoneId={globalMaxBgm}  bgeAmbientId={globalMaxBge}  effId={globalMaxEff}");

// ──────────────────────────────────────────────────────────────────────────────
// PART E — Resolution chain: mud_index → soundtable[index-1] → sound_id → .ogg
// ──────────────────────────────────────────────────────────────────────────────
Console.WriteLine("\n=== PART E: resolution chain examples (map001, stride=48) ===");
{
    // Resolve BGM: mud byte2 (bgmZoneId) → soundtable001.bgm record[bgmZoneId - 1] → sound_id
    string bgmPath = "data/map001/soundtable001.bgm";
    var bgmRaw = archive.GetFileContent(bgmPath);
    var bgmSpan = bgmRaw.Span;
    int bgmStride = 48;
    int bgmRecs = bgmRaw.Length / bgmStride;

    Console.WriteLine($"\n  BGM chain (soundtable001.bgm, {bgmRecs} records, stride {bgmStride}):");
    Console.WriteLine($"  Probing .mud bgmZoneIds: [{string.Join(", ", bgmIds)}]");
    foreach (byte idx in bgmIds)
    {
        // Try 1-based (idx-1) and 0-based (idx)
        foreach (int row in new[] { idx - 1, idx })
        {
            if (row < 0 || row >= bgmRecs) continue;
            var rec = bgmSpan.Slice(row * bgmStride, bgmStride);
            // Scan all dword fields for a plausible sound_id
            for (int fi = 0; fi < bgmStride / 4; fi++)
            {
                uint v = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(fi * 4));
                if (v >= 900_000_000u && v <= 999_999_999u)
                {
                    string oggPath2d = $"data/sound/2d/{v}.ogg";
                    string oggPath3d = $"data/sound/3d/{v}.ogg";
                    bool exists2d = archive.Contains(oggPath2d);
                    bool exists3d = archive.Contains(oggPath3d);
                    Console.WriteLine($"    mud_idx={idx} → row={row} (0-based) → field[{fi}]@{fi*4:X2} → sound_id={v}  ogg={( exists2d ? "2d-ok" : exists3d ? "3d-ok" : "MISSING")}");
                    break;
                }
            }
        }
    }

    // Resolve BGE
    string bgePath = "data/map001/soundtable001.bge";
    if (archive.Contains(bgePath))
    {
        var bgeRaw = archive.GetFileContent(bgePath);
        var bgeSpan = bgeRaw.Span;
        int bgeRecs = bgeRaw.Length / bgmStride;
        Console.WriteLine($"\n  BGE chain (soundtable001.bge, {bgeRecs} records, stride {bgmStride}):");
        Console.WriteLine($"  Probing .mud bgeAmbientIds union: [{string.Join(", ", allBge)}]");
        foreach (byte idx in allBge)
        {
            foreach (int row in new[] { idx - 1, idx })
            {
                if (row < 0 || row >= bgeRecs) continue;
                var rec = bgeSpan.Slice(row * bgmStride, bgmStride);
                for (int fi = 0; fi < bgmStride / 4; fi++)
                {
                    uint v = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(fi * 4));
                    if (v >= 900_000_000u && v <= 999_999_999u)
                    {
                        string ogg2d = $"data/sound/2d/{v}.ogg";
                        string ogg3d = $"data/sound/3d/{v}.ogg";
                        bool ok2d = archive.Contains(ogg2d);
                        bool ok3d = archive.Contains(ogg3d);
                        Console.WriteLine($"    mud_idx={idx} → row={row} → field[{fi}]@{fi*4:X2} → sound_id={v}  ogg={( ok2d ? "2d-ok" : ok3d ? "3d-ok" : "MISSING")}");
                        break;
                    }
                }
            }
        }
    }

    // Resolve EFF
    string effPath = "data/map001/soundtable001.eff";
    if (archive.Contains(effPath))
    {
        var effRaw = archive.GetFileContent(effPath);
        var effSpan = effRaw.Span;
        int effRecs = effRaw.Length / bgmStride;
        Console.WriteLine($"\n  EFF chain (soundtable001.eff, {effRecs} records, stride {bgmStride}):");
        Console.WriteLine($"  Probing .mud effIds union: [{string.Join(", ", allEff)}]");
        foreach (byte idx in allEff)
        {
            foreach (int row in new[] { idx - 1, idx })
            {
                if (row < 0 || row >= effRecs) continue;
                var rec = effSpan.Slice(row * bgmStride, bgmStride);
                for (int fi = 0; fi < bgmStride / 4; fi++)
                {
                    uint v = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(fi * 4));
                    if (v >= 900_000_000u && v <= 999_999_999u)
                    {
                        string ogg2d = $"data/sound/2d/{v}.ogg";
                        string ogg3d = $"data/sound/3d/{v}.ogg";
                        bool ok2d = archive.Contains(ogg2d);
                        bool ok3d = archive.Contains(ogg3d);
                        Console.WriteLine($"    mud_idx={idx} → row={row} → field[{fi}]@{fi*4:X2} → sound_id={v}  ogg={( ok2d ? "2d-ok" : ok3d ? "3d-ok" : "MISSING")}");
                        break;
                    }
                }
            }
        }
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// PART F — Check .wlk / .run structure
// ──────────────────────────────────────────────────────────────────────────────
Console.WriteLine("\n=== PART F: .wlk and .run table sizes (all maps) ===");
foreach (string ext2 in new[] { ".wlk", ".run" })
{
    var sizes = new Dictionary<int, int>();
    foreach (var e in archive.GetEntries().ToArray().Where(e => e.Name.EndsWith(ext2)))
        sizes.TryGetValue((int)e.DataSize, out int c);  // just gather distinct sizes
    // Rewrite: just print one example per map
    string ex = archive.GetEntries().ToArray().FirstOrDefault(e => e.Name.EndsWith(ext2)).Name ?? "";
    if (ex != "")
    {
        var raw = archive.GetFileContent(ex);
        Console.WriteLine($"  Example {ext2}: {ex}  size={raw.Length}  /48={raw.Length/48}  /4={raw.Length/4}");
        // print first record
        if (raw.Length >= 48)
        {
            Console.Write("  first 48 bytes: ");
            for (int i = 0; i < 48; i++) Console.Write($"{raw.Span[i]:X2} ");
            Console.WriteLine();
        }
    }
}

Console.WriteLine("\nDone.");
return 0;
