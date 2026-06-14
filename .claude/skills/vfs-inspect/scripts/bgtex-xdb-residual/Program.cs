// bgtex-xdb-residual — THROWAWAY harness.
// NOT a solution member. Never committed, never git-add'd.
//
// Part A: scan ALL bgtexture.lst files in the VFS for kind bytes != 0x01.
// Part B: analyze the five small .xdb tables for column characterization.

using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// --- Probe client locations ---
string infPath = "D:/MartialHeroesClient/data.inf";
string vfsPath = "D:/MartialHeroesClient/data/data.vfs";

// Try the embedded clientdata/ path first
string repoRoot = FindRepoRoot() ?? "C:/Users/Arius/RiderProjects/MartialHeroes";
string embeddedInf = Path.Combine(repoRoot, "05.Presentation/MartialHeroes.Client.Godot/clientdata/data.inf");
string embeddedVfs = Path.Combine(repoRoot, "05.Presentation/MartialHeroes.Client.Godot/clientdata/data/data.vfs");
if (File.Exists(embeddedInf) && File.Exists(embeddedVfs))
{
    infPath = embeddedInf;
    vfsPath = embeddedVfs;
}

if (!File.Exists(infPath) || !File.Exists(vfsPath))
{
    Console.Error.WriteLine($"Client not found.\n  inf: {infPath}\n  vfs: {vfsPath}");
    return 1;
}

using MappedVfsArchive archive = MappedVfsArchive.Open(infPath, vfsPath);
ReadOnlySpan<VfsEntry> entries = archive.GetEntries();
Console.WriteLine($"Mounted {entries.Length:N0} entries from {infPath}");
Console.WriteLine();

// =============================================================
// PART A — Scan all bgtexture.lst files for kind != 0x01
// =============================================================
Console.WriteLine("=== PART A: bgtexture.lst kind-byte full scan ===");
Console.WriteLine();

int lstFilesScanned = 0;
int totalRecordsChecked = 0;
int nonOneKindRecords = 0;
var nonOneExamples = new List<(string file, int recIdx, byte kind, string relpath)>();

foreach (ref readonly VfsEntry entry in entries)
{
    if (!entry.Name.EndsWith(".lst", StringComparison.OrdinalIgnoreCase))
        continue;
    // Only bgtexture.lst files (not area cell manifests like d1.lst, d2.lst etc.)
    if (!entry.Name.Contains("bgtexture"))
        continue;

    lstFilesScanned++;
    ReadOnlyMemory<byte> data = archive.GetFileContent(entry.Name);
    ReadOnlySpan<byte> span = data.Span;

    if (span.Length < 4)
    {
        Console.WriteLine($"  SKIP (too short): {entry.Name}");
        continue;
    }

    uint recordCount = BinaryPrimitives.ReadUInt32LittleEndian(span[..4]);
    int expectedSize = 4 + (int)recordCount * 48;

    Console.WriteLine($"  File: {entry.Name}");
    Console.WriteLine($"    Size: {span.Length} bytes, record_count={recordCount}, expected={expectedSize}, match={span.Length == expectedSize}");

    // Walk every record
    for (int i = 0; i < (int)recordCount; i++)
    {
        int offset = 4 + i * 48;
        if (offset + 48 > span.Length) break;

        byte kind = span[offset];
        totalRecordsChecked++;

        if (kind != 0x01)
        {
            nonOneKindRecords++;
            // Decode relpath (47 bytes, null-terminated, ASCII/CP949)
            ReadOnlySpan<byte> relpathBytes = span.Slice(offset + 1, 47);
            int nullPos = relpathBytes.IndexOf((byte)0);
            string relpath = Encoding.ASCII.GetString(nullPos >= 0 ? relpathBytes[..nullPos] : relpathBytes);
            nonOneExamples.Add((entry.Name, i, kind, relpath));
            if (nonOneExamples.Count <= 20)
            {
                Console.WriteLine($"    [!] Record {i}: kind=0x{kind:X2}, relpath='{relpath}'");
            }
        }
    }

    Console.WriteLine($"    Records checked: {recordCount}, non-0x01 kind found: {(nonOneKindRecords > 0 ? nonOneKindRecords.ToString() : "none")}");
}

Console.WriteLine();
Console.WriteLine($"PART A SUMMARY:");
Console.WriteLine($"  .lst files with 'bgtexture' in name: {lstFilesScanned}");
Console.WriteLine($"  Total records checked: {totalRecordsChecked}");
Console.WriteLine($"  Records with kind != 0x01: {nonOneKindRecords}");
if (nonOneKindRecords == 0)
    Console.WriteLine("  VERDICT: kind == 0x01 is CONSTANT across all bgtexture.lst records in the VFS.");
else
    Console.WriteLine($"  VERDICT: {nonOneKindRecords} records with non-0x01 kind found — see examples above.");

Console.WriteLine();

// =============================================================
// PART B — Analyze the five small .xdb tables
// =============================================================
Console.WriteLine("=== PART B: .xdb table column characterization ===");
Console.WriteLine();

// Helper: read all records, return raw bytes
static ReadOnlyMemory<byte> GetXdb(MappedVfsArchive arc, string path)
    => arc.Contains(path) ? arc.GetFileContent(path) : ReadOnlyMemory<byte>.Empty;

static float ReadF32(ReadOnlySpan<byte> s, int off) =>
    BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(s[off..]));
static uint ReadU32(ReadOnlySpan<byte> s, int off) =>
    BinaryPrimitives.ReadUInt32LittleEndian(s[off..]);

// --- §1 actor_size.xdb (stride 12, 15 records) ---
{
    Console.WriteLine("--- §1 actor_size.xdb (stride=12, expected 15 records) ---");
    var mem = GetXdb(archive, "data/script/actor_size.xdb");
    if (mem.IsEmpty) { Console.WriteLine("  NOT FOUND"); }
    else
    {
        var s = mem.Span;
        int stride = 12, count = s.Length / stride;
        Console.WriteLine($"  Size={s.Length}, stride={stride}, count={count}");
        // Check scale_a axis hint: examine all 15 records
        // Collect: actor_kind_id, scale_a, scale_b
        float minA = float.MaxValue, maxA = float.MinValue;
        float minB = float.MaxValue, maxB = float.MinValue;
        var nonUnityA = new List<(uint id, float a, float b)>();
        var nonUnityB = new List<(uint id, float a, float b)>();
        Console.WriteLine($"  All records (id, scale_a, scale_b):");
        for (int i = 0; i < count; i++)
        {
            uint id = ReadU32(s, i * stride + 0);
            float a = ReadF32(s, i * stride + 4);
            float b = ReadF32(s, i * stride + 8);
            Console.WriteLine($"    [{i:D2}] id={id}, scale_a={a:F4}, scale_b={b:F4}");
            if (a < minA) minA = a;
            if (a > maxA) maxA = a;
            if (b < minB) minB = b;
            if (b > maxB) maxB = b;
            if (Math.Abs(a - 1.0f) > 0.001f) nonUnityA.Add((id, a, b));
            if (Math.Abs(b - 1.0f) > 0.001f) nonUnityB.Add((id, a, b));
        }
        Console.WriteLine($"  scale_a range: [{minA:F4}..{maxA:F4}], non-unity count: {nonUnityA.Count}");
        Console.WriteLine($"  scale_b range: [{minB:F4}..{maxB:F4}], non-unity count: {nonUnityB.Count}");
        // Axis hint: if one scale tends to deviate from 1 more than the other,
        // the one with the larger absolute range is more likely horizontal (width) since mobs vary in girth
        Console.WriteLine($"  Axis heuristic: larger absolute deviation -> more likely horizontal/radial axis");
    }
    Console.WriteLine();
}

// --- §2 buff_icon_position.xdb (stride 12, 134 records) ---
{
    Console.WriteLine("--- §2 buff_icon_position.xdb (stride=12, expected 134 records) ---");
    var mem = GetXdb(archive, "data/script/buff_icon_position.xdb");
    if (mem.IsEmpty) { Console.WriteLine("  NOT FOUND"); }
    else
    {
        var s = mem.Span;
        int stride = 12, count = s.Length / stride;
        Console.WriteLine($"  Size={s.Length}, stride={stride}, count={count}");

        // Check buff_id continuity and sprite_y values
        uint prevId = 0;
        bool idIsContiguous = true;
        var xValues = new Dictionary<uint, int>();
        var yValues = new Dictionary<uint, int>();
        var idValues = new List<uint>();
        uint minId = uint.MaxValue, maxId = 0;

        for (int i = 0; i < count; i++)
        {
            uint buffId = ReadU32(s, i * stride + 0);
            uint spriteX = ReadU32(s, i * stride + 4);
            uint spriteY = ReadU32(s, i * stride + 8);
            idValues.Add(buffId);
            if (buffId < minId) minId = buffId;
            if (buffId > maxId) maxId = buffId;
            if (i > 0 && buffId != prevId + 1) idIsContiguous = false;
            prevId = buffId;

            if (!xValues.ContainsKey(spriteX)) xValues[spriteX] = 0;
            xValues[spriteX]++;
            if (!yValues.ContainsKey(spriteY)) yValues[spriteY] = 0;
            yValues[spriteY]++;
        }

        Console.WriteLine($"  buff_id: min={minId}, max={maxId}, contiguous={idIsContiguous}");
        Console.WriteLine($"  buff_id first 10: {string.Join(", ", idValues.Take(10))}");
        Console.WriteLine($"  buff_id last 5: {string.Join(", ", idValues.TakeLast(5))}");
        Console.WriteLine($"  sprite_x distinct values: {xValues.Count}, sorted: {string.Join(", ", xValues.Keys.OrderBy(k => k).Take(20))}");
        Console.WriteLine($"  sprite_y distinct values: {yValues.Count}, sorted: {string.Join(", ", yValues.Keys.OrderBy(k => k).Take(20))}");
        // Check if sprite_x outliers (>= 1024)
        var xOutliers = xValues.Keys.Where(k => k >= 1024).OrderBy(k => k).ToList();
        Console.WriteLine($"  sprite_x outliers (>=1024): {(xOutliers.Count == 0 ? "none" : string.Join(", ", xOutliers))}");
        // Check sprite_y for row-index pattern (multiples of 25?) vs constant
        bool yIsConstant = yValues.Count == 1;
        Console.WriteLine($"  sprite_y is constant: {yIsConstant}" + (yIsConstant ? $" (value={yValues.Keys.First()})" : ""));
        if (!yIsConstant)
        {
            // Check if stepping by 25
            var yOrdered = yValues.Keys.OrderBy(k => k).ToList();
            bool allStep25 = yOrdered.Count > 1 && yOrdered.Zip(yOrdered.Skip(1)).All(p => p.Second - p.First == 25);
            Console.WriteLine($"  sprite_y steps by 25: {allStep25}");
        }
        // Print first 10 full records
        Console.WriteLine("  First 10 records (buff_id, sprite_x, sprite_y):");
        for (int i = 0; i < Math.Min(10, count); i++)
        {
            uint id = ReadU32(s, i * stride);
            uint x = ReadU32(s, i * stride + 4);
            uint y = ReadU32(s, i * stride + 8);
            Console.WriteLine($"    [{i:D3}] buff_id={id}, sprite_x={x}, sprite_y={y}");
        }
    }
    Console.WriteLine();
}

// --- §3 effectscale.xdb (stride 8, 2 records) ---
{
    Console.WriteLine("--- §3 effectscale.xdb (stride=8, expected 2 records) ---");
    var mem = GetXdb(archive, "data/script/effectscale.xdb");
    if (mem.IsEmpty) { Console.WriteLine("  NOT FOUND"); }
    else
    {
        var s = mem.Span;
        int stride = 8, count = s.Length / stride;
        Console.WriteLine($"  Size={s.Length}, stride={stride}, count={count}");
        for (int i = 0; i < count; i++)
        {
            uint key = ReadU32(s, i * stride);
            float scale = ReadF32(s, i * stride + 4);
            // Also try reading key as two u16
            ushort kLo = BinaryPrimitives.ReadUInt16LittleEndian(s[(i * stride)..]);
            ushort kHi = BinaryPrimitives.ReadUInt16LittleEndian(s[(i * stride + 2)..]);
            Console.WriteLine($"    [{i}] effect_key={key} (0x{key:X8}) [lo16={kLo}, hi16={kHi}], scale={scale:F4}");
        }
        // Check if key difference = 1
        if (count == 2)
        {
            uint k0 = ReadU32(s, 0);
            uint k1 = ReadU32(s, 8);
            Console.WriteLine($"  key diff: {(long)k1 - (long)k0}");
        }
    }
    Console.WriteLine();
}

// --- §4 vehicle.xdb (stride 52, 58 records) ---
{
    Console.WriteLine("--- §4 vehicle.xdb (stride=52, expected 58 records) ---");
    var mem = GetXdb(archive, "data/script/vehicle.xdb");
    if (mem.IsEmpty) { Console.WriteLine("  NOT FOUND"); }
    else
    {
        var s = mem.Span;
        int stride = 52, count = s.Length / stride;
        Console.WriteLine($"  Size={s.Length}, stride={stride}, count={count}");

        // Track field distributions across all 58 records
        var tagAValues = new Dictionary<uint, int>();
        var tagBValues = new Dictionary<uint, int>();
        bool allParamZero = true;

        Console.WriteLine("  All records (vehicle_id, item_id, tag_a, tag_b, param_0..8 if non-zero):");
        for (int i = 0; i < count; i++)
        {
            uint vid = ReadU32(s, i * stride + 0);
            uint iid = ReadU32(s, i * stride + 4);
            uint tagA = ReadU32(s, i * stride + 8);
            uint tagB = ReadU32(s, i * stride + 12);

            float p0 = ReadF32(s, i * stride + 16);
            float p1 = ReadF32(s, i * stride + 20);
            float p2 = ReadF32(s, i * stride + 24);
            float p3 = ReadF32(s, i * stride + 28);
            float p4 = ReadF32(s, i * stride + 32);
            float p5 = ReadF32(s, i * stride + 36);
            float p6 = ReadF32(s, i * stride + 40);
            float p7 = ReadF32(s, i * stride + 44);
            float p8 = ReadF32(s, i * stride + 48);

            bool hasNonZeroParam = p0 != 0 || p1 != 0 || p2 != 0 || p3 != 0 || p4 != 0 ||
                                   p5 != 0 || p6 != 0 || p7 != 0 || p8 != 0;
            if (hasNonZeroParam) allParamZero = false;

            if (!tagAValues.ContainsKey(tagA)) tagAValues[tagA] = 0;
            tagAValues[tagA]++;
            if (!tagBValues.ContainsKey(tagB)) tagBValues[tagB] = 0;
            tagBValues[tagB]++;

            string paramNote = hasNonZeroParam
                ? $" params=[{p0:F3},{p1:F3},{p2:F3},{p3:F3},{p4:F3},{p5:F3},{p6:F3},{p7:F3},{p8:F3}]"
                : " params=all_zero";
            Console.WriteLine($"    [{i:D2}] vid={vid}, iid={iid}, tagA=0x{tagA:X8}, tagB=0x{tagB:X8}{paramNote}");
        }

        Console.WriteLine($"  tag_a distinct values: {tagAValues.Count}");
        foreach (var kv in tagAValues.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"    0x{kv.Key:X8}: {kv.Value} records");
        Console.WriteLine($"  tag_b distinct values: {tagBValues.Count}");
        foreach (var kv in tagBValues.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"    0x{kv.Key:X8}: {kv.Value} records");
        Console.WriteLine($"  All float params zero: {allParamZero}");
    }
    Console.WriteLine();
}

// --- §5 creature_item.xdb (stride 48, 921 records) ---
{
    Console.WriteLine("--- §5 creature_item.xdb (stride=48, expected 921 records) ---");
    var mem = GetXdb(archive, "data/script/creature_item.xdb");
    if (mem.IsEmpty) { Console.WriteLine("  NOT FOUND"); }
    else
    {
        var s = mem.Span;
        int stride = 48, count = s.Length / stride;
        Console.WriteLine($"  Size={s.Length}, stride={stride}, count={count}");

        // Characterize the still-UNVERIFIED columns:
        //   +32: scale_or_radius (f32) - semantic?
        //   +36: unknown_u1 (u32) - always 0?
        //   +40..+43: four flags or u32?
        //   +44: probability (u32) - always 100?
        // Also: creature_key structure (sequential-by-1 compound?)
        // Also: axis mapping hint for attach_f0..f5

        var prob100 = 0; var probOther = new Dictionary<uint, int>();
        var u1Zero = 0; var u1Other = new Dictionary<uint, int>();
        var flag0Vals = new Dictionary<byte, int>();
        var flag1Vals = new Dictionary<byte, int>();
        var flag2Vals = new Dictionary<byte, int>();
        var flag3Vals = new Dictionary<byte, int>();
        float minRadius = float.MaxValue, maxRadius = float.MinValue;
        bool keySeqBy1 = true;
        uint prevKey = 0;

        // Sample first 10 + last 5 + first non-zero-param ones
        Console.WriteLine("  First 10 records:");
        for (int i = 0; i < Math.Min(10, count); i++)
        {
            uint key = ReadU32(s, i * stride + 0);
            uint itemId = ReadU32(s, i * stride + 4);
            float f0 = ReadF32(s, i * stride + 8);
            float f1 = ReadF32(s, i * stride + 12);
            float f2 = ReadF32(s, i * stride + 16);
            float f3 = ReadF32(s, i * stride + 20);
            float f4 = ReadF32(s, i * stride + 24);
            float f5 = ReadF32(s, i * stride + 28);
            float radius = ReadF32(s, i * stride + 32);
            uint u1 = ReadU32(s, i * stride + 36);
            byte fl0 = s[i * stride + 40];
            byte fl1 = s[i * stride + 41];
            byte fl2 = s[i * stride + 42];
            byte fl3 = s[i * stride + 43];
            uint prob = ReadU32(s, i * stride + 44);
            Console.WriteLine($"    [{i:D3}] key=0x{key:X8}, item={itemId}, f=[{f0:F2},{f1:F2},{f2:F2},{f3:F2},{f4:F2},{f5:F2}], radius={radius:F2}, u1={u1}, fl=[{fl0},{fl1},{fl2},{fl3}], prob={prob}");
        }

        Console.WriteLine("  Last 5 records:");
        for (int i = count - 5; i < count; i++)
        {
            uint key = ReadU32(s, i * stride + 0);
            uint itemId = ReadU32(s, i * stride + 4);
            float f0 = ReadF32(s, i * stride + 8);
            float f1 = ReadF32(s, i * stride + 12);
            float f2 = ReadF32(s, i * stride + 16);
            float f3 = ReadF32(s, i * stride + 20);
            float f4 = ReadF32(s, i * stride + 24);
            float f5 = ReadF32(s, i * stride + 28);
            float radius = ReadF32(s, i * stride + 32);
            uint u1 = ReadU32(s, i * stride + 36);
            byte fl0 = s[i * stride + 40];
            byte fl1 = s[i * stride + 41];
            byte fl2 = s[i * stride + 42];
            byte fl3 = s[i * stride + 43];
            uint prob = ReadU32(s, i * stride + 44);
            Console.WriteLine($"    [{i:D3}] key=0x{key:X8}, item={itemId}, f=[{f0:F2},{f1:F2},{f2:F2},{f3:F2},{f4:F2},{f5:F2}], radius={radius:F2}, u1={u1}, fl=[{fl0},{fl1},{fl2},{fl3}], prob={prob}");
        }

        // Full-table statistics
        for (int i = 0; i < count; i++)
        {
            uint key = ReadU32(s, i * stride + 0);
            float radius = ReadF32(s, i * stride + 32);
            uint u1 = ReadU32(s, i * stride + 36);
            byte fl0 = s[i * stride + 40];
            byte fl1 = s[i * stride + 41];
            byte fl2 = s[i * stride + 42];
            byte fl3 = s[i * stride + 43];
            uint prob = ReadU32(s, i * stride + 44);

            if (i > 0 && key != prevKey + 1) keySeqBy1 = false;
            prevKey = key;

            if (radius < minRadius) minRadius = radius;
            if (radius > maxRadius) maxRadius = radius;
            if (u1 == 0) u1Zero++; else { if (!u1Other.ContainsKey(u1)) u1Other[u1] = 0; u1Other[u1]++; }
            if (prob == 100) prob100++; else { if (!probOther.ContainsKey(prob)) probOther[prob] = 0; probOther[prob]++; }
            if (!flag0Vals.ContainsKey(fl0)) flag0Vals[fl0] = 0; flag0Vals[fl0]++;
            if (!flag1Vals.ContainsKey(fl1)) flag1Vals[fl1] = 0; flag1Vals[fl1]++;
            if (!flag2Vals.ContainsKey(fl2)) flag2Vals[fl2] = 0; flag2Vals[fl2]++;
            if (!flag3Vals.ContainsKey(fl3)) flag3Vals[fl3] = 0; flag3Vals[fl3]++;
        }

        Console.WriteLine($"  creature_key sequential-by-1: {keySeqBy1}");
        Console.WriteLine($"  scale_or_radius (+32): min={minRadius:F4}, max={maxRadius:F4}");
        Console.WriteLine($"  unknown_u1 (+36): zero={u1Zero}/{count}, non-zero: {string.Join(", ", u1Other.Select(kv => $"0x{kv.Key:X}={kv.Value}"))}");
        Console.WriteLine($"  flag_0 (+40): distinct={flag0Vals.Count}, values={string.Join(", ", flag0Vals.Select(kv => $"{kv.Key}={kv.Value}"))}");
        Console.WriteLine($"  flag_1 (+41): distinct={flag1Vals.Count}, values={string.Join(", ", flag1Vals.Select(kv => $"{kv.Key}={kv.Value}"))}");
        Console.WriteLine($"  flag_2 (+42): distinct={flag2Vals.Count}, values={string.Join(", ", flag2Vals.Select(kv => $"{kv.Key}={kv.Value}"))}");
        Console.WriteLine($"  flag_3 (+43): distinct={flag3Vals.Count}, values={string.Join(", ", flag3Vals.Select(kv => $"{kv.Key}={kv.Value}"))}");
        Console.WriteLine($"  probability (+44): ==100 count={prob100}/{count}, others={string.Join(", ", probOther.Select(kv => $"{kv.Key}={kv.Value}"))}");

        // Try to infer creature_key structure: split high16/low16
        uint firstKey = ReadU32(s, 0);
        uint lastKey = ReadU32(s, (count - 1) * stride);
        Console.WriteLine($"  creature_key first=0x{firstKey:X8} ({firstKey}), last=0x{lastKey:X8} ({lastKey})");
        // Check if key upper half is constant (compound id = type | index)
        uint firstHi = firstKey >> 16;
        bool hiConstant = true;
        for (int i = 0; i < count; i++)
        {
            uint k = ReadU32(s, i * stride);
            if ((k >> 16) != firstHi) { hiConstant = false; break; }
        }
        Console.WriteLine($"  creature_key hi16 constant: {hiConstant}" + (hiConstant ? $" (={firstHi})" : ""));
    }
    Console.WriteLine();
}

Console.WriteLine("Done.");
return 0;

static string? FindRepoRoot()
{
    string? dir = AppContext.BaseDirectory;
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir, "MartialHeroes.slnx"))) return dir;
        dir = Path.GetDirectoryName(dir);
    }
    return null;
}
