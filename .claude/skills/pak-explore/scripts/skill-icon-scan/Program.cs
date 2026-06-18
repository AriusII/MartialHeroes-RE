// skill-icon-scan — throwaway harness (extended).
// MISSION:
//   1. Find the actual on-disk offset of (iconSrcX, iconSrcY) in skills.scr by full-range scan.
//   2. The IDA note gives in-memory offset +546/+548; this may differ from on-disk layout.
//   3. Full scan: for every even offset 0..Stride-4, check if reading (u16, u16) there
//      gives values <= 489 for ALL real records. Track best candidate by validity %.
//   4. Extra check: for real records where skill_id is unique (non-duplicate), what pairs look
//      consistent across all class variants (skill 11,21,31,41 = same skill across 4 classes;
//      their icon coords SHOULD be the same or at least both valid).
//   5. Also do the texturelist.txt and supplementary sheet analysis.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Encoding cp949 = Encoding.GetEncoding(949);

string infPath = "D:/MartialHeroesClient/data.inf";
string vfsPath = "D:/MartialHeroesClient/data/data.vfs";

foreach (string root in DefaultClientRoots())
{
    string ci = Path.Combine(root, "data.inf");
    string cv = Path.Combine(root, "data", "data.vfs");
    if (File.Exists(ci) && File.Exists(cv)) { infPath = ci; vfsPath = cv; break; }
}

if (!File.Exists(infPath) || !File.Exists(vfsPath))
{
    Console.Error.WriteLine($"Client not found. inf={infPath}  vfs={vfsPath}");
    return 1;
}

using MappedVfsArchive archive = MappedVfsArchive.Open(infPath, vfsPath);
Console.WriteLine($"Mounted {archive.GetEntries().Length:N0} entries from {infPath}");

// =============================================================================
// 1. SKILLS.SCR full-range icon-pair scan
// =============================================================================
Console.WriteLine("\n=== skills.scr full-range icon-pair scan ===");

const string SkillsPath = "data/script/skills.scr";
const int Stride = 1504;
const int MaxSkillId = 10_000_000;
const int MaxCategory = 300;
const int SheetSize = 512;
const int CellSize = 23;
const int MaxCoord = SheetSize - CellSize; // 489

if (!archive.Contains(SkillsPath))
{ Console.Error.WriteLine("skills.scr not found"); return 1; }

ReadOnlyMemory<byte> skillsRaw = archive.GetFileContent(SkillsPath);
int fileSize = skillsRaw.Length;
int maxRecords = fileSize / Stride;
Console.WriteLine($"  File size: {fileSize:N0} bytes, stride {Stride} -> {maxRecords} slots, tail {fileSize % Stride} bytes");

ReadOnlySpan<byte> skills = skillsRaw.Span;

// --- Collect real records (first occurrence of each skill_id only) ---
// Rationale: skill_id=11 appears multiple times; the FIRST occurrence in the file
// is the canonical record (BST insert uses first encountered or last? Let's collect all
// and use best-validity approach).
var allReal = new List<(int recIdx, uint skillId, uint category, string name)>();
var seenSkillIds = new HashSet<uint>();
var firstOccurrence = new Dictionary<uint, int>(); // skill_id -> first recIdx

for (int i = 0; i < maxRecords; i++)
{
    int off = i * Stride;
    if (off + Stride > skills.Length) break;
    ReadOnlySpan<byte> rec = skills.Slice(off, Stride);

    uint skillId = BinaryPrimitives.ReadUInt32LittleEndian(rec[0..4]);
    uint category = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..8]);

    if (skillId == 0 || skillId >= MaxSkillId) continue;
    if (category >= MaxCategory) continue;

    int nameEnd = rec[8..].IndexOf((byte)0);
    if (nameEnd <= 0) continue;
    string name = cp949.GetString(rec.Slice(8, nameEnd));

    allReal.Add((i, skillId, category, name));
    if (!firstOccurrence.ContainsKey(skillId)) firstOccurrence[skillId] = i;
    seenSkillIds.Add(skillId);
}

Console.WriteLine($"  Total real record slots (all occurrences): {allReal.Count}");
Console.WriteLine($"  Distinct skill_ids: {seenSkillIds.Count}");
Console.WriteLine($"  Duplicate entries (same skill_id, multiple slots): {allReal.Count - seenSkillIds.Count}");

// Use FIRST occurrence of each skill_id for the scan.
var firstReal = allReal.Where(r => firstOccurrence[r.skillId] == r.recIdx).ToList();
Console.WriteLine($"  First-occurrence-only real records: {firstReal.Count}");

// --- Full-range scan: find offset pairs where ALL first-occurrence records have valid coords ---
Console.WriteLine("\n  Full-range scan (offX from 0 to Stride-4, step 2):");
Console.WriteLine("  Looking for offsets where ALL real (first-occ) records yield u16 pair <= 489");
Console.WriteLine($"  {"offX",6} {"offY",6}  {"valid%",7}  {"all_valid",9}  {"distinct_X",10} {"distinct_Y",10}  notes");

var candidates = new List<(int offX, int offY, int validCount, double pct, int dX, int dY)>();

for (int offX = 0; offX <= Stride - 4; offX += 2)
{
    int offY = offX + 2;

    int validCount = 0;
    var xVals = new HashSet<int>();
    var yVals = new HashSet<int>();

    foreach ((int recIdx, _, _, _) in firstReal)
    {
        int recOff = recIdx * Stride;
        ReadOnlySpan<byte> rec = skills.Slice(recOff, Stride);
        ushort srcX = BinaryPrimitives.ReadUInt16LittleEndian(rec.Slice(offX, 2));
        ushort srcY = BinaryPrimitives.ReadUInt16LittleEndian(rec.Slice(offY, 2));

        if (srcX <= MaxCoord && srcY <= MaxCoord)
        {
            validCount++;
            xVals.Add(srcX);
            yVals.Add(srcY);
        }
    }

    double pct = firstReal.Count > 0 ? 100.0 * validCount / firstReal.Count : 0;

    if (pct >= 90.0)
    {
        candidates.Add((offX, offY, validCount, pct, xVals.Count, yVals.Count));
    }
}

Console.WriteLine($"  Candidates with >= 90% validity: {candidates.Count}");
foreach ((int offX, int offY, int validCount, double pct, int dX, int dY) in candidates.OrderByDescending(c => c.pct).ThenByDescending(c => c.dX + c.dY))
{
    string notes = "";
    if (offX == 546) notes = " <-- IDA in-memory offset";
    bool is100 = validCount == firstReal.Count;
    Console.WriteLine($"  {offX,6} {offY,6}  {pct,7:F1}%  {(is100?"ALL":validCount.ToString()),9}  {dX,10} {dY,10}  {notes}");
}

// --- Also run with ALL occurrences (not just first) ---
Console.WriteLine("\n  Same scan using ALL real occurrences (not just first):");
var candidates2 = new List<(int offX, int offY, int validCount, double pct, int dX, int dY)>();
for (int offX = 0; offX <= Stride - 4; offX += 2)
{
    int offY = offX + 2;
    int validCount = 0;
    var xVals = new HashSet<int>();
    var yVals = new HashSet<int>();
    foreach ((int recIdx, _, _, _) in allReal)
    {
        int recOff = recIdx * Stride;
        ReadOnlySpan<byte> rec = skills.Slice(recOff, Stride);
        ushort srcX = BinaryPrimitives.ReadUInt16LittleEndian(rec.Slice(offX, 2));
        ushort srcY = BinaryPrimitives.ReadUInt16LittleEndian(rec.Slice(offY, 2));
        if (srcX <= MaxCoord && srcY <= MaxCoord) { validCount++; xVals.Add(srcX); yVals.Add(srcY); }
    }
    double pct = allReal.Count > 0 ? 100.0 * validCount / allReal.Count : 0;
    if (pct >= 90.0)
        candidates2.Add((offX, offY, validCount, pct, xVals.Count, yVals.Count));
}
Console.WriteLine($"  Candidates with >= 90% validity: {candidates2.Count}");
foreach ((int offX, int offY, int validCount, double pct, int dX, int dY) in candidates2.OrderByDescending(c => c.pct).ThenByDescending(c => c.dX + c.dY).Take(20))
{
    string notes = "";
    if (offX == 546) notes = " <-- IDA in-memory offset";
    bool is100 = validCount == allReal.Count;
    Console.WriteLine($"  {offX,6} {offY,6}  {pct,7:F1}%  {(is100?"ALL":validCount.ToString()),9}  {dX,10} {dY,10}  {notes}");
}

// --- Deep look at the best candidates ---
// Find the pair with 100% validity AND most distinct values (best information content)
var best = candidates2.OrderByDescending(c => c.validCount).ThenByDescending(c => c.dX + c.dY).Take(5).ToList();
if (best.Count > 0)
{
    Console.WriteLine($"\n--- Top {best.Count} candidate(s) deep dive ---");
    foreach ((int offX, int offY, _, _, _, _) in best)
    {
        Console.WriteLine($"\nOffset +{offX}/+{offY}:");
        Console.WriteLine($"  {"skill_id",10}  {"cat",5}  {"srcX",5}  {"srcY",5}  name");
        // Print first occurrence sorted by skill_id.
        foreach ((int recIdx, uint skillId, uint category, string name) in
            firstReal.OrderBy(r => r.skillId).Take(40))
        {
            int recOff = recIdx * Stride;
            ReadOnlySpan<byte> rec = skills.Slice(recOff, Stride);
            ushort srcX = BinaryPrimitives.ReadUInt16LittleEndian(rec.Slice(offX, 2));
            ushort srcY = BinaryPrimitives.ReadUInt16LittleEndian(rec.Slice(offY, 2));
            string valid = (srcX <= MaxCoord && srcY <= MaxCoord) ? "" : " [!]";
            Console.WriteLine($"  {skillId,10}  {category,5}  {srcX,5}  {srcY,5}  {name}{valid}");
        }
    }
}

// --- Check class-consistency: skills 11/21/31/41 (same skill for 4 classes) should share coord ---
Console.WriteLine("\n--- Class-variant consistency check ---");
Console.WriteLine("  Skills 11,21,31,41 = 경공 for 4 classes; icons should share (srcX,srcY)");
Console.WriteLine("  Skills 12,22,32,42 = 초상비 for 4 classes; same logic");

if (best.Count > 0)
{
    foreach ((int offX, int offY, _, _, _, _) in best.Take(2))
    {
        Console.WriteLine($"\n  At +{offX}/+{offY}:");
        foreach (uint[] group in new uint[][]
        {
            [11u, 21u, 31u, 41u],
            [12u, 22u, 32u, 42u],
            [13u, 23u, 33u, 43u],
            [111u, 121u, 131u, 141u],
        })
        {
            var coords = new List<(uint id, ushort x, ushort y)>();
            foreach (uint sid in group)
            {
                if (!firstOccurrence.TryGetValue(sid, out int ri)) continue;
                int recOff = ri * Stride;
                ReadOnlySpan<byte> rec = skills.Slice(recOff, Stride);
                ushort sx = BinaryPrimitives.ReadUInt16LittleEndian(rec.Slice(offX, 2));
                ushort sy = BinaryPrimitives.ReadUInt16LittleEndian(rec.Slice(offY, 2));
                coords.Add((sid, sx, sy));
            }
            if (coords.Count == 0) continue;
            bool allSame = coords.All(c => c.x == coords[0].x && c.y == coords[0].y);
            string summary = string.Join("  ", coords.Select(c => $"[{c.id}]({c.x},{c.y})"));
            Console.WriteLine($"    {(allSame?"SAME  ":"DIFF  ")}{summary}");
        }
    }
}

// --- Raw bytes at +540..+560 for first 5 distinct real records ---
Console.WriteLine("\n--- Raw bytes at offsets +520..+560 for first 5 distinct real records ---");
foreach ((int recIdx, uint skillId, uint category, string name) in firstReal.OrderBy(r => r.skillId).Take(5))
{
    int recOff = recIdx * Stride;
    ReadOnlySpan<byte> rec = skills.Slice(recOff, Stride);
    Console.Write($"  skill_id={skillId,6} [{name,-20}] +520..+559: ");
    for (int bi = 520; bi < 560; bi++)
        Console.Write($"{rec[bi]:X2} ");
    Console.WriteLine();
}

// =============================================================================
// 2. data/item/texturelist.txt analysis
// =============================================================================
Console.WriteLine("\n\n=== data/item/texturelist.txt ===");

const string TextureListPath = "data/item/texturelist.txt";
if (!archive.Contains(TextureListPath))
{ Console.Error.WriteLine("texturelist.txt not found"); }
else
{
    ReadOnlyMemory<byte> tlRaw = archive.GetFileContent(TextureListPath);
    string tlText = cp949.GetString(tlRaw.Span);
    string[] lines = tlText.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);

    Console.WriteLine($"  File size: {tlRaw.Length:N0} bytes");
    Console.WriteLine($"  Total non-empty lines: {lines.Length}");

    var entries = new List<(long texId, string filename)>();
    var idSeen = new Dictionary<long, int>();
    int noLeadingDigits = 0;
    long minId = long.MaxValue, maxId = long.MinValue;

    foreach (string line in lines)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0) continue;
        int digitEnd = 0;
        while (digitEnd < trimmed.Length && char.IsAsciiDigit(trimmed[digitEnd])) digitEnd++;
        if (digitEnd == 0) { noLeadingDigits++; continue; }
        if (!long.TryParse(trimmed[..digitEnd], NumberStyles.None, CultureInfo.InvariantCulture, out long texId))
        { noLeadingDigits++; continue; }
        entries.Add((texId, trimmed));
        idSeen.TryGetValue(texId, out int n); idSeen[texId] = n + 1;
        if (texId < minId) minId = texId;
        if (texId > maxId) maxId = texId;
    }

    int duplicates = idSeen.Values.Count(v => v > 1);
    Console.WriteLine($"  Parsed entries (with leading numeric prefix): {entries.Count}");
    Console.WriteLine($"  Lines without leading digits: {noLeadingDigits}");
    Console.WriteLine($"  tex_id range: {minId} .. {maxId}");
    Console.WriteLine($"  Distinct tex_ids: {idSeen.Count}");
    Console.WriteLine($"  Duplicate tex_ids (same atol prefix): {duplicates}");

    int exists = 0, missing = 0;
    var missingExamples = new List<string>();
    foreach ((long texId, string filename) in entries)
    {
        string vfsPath2 = $"data/item/texture/{filename.ToLowerInvariant()}";
        if (archive.Contains(vfsPath2)) exists++;
        else { missing++; if (missingExamples.Count < 5) missingExamples.Add(vfsPath2); }
    }
    Console.WriteLine($"  Files found in VFS:   {exists}/{entries.Count}");
    Console.WriteLine($"  Files MISSING in VFS: {missing}/{entries.Count}");
    foreach (string mp in missingExamples) Console.WriteLine($"    MISSING: {mp}");

    Console.WriteLine("\n  First 25 entries:");
    Console.WriteLine($"  {"tex_id",12}  filename");
    foreach ((long texId, string filename) in entries.Take(25))
        Console.WriteLine($"  {texId,12}  {filename}");

    Console.WriteLine("\n  Last 5 entries:");
    foreach ((long texId, string filename) in entries.TakeLast(5))
        Console.WriteLine($"  {texId,12}  {filename}");

    if (duplicates > 0)
    {
        Console.WriteLine($"\n  Duplicate tex_id entries ({duplicates} ids):");
        foreach ((long id, int count) in idSeen.Where(kv => kv.Value > 1))
        {
            Console.WriteLine($"    tex_id={id} appears {count}x:");
            foreach ((long tId, string fn) in entries.Where(e => e.texId == id))
                Console.WriteLine($"      {fn}");
        }
    }
}

// =============================================================================
// 3. Supplementary icon sheets
// =============================================================================
Console.WriteLine("\n\n=== Supplementary skill icon sheets ===");
Console.WriteLine("  data/ui/skillicon/ entries:");
foreach (VfsEntry e in archive.GetEntries())
    if (e.Name.StartsWith("data/ui/skillicon/", StringComparison.Ordinal))
        Console.WriteLine($"  {e.Name,-60}  size={e.DataSize:N0}");

string[] bonusPaths = [
    "data/ui/skillicon/stateiconlist.txt",
    "data/ui/skillicon/iconlist.txt",
    "data/ui/iconlist.txt",
    "data/script/skillicon.do",
    "data/script/skillicon.scr",
    "data/script/stateicon.do",
    "data/script/stateicon.scr",
];
Console.WriteLine("\n  Checking for supplementary icon catalog files:");
foreach (string p in bonusPaths)
    Console.WriteLine($"  {p,-50}  exists={archive.Contains(p)}");

Console.WriteLine("\nDone.");
return 0;

static IEnumerable<string> DefaultClientRoots()
{
    string? env = Environment.GetEnvironmentVariable("MH_CLIENT_DIR");
    if (!string.IsNullOrWhiteSpace(env)) yield return env;
    for (DirectoryInfo? d = new(AppContext.BaseDirectory); d != null; d = d.Parent)
        yield return Path.Combine(d.FullName, "05.Presentation", "MartialHeroes.Client.Godot", "clientdata");
    yield return "D:/MartialHeroesClient";
    yield return "C:/MartialHeroesClient";
}
