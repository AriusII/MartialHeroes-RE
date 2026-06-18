// THROWAWAY harness v3 — items.scr STATS BLOCK cross-family verification
// NOT in MartialHeroes.slnx, never committed, never git add.
//
// Key insight from v2 output:
//   items.scr contains MULTIPLE RECORD TYPES interleaved:
//   Type A: "real item" with Korean name, uid in 0x0Bxx range, desc@0x38, tail=37-38 bytes → stride 131-132
//   Type B: "short filler" with quasi-random bytes as "name", uid=01070100 or similar, desc=1-2 bytes, tail=0 → stride 58-60
//   Type C: "long block" with uid=0, desc=0 bytes, tail=301 → stride 358
//
//   Pattern: [A, B, C] × N  (each "item" is a 3-record group)
//
//   The STATS BLOCK is actually INSIDE the Type C record (tail=301=0x12D bytes after the desc NUL).
//   The Type A record's tail (37-38 bytes after desc NUL) may be a short aux block.
//
//   From the first record's tail:
//   rec[0] (weapon 태산거웅도 이건): tail_hex = 00..00 EB B5 FC...
//   The 00..00 prefix is zeros, then EB B5 FC 0B = 0x0BFCB5EB (little-endian).
//   rec[3] (weapon +1): tail = 00..00 EB B5 FC 0B  (same 0x0BFCB5EB! + variant-specific suffix)
//
//   But the TYPE C tail (301 bytes) is where the real stats must be.
//   Looking at rec[2] (Type C, first in file) tail:
//     [000] 00 00 00 00 00 01 00 00 00 00 00 00 00 00 00 00
//     [010] 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00  (all zero)
//     ...
//   rec[5] (Type C, second):
//     [000] 00 00 00 00 00 02 00 00 (byte at [5]=2, previous was 1)  → counter!
//   rec[8] (Type C, third):
//     [000] 00 00 00 00 00 03 00 00 (byte [5]=3 → counter increments)
//
//   So Type C's tail byte[5] is a sequential counter (1,2,3,...).
//   We need to find WHERE in the Type C tail the actual item stats appear.
//
// NEW STRATEGY:
//   1. For a weapon item at the start, Type A tail: map to its corresponding Type C tail.
//      The group pattern is [A, B, C] → item group index is counter from Type C.
//   2. Sample Type A tails for: weapon enchant 0,+1,...,+15; armor; ring; consumable
//   3. Sample Type C tails for the same items
//   4. Look for offset within Type C tail that carries stat-like values
//      (damage for weapons, defense for armor, etc.)

using System.Text;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var cp949 = Encoding.GetEncoding(949);

var infPath = @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata\data.inf";
var vfsPath = @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata\data\data.vfs";
if (!File.Exists(infPath)) { infPath = @"D:\MartialHeroesClient\data.inf"; vfsPath = @"D:\MartialHeroesClient\data\data.vfs"; }

Console.WriteLine($"Mounting VFS from {infPath}");
using var vfs = MappedVfsArchive.Open(infPath, vfsPath);

var ib = vfs.GetFileContent("data/script/items.scr").ToArray();
Console.WriteLine($"items.scr size = {ib.Length:N0} bytes");
Console.WriteLine();

// ─── Helpers ───

static string Cp949Str(byte[] arr, int off, int maxBytes, Encoding cp949)
{
    int end = off;
    int limit = Math.Min(off + maxBytes, arr.Length);
    while (end < limit && arr[end] != 0) end++;
    if (end == off) return "";
    return cp949.GetString(arr, off, end - off);
}

static uint U32(byte[] a, int off) => off + 4 <= a.Length ? BitConverter.ToUInt32(a, off) : 0;
static ushort U16(byte[] a, int off) => off + 2 <= a.Length ? BitConverter.ToUInt16(a, off) : (ushort)0;
static float F32(byte[] a, int off) => off + 4 <= a.Length ? BitConverter.ToSingle(a, off) : 0f;

static bool LooksLikeName(byte[] arr, int off)
{
    if (off + 52 >= arr.Length) return false;
    if (arr[off] == 0) return false;
    // pad_30 @ off+48..+51 must be 0x00000000
    return arr[off+48] == 0 && arr[off+49] == 0 && arr[off+50] == 0 && arr[off+51] == 0
        && arr[off+4] != 0; // at least 4 valid name bytes
}

static int FindNextRec(byte[] arr, int from, int maxScan = 8192)
{
    for (int i = from; i < from + maxScan && i < arr.Length - 4; i++)
        if (LooksLikeName(arr, i)) return i;
    return -1;
}

// ─── Walk the first 200 records and classify them ───
Console.WriteLine("=== RECORD CLASSIFICATION (first 200 records) ===");
// RecType: A=real item (uid in valid range), B=short filler, C=long block

var allRecs = new List<RecInfo>();
int pos = 0;
while (pos < ib.Length && allRecs.Count < 200)
{
    if (!LooksLikeName(ib, pos))
    {
        // Try to align
        bool ok = false;
        for (int k = 1; k <= 8; k++)
            if (pos + k + 52 < ib.Length && LooksLikeName(ib, pos + k)) { pos += k; ok = true; break; }
        if (!ok) break;
    }

    string nm = Cp949Str(ib, pos, 48, cp949);
    uint uid = U32(ib, pos + 0x34);

    int descBase = pos + 0x38;
    int descE = descBase;
    while (descE < ib.Length && ib[descE] != 0) descE++;
    int descLen = descE - descBase;

    int afterNul = descE + 1;
    int nextRec = FindNextRec(ib, afterNul, 8192);
    if (nextRec < 0)
    {
        // End of file or can't find next
        var tailBytes2 = ib.Skip(afterNul).Take(ib.Length - afterNul).ToArray();
        int tp = uid >= 0x0B000000 && uid <= 0x0FFFFFFF ? 1 : (descLen <= 4 ? 2 : 3);
        allRecs.Add(new RecInfo(pos, ib.Length, nm, uid, descLen, tailBytes2, tp));
        break;
    }

    var tail = ib.Skip(afterNul).Take(nextRec - afterNul).ToArray();
    int recType = uid >= 0x0B000000 && uid <= 0x0FFFFFFF ? 1 : (tail.Length == 0 ? 2 : 3);
    allRecs.Add(new RecInfo(pos, nextRec, nm, uid, descLen, tail, recType));
    pos = nextRec;
}

// Summarize
var typeA = allRecs.Where(r => r.RecType == 1).ToList();
var typeB = allRecs.Where(r => r.RecType == 2).ToList();
var typeC = allRecs.Where(r => r.RecType == 3).ToList();
Console.WriteLine($"Records walked: {allRecs.Count} (TypeA={typeA.Count}, TypeB={typeB.Count}, TypeC={typeC.Count})");
Console.WriteLine($"TypeA (real items): uid range 0x{(typeA.Count>0?typeA.Min(r=>r.Uid):0):X8}..0x{(typeA.Count>0?typeA.Max(r=>r.Uid):0):X8}");
Console.WriteLine($"TypeB (fillers): tail.Length range {(typeB.Count>0?typeB.Min(r=>r.Tail.Length):0)}..{(typeB.Count>0?typeB.Max(r=>r.Tail.Length):0)}");
Console.WriteLine($"TypeC (long blocks): tail.Length range {(typeC.Count>0?typeC.Min(r=>r.Tail.Length):0)}..{(typeC.Count>0?typeC.Max(r=>r.Tail.Length):0)}");
Console.WriteLine();

// Print first 15 records with type
Console.WriteLine("First 15 records:");
foreach (var r in allRecs.Take(15))
{
    string nm2 = r.Name.Length > 30 ? r.Name[..30] : r.Name;
    Console.WriteLine($"  @0x{r.Start:X6} T={r.RecType} uid=0x{r.Uid:X8} descLen={r.DescLen,3} tailLen={r.Tail.Length,4} stride={r.End-r.Start,5} name='{nm2}'");
}
Console.WriteLine();

// ─── GROUPING: each group is [A, B, C] ───
// Group n: recs[3n], recs[3n+1], recs[3n+2]
Console.WriteLine("=== GROUP STRUCTURE VERIFICATION ===");
Console.WriteLine("Checking [A,B,C] pattern:");
for (int i = 0; i < Math.Min(5, allRecs.Count / 3); i++)
{
    var a = allRecs[3*i];
    var b = allRecs[3*i+1];
    var c = allRecs[3*i+2];
    Console.WriteLine($"  Group {i}: A(T={a.RecType} uid=0x{a.Uid:X8} '{a.Name}') B(T={b.RecType}) C(T={c.RecType} tail={c.Tail.Length})");
}
Console.WriteLine();

// ─── TYPE A TAIL ANALYSIS (the short tail after desc NUL) ───
Console.WriteLine("=== TYPE A TAIL ANALYSIS ===");
Console.WriteLine("Comparing the short tail (after desc NUL) across first 10 TypeA records:");
Console.WriteLine();
foreach (var r in typeA.Take(10))
{
    Console.WriteLine($"  '{r.Name}' uid=0x{r.Uid:X8} tailLen={r.Tail.Length}");
    if (r.Tail.Length >= 4)
    {
        Console.Write("    tail[0..min(40,len)]: ");
        for (int i = 0; i < Math.Min(40, r.Tail.Length); i++)
            Console.Write($"{r.Tail[i]:X2} ");
        Console.WriteLine();
    }
    if (r.Tail.Length >= 4) Console.WriteLine($"    [0x00] u32=0x{U32(r.Tail,0):X8}  [0x04] u32=0x{U32(r.Tail,4):X8}");
}
Console.WriteLine();

// ─── TYPE C TAIL ANALYSIS (the long 301-byte tail) ───
Console.WriteLine("=== TYPE C TAIL ANALYSIS ===");
Console.WriteLine("The long tail after TypeC desc NUL — SUSPECTED to hold the real stats block.");
Console.WriteLine();

// For the first item group (weapon 이건 enchant 0..N), collect TypeC tails
// and look for where values change across enchant levels
var groupedTypeA = typeA.ToList();
var groupedTypeC = typeC.ToList();

if (groupedTypeA.Count >= 3 && groupedTypeC.Count >= 3)
{
    Console.WriteLine("Comparing TypeC tails for first 4 item groups (enchant variants of same weapon):");
    for (int gi = 0; gi < Math.Min(4, groupedTypeC.Count); gi++)
    {
        var c = groupedTypeC[gi];
        Console.WriteLine($"\n  Group {gi} TypeC: uid=0x{c.Uid:X8} tail={c.Tail.Length} bytes");
        Console.Write("  [000] ");
        for (int i = 0; i < Math.Min(80, c.Tail.Length); i++)
        {
            if (i > 0 && i % 16 == 0) Console.Write($"\n  [{i:X3}] ");
            Console.Write($"{c.Tail[i]:X2} ");
        }
        Console.WriteLine();
        // Named field readout
        if (c.Tail.Length >= 8)  Console.WriteLine($"  [0x00] u32=0x{U32(c.Tail,0x00):X8}  [0x04] u32=0x{U32(c.Tail,0x04):X8}");
        if (c.Tail.Length >= 16) Console.WriteLine($"  [0x08] u32=0x{U32(c.Tail,0x08):X8}  [0x0C] u32=0x{U32(c.Tail,0x0C):X8}");
        if (c.Tail.Length >= 32) Console.WriteLine($"  [0x10] u32=0x{U32(c.Tail,0x10):X8}  [0x14] u32=0x{U32(c.Tail,0x14):X8}");
    }
}
Console.WriteLine();

// ─── FULL FILE SCAN for TypeA records ───
// Walk the entire file and collect ALL TypeA records with their corresponding TypeC tail
Console.WriteLine("=== FULL FILE SCAN — collecting all [A,B,C] groups ===");

var groups = new List<ItemGroup>();
pos = 0;
var batchRecs = new List<RecInfo>();
int totalWalked = 0;

while (pos < ib.Length && totalWalked < 120000)
{
    if (!LooksLikeName(ib, pos))
    {
        int nx = FindNextRec(ib, pos, 512);
        if (nx < 0) break;
        pos = nx;
    }

    string nm3 = Cp949Str(ib, pos, 48, cp949);
    uint uid3 = U32(ib, pos + 0x34);

    int descBase3 = pos + 0x38;
    int descE3 = descBase3;
    while (descE3 < ib.Length && ib[descE3] != 0) descE3++;
    int descLen3 = descE3 - descBase3;
    int afterNul3 = descE3 + 1;

    int nextRec3 = FindNextRec(ib, afterNul3, 8192);
    if (nextRec3 < 0) break;

    var tail3 = ib.Skip(afterNul3).Take(nextRec3 - afterNul3).ToArray();
    int rtype3 = uid3 >= 0x0B000000 && uid3 <= 0x0FFFFFFF ? 1 : (tail3.Length == 0 ? 2 : 3);
    batchRecs.Add(new RecInfo(pos, nextRec3, nm3, uid3, descLen3, tail3, rtype3));
    pos = nextRec3;
    totalWalked++;
}

Console.WriteLine($"Total records walked: {totalWalked}");

// Build groups from batched records
int gi2 = 0;
while (gi2 + 2 < batchRecs.Count)
{
    var a = batchRecs[gi2];
    var b = batchRecs[gi2+1];
    var c = batchRecs[gi2+2];
    if (a.RecType == 1 && c.RecType == 3)
    {
        groups.Add(new ItemGroup(a, b, c));
        gi2 += 3;
    }
    else
    {
        gi2++;
    }
}

Console.WriteLine($"Groups (A,B,C) assembled: {groups.Count}");
Console.WriteLine();

// ─── STATS ANALYSIS IN TYPE C TAILS ───
// For each group, we have RecA (name, uid, desc) and RecC (tail = stats block).
// Let's tabulate RecC.Tail fields by item category.

// CATEGORY DETECTION (by item name keywords):
static string GetCategory(string name)
{
    if (name.Contains('도') || name.Contains('검') || name.Contains('창') || name.Contains('편') || name.Contains('절'))
        return "WEAPON_1H";
    if (name.Contains('장') && (name.Contains('창') || name.Contains('봉') || name.Contains('도')))
        return "WEAPON_2H";
    if (name.Contains("갑옷") || name.Contains("외갑") || name.Contains("내갑") || name.Contains("상의"))
        return "ARMOR_BODY";
    if (name.Contains("화") && name.Length <= 10 && !name.Contains("천"))
        return "ARMOR_FEET";
    if (name.Contains("반지"))
        return "ACCESSORY_RING";
    if (name.Contains('단') && name.Length <= 8)
        return "CONSUMABLE";
    if (name.Contains("갑") && name.Length <= 12)
        return "ARMOR_GENERIC";
    return "OTHER";
}

// Collect one representative group per category
var catReps = new Dictionary<string, ItemGroup>();
foreach (var g in groups)
{
    string cat = GetCategory(g.RecA.Name);
    if (!catReps.ContainsKey(cat))
        catReps[cat] = g;
}

Console.WriteLine("=== CROSS-FAMILY STATS BLOCK COMPARISON (TYPE C TAILS) ===");
Console.WriteLine($"Categories found: {string.Join(", ", catReps.Keys)}");
Console.WriteLine();

// Print detailed stats for one rep per category
foreach (var kv in catReps.Take(6))
{
    var g = kv.Value;
    var ta = g.RecA.Tail; // short tail
    var tc = g.RecC?.Tail ?? []; // long tail (stats block)
    int tcLen = tc.Length;

    Console.WriteLine($"=== Category: {kv.Key} ===");
    Console.WriteLine($"  Name: '{g.RecA.Name}' uid=0x{g.RecA.Uid:X8}");
    Console.WriteLine($"  Desc: '{Cp949Str(ib, g.RecA.Start + 0x38, g.RecA.DescLen + 1, cp949)}'");
    Console.WriteLine($"  TypeA tail: {ta.Length} bytes");
    if (ta.Length > 0)
    {
        Console.Write("    TA[hex]: ");
        for (int i = 0; i < Math.Min(40, ta.Length); i++) Console.Write($"{ta[i]:X2} ");
        Console.WriteLine();
    }
    Console.WriteLine($"  TypeC tail (stats): {tcLen} bytes");
    if (tcLen > 0)
    {
        Console.Write("  TC[000]: ");
        for (int i = 0; i < Math.Min(80, tcLen); i++)
        {
            if (i > 0 && i % 16 == 0) Console.Write($"\n  TC[{i:X3}]: ");
            Console.Write($"{tc[i]:X2} ");
        }
        Console.WriteLine();

        // Named fields — using the §1.4 offsets but relative to TypeC tail start
        Console.WriteLine($"  [0x00] u32 = 0x{U32(tc,0x00):X8}  (template_ref_a? or counter?)");
        if (tcLen >= 8)  Console.WriteLine($"  [0x04] u32 = 0x{U32(tc,0x04):X8}");
        if (tcLen >= 12) Console.WriteLine($"  [0x08] u32 = 0x{U32(tc,0x08):X8}");
        if (tcLen >= 16) Console.WriteLine($"  [0x0C] u32 = 0x{U32(tc,0x0C):X8}");
        if (tcLen >= 0x28) Console.WriteLine($"  [0x24] f32 = {F32(tc,0x24):G6}  [0x28] f32 = {F32(tc,0x28):G6}");
        if (tcLen >= 0x30) Console.WriteLine($"  [0x2C] u32 = 0x{U32(tc,0x2C):X8}  [0x30] u32 = 0x{U32(tc,0x30):X8}");
        if (tcLen >= 0x3C) Console.WriteLine($"  [0x34] u32 = 0x{U32(tc,0x34):X8}  [0x38] u32 = 0x{U32(tc,0x38):X8}");
        if (tcLen >= 0x48) Console.WriteLine($"  [0x3C] u16 = 0x{U16(tc,0x3C):X4}  [0x3E] = 0x{U16(tc,0x3E):X4}  [0x40] = 0x{U16(tc,0x40):X4}  [0x42] = 0x{U16(tc,0x42):X4}  [0x44] = 0x{U16(tc,0x44):X4}  [0x46] = 0x{U16(tc,0x46):X4}");
        if (tcLen >= 0x5C) Console.WriteLine($"  [0x50] u32 = 0x{U32(tc,0x50):X8}  [0x54] = 0x{U32(tc,0x54):X8}  [0x58] = 0x{U32(tc,0x58):X8}");
    }
    Console.WriteLine();
}

// ─── ENCHANT VARIANT ANALYSIS ───
// For the first weapon, show Type C tails for enchant +0,+1,...,+14
Console.WriteLine("=== ENCHANT VARIANT COMPARISON (Type C tails, first weapon family) ===");
Console.WriteLine("Looking for enchant variants with same base name...");

// Get first weapon's base name
string firstWeaponBase = "";
if (groups.Count > 0)
{
    string nm0 = groups[0].RecA.Name;
    // Strip "+N" suffix if any
    int plusIdx = nm0.LastIndexOf('+');
    firstWeaponBase = plusIdx > 0 ? nm0[..plusIdx].TrimEnd() : nm0.TrimEnd();
    Console.WriteLine($"Base name: '{firstWeaponBase}'");
}

var enchantGroups = groups
    .Where(g => g.RecA.Name.StartsWith(firstWeaponBase, StringComparison.Ordinal))
    .Take(8)
    .ToList();

Console.WriteLine($"Enchant variants found: {enchantGroups.Count}");
Console.WriteLine();

// Compare Type C tail offsets that vary across enchant levels
if (enchantGroups.Count >= 3)
{
    int tcLen2 = enchantGroups[0].RecC?.Tail.Length ?? 0;
    Console.WriteLine($"TypeC tail length: {tcLen2}");
    Console.WriteLine();

    // For each offset in the TypeC tail, check if value changes across enchant levels
    // Values that CHANGE → stat fields; values that DON'T CHANGE → template/flag fields
    Console.WriteLine($"{"Offset",-9} {"Base(+0)",-12}");
    foreach (var eg in enchantGroups)
        Console.Write($"  {eg.RecA.Name[..Math.Min(eg.RecA.Name.Length,15)]:15}");
    Console.WriteLine();
    Console.WriteLine(new string('-', 120));

    // Print u32 values at each offset for first 80 bytes
    for (int off = 0; off + 3 < tcLen2 && off < 0x80; off += 4)
    {
        var vals = enchantGroups.Select(g => g.RecC != null && off + 4 <= g.RecC.Tail.Length ? U32(g.RecC.Tail, off) : 0u).ToArray();
        bool anyChange = vals.Distinct().Count() > 1;
        string marker = anyChange ? " ← VARIES" : "";
        Console.Write($"  [0x{off:X3}]  ");
        foreach (var v in vals) Console.Write($"0x{v:X8}  ");
        Console.WriteLine(marker);
    }
}
Console.WriteLine();

// ─── CROSS-CATEGORY STATS COMPARISON (Type C tails) ───
Console.WriteLine("=== CROSS-CATEGORY Type C TAIL FIELD COMPARISON ===");
Console.WriteLine("One representative per category, showing all u32 values in the Type C tail:");
Console.WriteLine();

// Find representatives from 4 distinct categories
var targets = new (string Category, string NameHint)[]
{
    ("WEAPON", "도"),
    ("ARMOR", "갑"),
    ("RING", "반지"),
    ("CONSUMABLE", "단"),
};

var catItems = new List<(string Cat, ItemGroup G)>();
foreach (var (cat, hint) in targets)
{
    var g2 = groups.FirstOrDefault(g => g.RecA.Name.Contains(hint) && g.RecC != null && g.RecC.Tail.Length > 50);
    if (g2 != null) catItems.Add((cat, g2));
}

// Print column header
Console.Write($"{"Offset",-9}");
foreach (var (cat, _) in catItems) Console.Write($"  {cat,-14}");
Console.WriteLine("  VARIES?");

int maxTcLen = catItems.Count > 0 ? catItems.Max(x => x.G.RecC?.Tail.Length ?? 0) : 0;
for (int off = 0; off + 3 < maxTcLen && off < 0xC0; off += 4)
{
    var vals = catItems.Select(x => x.G.RecC != null && off + 4 <= x.G.RecC.Tail.Length ? U32(x.G.RecC.Tail, off) : 0u).ToArray();
    bool varies = vals.Distinct().Count() > 1;
    Console.Write($"  [0x{off:X3}]  ");
    foreach (var v in vals) Console.Write($"0x{v:X8}  ");
    Console.WriteLine(varies ? "  VARIES" : "");
}
Console.WriteLine();

// Names of the representative items used
Console.WriteLine("Representative items per category:");
foreach (var (cat, g2) in catItems)
    Console.WriteLine($"  {cat}: '{g2.RecA.Name}' uid=0x{g2.RecA.Uid:X8}");
Console.WriteLine();

// ─── AGGREGATE STATS: total item count, TypeC tail distribution ───
Console.WriteLine("=== AGGREGATE STATISTICS ===");
Console.WriteLine($"Total groups walked: {groups.Count}");
var tcSizes = groups.Where(g => g.RecC != null).Select(g => g.RecC!.Tail.Length).ToList();
Console.WriteLine($"TypeC tail sizes: {string.Join(", ", tcSizes.Distinct().OrderBy(x => x))}");
var taSizes = groups.Select(g => g.RecA.Tail.Length).ToList();
Console.WriteLine($"TypeA tail sizes: {string.Join(", ", taSizes.Distinct().OrderBy(x => x))}");
Console.WriteLine();

// ─── LONG-RANGE SAMPLE: categories from across the file ───
Console.WriteLine("=== LONG-RANGE CROSS-CATEGORY SAMPLING ===");
int[] sampleIdxs = [0, groups.Count/10, groups.Count/5, groups.Count/3, groups.Count/2, groups.Count*2/3, groups.Count*4/5, groups.Count*9/10];
foreach (int idx in sampleIdxs.Distinct())
{
    if (idx >= groups.Count) continue;
    var g3 = groups[idx];
    var tc3 = g3.RecC?.Tail ?? [];
    int tcl = tc3.Length;
    Console.WriteLine($"  Group[{idx}] '{g3.RecA.Name}' uid=0x{g3.RecA.Uid:X8} cat={GetCategory(g3.RecA.Name)}");
    if (tcl > 0)
    {
        Console.WriteLine($"    TC tail {tcl} bytes: [0x00]=0x{U32(tc3,0):X8} [0x04]=0x{U32(tc3,4):X8} [0x08]=0x{U32(tc3,8):X8}");
        if (tcl >= 0x2C) Console.WriteLine($"    [0x24]=f32:{F32(tc3,0x24):G5} [0x28]=f32:{F32(tc3,0x28):G5}");
        if (tcl >= 0x38) Console.WriteLine($"    [0x2C]=0x{U32(tc3,0x2C):X8} [0x30]=0x{U32(tc3,0x30):X8} [0x34]=0x{U32(tc3,0x34):X8}");
        if (tcl >= 0x48) Console.WriteLine($"    [0x3C]=0x{U16(tc3,0x3C):X4} [0x3E]=0x{U16(tc3,0x3E):X4} [0x40]=0x{U16(tc3,0x40):X4} [0x42]=0x{U16(tc3,0x42):X4}");
    }
}
Console.WriteLine();
Console.WriteLine("Done.");

// ─── Record type declarations (must follow top-level statements in C# file-scoped programs) ───
record RecInfo(int Start, int End, string Name, uint Uid, int DescLen, byte[] Tail, int RecType);
record ItemGroup(RecInfo RecA, RecInfo? RecB, RecInfo? RecC);
