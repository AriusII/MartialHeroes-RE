// V10 — arr-spawn-val: validate npc*.arr (28B) and mob*.arr (20B) across all VFS instances.
// THROWAWAY — not in slnx, not committed.
using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Vfs;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
// CP949 registered (not used for these binary files, but mandatory for any text)

string infPath = "C:/Users/Arius/RiderProjects/MartialHeroes/05.Presentation/MartialHeroes.Client.Godot/clientdata/data.inf";
string vfsPath = "C:/Users/Arius/RiderProjects/MartialHeroes/05.Presentation/MartialHeroes.Client.Godot/clientdata/data/data.vfs";

for (int a = 0; a < args.Length - 1; a++)
{
    if (args[a] == "--inf") infPath = args[a + 1];
    if (args[a] == "--vfs") vfsPath = args[a + 1];
}

using var archive = MappedVfsArchive.Open(infPath, vfsPath);
var entries = archive.GetEntries();

// Collect npc and mob paths
var npcPaths = new List<string>();
var mobPaths = new List<string>();
foreach (var e in entries)
{
    string name = e.Name;
    if (!name.EndsWith(".arr")) continue;
    string fn = System.IO.Path.GetFileName(name);
    if (fn.StartsWith("npc")) npcPaths.Add(name);
    else if (fn.StartsWith("mob")) mobPaths.Add(name);
}
npcPaths.Sort();
mobPaths.Sort();

Console.WriteLine($"=== NPC .arr files: {npcPaths.Count} ===");
Console.WriteLine($"=== MOB .arr files: {mobPaths.Count} ===");
Console.WriteLine();

// =========================================================
// NPC analysis (28-byte stride)
// =========================================================
Console.WriteLine("--- NPC STRIDE ANALYSIS (expected 28) ---");

// Per-field accumulators for NPC
var npcField02_vals = new Dictionary<ushort, int>();
var npcField02_nonzero = 0;
var npcSpawnType_vals = new Dictionary<uint, int>();
var npcUnk20_vals = new Dictionary<uint, int>();
var npcUnk24_vals = new Dictionary<uint, int>();
var npcRotY_vals = new List<float>();
var npcWorldX_vals = new List<float>();
var npcWorldZ_vals = new List<float>();
var npcMobId_vals = new Dictionary<ushort, int>();
int npcTotalRecords = 0;
var npcBadStride = new List<string>();
var npcBadFiles = new List<(string path, int size, int rem)>();

foreach (var path in npcPaths)
{
    var data = archive.GetFileContent(path);
    int sz = data.Length;
    int rem = sz % 28;
    int recs = sz / 28;

    if (rem != 0)
    {
        npcBadFiles.Add((path, sz, rem));
        // Still parse what we can
    }

    // Use production parser
    var parsed = NpcSpawnParser.Parse(data);
    npcTotalRecords += parsed.Records.Length;

    foreach (var rec in parsed.Records)
    {
        // field_02
        npcField02_vals.TryGetValue(rec.Field02, out int f2c);
        npcField02_vals[rec.Field02] = f2c + 1;
        if (rec.Field02 != 0) npcField02_nonzero++;

        // spawn_type
        npcSpawnType_vals.TryGetValue(rec.SpawnType, out int stc);
        npcSpawnType_vals[rec.SpawnType] = stc + 1;

        // unknown_20
        npcUnk20_vals.TryGetValue(rec.Unknown20, out int u20c);
        npcUnk20_vals[rec.Unknown20] = u20c + 1;

        // unknown_24
        npcUnk24_vals.TryGetValue(rec.Unknown24, out int u24c);
        npcUnk24_vals[rec.Unknown24] = u24c + 1;

        // rotation_y
        npcRotY_vals.Add(rec.RotationY);

        // coords
        npcWorldX_vals.Add(rec.WorldX);
        npcWorldZ_vals.Add(rec.WorldZ);

        // mob_id
        npcMobId_vals.TryGetValue(rec.MobId, out int midc);
        npcMobId_vals[rec.MobId] = midc + 1;
    }
}

Console.WriteLine($"Total NPC records: {npcTotalRecords}");
Console.WriteLine($"Files with stride mismatch (rem != 0):");
if (npcBadFiles.Count == 0) Console.WriteLine("  (none)");
foreach (var (p, sz, r) in npcBadFiles)
    Console.WriteLine($"  {p}  size={sz}  rem={r}  records_parsed={sz/28}");

Console.WriteLine();
Console.WriteLine("NPC field_02 (offset +2, u16):");
Console.WriteLine($"  Distinct values: {npcField02_vals.Count}");
foreach (var kv in npcField02_vals.OrderByDescending(x => x.Value))
    Console.WriteLine($"    val={kv.Key} count={kv.Value} ({100.0*kv.Value/npcTotalRecords:F1}%)");

Console.WriteLine();
Console.WriteLine("NPC spawn_type (offset +16, u32):");
Console.WriteLine($"  Distinct values: {npcSpawnType_vals.Count}");
foreach (var kv in npcSpawnType_vals.OrderByDescending(x => x.Value))
    Console.WriteLine($"    val={kv.Key} count={kv.Value} ({100.0*kv.Value/npcTotalRecords:F1}%)");

Console.WriteLine();
Console.WriteLine("NPC unknown_20 (offset +20, u32):");
Console.WriteLine($"  Distinct values: {npcUnk20_vals.Count}");
foreach (var kv in npcUnk20_vals.OrderByDescending(x => x.Value))
    Console.WriteLine($"    val={kv.Key} count={kv.Value} ({100.0*kv.Value/npcTotalRecords:F1}%)");

Console.WriteLine();
Console.WriteLine("NPC unknown_24 (offset +24, u32):");
Console.WriteLine($"  Distinct values: {npcUnk24_vals.Count}");
foreach (var kv in npcUnk24_vals.OrderByDescending(x => x.Value))
    Console.WriteLine($"    val={kv.Key} count={kv.Value} ({100.0*kv.Value/npcTotalRecords:F1}%)");

Console.WriteLine();
if (npcRotY_vals.Count > 0)
{
    var sorted = npcRotY_vals.OrderBy(x => x).ToList();
    Console.WriteLine($"NPC rotation_y (offset +12, f32): count={sorted.Count}");
    Console.WriteLine($"  min={sorted.First():F4}  max={sorted.Last():F4}  mean={sorted.Average():F4}");
    // Show distribution buckets
    var buckets = new Dictionary<string, int>();
    foreach (var v in sorted)
    {
        // Detect if it looks like a float (finite) or 0xCCCCCCCC (uninit garbage)
        uint bits = BitConverter.SingleToUInt32Bits(v);
        string bucket;
        if (!float.IsFinite(v)) bucket = "non-finite";
        else if (bits == 0xCCCCCCCC) bucket = "0xCCCCCCCC(uninit)";
        else if (MathF.Abs(v) < 0.001f) bucket = "~0";
        else if (v >= 1.4f && v <= 1.6f) bucket = "~pi/2(1.57)";
        else if (v >= 3.0f && v <= 3.3f) bucket = "~pi(3.14)";
        else if (v >= 6.0f && v <= 6.4f) bucket = "~2pi(6.28)";
        else bucket = $"other({v:F2})";
        buckets.TryGetValue(bucket, out int bc);
        buckets[bucket] = bc + 1;
    }
    foreach (var kv in buckets.OrderByDescending(x => x.Value))
        Console.WriteLine($"    {kv.Key}: {kv.Value} ({100.0*kv.Value/sorted.Count:F1}%)");
    // Sample 5 raw values
    Console.Write("  sample values: ");
    foreach (var v in sorted.Take(5)) Console.Write($"{v:F4} ");
    Console.WriteLine();
}

Console.WriteLine();
Console.WriteLine($"NPC world_x: min={npcWorldX_vals.Min():F1}  max={npcWorldX_vals.Max():F1}");
Console.WriteLine($"NPC world_z: min={npcWorldZ_vals.Min():F1}  max={npcWorldZ_vals.Max():F1}");
Console.WriteLine($"NPC mob_id: distinct={npcMobId_vals.Count}  min={npcMobId_vals.Keys.Min()}  max={npcMobId_vals.Keys.Max()}");

// Check npc207 specifically — 240 bytes, 240%28 = 16 remainder
Console.WriteLine();
Console.WriteLine("--- npc207.arr deep dive (240 bytes, remainder 16) ---");
{
    var data = archive.GetFileContent("data/map207/npc207.arr");
    Console.WriteLine($"  size={data.Length}  full_records={data.Length/28}  trailing_bytes={data.Length%28}");
    // Check if last 16 bytes match the npc000 pattern (partial record)
    var span = data.Span;
    int fullRecs = data.Length / 28;
    Console.WriteLine($"  Full records parsed: {fullRecs}");
    int tailOff = fullRecs * 28;
    Console.Write($"  Trailing {data.Length - tailOff} bytes: ");
    for (int i = tailOff; i < data.Length; i++) Console.Write($"{span[i]:X2} ");
    Console.WriteLine();
    // Interpret the 8 full records from npc207
    var parsed = NpcSpawnParser.Parse(data);
    Console.WriteLine($"  Records: mob_ids={string.Join(",", parsed.Records.Select(r => r.MobId))}");
    Console.WriteLine($"  spawn_types={string.Join(",", parsed.Records.Select(r => r.SpawnType))}");
    Console.WriteLine($"  field02s={string.Join(",", parsed.Records.Select(r => r.Field02))}");
    // check if the 0xCCCC pattern is in field_02 of npc207
    Console.WriteLine("  Note: 0xCCCC = 52428 decimal = uninitialized memory marker");
}

// =========================================================
// MOB analysis (20-byte stride)
// =========================================================
Console.WriteLine();
Console.WriteLine("--- MOB STRIDE ANALYSIS (expected 20) ---");

// Per-field accumulators for MOB
var mobPad_vals = new Dictionary<ushort, int>();
var mobFieldC_vals = new List<float>();
var mobField10_vals = new List<float>();
var mobWorldX_vals = new List<float>();
var mobWorldZ_vals = new List<float>();
var mobMobId_vals = new Dictionary<ushort, int>();
int mobTotalRecordsRaw = 0;  // before dedup
int mobTotalRecordsParsed = 0;  // after dedup in parser
var mobBadFiles = new List<(string path, int size, int rem)>();
int mobZeroId_count = 0;

foreach (var path in mobPaths)
{
    var data = archive.GetFileContent(path);
    int sz = data.Length;
    int rem = sz % 20;
    int recs = sz / 20;

    if (rem != 0) mobBadFiles.Add((path, sz, rem));

    mobTotalRecordsRaw += recs;

    // Use production parser (includes dedup)
    var parsed = MobSpawnParser.Parse(data);
    mobTotalRecordsParsed += parsed.Length;

    // Raw scan (before dedup) for stats
    var span = data.Span;
    for (int i = 0; i < recs; i++)
    {
        int off = i * 20;
        ushort mobId = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(off, 2));
        ushort pad = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(off + 2, 2));
        float wx = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(off + 4, 4));
        float wz = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(off + 8, 4));
        float fc = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(off + 12, 4));
        float f10 = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(off + 16, 4));

        if (mobId == 0) { mobZeroId_count++; continue; }

        mobPad_vals.TryGetValue(pad, out int pc);
        mobPad_vals[pad] = pc + 1;

        mobFieldC_vals.Add(fc);
        mobField10_vals.Add(f10);
        mobWorldX_vals.Add(wx);
        mobWorldZ_vals.Add(wz);

        mobMobId_vals.TryGetValue(mobId, out int mc);
        mobMobId_vals[mobId] = mc + 1;
    }
}

int mobNonZeroRaw = mobTotalRecordsRaw - mobZeroId_count;

Console.WriteLine($"Total MOB raw records (before dedup, before zero-id filter): {mobTotalRecordsRaw}");
Console.WriteLine($"Zero-id records skipped: {mobZeroId_count}");
Console.WriteLine($"Non-zero raw records: {mobNonZeroRaw}");
Console.WriteLine($"After dedup (production parser output): {mobTotalRecordsParsed}");
Console.WriteLine($"Files with stride mismatch (rem != 0):");
if (mobBadFiles.Count == 0) Console.WriteLine("  (none)");
foreach (var (p, sz, r) in mobBadFiles)
    Console.WriteLine($"  {p}  size={sz}  rem={r}");

Console.WriteLine();
Console.WriteLine("MOB pad (offset +2, u16):");
Console.WriteLine($"  Distinct values: {mobPad_vals.Count}");
foreach (var kv in mobPad_vals.OrderByDescending(x => x.Value).Take(15))
    Console.WriteLine($"    val={kv.Key} (0x{kv.Key:X4}) count={kv.Value} ({100.0*kv.Value/mobNonZeroRaw:F2}%)");

Console.WriteLine();
Console.WriteLine($"MOB world_x: min={mobWorldX_vals.Min():F1}  max={mobWorldX_vals.Max():F1}");
Console.WriteLine($"MOB world_z: min={mobWorldZ_vals.Min():F1}  max={mobWorldZ_vals.Max():F1}");
Console.WriteLine($"MOB mob_id: distinct={mobMobId_vals.Count}  min={mobMobId_vals.Keys.Min()}  max={mobMobId_vals.Keys.Max()}");

Console.WriteLine();
if (mobFieldC_vals.Count > 0)
{
    var sorted = mobFieldC_vals.OrderBy(x => x).ToList();
    Console.WriteLine($"MOB field_c (offset +12, f32): count={sorted.Count}");
    Console.WriteLine($"  min={sorted.First():F4}  max={sorted.Last():F4}  mean={sorted.Average():F4}");
    // Bucket analysis
    var buckets = new Dictionary<string, int>();
    foreach (var v in sorted)
    {
        uint bits = BitConverter.SingleToUInt32Bits(v);
        string bucket;
        if (!float.IsFinite(v)) bucket = "non-finite";
        else if (bits == 0xCCCCCCCC) bucket = "0xCCCCCCCC(uninit)";
        else if (MathF.Abs(v) < 0.001f) bucket = "~0";
        else if (v >= 1.4f && v <= 1.6f) bucket = "~pi/2(1.57)";
        else if (v >= 3.0f && v <= 3.3f) bucket = "~pi(3.14)";
        else if (v >= 6.0f && v <= 6.4f) bucket = "~2pi(6.28)";
        else bucket = $"other({v:F2})";
        buckets.TryGetValue(bucket, out int bc);
        buckets[bucket] = bc + 1;
    }
    foreach (var kv in buckets.OrderByDescending(x => x.Value))
        Console.WriteLine($"    {kv.Key}: {kv.Value} ({100.0*kv.Value/sorted.Count:F1}%)");
    // Sample values
    Console.Write("  sample values (first 5): ");
    foreach (var v in sorted.Take(5)) Console.Write($"{v:F4} ");
    Console.WriteLine();
    Console.Write("  sample values (last 5): ");
    foreach (var v in sorted.TakeLast(5)) Console.Write($"{v:F4} ");
    Console.WriteLine();
}

Console.WriteLine();
if (mobField10_vals.Count > 0)
{
    var sorted = mobField10_vals.OrderBy(x => x).ToList();
    Console.WriteLine($"MOB field_10 (offset +16, f32): count={sorted.Count}");
    Console.WriteLine($"  min={sorted.First():F4}  max={sorted.Last():F4}  mean={sorted.Average():F4}");
    var buckets = new Dictionary<string, int>();
    foreach (var v in sorted)
    {
        uint bits = BitConverter.SingleToUInt32Bits(v);
        string bucket;
        if (!float.IsFinite(v)) bucket = "non-finite";
        else if (bits == 0xCCCCCCCC) bucket = "0xCCCCCCCC(uninit)";
        else if (MathF.Abs(v) < 0.001f) bucket = "~0.0";
        else if (v >= 0.5f && v <= 1.5f) bucket = "~1.0";
        else bucket = $"other({v:F2})";
        buckets.TryGetValue(bucket, out int bc);
        buckets[bucket] = bc + 1;
    }
    foreach (var kv in buckets.OrderByDescending(x => x.Value))
        Console.WriteLine($"    {kv.Key}: {kv.Value} ({100.0*kv.Value/sorted.Count:F1}%)");
    // Sample raw values
    Console.Write("  sample values (first 5): ");
    foreach (var v in sorted.Take(5)) Console.Write($"{v:F4} ");
    Console.WriteLine();
    Console.Write("  sample values (last 5): ");
    foreach (var v in sorted.TakeLast(5)) Console.Write($"{v:F4} ");
    Console.WriteLine();
    // Check if field10 is always the same as field_c (rotation?)
    int matchFC = 0;
    for (int i = 0; i < mobFieldC_vals.Count; i++)
        if (MathF.Abs(mobFieldC_vals[i] - mobField10_vals[i]) < 0.001f) matchFC++;
    Console.WriteLine($"  Records where field10 == field_c: {matchFC}/{mobFieldC_vals.Count} ({100.0*matchFC/mobFieldC_vals.Count:F1}%)");
}

// Check mob001 first few records manually (raw, no dedup)
Console.WriteLine();
Console.WriteLine("--- mob001.arr first 4 raw records (manual decode) ---");
{
    var data = archive.GetFileContent("data/map001/mob001.arr");
    var span = data.Span;
    for (int i = 0; i < 4 && (i+1)*20 <= data.Length; i++)
    {
        int off = i * 20;
        ushort mobId = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(off, 2));
        ushort pad = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(off + 2, 2));
        float wx = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(off + 4, 4));
        float wz = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(off + 8, 4));
        float fc = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(off + 12, 4));
        float f10 = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(off + 16, 4));
        Console.WriteLine($"  rec[{i}]: mobId={mobId} pad={pad}(0x{pad:X4}) X={wx:F1} Z={wz:F1} fc={fc:F4} f10={f10:F4}");
    }
}

// Check npc001 first few records
Console.WriteLine();
Console.WriteLine("--- npc001.arr first 4 records (via production parser) ---");
{
    var data = archive.GetFileContent("data/map001/npc001.arr");
    var parsed = NpcSpawnParser.Parse(data);
    foreach (var rec in parsed.Records.Take(4))
        Console.WriteLine($"  mobId={rec.MobId} field02={rec.Field02}(0x{rec.Field02:X4}) X={rec.WorldX:F1} Z={rec.WorldZ:F1} rotY={rec.RotationY:F4} spawnType={rec.SpawnType} unk20={rec.Unknown20} unk24={rec.Unknown24}");
}

// Check the 0xCCCC value in npc207
Console.WriteLine();
Console.WriteLine("--- Interpretation of 0xCCCC (52428) in field_02 ---");
Console.WriteLine("  0xCCCC = 52428 = common MSVC debug fill for uninitialized memory");
Console.WriteLine("  If field_02 == 0xCCCC in some records, those records may be uninitialized/invalid");
{
    int ccccCount = 0;
    foreach (var p in npcPaths)
    {
        var data = archive.GetFileContent(p);
        var parsed = NpcSpawnParser.Parse(data);
        foreach (var rec in parsed.Records)
            if (rec.Field02 == 0xCCCC) ccccCount++;
    }
    Console.WriteLine($"  Records with field_02 == 0xCCCC: {ccccCount}");
}

// Additional: check mob's "pad" for 0x760E pattern seen in mob001
Console.WriteLine();
Console.WriteLine("--- MOB pad analysis: is 0x760E common? ---");
{
    mobPad_vals.TryGetValue(0x760E, out int v760e);
    Console.WriteLine($"  pad=0x760E: {v760e} ({100.0*v760e/mobNonZeroRaw:F2}% of non-zero records)");
}

Console.WriteLine();
Console.WriteLine("=== DONE ===");
