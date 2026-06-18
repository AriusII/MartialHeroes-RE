// V15-ui-val: throwaway harness for Campaign-8 lane V15.
// Validates mobinfo.mi record-by-record and counts crestlist.txt lines.
// NEVER committed. NEVER added to MartialHeroes.slnx.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var cp949 = Encoding.GetEncoding(949);

string infPath = "C:/Users/Arius/RiderProjects/MartialHeroes/05.Presentation/MartialHeroes.Client.Godot/clientdata/data.inf";
string vfsPath = "C:/Users/Arius/RiderProjects/MartialHeroes/05.Presentation/MartialHeroes.Client.Godot/clientdata/data/data.vfs";

using var archive = MappedVfsArchive.Open(infPath, vfsPath);
Console.WriteLine($"Mounted {archive.GetEntries().Length:N0} entries.");

// ===========================
// 1. mobinfo.mi — full parse
// ===========================
Console.WriteLine("\n=== mobinfo.mi ===");
var miBytes = archive.GetFileContent("data/ui/mobinfo.mi");
var mi = miBytes.Span;
Console.WriteLine($"  Total size: {mi.Length} bytes");

uint recordCount = BinaryPrimitives.ReadUInt32LittleEndian(mi[0..4]);
Console.WriteLine($"  Header recordCount: {recordCount}");
Console.WriteLine($"  Expected size: 4 + {recordCount}*28 = {4 + recordCount * 28}  (matches={4 + recordCount * 28 == mi.Length})");

// Track field statistics
var field1Vals = new List<uint>();
var field2Vals = new List<uint>();
var field3Vals = new List<uint>();
var field4Vals = new List<uint>();
var field5Vals = new List<uint>();
var field6Vals = new List<uint>();

Console.WriteLine("\n  rec  f0(ord) f1       f2       f3  f4         f5         f6");
for (int i = 0; i < (int)recordCount; i++)
{
    int off = 4 + i * 28;
    uint f0 = BinaryPrimitives.ReadUInt32LittleEndian(mi[off..]);
    uint f1 = BinaryPrimitives.ReadUInt32LittleEndian(mi[(off + 4)..]);
    uint f2 = BinaryPrimitives.ReadUInt32LittleEndian(mi[(off + 8)..]);
    uint f3 = BinaryPrimitives.ReadUInt32LittleEndian(mi[(off + 12)..]);
    uint f4 = BinaryPrimitives.ReadUInt32LittleEndian(mi[(off + 16)..]);
    uint f5 = BinaryPrimitives.ReadUInt32LittleEndian(mi[(off + 20)..]);
    uint f6 = BinaryPrimitives.ReadUInt32LittleEndian(mi[(off + 24)..]);

    string F(uint v) => v == 0xFFFFFFFF ? "NONE" : v.ToString();
    Console.WriteLine($"  [{i,2}] {f0,3}  {F(f1),-8} {F(f2),-8} {F(f3),3}  {F(f4),-10} {F(f5),-10} {F(f6)}");

    field1Vals.Add(f1);
    field2Vals.Add(f2);
    field3Vals.Add(f3);
    field4Vals.Add(f4);
    field5Vals.Add(f5);
    field6Vals.Add(f6);
}

// Statistics helper
void PrintStats(string name, List<uint> vals)
{
    var nonSentinel = vals.FindAll(v => v != 0xFFFFFFFF);
    int sentinel = vals.Count - nonSentinel.Count;
    uint min = uint.MaxValue, max = 0;
    foreach (var v in nonSentinel) { if (v < min) min = v; if (v > max) max = v; }
    var distinct = new HashSet<uint>(nonSentinel);
    Console.WriteLine($"  {name}: distinct={distinct.Count}  sentinel={sentinel}/{vals.Count}  " +
                      (nonSentinel.Count > 0 ? $"min={min}  max={max}" : "all-sentinel"));
}

Console.WriteLine("\n  --- field stats ---");
PrintStats("f1", field1Vals);
PrintStats("f2", field2Vals);
PrintStats("f3", field3Vals);
PrintStats("f4", field4Vals);
PrintStats("f5", field5Vals);
PrintStats("f6", field6Vals);

// Check f1/f2 ±1 pair relationship
Console.WriteLine("\n  --- f1/f2 pair analysis ---");
int pairMatch = 0, pairMismatch = 0;
for (int i = 0; i < (int)recordCount; i++)
{
    uint f1 = field1Vals[i], f2 = field2Vals[i];
    if (f1 == 0xFFFFFFFF || f2 == 0xFFFFFFFF) continue;
    long diff = (long)f1 - (long)f2;
    if (diff == 1 || diff == -1) pairMatch++;
    else { pairMismatch++; Console.WriteLine($"    rec[{i}] f1={f1} f2={f2} diff={diff}"); }
}
Console.WriteLine($"  f1/f2 ±1 pair: {pairMatch} match / {pairMismatch} mismatch (excl. sentinel)");

// Check f4/f5 delta
Console.WriteLine("\n  --- f4/f5 pair analysis ---");
var deltas = new Dictionary<long, int>();
for (int i = 0; i < (int)recordCount; i++)
{
    uint f4 = field4Vals[i], f5 = field5Vals[i];
    if (f4 == 0xFFFFFFFF || f5 == 0xFFFFFFFF) continue;
    long diff = (long)f5 - (long)f4;
    deltas.TryGetValue(diff, out int cnt); deltas[diff] = cnt + 1;
}
foreach (var kv in deltas) Console.WriteLine($"    delta {kv.Key}: {kv.Value} records");

// ===========================
// 2. crestlist.txt — line count and field analysis
// ===========================
Console.WriteLine("\n=== crestlist.txt ===");
var crestBytes = archive.GetFileContent("data/ui/guildicon/crestlist.txt");
string crestText = cp949.GetString(crestBytes.Span);
var lines = crestText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
Console.WriteLine($"  File size: {crestBytes.Length} bytes");
Console.WriteLine($"  Line count (non-empty): {lines.Length}");

// Analyse structure: is it always {region}_{type}_{guild_id}_{server_id}_{name}.dds ?
int wellFormed = 0, malformed = 0;
var regions = new HashSet<string>();
var types = new HashSet<string>();
var serverIds = new HashSet<string>();
int hasKoreanName = 0;

foreach (var line in lines)
{
    var parts = line.Split('_');
    if (parts.Length >= 5 && line.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
    {
        wellFormed++;
        regions.Add(parts[0]);
        types.Add(parts[1]);
        serverIds.Add(parts[3]);
        // Check if name part (after 4th underscore) is present
        string nameAndExt = string.Join("_", parts[4..]);
        if (nameAndExt.Length > 4) hasKoreanName++;
    }
    else malformed++;
}
Console.WriteLine($"  Well-formed {"{region}_{type}_{id}_{server}_{name}.dds"}: {wellFormed}");
Console.WriteLine($"  Malformed / exceptions: {malformed}");
Console.WriteLine($"  Region values: [{string.Join(",", regions)}]");
Console.WriteLine($"  Type values: [{string.Join(",", types)}]");
Console.WriteLine($"  ServerId values: [{string.Join(",", serverIds)}]");
Console.WriteLine($"  Lines with name field: {hasKoreanName}");

// Show first and last few lines for cross-check
Console.WriteLine($"\n  First 3 lines (CP949):");
for (int i = 0; i < Math.Min(3, lines.Length); i++) Console.WriteLine($"    [{i}] {lines[i]}");
Console.WriteLine($"  Last 3 lines (CP949):");
for (int i = Math.Max(0, lines.Length - 3); i < lines.Length; i++) Console.WriteLine($"    [{i}] {lines[i]}");
