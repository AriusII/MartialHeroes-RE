// THROWAWAY — CV-2: backdrop cell d000x10000z9990 texture-chain verification.
// Reads bgtexture.txt (data/map000/texture/bgtexture.txt), finds the rows for specific
// texture ids referenced in d000x10000z9990.map, and checks VFS existence of the
// resolved data/map000/texture/... paths.
// NEVER add to MartialHeroes.slnx. NEVER commit.

using System.Text;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Encoding cp949 = Encoding.GetEncoding(949);

// Resolve client path
string infPath = "D:/MartialHeroesClient/data.inf";
string vfsPath = "D:/MartialHeroesClient/data/data.vfs";

string[] roots = [
    Environment.GetEnvironmentVariable("MH_CLIENT_DIR") ?? "",
    "C:/Users/Arius/RiderProjects/MartialHeroes/05.Presentation/MartialHeroes.Client.Godot/clientdata",
    "D:/MartialHeroesClient"
];
foreach (var root in roots)
{
    if (string.IsNullOrEmpty(root)) continue;
    var ci = Path.Combine(root, "data.inf");
    var cv = Path.Combine(root, "data", "data.vfs");
    if (File.Exists(ci) && File.Exists(cv)) { infPath = ci; vfsPath = cv; break; }
}

using var vfs = MappedVfsArchive.Open(infPath, vfsPath);

// Texture ids referenced in d000x10000z9990.map:
// - TERRAIN section:      116
// - FX3 section:          829, 830, 827
// - FX5 section:          829, 830, 827  (same as FX3)
// - BUILDING section:     824, 858, 819, 822, 821, 820, 823
var targetIds = new HashSet<int> { 116, 819, 820, 821, 822, 823, 824, 827, 829, 830, 858 };

// Read bgtexture.txt
const string bgtexPath = "data/map000/texture/bgtexture.txt";
var raw = vfs.GetFileContent(bgtexPath);
string text = cp949.GetString(raw.Span);

Console.WriteLine($"bgtexture.txt  size={raw.Length} bytes");
Console.WriteLine($"Resolving {targetIds.Count} distinct texture ids from .map sections:");
Console.WriteLine();
Console.WriteLine($"{"id",-6} {"col1",-6} {"rel_path",-40} {"vfs_path",-60} {"exists"}");
Console.WriteLine(new string('-', 130));

int resolvedCount = 0;
int missingCount = 0;

foreach (var line in text.Split('\n'))
{
    var trimmed = line.TrimEnd('\r');
    if (string.IsNullOrWhiteSpace(trimmed)) continue;
    var parts = trimmed.Split('\t');
    if (parts.Length < 3) continue;
    if (!int.TryParse(parts[0].Trim(), out int id)) continue;
    if (!targetIds.Contains(id)) continue;

    string col1 = parts[1].Trim();
    string relPath = parts[2].Trim();
    // Resolve: the rel_path is relative to data/map000/texture/
    // Extension: check .dds first, then .png (bgtexture uses no extension in the path itself)
    string baseVfsPath = $"data/map000/texture/{relPath}";
    // Try common extensions
    string? foundPath = null;
    foreach (var ext in new[] { ".dds", ".png", "" })
    {
        var candidate = baseVfsPath + ext;
        if (vfs.Contains(candidate)) { foundPath = candidate; break; }
    }

    bool exists = foundPath != null;
    string vfsDisplay = foundPath ?? baseVfsPath + ".{dds|png} NOT FOUND";
    Console.WriteLine($"{id,-6} {col1,-6} {relPath,-40} {vfsDisplay,-60} {(exists ? "OK" : "MISSING")}");
    if (exists) resolvedCount++; else missingCount++;
}

Console.WriteLine();
Console.WriteLine($"Result: {resolvedCount} resolved OK, {missingCount} MISSING (out of {targetIds.Count} distinct ids)");

// Also print section summary
Console.WriteLine();
Console.WriteLine("Section → texture id mapping from d000x10000z9990.map:");
Console.WriteLine("  TERRAIN:   116");
Console.WriteLine("  FX3:       829, 830, 827");
Console.WriteLine("  FX5:       829, 830, 827  (identical to FX3)");
Console.WriteLine("  BUILDING:  824, 858, 819, 822, 821, 820, 823");
Console.WriteLine();
Console.WriteLine("Distinct ids by section type:");
Console.WriteLine("  Terrain ground:  {116}");
Console.WriteLine("  Water (FX3/FX5): {827, 829, 830}");
Console.WriteLine("  Building (BUD):  {819, 820, 821, 822, 823, 824, 858}");
Console.WriteLine($"  TOTAL distinct:  {targetIds.Count}");
