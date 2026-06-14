// THROWAWAY harness — create-char-scan — never commit, never add to solution
// Analyses skin.txt + skinlist.txt + face/ + UI assets for character creation screen recovery.
using System.Text;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var cp949 = Encoding.GetEncoding(949);

var infPath = @"D:/MartialHeroesClient/data.inf";
var vfsPath = @"D:/MartialHeroesClient/data/data.vfs";

// Try project-local clientdata fallback
if (!File.Exists(infPath))
{
    infPath = @"C:/Users/Arius/RiderProjects/MartialHeroes/05.Presentation/MartialHeroes.Client.Godot/clientdata/data.inf";
    vfsPath = @"C:/Users/Arius/RiderProjects/MartialHeroes/05.Presentation/MartialHeroes.Client.Godot/clientdata/data/data.vfs";
}

Console.WriteLine($"Mounting VFS from: {infPath}");
using var vfs = MappedVfsArchive.Open(infPath, vfsPath);
Console.WriteLine($"Mounted {vfs.GetEntries().Length:N0} entries.");
Console.WriteLine();

// ─────────────────────────────────────────────────────────────
// 1. Parse skin.txt
// ─────────────────────────────────────────────────────────────
Console.WriteLine("=== SKIN.TXT ANALYSIS ===");
var skinBytes = vfs.GetFileContent("data/char/skin.txt");
var skinText = cp949.GetString(skinBytes.ToArray());
var skinLines = skinText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

// Row 0 = total count
Console.WriteLine($"Row 0 (count): {skinLines[0].Trim()}");
Console.WriteLine($"Total data rows (after row 0): {skinLines.Length - 1}");
Console.WriteLine();

// Parse data rows: tab-separated, 6 columns
// col0=IdA col1=IdB col2=class col3=variant col4=tex_id_A col5=tex_id_B
// Based on observed data structure
var skinRows = new List<(int idA, int idB, int classId, int variant, long texIdA, long texIdB)>();
for (int i = 1; i < skinLines.Length; i++)
{
    var cols = skinLines[i].TrimEnd('\r').Split('\t');
    if (cols.Length < 4) continue;
    if (!int.TryParse(cols[0], out int idA)) continue;
    if (!int.TryParse(cols[1], out int idB)) continue;
    if (!int.TryParse(cols[2], out int classId)) continue;
    if (!int.TryParse(cols[3], out int variant)) continue;
    long texA = cols.Length > 4 && long.TryParse(cols[4], out long ta) ? ta : 0;
    long texB = cols.Length > 5 && long.TryParse(cols[5], out long tb) ? tb : 0;
    skinRows.Add((idA, idB, classId, variant, texA, texB));
}

Console.WriteLine($"Parsed {skinRows.Count} skin rows.");
Console.WriteLine();

// ─────────────────────────────────────────────────────────────
// 2. Identify starter rows: IdA=1 + classId != 0 (class/sex selector rows)
// ─────────────────────────────────────────────────────────────
Console.WriteLine("=== STARTER CLASS ROWS (idA=1, classId!=0) ===");
var starterRows = skinRows.Where(r => r.idA == 1 && r.classId != 0).ToList();
Console.WriteLine($"Count: {starterRows.Count}");
Console.WriteLine("idA\tidB\tclassId\tvariant\ttexIdA\ttexIdB");
foreach (var r in starterRows.OrderBy(x => x.classId).ThenBy(x => x.idB))
{
    Console.WriteLine($"{r.idA}\t{r.idB}\t{r.classId}\t{r.variant}\t{r.texIdA}\t{r.texIdB}");
}
Console.WriteLine();

// Distinct class IDs used in starter rows
var starterClasses = starterRows.Select(r => r.classId).Distinct().OrderBy(x => x).ToList();
Console.WriteLine($"Distinct classIds in starter rows: {string.Join(", ", starterClasses)}");
Console.WriteLine();

// ─────────────────────────────────────────────────────────────
// 3. Variant distribution per class in starter rows
// ─────────────────────────────────────────────────────────────
Console.WriteLine("=== VARIANT OPTIONS PER CLASS (starter idA=1) ===");
foreach (var classId in starterClasses)
{
    var classRows = starterRows.Where(r => r.classId == classId).OrderBy(r => r.idB).ToList();
    Console.WriteLine($"  ClassId={classId}: {classRows.Count} rows, idBs=[{string.Join(",", classRows.Select(r => r.idB))}]");
    foreach (var r in classRows)
        Console.WriteLine($"    idB={r.idB} variant={r.variant} texA={r.texIdA} texB={r.texIdB}");
}
Console.WriteLine();

// ─────────────────────────────────────────────────────────────
// 4. idA=1, classId=0 rows (the "all class" skinned mesh rows — items/appearance)
// ─────────────────────────────────────────────────────────────
Console.WriteLine("=== IdA=1 / classId=0 ROWS (appearance skin-id rows) ===");
var appearanceRows = skinRows.Where(r => r.idA == 1 && r.classId == 0).ToList();
Console.WriteLine($"Count: {appearanceRows.Count}");
// Show first 20 only (sample)
Console.WriteLine("(First 20 rows shown)");
Console.WriteLine("idA\tidB\tclassId\tvariant\ttexIdA\ttexIdB");
foreach (var r in appearanceRows.Take(20))
    Console.WriteLine($"{r.idA}\t{r.idB}\t{r.classId}\t{r.variant}\t{r.texIdA}\t{r.texIdB}");
Console.WriteLine();

// ─────────────────────────────────────────────────────────────
// 5. All distinct idA values in skin.txt
// ─────────────────────────────────────────────────────────────
var allIdAs = skinRows.Select(r => r.idA).Distinct().OrderBy(x => x).ToList();
Console.WriteLine($"=== ALL DISTINCT idA VALUES IN SKIN.TXT ===");
Console.WriteLine(string.Join(", ", allIdAs));
Console.WriteLine();

// ─────────────────────────────────────────────────────────────
// 6. Decode tex_id encoding scheme: 9-digit ids
//    Hypothesis: 2CCSGVVVV where CC=class, S=sex(?), G=grade, VVVV=variant
//    or perhaps: 2CCSGVVVV = 2 + class(2d) + subclass(1d) + variant(5d)
// ─────────────────────────────────────────────────────────────
Console.WriteLine("=== SKN PATH VERIFICATION (starter class rows, texIdA != 0) ===");
// Verify .skn files exist for starter class skin_ids
foreach (var r in starterRows.Where(r => r.texIdA != 0).Take(8))
{
    var sknPath = $"data/char/skin/g{r.texIdA}.skn";
    var exists = vfs.Contains(sknPath);
    Console.WriteLine($"  g{r.texIdA}.skn → {(exists ? "EXISTS" : "MISSING")}  (classId={r.classId} idB={r.idB})");
}
Console.WriteLine();

// ─────────────────────────────────────────────────────────────
// 7. Skinlist.txt — maps skinlist IDs to .skn filenames
// ─────────────────────────────────────────────────────────────
Console.WriteLine("=== SKINLIST.TXT ===");
var slBytes = vfs.GetFileContent("data/char/skinlist.txt");
var slText = cp949.GetString(slBytes.ToArray());
var slLines = slText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
Console.WriteLine($"Total lines: {slLines.Length}  (row 0 = {slLines[0].Trim()})");
Console.WriteLine("First 30 rows:");
foreach (var ln in slLines.Take(30))
    Console.WriteLine($"  {ln.TrimEnd('\r')}");
Console.WriteLine();

// ─────────────────────────────────────────────────────────────
// 8. Face asset census: data/ui/face/*.tga (non-subdir)
// ─────────────────────────────────────────────────────────────
Console.WriteLine("=== FACE ASSET CENSUS (data/ui/face/*.tga) ===");
var faceEntries = vfs.GetEntries()
    .ToArray()
    .Where(e => e.Name.StartsWith("data/ui/face/") && e.Name.EndsWith(".tga")
                && e.Name.Count(c => c == '/') == 3)  // not in a subdir
    .OrderBy(e => e.Name)
    .ToList();
Console.WriteLine($"Total face TGAs at data/ui/face/ root: {faceEntries.Count}");
foreach (var e in faceEntries)
    Console.WriteLine($"  {e.Name}  ({e.DataSize:N0} bytes)");
Console.WriteLine();

// Parse naming pattern: {class}_{face_variant}.tga
// Determine how many classes and variants
var faceParsed = faceEntries.Select(e =>
{
    var stem = Path.GetFileNameWithoutExtension(e.Name);
    var parts = stem.Split('_');
    return (classId: int.Parse(parts[0]), variant: int.Parse(parts[1]), name: e.Name);
}).ToList();
var faceClasses = faceParsed.Select(f => f.classId).Distinct().OrderBy(x => x).ToList();
Console.WriteLine($"Face classIds: {string.Join(", ", faceClasses)}");
foreach (var cls in faceClasses)
{
    var variants = faceParsed.Where(f => f.classId == cls).Select(f => f.variant).OrderBy(x => x).ToList();
    Console.WriteLine($"  Class {cls}: variants {string.Join(", ", variants)}");
}
Console.WriteLine();

// ─────────────────────────────────────────────────────────────
// 9. UI Atlas: characwindow.dds + any *charac* / *create* / *newchar*
// ─────────────────────────────────────────────────────────────
Console.WriteLine("=== UI ATLAS (charac/create/newchar) ===");
var uiAtlas = vfs.GetEntries()
    .ToArray()
    .Where(e => e.Name.StartsWith("data/ui/") &&
                (e.Name.Contains("charac") || e.Name.Contains("create") ||
                 e.Name.Contains("newchar") || e.Name.Contains("newchr")))
    .OrderBy(e => e.Name)
    .ToList();
foreach (var e in uiAtlas)
    Console.WriteLine($"  {e.Name}  ({e.DataSize:N0} bytes)");
Console.WriteLine();

// ─────────────────────────────────────────────────────────────
// 10. Hair assets: look for hair under data/ui/ or data/char/
// ─────────────────────────────────────────────────────────────
Console.WriteLine("=== HAIR ASSET SEARCH ===");
var hairEntries = vfs.GetEntries()
    .ToArray()
    .Where(e => e.Name.Contains("hair"))
    .OrderBy(e => e.Name)
    .ToList();
Console.WriteLine($"Found {hairEntries.Count} entries containing 'hair':");
foreach (var e in hairEntries.Take(40))
    Console.WriteLine($"  {e.Name}  ({e.DataSize:N0} bytes)");
if (hairEntries.Count > 40) Console.WriteLine($"  ... ({hairEntries.Count - 40} more)");
Console.WriteLine();

// ─────────────────────────────────────────────────────────────
// 11. Tex lists for starter tex_id resolution
// ─────────────────────────────────────────────────────────────
Console.WriteLine("=== TEX PATH RESOLUTION (starter class rows) ===");
// The known chain: skin.txt texIdA → data/char/tex512512/{id}.png or tex10241024/{id}.png
var sampleTexIds = starterRows.Where(r => r.texIdA != 0).Select(r => r.texIdA).Distinct().Take(8).ToList();
foreach (var tid in sampleTexIds)
{
    var p512 = $"data/char/tex512512/{tid}.png";
    var p1024 = $"data/char/tex10241024/{tid}.png";
    var p256 = $"data/char/tex256512/{tid}.png";
    Console.WriteLine($"  texId={tid}: tex512512={vfs.Contains(p512)} tex1024={vfs.Contains(p1024)} tex256512={vfs.Contains(p256)}");
}
Console.WriteLine();

// ─────────────────────────────────────────────────────────────
// 12. IdA values range — all unique idA used for NPCs, monsters vs players
// ─────────────────────────────────────────────────────────────
Console.WriteLine("=== idA RANGE TABLE ===");
var idAGroups = skinRows.GroupBy(r => r.idA)
    .Select(g => (idA: g.Key, count: g.Count(), classes: g.Select(r => r.classId).Distinct().OrderBy(x => x).ToArray()))
    .OrderBy(g => g.idA)
    .ToList();
foreach (var g in idAGroups)
    Console.WriteLine($"  idA={g.idA}: {g.count} rows, classIds=[{string.Join(",", g.classes)}]");
Console.WriteLine();

// ─────────────────────────────────────────────────────────────
// 13. Tex list files — read header line
// ─────────────────────────────────────────────────────────────
Console.WriteLine("=== TEX LIST FILES ===");
foreach (var path in new[] { "data/char/tex512512list.txt", "data/char/tex10241024list.txt",
                              "data/char/tex256256list.txt", "data/char/tex256512list.txt" })
{
    if (!vfs.Contains(path)) { Console.WriteLine($"  {path}: MISSING"); continue; }
    var bytes = vfs.GetFileContent(path);
    var text = cp949.GetString(bytes.ToArray());
    var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    Console.WriteLine($"  {path}: {lines.Length} lines, row0={lines[0].Trim()}");
    foreach (var ln in lines.Skip(1).Take(5))
        Console.WriteLine($"    {ln.TrimEnd('\r')}");
}
Console.WriteLine();

Console.WriteLine("=== DONE ===");
