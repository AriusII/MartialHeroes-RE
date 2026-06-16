// xeff-charselect — THROWAWAY harness
// Parses char_select-u.xeff from the real client VFS and extracts, for ALL 68 sub-effects:
//   - emitter_type, resource_id, tex_count, anim_loop, anim_stride
//   - ALL texture names (64-byte name table slots, null-padded ASCII)
//   - velocity_x/y/z (first float triplet of each keyframe/static entry = local position offset)
//   - size_x/y/z (second float triplet = billboard half-extents)
//
// Layout per Docs/RE/formats/effects.md Section A (CONFIRMED spec):
//   File header: 8 bytes (effect_id u32 + sub_effect_count u32)
//   Each sub-effect block:
//     Fixed head 24 bytes: emitter_type, resource_id, anim_flag, field_unknown_a, element_dword2, tex_count
//     Name table: tex_count × 64 bytes
//     Curve section: 4 passes, each u32 count + count×f32
//     Track header: 9 bytes (anim_loop u8 + anim_stride u32 + anim_base_time u32)
//     Keyframe array:
//       animated (anim_loop != 0): tex_count × (u32 index + 9×f32) = tex_count × 40 bytes
//       static (anim_loop == 0):
//         emitter_type == 2: 6×f32 + 3×f32 = 36 bytes
//         else:              6×f32 = 24 bytes
//
// NEVER committed. NEVER added to MartialHeroes.slnx.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var cp949 = Encoding.GetEncoding(949);

// --- VFS paths (project-local clientdata, fallback to D:\MartialHeroesClient) ---
string infPath, vfsPath;
var localBase = @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata";
var localInf = Path.Combine(localBase, "data.inf");
var localVfs = Path.Combine(localBase, "data", "data.vfs");
if (File.Exists(localInf) && File.Exists(localVfs))
{
    infPath = localInf;
    vfsPath = localVfs;
}
else
{
    infPath = @"D:\MartialHeroesClient\data.inf";
    vfsPath = @"D:\MartialHeroesClient\data\data.vfs";
}

Console.WriteLine($"Opening VFS: {vfsPath}");

using var archive = MappedVfsArchive.Open(infPath, vfsPath);

const string xeffPath = "data/effect/xeff/char_select-u.xeff";
if (!archive.Contains(xeffPath))
{
    Console.Error.WriteLine($"ERROR: '{xeffPath}' not found in VFS.");
    return 1;
}

var mem = archive.GetFileContent(xeffPath);
var raw = mem.Span;
Console.WriteLine($"File size: {raw.Length} bytes");

// --- Parse file header (8 bytes) ---
if (raw.Length < 8)
{
    Console.Error.WriteLine("File too short for header.");
    return 1;
}

uint effectId = LE32(raw, 0);
uint subEffectCount = LE32(raw, 4);
Console.WriteLine($"effect_id:        {effectId}");
Console.WriteLine($"sub_effect_count: {subEffectCount}");
Console.WriteLine();

if (effectId == 0x46464558u)
{
    Console.Error.WriteLine("ERROR: effect_id == anti-magic sentinel 0x46464558 — file is invalid.");
    return 1;
}

// --- Walk sub-effect blocks ---
int pos = 8; // bytes consumed so far

// We collect results for the final table
var results = new List<SubEffectRow>();

for (uint si = 0; si < subEffectCount; si++)
{
    int blockStart = pos;

    // A.4.0 — Fixed head (24 bytes)
    if (pos + 24 > raw.Length) { Console.Error.WriteLine($"[{si}] Truncated at fixed head (pos={pos})"); break; }
    uint emitterType    = LE32(raw, pos + 0x00);
    uint resourceId     = LE32(raw, pos + 0x04);
    uint animFlag       = LE32(raw, pos + 0x08);
    uint fieldUnknownA  = LE32(raw, pos + 0x0C);
    uint elementDword2  = LE32(raw, pos + 0x10);
    uint texCount       = LE32(raw, pos + 0x14);
    pos += 24;

    // A.4.1 — Name table (tex_count × 64 bytes)
    var texNames = new List<string>();
    int nameTableSize = (int)texCount * 64;
    if (pos + nameTableSize > raw.Length) { Console.Error.WriteLine($"[{si}] Truncated at name table (pos={pos}, need {nameTableSize})"); break; }
    for (int t = 0; t < (int)texCount; t++)
    {
        int slot = pos + t * 64;
        // Find null terminator
        int len = 0;
        while (len < 64 && raw[slot + len] != 0) len++;
        string name = cp949.GetString(raw.Slice(slot, len).ToArray());
        texNames.Add(name);
    }
    pos += nameTableSize;

    // A.4.2 — Curve section (4 passes, each: u32 count + count×f32)
    for (int pass = 0; pass < 4; pass++)
    {
        if (pos + 4 > raw.Length) { Console.Error.WriteLine($"[{si}] Truncated at curve pass {pass} count (pos={pos})"); goto nextBlock; }
        uint curveCount = LE32(raw, pos);
        pos += 4;
        int curveBytes = (int)curveCount * 4;
        if (pos + curveBytes > raw.Length) { Console.Error.WriteLine($"[{si}] Truncated at curve pass {pass} data (pos={pos}, count={curveCount})"); goto nextBlock; }
        pos += curveBytes;
    }

    // A.4.3 — Track header (9 bytes)
    if (pos + 9 > raw.Length) { Console.Error.WriteLine($"[{si}] Truncated at track header (pos={pos})"); break; }
    byte animLoop       = raw[pos];
    uint animStride     = LE32(raw, pos + 1);
    uint animBaseTime   = LE32(raw, pos + 5);
    pos += 9;

    // A.4.4 / A.4.6 — Keyframe array
    float vel_x = 0, vel_y = 0, vel_z = 0;
    float sz_x = 0, sz_y = 0, sz_z = 0;

    if (animLoop != 0)
    {
        // Animated: tex_count × (u32 index + 9×f32) = tex_count × 40 bytes
        int kfTotal = (int)texCount * 40;
        if (pos + kfTotal > raw.Length) { Console.Error.WriteLine($"[{si}] Truncated at animated keyframes (pos={pos}, need {kfTotal})"); break; }
        // Read first keyframe only for position data (frame 0 = representative)
        // u32 kf_index then 9 floats
        uint kfIndex = LE32(raw, pos);
        vel_x = LEF32(raw, pos + 4);
        vel_y = LEF32(raw, pos + 8);
        vel_z = LEF32(raw, pos + 12);
        sz_x  = LEF32(raw, pos + 16);
        sz_y  = LEF32(raw, pos + 20);
        sz_z  = LEF32(raw, pos + 24);
        pos += kfTotal;
    }
    else
    {
        // Static: emitter_type == 2 → 36 bytes, else 24 bytes
        int staticSize = (emitterType == 2) ? 36 : 24;
        if (pos + staticSize > raw.Length) { Console.Error.WriteLine($"[{si}] Truncated at static entry (pos={pos}, need {staticSize})"); break; }
        vel_x = LEF32(raw, pos + 0);
        vel_y = LEF32(raw, pos + 4);
        vel_z = LEF32(raw, pos + 8);
        sz_x  = LEF32(raw, pos + 12);
        sz_y  = LEF32(raw, pos + 16);
        sz_z  = LEF32(raw, pos + 20);
        pos += staticSize;
    }

    results.Add(new SubEffectRow(
        Index: (int)si,
        EmitterType: emitterType,
        ResourceId: resourceId,
        AnimFlag: animFlag,
        TexCount: texCount,
        AnimLoop: animLoop,
        AnimStride: animStride,
        AnimBaseTime: animBaseTime,
        TexNames: texNames,
        VelX: vel_x, VelY: vel_y, VelZ: vel_z,
        SzX: sz_x, SzY: sz_y, SzZ: sz_z,
        BlockStart: blockStart
    ));

    continue;
    nextBlock:
    Console.Error.WriteLine($"[{si}] Skipped block due to truncation error at pos={pos}");
    break;
}

int bytesConsumed = pos;
int residual = raw.Length - bytesConsumed;
Console.WriteLine($"Bytes consumed: {bytesConsumed} / {raw.Length}  residual: {residual}");
Console.WriteLine($"Sub-effects parsed: {results.Count} / {subEffectCount}");
Console.WriteLine();

// --- Print table ---
Console.WriteLine("idx | emitter_type | resource_id | anim_loop | tex_count | anim_stride_ms | tex_names | vel_x | vel_y | vel_z | sz_x | sz_y | sz_z | block_start");
Console.WriteLine(new string('-', 200));
foreach (var r in results)
{
    string namesJoined = string.Join("; ", r.TexNames);
    Console.WriteLine($"{r.Index,3} | {r.EmitterType,12} | {r.ResourceId,11} | {r.AnimLoop,9} | {r.TexCount,9} | {r.AnimStride,14} | {namesJoined,-60} | {r.VelX,10:F4} | {r.VelY,10:F4} | {r.VelZ,10:F4} | {r.SzX,8:F4} | {r.SzY,8:F4} | {r.SzZ,8:F4} | 0x{r.BlockStart:X6}");
}

Console.WriteLine();
Console.WriteLine("=== UNIQUE TEXTURE NAMES ===");
var uniqueNames = new SortedSet<string>();
foreach (var r in results)
    foreach (var n in r.TexNames)
        if (!string.IsNullOrEmpty(n))
            uniqueNames.Add(n);
foreach (var n in uniqueNames)
    Console.WriteLine($"  {n}");

Console.WriteLine();
Console.WriteLine("=== VELOCITY_X DISTRIBUTION (for brazier/waterfall grouping) ===");
var byVelX = results
    .GroupBy(r => Math.Round(r.VelX, 1))
    .OrderBy(g => g.Key);
foreach (var g in byVelX)
{
    Console.WriteLine($"  vel_x ~ {g.Key,8:F1}: {g.Count(),3} sub-effects | indices: {string.Join(",", g.Select(r => r.Index))}");
}

Console.WriteLine();
Console.WriteLine("=== TEXTURE NAME GROUPS ===");
var byTex = results
    .GroupBy(r => r.TexNames.Count > 0 ? r.TexNames[0] : "<empty>")
    .OrderBy(g => g.Key);
foreach (var g in byTex)
{
    var velXVals = g.Select(r => r.VelX).Distinct().Select(v => v.ToString("F2"));
    Console.WriteLine($"  tex[0]={g.Key,-40} count={g.Count(),3} vel_x=[{string.Join(",", velXVals)}]");
}

return 0;

// --- Helpers ---
static uint LE32(ReadOnlySpan<byte> data, int offset)
    => (uint)(data[offset] | (data[offset+1] << 8) | (data[offset+2] << 16) | (data[offset+3] << 24));

static float LEF32(ReadOnlySpan<byte> data, int offset)
{
    uint bits = (uint)(data[offset] | (data[offset+1] << 8) | (data[offset+2] << 16) | (data[offset+3] << 24));
    return BitConverter.Int32BitsToSingle((int)bits);
}

record SubEffectRow(
    int Index,
    uint EmitterType, uint ResourceId, uint AnimFlag,
    uint TexCount, byte AnimLoop, uint AnimStride, uint AnimBaseTime,
    List<string> TexNames,
    float VelX, float VelY, float VelZ,
    float SzX, float SzY, float SzZ,
    int BlockStart
);
