// effect-assets-probe — Campaign 9 Wave 2 Lane V3
// Deep decode of char_select-u.xeff (68 sub-effects) + resolution through particleEmitter.eff
// + cell water layer lookup for d000x10000z9990.
// THROWAWAY — never add to MartialHeroes.slnx, never commit, metadata/counts only.

using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var cp949 = Encoding.GetEncoding(949);

const string InfPath = @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata\data.inf";
const string VfsPath = @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata\data\data.vfs";

Console.WriteLine("=== effect-assets-probe: Campaign 9 Wave 2 Lane V3 ===");
Console.WriteLine();

using var archive = MappedVfsArchive.Open(InfPath, VfsPath);
Console.WriteLine($"Mounted: {archive.GetEntries().Length:N0} VFS entries");
Console.WriteLine();

// ═══════════════════════════════════════════════════════════════════════════════
// SECTION 1: char_select-u.xeff — decode with XeffParser
// ═══════════════════════════════════════════════════════════════════════════════
Console.WriteLine("═══ SECTION 1: char_select-u.xeff ═══");

const string XeffPath = "data/effect/xeff/char_select-u.xeff";
if (!archive.Contains(XeffPath))
{
    Console.Error.WriteLine($"[ABORT] Not found: {XeffPath}");
    return 1;
}

var xeffBytes = archive.GetFileContent(XeffPath);
Console.WriteLine($"  Path: {XeffPath}");
Console.WriteLine($"  Size: {xeffBytes.Length:N0} bytes");

XeffData xeff;
try
{
    xeff = XeffParser.ParseXeff(xeffBytes);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[ABORT] XeffParser failed: {ex.Message}");
    return 1;
}

Console.WriteLine($"  effect_id:        {xeff.EffectId}");
Console.WriteLine($"  sub_effect_count: {xeff.SubEffectCount}");
Console.WriteLine();

// emitter_type distribution
var etCounts = new Dictionary<uint, int>();
var resourceIds = new SortedSet<uint>();
int animCount = 0, staticCount = 0;

// Detailed per-sub-effect summary (first 5 + last 5 as samples)
Console.WriteLine("  Per-sub-effect summary (ALL 68):");
Console.WriteLine($"  {"idx",-4} {"emitter_type",-14} {"resource_id",-14} {"tex_count",-10} {"anim_loop",-10} {"anim_stride_ms",-16} {"alpha_keys",-12} {"tex_name[0]",-30}");
Console.WriteLine("  " + new string('-', 120));

for (int i = 0; i < xeff.SubEffects.Length; i++)
{
    var se = xeff.SubEffects[i];
    etCounts.TryGetValue(se.EmitterType, out int ec);
    etCounts[se.EmitterType] = ec + 1;
    resourceIds.Add(se.ResourceId);

    if (se.AnimLoop != 0) animCount++; else staticCount++;

    string etLabel = se.EmitterType switch
    {
        0 => "0=billboard",
        1 => "1=mesh",
        2 => "2=directional",
        _ => $"{se.EmitterType}=UNKNOWN"
    };
    string texName0 = se.TextureNames.Length > 0 ? se.TextureNames[0] : "(none)";
    Console.WriteLine($"  [{i,2}] {etLabel,-14} res={se.ResourceId,-12} tex_cnt={se.EntryCount,-8} anim_loop={se.AnimLoop,-8} stride={se.AnimStride,-14} alpha_keys={se.AlphaKeys.Length,-10} tex0=\"{texName0}\"");
}

Console.WriteLine();
Console.WriteLine("  emitter_type distribution:");
foreach (var kv in etCounts.OrderBy(k => k.Key))
{
    string label = kv.Key switch { 0 => "billboard", 1 => "mesh-particle", 2 => "directional", _ => "UNKNOWN" };
    Console.WriteLine($"    type {kv.Key} ({label}): {kv.Value} sub-effects");
}

Console.WriteLine();
Console.WriteLine("  anim_loop distribution:");
Console.WriteLine($"    animated (anim_loop != 0): {animCount}");
Console.WriteLine($"    static   (anim_loop == 0): {staticCount}");

Console.WriteLine();
Console.WriteLine($"  Distinct resource_ids ({resourceIds.Count}):");
foreach (uint rid in resourceIds)
{
    bool isGpu = rid >= 10000;
    Console.WriteLine($"    {rid}  ({(isGpu ? "GPU-particle >= 10000" : "mesh-ref < 10000")})");
}

// ═══════════════════════════════════════════════════════════════════════════════
// SECTION 2: particleEmitter.eff — parse + resolve resource_ids
// ═══════════════════════════════════════════════════════════════════════════════
Console.WriteLine();
Console.WriteLine("═══ SECTION 2: particleEmitter.eff — parse + resource_id resolution ═══");

const string PePath = "data/effect/particle/particleemitter.eff";
if (!archive.Contains(PePath))
{
    Console.Error.WriteLine($"[ABORT] Not found: {PePath}");
    return 1;
}

var peBytes = archive.GetFileContent(PePath);
Console.WriteLine($"  Path: {PePath}");
Console.WriteLine($"  Size: {peBytes.Length:N0} bytes");

ParticleEmitterTable peTable;
try
{
    peTable = ParticleEmitterParser.Parse(peBytes);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[ABORT] ParticleEmitterParser failed: {ex.Message}");
    return 1;
}
Console.WriteLine($"  Entries parsed: {peTable.Entries.Length}");
Console.WriteLine();

// Resolve each unique resource_id from the xeff
Console.WriteLine("  Resolution of char_select resource_ids through particleEmitter.eff:");
Console.WriteLine($"  {"resource_id",-14} {"entry_id",-10} {"sprite_size_x",-16} {"sprite_size_y",-16} {"max_particles",-14} {"num_frames",-12} {"texture_name",-40}");
Console.WriteLine("  " + new string('-', 130));

// Only GPU particle ids (>= 10000) go through particleEmitter.eff
var gpuRids = resourceIds.Where(rid => rid >= 10000).ToList();
var meshRids = resourceIds.Where(rid => rid < 10000).ToList();

if (meshRids.Count > 0)
{
    Console.WriteLine($"  NOTE: {meshRids.Count} mesh-particle resource_id(s) (< 10000) go through .xobj shared mesh table, NOT particleEmitter.eff:");
    foreach (uint rid in meshRids)
        Console.WriteLine($"    resource_id={rid} -> .xobj slot {rid}");
    Console.WriteLine();
}

foreach (uint rid in gpuRids)
{
    var entry = peTable.TryGetById(rid);
    if (entry == null)
    {
        Console.WriteLine($"  {rid,-14} MISS (no entry with entry_id={rid} in particleEmitter.eff)");
    }
    else
    {
        Console.WriteLine($"  {rid,-14} {entry.EntryId,-10} {entry.SpriteSizeX,-16:F4} {entry.SpriteSizeY,-16:F4} {entry.MaxParticles,-14} {entry.NumFrames,-12} \"{entry.TextureName}\"");
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// SECTION 3: Confirm texture VFS paths
// ═══════════════════════════════════════════════════════════════════════════════
Console.WriteLine();
Console.WriteLine("═══ SECTION 3: texture VFS path confirmation ═══");

// Collect distinct texture names from resolved entries
var texNamesFromParticle = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
foreach (uint rid in gpuRids)
{
    var entry = peTable.TryGetById(rid);
    if (entry != null && !string.IsNullOrEmpty(entry.TextureName))
        texNamesFromParticle.Add(entry.TextureName);
}

// Also collect texture names embedded directly in xeff sub-effects (for non-GPU/direct refs)
var texNamesFromXeff = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
foreach (var se in xeff.SubEffects)
    foreach (var tn in se.TextureNames)
        if (!string.IsNullOrEmpty(tn))
            texNamesFromXeff.Add(tn);

Console.WriteLine($"  Texture names from particleEmitter.eff entries: {texNamesFromParticle.Count}");
Console.WriteLine($"  Texture names embedded in xeff name-tables:     {texNamesFromXeff.Count}");
Console.WriteLine();

// Check standard prefix: data/effect/texture/<name>.tga
string[] prefixes = [
    "data/effect/texture/",
    "data/effect/particle/",
    "data/effect/",
];

void CheckTexturePath(string name)
{
    // Try common VFS paths
    string[] candidates = [
        $"data/effect/texture/{name}.tga",
        $"data/effect/texture/{name}.dds",
        $"data/effect/particle/{name}.tga",
        $"data/effect/particle/{name}.dds",
        name,  // raw path if already absolute
    ];
    bool found = false;
    foreach (string c in candidates)
    {
        string lower = c.ToLowerInvariant();
        if (archive.Contains(lower))
        {
            var bytes = archive.GetFileContent(lower);
            // Try to read DDS header
            string extra = "";
            if (bytes.Length >= 128 && bytes.Span[0] == (byte)'D' && bytes.Span[1] == (byte)'D'
                && bytes.Span[2] == (byte)'S' && bytes.Span[3] == (byte)' ')
            {
                uint height = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Span[12..]);
                uint width  = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Span[16..]);
                // fourCC at offset 0x54 in DDS header
                byte f0 = bytes.Span[0x54], f1 = bytes.Span[0x55], f2 = bytes.Span[0x56], f3 = bytes.Span[0x57];
                string fourcc = new string([(char)f0, (char)f1, (char)f2, (char)f3]);
                extra = $"  DDS {width}x{height} FourCC={fourcc}";
            }
            else if (bytes.Length > 4)
            {
                // Try TGA — no magic, just note size
                extra = $"  TGA-likely size={bytes.Length}B";
            }
            Console.WriteLine($"    \"{name}\" -> {lower}  [EXISTS]{extra}");
            found = true;
            break;
        }
    }
    if (!found)
        Console.WriteLine($"    \"{name}\" -> NOT FOUND (tried: {string.Join(", ", candidates.Select(c => c.ToLowerInvariant()))})");
}

Console.WriteLine("  From particleEmitter.eff entries:");
foreach (string tn in texNamesFromParticle)
    CheckTexturePath(tn);

if (texNamesFromXeff.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("  From xeff sub-effect name tables (direct references):");
    foreach (string tn in texNamesFromXeff)
        CheckTexturePath(tn);
}

// ═══════════════════════════════════════════════════════════════════════════════
// SECTION 4: Cell water layer for d000x10000z9990
// ═══════════════════════════════════════════════════════════════════════════════
Console.WriteLine();
Console.WriteLine("═══ SECTION 4: Cell water layer — d000x10000z9990 ═══");

// Water layer extensions: .fx3 and .fx5 per spec
string[] waterExtensions = [".fx3", ".fx5", ".fx1", ".fx2", ".fx4", ".fx6", ".fx7"];
string cellStem = "d000x10000z9990";

Console.WriteLine($"  Searching for cell stem '{cellStem}' under data/map000/...");
Console.WriteLine();

// Find all entries matching the cell stem
var cellEntries = new List<(string name, int size)>();
foreach (var entry in archive.GetEntries())
{
    if (entry.Name.Contains(cellStem, StringComparison.OrdinalIgnoreCase))
        cellEntries.Add((entry.Name, (int)entry.DataSize));
}

Console.WriteLine($"  All VFS entries containing '{cellStem}':");
if (cellEntries.Count == 0)
    Console.WriteLine("    (none)");
else
    foreach (var (name, size) in cellEntries.OrderBy(e => e.name))
        Console.WriteLine($"    {name}  size={size:N0}B");

Console.WriteLine();

// Specifically check water layer files
Console.WriteLine("  Water layer file check (fx3/fx5):");
foreach (string ext in waterExtensions)
{
    string path = $"data/map000/{cellStem}{ext}";
    string pathLower = path.ToLowerInvariant();
    bool exists = archive.Contains(pathLower);
    if (exists)
    {
        var bytes = archive.GetFileContent(pathLower);
        Console.WriteLine($"    {pathLower}  EXISTS  size={bytes.Length:N0}B");
        // Print first 16 bytes as hex for identification
        int showBytes = Math.Min(16, bytes.Length);
        Console.Write("    First bytes: ");
        for (int i = 0; i < showBytes; i++) Console.Write($"{bytes.Span[i]:X2} ");
        Console.WriteLine();
    }
    else
    {
        Console.WriteLine($"    {pathLower}  NOT FOUND");
    }
}

// Search for water texture files named in spec
Console.WriteLine();
Console.WriteLine("  Spec-named water textures (_water_new01/03/04):");
string[] waterTexNames = ["_water_new01", "_water_new02", "_water_new03", "_water_new04"];
foreach (string wtn in waterTexNames)
{
    // Search across possible prefixes
    bool found = false;
    string[] wtCandidates = [
        $"data/map000/texture/{wtn}.dds",
        $"data/map000/texture/{wtn}.tga",
        $"data/effect/texture/{wtn}.dds",
        $"data/effect/texture/{wtn}.tga",
    ];
    foreach (string c in wtCandidates)
    {
        if (archive.Contains(c))
        {
            var bytes = archive.GetFileContent(c);
            string extra = "";
            if (bytes.Length >= 128 && bytes.Span[0] == 'D' && bytes.Span[1] == 'D' && bytes.Span[2] == 'S' && bytes.Span[3] == ' ')
            {
                uint h = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Span[12..]);
                uint w = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Span[16..]);
                byte f0 = bytes.Span[0x54], f1 = bytes.Span[0x55], f2 = bytes.Span[0x56], f3 = bytes.Span[0x57];
                string fourcc = new string([(char)f0, (char)f1, (char)f2, (char)f3]);
                extra = $"  DDS {w}x{h} FourCC={fourcc}";
            }
            Console.WriteLine($"    {wtn} -> {c}  [EXISTS]{extra}");
            found = true;
            break;
        }
    }
    if (!found)
    {
        // Try to find it anywhere in VFS
        var matches = new List<string>();
        foreach (var entry in archive.GetEntries())
            if (entry.Name.Contains(wtn, StringComparison.OrdinalIgnoreCase))
                matches.Add(entry.Name);
        if (matches.Count > 0)
            Console.WriteLine($"    {wtn} -> found elsewhere: {string.Join(", ", matches.Take(5))}");
        else
            Console.WriteLine($"    {wtn} -> NOT FOUND anywhere in VFS");
    }
}

// Also search for fx3/fx5 pattern in general to understand water layer convention
Console.WriteLine();
Console.WriteLine("  fx3/fx5 files under data/map000/ (water layer sample):");
int fx3Count = 0, fx5Count = 0;
foreach (var entry in archive.GetEntries())
{
    if (entry.Name.StartsWith("data/map000/"))
    {
        if (entry.Name.EndsWith(".fx3")) fx3Count++;
        if (entry.Name.EndsWith(".fx5")) fx5Count++;
    }
}
Console.WriteLine($"    Total .fx3 under data/map000/: {fx3Count}");
Console.WriteLine($"    Total .fx5 under data/map000/: {fx5Count}");

// Print a few fx3/fx5 examples to understand naming convention
Console.WriteLine("  Sample .fx3 entries (first 5):");
int shown = 0;
foreach (var entry in archive.GetEntries())
    if (entry.Name.StartsWith("data/map000/") && entry.Name.EndsWith(".fx3") && shown++ < 5)
        Console.WriteLine($"    {entry.Name}  size={entry.DataSize}B");

Console.WriteLine("  Sample .fx5 entries (first 5):");
shown = 0;
foreach (var entry in archive.GetEntries())
    if (entry.Name.StartsWith("data/map000/") && entry.Name.EndsWith(".fx5") && shown++ < 5)
        Console.WriteLine($"    {entry.Name}  size={entry.DataSize}B");

Console.WriteLine();
Console.WriteLine("=== effect-assets-probe: done ===");
return 0;
