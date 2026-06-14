// charselect3d-scan — THROWAWAY harness for Campaign 4 CS3D recovery.
// Phase CS3D (RECOVERY) - Environment lane:
//   ambient effect (380003000), sky, lightmaps, fog/cloud for char-select backdrop.
// NEVER COMMIT. NEVER ADD TO SOLUTION. Reads real VFS via production MappedVfsArchive API.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var cp949 = Encoding.GetEncoding(949);

// --- Locate client VFS ---
string infPath = "";
string vfsPath = "";
foreach (var root in new[]
{
    Environment.GetEnvironmentVariable("MH_CLIENT_DIR") ?? "",
    @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata",
    @"D:\MartialHeroesClient",
})
{
    if (string.IsNullOrEmpty(root)) continue;
    var inf = Path.Combine(root, "data.inf");
    var vfs = Path.Combine(root, "data", "data.vfs");
    if (File.Exists(inf) && File.Exists(vfs)) { infPath = inf; vfsPath = vfs; break; }
}
if (string.IsNullOrEmpty(infPath)) { Console.Error.WriteLine("ERROR: client VFS not found"); return 1; }

Console.WriteLine($"Mounted: {infPath}");
using var archive = MappedVfsArchive.Open(infPath, vfsPath);
var allEntries = archive.GetEntries().ToArray();
Console.WriteLine($"Total entries: {allEntries.Length}");
Console.WriteLine();

// ── 1. xeffect.lst — binary map (stride 30, u32 count header) ─────────────────
Console.WriteLine("=== 1. xeffect.lst — effect_id → filename binary index ===");
const string lstPath = "data/effect/xeffect.lst";
var lstBytes = archive.GetFileContent(lstPath).ToArray();
int lstCount = BitConverter.ToInt32(lstBytes, 0);
const int STRIDE = 30;
Console.WriteLine($"  path: {lstPath}  size: {lstBytes.Length} bytes");
Console.WriteLine($"  header u32: {lstCount} entries  stride: {STRIDE}  expected: {4 + lstCount * STRIDE}  match: {lstBytes.Length == 4 + lstCount * STRIDE}");
Console.WriteLine();

// Scan all entries for 380003 and char_select/zone_sel
Console.WriteLine("  Entries containing '380003':");
bool found380003 = false;
for (int i = 0; i < lstCount; i++)
{
    int off = 4 + i * STRIDE;
    if (off + STRIDE > lstBytes.Length) break;
    var nameSpan = lstBytes.AsSpan(off, STRIDE);
    int nullIdx = nameSpan.IndexOf((byte)0);
    string name = nullIdx >= 0 ? cp949.GetString(nameSpan[..nullIdx]) : cp949.GetString(nameSpan);
    if (string.IsNullOrWhiteSpace(name)) continue;
    if (name.Contains("380003")) { Console.WriteLine($"    [{i}] \"{name}\""); found380003 = true; }
}
if (!found380003) Console.WriteLine("    (none — note: xeffect.lst uses filenames only, not numeric IDs)");

Console.WriteLine("  Entries containing 'char_sel' or 'zone_sel':");
bool foundSel = false;
for (int i = 0; i < lstCount; i++)
{
    int off = 4 + i * STRIDE;
    if (off + STRIDE > lstBytes.Length) break;
    var nameSpan = lstBytes.AsSpan(off, STRIDE);
    int nullIdx = nameSpan.IndexOf((byte)0);
    string name = nullIdx >= 0 ? cp949.GetString(nameSpan[..nullIdx]) : cp949.GetString(nameSpan);
    if (string.IsNullOrWhiteSpace(name)) continue;
    if (name.Contains("char_sel", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("zone_sel", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("sel_", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"    [{i}] \"{name}\"");
        foundSel = true;
    }
}
if (!foundSel) Console.WriteLine("    (none)");
Console.WriteLine();

// ── 2. xeffect.txt — plain-text filename list, CP949 ─────────────────────────
Console.WriteLine("=== 2. xeffect.txt — complete .xeff filename list ===");
const string txtPath = "data/effect/xeffect.txt";
var txtBytes = archive.GetFileContent(txtPath).ToArray();
var txtContent = cp949.GetString(txtBytes);
var txtLines = txtContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
Console.WriteLine($"  path: {txtPath}  size: {txtBytes.Length} bytes  lines: {txtLines.Length}");
Console.WriteLine();

Console.WriteLine("  Lines containing '3800' (sel- / ambient- related effects):");
foreach (var line in txtLines)
    if (line.Trim().Contains("3800")) Console.WriteLine($"    {line.Trim()}");

Console.WriteLine();
Console.WriteLine("  Lines containing 'char_sel', 'zone_sel', 'sel_', 'ambient', 'sky', 'atmos':");
foreach (var line in txtLines)
{
    var t = line.Trim();
    if (t.Contains("char_sel", StringComparison.OrdinalIgnoreCase) ||
        t.Contains("zone_sel", StringComparison.OrdinalIgnoreCase) ||
        t.Contains("/sel_", StringComparison.OrdinalIgnoreCase) ||
        t.Contains("ambient", StringComparison.OrdinalIgnoreCase) ||
        t.StartsWith("sel_", StringComparison.OrdinalIgnoreCase) ||
        t.Contains("sky", StringComparison.OrdinalIgnoreCase) ||
        t.Contains("atmos", StringComparison.OrdinalIgnoreCase))
        Console.WriteLine($"    {t}");
}
Console.WriteLine();

// ── 3. All xeff files in VFS — sel_* and char_select / zone_sel ────────────
Console.WriteLine("=== 3. VFS .xeff files: sel_* / char_select / zone_sel / 3800* ===");
foreach (var entry in allEntries)
{
    string n = entry.Name;
    if (!n.EndsWith(".xeff")) continue;
    string stem = Path.GetFileName(n);
    if (stem.StartsWith("sel_") || stem.Contains("char_sel") || stem.Contains("zone_sel") ||
        stem.StartsWith("380") || stem.Contains("ambient") || stem.Contains("atmos"))
        Console.WriteLine($"  {n}  ({entry.DataSize} bytes)");
}
Console.WriteLine();

// ── 4. Verify individual xeff file paths ──────────────────────────────────────
Console.WriteLine("=== 4. Key .xeff path existence check ===");
string[] xeffCheck = [
    "data/effect/xeff/char_select-u.xeff",
    "data/effect/xeff/zone_sel_u.xeff",
    "data/effect/xeff/zone_sel2-u.xeff",
    "data/effect/xeff/380003000.xeff",
    "data/effect/xeff/380003001.xeff",
    "data/effect/xeff/380002001.xeff",
    "data/effect/xeff/380002002.xeff",
    "data/effect/xeff/380002004.xeff",
    "data/effect/xeff/380002005.xeff",
    "data/effect/xeff/380001001.xeff",
    "data/effect/xeff/sel_j01_ef380002002.xeff",
    "data/effect/xeff/sel_jd_ef380004000.xeff",
    "data/effect/xeff/sel_mob_ef380001001.xeff",
    "data/effect/xeff/sel_o01_ef380002001.xeff",
    "data/effect/xeff/sel_s01_ef380002003.xeff",
];
foreach (var p in xeffCheck)
{
    bool ex = archive.Contains(p);
    if (ex)
    {
        var sz = archive.GetFileContent(p).Length;
        Console.WriteLine($"  EXISTS  {p}  ({sz} bytes)");
    }
    else
        Console.WriteLine($"  MISSING {p}");
}
Console.WriteLine();

// ── 5. Hex head of ambient xeff candidates ────────────────────────────────────
Console.WriteLine("=== 5. Head bytes of char_select-u.xeff (first 64 bytes) ===");
if (archive.Contains("data/effect/xeff/char_select-u.xeff"))
{
    var b = archive.GetFileContent("data/effect/xeff/char_select-u.xeff").ToArray();
    Console.WriteLine($"  size: {b.Length} bytes");
    Console.Write("  [00] ");
    for (int i = 0; i < Math.Min(64, b.Length); i++) Console.Write($"{b[i]:X2} ");
    Console.WriteLine();
    // Print printable strings in first 256 bytes
    PrintStrings("  strings: ", b, 256, cp949);
}
Console.WriteLine();

Console.WriteLine("=== 5b. Head bytes of zone_sel_u.xeff (first 64 bytes) ===");
if (archive.Contains("data/effect/xeff/zone_sel_u.xeff"))
{
    var b = archive.GetFileContent("data/effect/xeff/zone_sel_u.xeff").ToArray();
    Console.WriteLine($"  size: {b.Length} bytes");
    Console.Write("  [00] ");
    for (int i = 0; i < Math.Min(64, b.Length); i++) Console.Write($"{b[i]:X2} ");
    Console.WriteLine();
    PrintStrings("  strings: ", b, 256, cp949);
}
Console.WriteLine();

// ── 6. Sky / skybox files ──────────────────────────────────────────────────────
Console.WriteLine("=== 6. Sky / skybox files (.box, .bin, sky* pattern) ===");
int skyCount = 0;
foreach (var entry in allEntries)
{
    string n = entry.Name;
    bool isSky = n.EndsWith(".box") || (n.EndsWith(".bin") && (n.Contains("sky") || n.Contains("environ") || n.Contains("fog")));
    if (!isSky) isSky = n.Contains("/sky") || n.Contains("skybox");
    if (isSky)
    {
        Console.WriteLine($"  {n}  ({entry.DataSize} bytes)");
        skyCount++;
    }
}
if (skyCount == 0) Console.WriteLine("  (none found)");
Console.WriteLine();

// ── 7. data/effect/map/ — per-cell lightmap bitmaps ──────────────────────────
Console.WriteLine("=== 7. data/effect/map/ — lightmap files ===");
var effMapFiles = allEntries
    .Where(e => e.Name.StartsWith("data/effect/map/"))
    .OrderBy(e => e.Name)
    .ToList();
Console.WriteLine($"  Total: {effMapFiles.Count} files");
// Group by extension
var byExt = effMapFiles.GroupBy(e => Path.GetExtension(e.Name)).OrderBy(g => g.Key);
foreach (var grp in byExt)
    Console.WriteLine($"  {grp.Key ?? "(no ext)"}  count={grp.Count()}");
Console.WriteLine();
// Show first 30
int shown = 0;
foreach (var e in effMapFiles)
{
    Console.WriteLine($"  {e.Name}  ({e.DataSize} bytes)");
    if (++shown >= 30) { Console.WriteLine($"  ... ({effMapFiles.Count - shown} more)"); break; }
}
Console.WriteLine();

// ── 8. data/map000/ special (non-mesh, non-terrain) files ─────────────────────
Console.WriteLine("=== 8. data/map000/ non-mesh/non-terrain files ===");
string[] skipExts = [".dds", ".ted", ".map", ".sod", ".arr", ".bud", ".fx1", ".fx2", ".fx3", ".fx4", ".fx5", ".fx6", ".fx7", ".png"];
var map000Special = allEntries
    .Where(e => e.Name.StartsWith("data/map000/") && !skipExts.Any(ext => e.Name.EndsWith(ext)))
    .OrderBy(e => e.Name)
    .ToList();
Console.WriteLine($"  Total: {map000Special.Count} files");
foreach (var e in map000Special)
    Console.WriteLine($"  {e.Name}  ({e.DataSize} bytes)");
Console.WriteLine();

// ── 9. environment / weather / fog / cloud / ambient / day-night files ─────────
Console.WriteLine("=== 9. Environment-keyword files (non-texture, non-xeff, non-sound) ===");
string[] keywords = ["environ", "weather", "daynight", "day_night", "sunlight", "fog", "cloud", "atmos", "ambient", "skylight"];
string[] skipExtsFull = [".dds", ".png", ".xeff", ".ogg", ".mp3", ".wav", ".bmp"];
int envCount = 0;
foreach (var entry in allEntries)
{
    string n = entry.Name;
    if (skipExtsFull.Any(ext => n.EndsWith(ext))) continue;
    foreach (var kw in keywords)
    {
        if (n.Contains(kw, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  {n}  ({entry.DataSize} bytes)");
            envCount++;
            break;
        }
    }
}
if (envCount == 0) Console.WriteLine("  (none found)");
Console.WriteLine();

// ── 10. data/sky/ directory listing ───────────────────────────────────────────
Console.WriteLine("=== 10. data/sky/ directory ===");
var skyDir = allEntries.Where(e => e.Name.StartsWith("data/sky/")).ToList();
if (skyDir.Count == 0) Console.WriteLine("  (no files under data/sky/)");
else foreach (var e in skyDir) Console.WriteLine($"  {e.Name}  ({e.DataSize} bytes)");
Console.WriteLine();

// ── 11. Known skybox paths from formats/sky.md spec ───────────────────────────
Console.WriteLine("=== 11. Skybox spec paths (from formats/sky.md) ===");
string[] specSkyPaths = [
    "data/effect/sky.box",
    "data/sky/sky.box",
    "data/sky/skybox.bin",
    "data/map000/sky.box",
    "data/map000/skybox.bin",
];
foreach (var p in specSkyPaths)
    Console.WriteLine($"  {(archive.Contains(p) ? "EXISTS" : "MISSING")}  {p}");
Console.WriteLine();

// ── 12. data/map000/bgtexture.txt head (for cross-ref) ────────────────────────
Console.WriteLine("=== 12. data/map000/bgtexture.txt size ===");
if (archive.Contains("data/map000/bgtexture.txt"))
{
    var sz = archive.GetFileContent("data/map000/bgtexture.txt").Length;
    Console.WriteLine($"  EXISTS  data/map000/bgtexture.txt  ({sz} bytes)");
}
Console.WriteLine();

// ── 13. All .bmp files under data/ (lightmaps?) ──────────────────────────────
Console.WriteLine("=== 13. .bmp files under data/ (potential lightmaps) ===");
var bmpFiles = allEntries.Where(e => e.Name.EndsWith(".bmp")).OrderBy(e => e.Name).ToList();
Console.WriteLine($"  Total .bmp: {bmpFiles.Count}");
var bmpByDir = bmpFiles.GroupBy(e => string.Join("/", e.Name.Split('/').Take(3))).OrderBy(g => g.Key);
foreach (var grp in bmpByDir)
    Console.WriteLine($"    {grp.Key}/  count={grp.Count()}");
Console.WriteLine();
if (bmpFiles.Count <= 60)
    foreach (var e in bmpFiles) Console.WriteLine($"    {e.Name}  ({e.DataSize} bytes)");
else
{
    foreach (var e in bmpFiles.Take(30)) Console.WriteLine($"    {e.Name}  ({e.DataSize} bytes)");
    Console.WriteLine($"    ... ({bmpFiles.Count - 30} more)");
}
Console.WriteLine();

// ── 14. xeffect.lst: first entry head to confirm format ───────────────────────
Console.WriteLine("=== 14. xeffect.lst: first 5 entries decoded ===");
Console.WriteLine($"  (count={lstCount}, stride={STRIDE})");
for (int i = 0; i < Math.Min(5, lstCount); i++)
{
    int off = 4 + i * STRIDE;
    var nameSpan = lstBytes.AsSpan(off, STRIDE);
    int nullIdx = nameSpan.IndexOf((byte)0);
    string name = nullIdx >= 0 ? cp949.GetString(nameSpan[..nullIdx]) : cp949.GetString(nameSpan);
    Console.WriteLine($"  [{i}] \"{name}\"");
}
Console.WriteLine();

// ── 15. Collect all 'sel_*' xeff from lst ────────────────────────────────────
Console.WriteLine("=== 15. xeffect.lst: all 'sel_*' and '3800*' entries ===");
for (int i = 0; i < lstCount; i++)
{
    int off = 4 + i * STRIDE;
    if (off + STRIDE > lstBytes.Length) break;
    var nameSpan = lstBytes.AsSpan(off, STRIDE);
    int nullIdx = nameSpan.IndexOf((byte)0);
    string name = nullIdx >= 0 ? cp949.GetString(nameSpan[..nullIdx]) : cp949.GetString(nameSpan);
    if (string.IsNullOrWhiteSpace(name)) continue;
    if (name.StartsWith("sel_", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("380") ||
        name.Contains("char_sel", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("zone_sel", StringComparison.OrdinalIgnoreCase))
        Console.WriteLine($"  [{i}] \"{name}\"");
}
Console.WriteLine();

// ── 16. Printable string runs in zone_sel_u.xeff (broader scan) ───────────────
Console.WriteLine("=== 16. zone_sel_u.xeff printable string runs (first 2048 bytes) ===");
if (archive.Contains("data/effect/xeff/zone_sel_u.xeff"))
{
    var b = archive.GetFileContent("data/effect/xeff/zone_sel_u.xeff").ToArray();
    PrintStrings("  ", b, Math.Min(2048, b.Length), cp949);
}
Console.WriteLine();

Console.WriteLine("DONE.");
return 0;

static void PrintStrings(string prefix, byte[] data, int scanLen, Encoding cp949)
{
    var sb = new StringBuilder();
    var run = new StringBuilder();
    for (int i = 0; i < scanLen; i++)
    {
        byte by = data[i];
        if (by >= 0x20 && by < 0x7F) run.Append((char)by);
        else
        {
            if (run.Length >= 4) { if (sb.Length > 0) sb.Append(' '); sb.Append($"[{run}]"); }
            run.Clear();
        }
    }
    if (run.Length >= 4) { if (sb.Length > 0) sb.Append(' '); sb.Append($"[{run}]"); }
    Console.WriteLine($"{prefix}{sb}");
}
