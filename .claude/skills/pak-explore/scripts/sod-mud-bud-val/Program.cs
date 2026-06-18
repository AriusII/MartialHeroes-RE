// V11 — SOD edge_pad investigation
// NEVER committed.

using System.Buffers.Binary;
using MartialHeroes.Assets.Vfs;

string infPath = @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata\data.inf";
string vfsPath = @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata\data\data.vfs";

using var archive = MappedVfsArchive.Open(infPath, vfsPath);
var entries = archive.GetEntries();

Console.WriteLine("=== SOD edge_pad0 / edge_pad1 investigation ===");

// Sample the distinct non-zero values for edge_pad0 and edge_pad1
// and print a few examples with their context
var pad0Values = new Dictionary<float, int>();
var pad1Values = new Dictionary<float, int>();
int pad0NonZeroCount = 0, pad1NonZeroCount = 0;
int totalQuads = 0;
bool foundFirstPad0 = false, foundFirstPad1 = false;

var sodEntries = entries.ToArray().Where(e => e.Name.EndsWith(".sod", StringComparison.Ordinal)).ToList();

foreach (var e in sodEntries)
{
    ReadOnlyMemory<byte> raw;
    try { raw = archive.GetFileContent(e.Name); }
    catch { continue; }

    var span = raw.Span;
    if (span.Length < 4) continue;

    uint solidCount = BinaryPrimitives.ReadUInt32LittleEndian(span);
    long solidBlockEnd = 4L + solidCount * 108L;
    if (solidBlockEnd > span.Length) continue;

    int cursor = 4 + (int)solidCount * 108;

    for (int s = 0; s < (int)solidCount; s++)
    {
        if (cursor + 4 > span.Length) goto next;
        uint streamQC = BinaryPrimitives.ReadUInt32LittleEndian(span[cursor..]);
        cursor += 4;

        for (uint q = 0; q < streamQC; q++)
        {
            if (cursor + 48 > span.Length) goto next;

            float edgeSlope     = BinaryPrimitives.ReadSingleLittleEndian(span[(cursor + 32)..]);
            float edgePad0      = BinaryPrimitives.ReadSingleLittleEndian(span[(cursor + 36)..]);
            float edgeIntercept = BinaryPrimitives.ReadSingleLittleEndian(span[(cursor + 40)..]);
            float edgePad1      = BinaryPrimitives.ReadSingleLittleEndian(span[(cursor + 44)..]);

            if (edgePad0 != 0f)
            {
                pad0NonZeroCount++;
                if (!pad0Values.ContainsKey(edgePad0)) pad0Values[edgePad0] = 0;
                pad0Values[edgePad0]++;
                if (!foundFirstPad0 && pad0Values.Count <= 5)
                {
                    // Print the context
                    float x0 = BinaryPrimitives.ReadSingleLittleEndian(span[(cursor + 0)..]);
                    float z0 = BinaryPrimitives.ReadSingleLittleEndian(span[(cursor + 4)..]);
                    Console.WriteLine($"  pad0 nonzero example: file={e.Name} solid={s} quad={q}");
                    Console.WriteLine($"    corner0=({x0},{z0}) edgeSlope={edgeSlope} edgePad0={edgePad0} edgeIntercept={edgeIntercept} edgePad1={edgePad1}");
                    if (pad0Values.Count == 5) foundFirstPad0 = true;
                }
            }

            if (edgePad1 != 0f)
            {
                pad1NonZeroCount++;
                if (!pad1Values.ContainsKey(edgePad1)) pad1Values[edgePad1] = 0;
                pad1Values[edgePad1]++;
                if (!foundFirstPad1 && pad1Values.Count <= 5)
                {
                    float x0 = BinaryPrimitives.ReadSingleLittleEndian(span[(cursor + 0)..]);
                    float z0 = BinaryPrimitives.ReadSingleLittleEndian(span[(cursor + 4)..]);
                    Console.WriteLine($"  pad1 nonzero example: file={e.Name} solid={s} quad={q}");
                    Console.WriteLine($"    corner0=({x0},{z0}) edgeSlope={edgeSlope} edgePad0={edgePad0} edgeIntercept={edgeIntercept} edgePad1={edgePad1}");
                    if (pad1Values.Count == 5) foundFirstPad1 = true;
                }
            }

            totalQuads++;
            cursor += 48;
        }
    }
    next:;
}

Console.WriteLine($"\nTotal quads: {totalQuads}");
Console.WriteLine($"edge_pad0 nonzero: {pad0NonZeroCount} ({100.0*pad0NonZeroCount/totalQuads:F1}%)");
Console.WriteLine($"  Distinct nonzero pad0 values: {pad0Values.Count}");
Console.WriteLine($"  Top 10 pad0 values:");
foreach (var kv in pad0Values.OrderByDescending(k => k.Value).Take(10))
    Console.WriteLine($"    {kv.Key:G}: {kv.Value} quads");

Console.WriteLine($"edge_pad1 nonzero: {pad1NonZeroCount} ({100.0*pad1NonZeroCount/totalQuads:F1}%)");
Console.WriteLine($"  Distinct nonzero pad1 values: {pad1Values.Count}");
Console.WriteLine($"  Top 10 pad1 values:");
foreach (var kv in pad1Values.OrderByDescending(k => k.Value).Take(10))
    Console.WriteLine($"    {kv.Key:G}: {kv.Value} quads");

Console.WriteLine("\nV11 pad-check done.");
