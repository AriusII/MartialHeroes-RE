// THROWAWAY harness — config-constants-probe
// Analyses unverified constants in exp.scr, userpoint.scr, skills.scr, mobs.scr, products.scr
// NOT in MartialHeroes.slnx — never commit — runs from the scripts/ dir only.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var cp949 = Encoding.GetEncoding(949);

// VFS paths (try project-local first, then D:\)
string infPath, vfsPath;
var localInf = @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata\data.inf";
var localVfs = @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata\data\data.vfs";
if (File.Exists(localInf))
{
    infPath = localInf;
    vfsPath = localVfs;
}
else
{
    infPath = @"D:\MartialHeroesClient\data.inf";
    vfsPath = @"D:\MartialHeroesClient\data\data.vfs";
}

Console.WriteLine($"Opening VFS: {infPath}");
using var vfs = MappedVfsArchive.Open(infPath, vfsPath);
Console.WriteLine("VFS opened OK");

// ─────────────────────────────────────────────────────────────────
// 1. exp.scr — stride 20, 300 records
//    +2 = constant 64 (UNVERIFIED semantic)
//    +12 secondary EXP curve, +16 tertiary EXP curve
// ─────────────────────────────────────────────────────────────────
Console.WriteLine("\n══════════════════════════════════════════════");
Console.WriteLine("1. exp.scr — constant @ +2 and secondary/tertiary EXP curves");
Console.WriteLine("══════════════════════════════════════════════");
{
    const int stride = 20;
    var data = vfs.GetFileContent("data/script/exp.scr").ToArray();
    int count = data.Length / stride;
    Console.WriteLine($"   File size: {data.Length} bytes, records: {count}");

    // Verify the constant at +2
    bool allConst64 = true;
    for (int i = 0; i < count; i++)
    {
        var v = BitConverter.ToUInt16(data, i * stride + 2);
        if (v != 64) { allConst64 = false; Console.WriteLine($"   MISMATCH at record {i}: +2 = {v}"); }
    }
    Console.WriteLine($"   +2 constant 64 across all {count}: {allConst64}");

    // Analyse secondary (+12) and tertiary (+16) curves
    // Secondary: when does it first become non-zero?
    int secFirstNonZero = -1, secLastNonZero = -1;
    uint secMax = 0, secPrev = 0;
    int secDecreases = 0;
    for (int i = 0; i < count; i++)
    {
        var v = BitConverter.ToUInt32(data, i * stride + 12);
        if (v != 0 && secFirstNonZero < 0) secFirstNonZero = i + 1; // 1-based level
        if (v != 0) secLastNonZero = i + 1;
        if (v > secMax) secMax = v;
        if (v < secPrev && v != 0) secDecreases++;
        secPrev = v;
    }

    int terFirstNonZero = -1, terLastNonZero = -1;
    uint terMax = 0, terPrev = 0;
    int terDecreases = 0;
    for (int i = 0; i < count; i++)
    {
        var v = BitConverter.ToUInt32(data, i * stride + 16);
        if (v != 0 && terFirstNonZero < 0) terFirstNonZero = i + 1;
        if (v != 0) terLastNonZero = i + 1;
        if (v > terMax) terMax = v;
        if (v < terPrev && v != 0) terDecreases++;
        terPrev = v;
    }

    Console.WriteLine($"   Secondary (+12): first non-zero at L{secFirstNonZero}, last at L{secLastNonZero}, max={secMax}, decreasing transitions={secDecreases}");
    Console.WriteLine($"   Tertiary  (+16): first non-zero at L{terFirstNonZero}, last at L{terLastNonZero}, max={terMax}, decreasing transitions={terDecreases}");

    // Check whether secondary is strictly increasing (cumulative would be)
    // Also compare to primary sum (is secondary a sum of primary values?)
    uint primarySum = 0;
    bool secondaryIsPrimarySum = true;
    for (int i = 0; i < count; i++)
    {
        var primary = BitConverter.ToUInt32(data, i * stride + 4);
        primarySum += primary;
        var secondary = BitConverter.ToUInt32(data, i * stride + 12);
        // cumulative check: does secondary equal running sum of primary?
        // Only check from where secondary starts
        if (i + 1 >= secFirstNonZero && secondary != primarySum)
        {
            if (secondaryIsPrimarySum) Console.WriteLine($"   Secondary is NOT a cumsum of primary (first diff at L{i+1}: sec={secondary} vs cumsum={primarySum})");
            secondaryIsPrimarySum = false;
        }
    }
    if (secondaryIsPrimarySum) Console.WriteLine($"   Secondary IS the cumulative sum of primary!");

    // Print sample rows for secondary/tertiary around onset
    Console.WriteLine("\n   Sample rows around secondary-curve onset (levels 70-80):");
    for (int i = 69; i < Math.Min(80, count); i++)
    {
        var lvl  = BitConverter.ToUInt16(data, i * stride + 0);
        var c64  = BitConverter.ToUInt16(data, i * stride + 2);
        var prim = BitConverter.ToUInt32(data, i * stride + 4);
        var rsv  = BitConverter.ToUInt32(data, i * stride + 8);
        var sec  = BitConverter.ToUInt32(data, i * stride + 12);
        var ter  = BitConverter.ToUInt32(data, i * stride + 16);
        Console.WriteLine($"   L{lvl:D3} | c64={c64} | primary={prim,12} | rsv={rsv} | sec={sec,12} | ter={ter,10}");
    }

    Console.WriteLine("\n   Sample rows around tertiary onset (levels 183-195):");
    for (int i = 182; i < Math.Min(197, count); i++)
    {
        var lvl  = BitConverter.ToUInt16(data, i * stride + 0);
        var prim = BitConverter.ToUInt32(data, i * stride + 4);
        var sec  = BitConverter.ToUInt32(data, i * stride + 12);
        var ter  = BitConverter.ToUInt32(data, i * stride + 16);
        Console.WriteLine($"   L{lvl:D3} | primary={prim,12} | sec={sec,15} | ter={ter,10}");
    }

    // Check if secondary is a separate-per-level EXP-for-that-level (not cumulative)
    // by looking at its shape: does it look like per-step costs of a secondary track?
    Console.WriteLine("\n   Checking whether secondary looks cumulative or per-level:");
    // If per-level: each value = how much of secondary you need at that level
    // If cumulative: each value = total secondary XP from L1 to that level
    // Heuristic: does it ever DECREASE (then it can't be cumulative)?
    Console.WriteLine($"   Secondary: {(secDecreases == 0 ? "NEVER decreases (consistent with cumulative OR monotone per-level)" : $"DECREASES {secDecreases} times (not strictly cumulative)")}");
    Console.WriteLine($"   Tertiary:  {(terDecreases == 0 ? "NEVER decreases" : $"DECREASES {terDecreases} times")}");

    // Show a ratio of secondary/primary at first few non-zero secondary records
    Console.WriteLine("\n   Secondary/primary ratio at onset records:");
    int shown = 0;
    for (int i = 0; i < count && shown < 10; i++)
    {
        var sec = BitConverter.ToUInt32(data, i * stride + 12);
        var pri = BitConverter.ToUInt32(data, i * stride + 4);
        if (sec > 0)
        {
            double ratio = (double)sec / pri;
            Console.WriteLine($"   L{i+1:D3}: secondary={sec,12}, primary={pri,12}, ratio={ratio:F4}");
            shown++;
        }
    }

    // Plateau analysis: what value does secondary plateau at?
    uint secPlat = 0; int secPlateauStart = -1;
    for (int i = count - 1; i >= 0; i--)
    {
        var v = BitConverter.ToUInt32(data, i * stride + 12);
        if (v != 0) { secPlat = v; break; }
    }
    // Find first occurrence of that plateau value
    for (int i = 0; i < count; i++)
    {
        var v = BitConverter.ToUInt32(data, i * stride + 12);
        if (v == secPlat && secPlateauStart < 0) secPlateauStart = i + 1;
    }
    Console.WriteLine($"\n   Secondary plateau value: {secPlat} (starts at L{secPlateauStart})");

    uint terPlat = 0; int terPlateauStart = -1;
    for (int i = count - 1; i >= 0; i--)
    {
        var v = BitConverter.ToUInt32(data, i * stride + 16);
        if (v != 0) { terPlat = v; break; }
    }
    for (int i = 0; i < count; i++)
    {
        var v = BitConverter.ToUInt32(data, i * stride + 16);
        if (v == terPlat && terPlateauStart < 0) terPlateauStart = i + 1;
    }
    Console.WriteLine($"   Tertiary plateau value: {terPlat} (starts at L{terPlateauStart})");

    // Compare: primary plateaus at 1,999,557,415 from ~L143
    // Does secondary onset match a meaningful level boundary?
}

// ─────────────────────────────────────────────────────────────────
// 2. userpoint.scr — stride 32, 301 records
//    +2 = constant 25 (UNVERIFIED semantic)
// ─────────────────────────────────────────────────────────────────
Console.WriteLine("\n══════════════════════════════════════════════");
Console.WriteLine("2. userpoint.scr — constant 25 @ +2");
Console.WriteLine("══════════════════════════════════════════════");
{
    const int stride = 32;
    var data = vfs.GetFileContent("data/script/userpoint.scr").ToArray();
    int count = data.Length / stride;
    Console.WriteLine($"   File size: {data.Length}, records: {count}");

    // Sample the first 10 records to understand the structure
    Console.WriteLine("   First 15 records:");
    for (int i = 0; i < Math.Min(15, count); i++)
    {
        var key  = BitConverter.ToUInt16(data, i * stride + 0);
        var c25  = BitConverter.ToUInt16(data, i * stride + 2);
        var g1g  = BitConverter.ToUInt16(data, i * stride + 4);
        var g1p  = BitConverter.ToUInt16(data, i * stride + 6);
        var g1c  = BitConverter.ToUInt32(data, i * stride + 8);
        var g2g  = BitConverter.ToUInt16(data, i * stride + 12);
        var g2p  = BitConverter.ToUInt16(data, i * stride + 14);
        var g2c  = BitConverter.ToUInt32(data, i * stride + 16);
        var s_lo = BitConverter.ToUInt16(data, i * stride + 20);
        var s_hi = BitConverter.ToUInt16(data, i * stride + 22);
        var t1   = BitConverter.ToUInt32(data, i * stride + 24);
        var t2   = BitConverter.ToUInt32(data, i * stride + 28);
        Console.WriteLine($"   key={key:D3} c25={c25} g1gain={g1g:D4} g1pad={g1p} g1cum={g1c:D6} g2gain={g2g:D4} g2pad={g2p} g2cum={g2c:D6} sec=({s_lo},{s_hi}) ter=({t1},{t2})");
    }

    // Cross-check: is constant 25 equal to initial g1 gain at key=0?
    var key0_g1gain = BitConverter.ToUInt16(data, 0 * stride + 4);
    var key0_g2gain = BitConverter.ToUInt16(data, 0 * stride + 12);
    Console.WriteLine($"\n   Key=0: g1_gain={key0_g1gain}, g2_gain={key0_g2gain}, constant_25 at+2=25");

    // The creation stat budget hypothesis:
    // If you start at level 0 (key 0) with the cumulative points at key 0 being the creation budget:
    var key0_g1cum = BitConverter.ToUInt32(data, 0 * stride + 8);
    var key0_g2cum = BitConverter.ToUInt32(data, 0 * stride + 16);
    Console.WriteLine($"   Key=0: g1_cumulative={key0_g1cum}, g2_cumulative={key0_g2cum}");

    // The hypothesis is that constant 25 = starting stat-point budget at char creation
    // Look at users.scr to cross-check: the stat-point budget is separate from the stat-ratio grid
    // Also check: is 25 related to any of g1_gain (5), g1_cum, g2_gain (7), or a combination?
    Console.WriteLine($"\n   Candidate meanings for '25':");
    Console.WriteLine($"     g1_gain(key0)={key0_g1gain} + g2_gain(key0)={key0_g2gain} = {key0_g1gain + key0_g2gain}");

    // Check also against g1_gain across early keys
    Console.WriteLine("\n   g1_gain and c25 across keys 0-5:");
    for (int i = 0; i < Math.Min(6, count); i++)
    {
        var key  = BitConverter.ToUInt16(data, i * stride + 0);
        var c25  = BitConverter.ToUInt16(data, i * stride + 2);
        var g1g  = BitConverter.ToUInt16(data, i * stride + 4);
        var g2g  = BitConverter.ToUInt16(data, i * stride + 12);
        Console.WriteLine($"   key={key:D3}: c25={c25}, g1gain={g1g}, g2gain={g2g}, g1+g2={g1g+g2g}");
    }

    // Cross-reference with users.scr header constants (0x13=19, 0x43=67)
    // None relate to 25. What if 25 relates to exp.scr constant 64?
    // 64 = EXP table constant, 25 = stat-point table constant
    // Most likely they're separate: 64 might be max skill level, 25 might be creation budget
    // Let's see if there's a text reference anywhere
    // items.csv has stat requirement columns... do any items require exactly 25 of a stat at L1?
}

// ─────────────────────────────────────────────────────────────────
// 3. skills.scr — stride 1504 (fixed part), variable trailing
//    Analyse: 0x30000000 @+260, 0x00003000 @+1072, 1.0 @+1176,
//             7 @+1306, 0x00010000 @+1328, chain refs @+1116..+1180
// ─────────────────────────────────────────────────────────────────
Console.WriteLine("\n══════════════════════════════════════════════");
Console.WriteLine("3. skills.scr — unverified constants and chain-ref encoding");
Console.WriteLine("══════════════════════════════════════════════");
{
    const int fixedStride = 1504;
    var data = vfs.GetFileContent("data/script/skills.scr").ToArray();
    Console.WriteLine($"   File size: {data.Length} bytes, fixed/{fixedStride} = {data.Length / fixedStride} records + {data.Length % fixedStride} remainder");

    // Walk records (simplified: treat as variable-stride, read trailing count byte at +1500)
    // Real record = plausible skill ID (<10M) and category index (<300)
    var records = new List<(int offset, uint skillId, uint catIdx, int trailingN)>();
    int pos = 0;
    int maxRecords = 1000; // guard
    while (pos + fixedStride <= data.Length && records.Count < maxRecords)
    {
        var skillId = BitConverter.ToUInt32(data, pos);
        var catIdx  = BitConverter.ToUInt32(data, pos + 4);
        // trailing count at +1500 (within the 1504-byte fixed part, it's actually the last 4 bytes)
        // spec says trailing count byte is at +1500 area; let's read offset +1500 as u32 for safety
        var trailWord = BitConverter.ToUInt32(data, pos + 1500);
        var trailByte = data[pos + 1500]; // the spec says "a trailing-count byte"
        int N = trailByte;

        bool isReal = skillId > 0 && skillId < 10_000_000 && catIdx < 300;
        if (isReal) records.Add((pos, skillId, catIdx, N));

        pos += fixedStride + N * 8;
    }
    Console.WriteLine($"   Real records found: {records.Count}");

    // Analyse the unverified constants across all real records
    var vals_260 = new HashSet<uint>();
    var vals_1072 = new HashSet<uint>();
    var vals_1176_f32 = new HashSet<float>();
    var vals_1306 = new HashSet<ushort>();
    var vals_1328 = new HashSet<uint>();

    Console.WriteLine("\n   Constant field distributions across real records:");
    foreach (var (offset, skillId, catIdx, N) in records)
    {
        vals_260.Add(BitConverter.ToUInt32(data, offset + 260));
        vals_1072.Add(BitConverter.ToUInt32(data, offset + 1072));
        vals_1176_f32.Add(BitConverter.ToSingle(data, offset + 1176));
        vals_1306.Add(BitConverter.ToUInt16(data, offset + 1306));
        vals_1328.Add(BitConverter.ToUInt32(data, offset + 1328));
    }
    Console.WriteLine($"   +260  distinct values: {string.Join(", ", vals_260)} (hex: {string.Join(", ", vals_260.Select(v => $"0x{v:X8}"))})");
    Console.WriteLine($"   +1072 distinct values: {string.Join(", ", vals_1072)} (hex: {string.Join(", ", vals_1072.Select(v => $"0x{v:X8}"))})");
    Console.WriteLine($"   +1176 f32 distinct:    {string.Join(", ", vals_1176_f32)}");
    Console.WriteLine($"   +1306 distinct u16:    {string.Join(", ", vals_1306)}");
    Console.WriteLine($"   +1328 distinct u32:    {string.Join(", ", vals_1328)} (hex: {string.Join(", ", vals_1328.Select(v => $"0x{v:X8}"))})");

    // ---- Chain reference encoding analysis ----
    Console.WriteLine("\n   Chain-ref encoding analysis (+1116..+1136, +1180):");
    // Sample a few real skills across different categories
    int shown = 0;
    foreach (var (offset, skillId, catIdx, N) in records)
    {
        if (shown >= 8) break;
        var ref0 = BitConverter.ToUInt32(data, offset + 1116);
        var ref1 = BitConverter.ToUInt32(data, offset + 1120);
        var ref2 = BitConverter.ToUInt32(data, offset + 1124);
        var ref3 = BitConverter.ToUInt32(data, offset + 1128);
        var ref4 = BitConverter.ToUInt32(data, offset + 1132);
        var ref5 = BitConverter.ToUInt32(data, offset + 1136);
        var ref6 = BitConverter.ToUInt32(data, offset + 1180);
        var ref1296 = BitConverter.ToUInt32(data, offset + 1296);
        var ref1300 = BitConverter.ToUInt32(data, offset + 1300);
        var ref1294 = BitConverter.ToUInt16(data, offset + 1294);
        var sp_cost = BitConverter.ToUInt16(data, offset + 1292);
        var motA    = BitConverter.ToUInt16(data, offset + 1304);
        var prereq  = BitConverter.ToUInt32(data, offset + 1280);
        if (ref0 == 0 && ref1 == 0 && ref5 == 0) continue; // skip fully-zero refs
        Console.WriteLine($"   skill={skillId} cat={catIdx} prereq={prereq} sp={sp_cost} motA={motA}");
        Console.WriteLine($"     refs[0..5]= {ref0} {ref1} {ref2} {ref3} {ref4} {ref5}");
        Console.WriteLine($"     ref6(+1180)={ref6}, +1294={ref1294}, +1296={ref1296}, +1300={ref1300}");
        shown++;
    }

    // ---- Decode the composite chain ref schema ----
    Console.WriteLine("\n   Composite chain-ref decode attempt:");
    Console.WriteLine("   Format hypothesis: the chain refs are decimal-digit composites.");
    Console.WriteLine("   Looking at ref[0] for each real skill...");
    var refSamples = new List<(uint skillId, uint catIdx, uint ref0)>();
    foreach (var (offset, skillId, catIdx, N) in records)
    {
        var ref0 = BitConverter.ToUInt32(data, offset + 1116);
        if (ref0 != 0) refSamples.Add((skillId, catIdx, ref0));
    }

    // Try to extract digit groups from the composite
    // Known example: 141100041 for skill 11, slot 0
    // Let's see if: leading digit = class/type (1=class-1), then skillId segment, then slot?
    Console.WriteLine($"   Non-zero ref[0] samples (first 15):");
    int sampleCount = 0;
    foreach (var (skillId, catIdx, ref0) in refSamples)
    {
        if (sampleCount >= 15) break;
        // Try decompose: assuming 9-digit number split as 1-3-5 or similar
        string s = ref0.ToString();
        Console.WriteLine($"     skill={skillId:D6} cat={catIdx:D3} ref0={ref0} ({s.PadLeft(9,'0')})");
        sampleCount++;
    }

    // Cross-check: does ref0 contain the skillId as a substring?
    Console.WriteLine("\n   Checking if skillId appears as substring in ref0:");
    int containsCount = 0;
    foreach (var (skillId, catIdx, ref0) in refSamples)
    {
        string refStr = ref0.ToString();
        string idStr = skillId.ToString();
        if (refStr.Contains(idStr)) containsCount++;
    }
    Console.WriteLine($"   {containsCount}/{refSamples.Count} ref0 values contain the skillId as substring");

    // +1306 constant 7: cross-check with motion index at +1304
    Console.WriteLine("\n   +1304 motA vs +1306 const-7 across real records:");
    var motVals = new Dictionary<ushort, int>();
    var c7Vals  = new Dictionary<ushort, int>();
    foreach (var (offset, skillId, catIdx, N) in records)
    {
        var motA = BitConverter.ToUInt16(data, offset + 1304);
        var c7   = BitConverter.ToUInt16(data, offset + 1306);
        motVals.TryAdd(motA, 0); motVals[motA]++;
        c7Vals.TryAdd(c7, 0);   c7Vals[c7]++;
    }
    Console.WriteLine($"   motA (+1304) distinct values ({motVals.Count}): {string.Join(", ", motVals.OrderBy(k=>k.Key).Select(kv => $"{kv.Key}({kv.Value})"))}");
    Console.WriteLine($"   c7   (+1306) distinct values ({c7Vals.Count}): {string.Join(", ", c7Vals.OrderBy(k=>k.Key).Select(kv => $"{kv.Key}({kv.Value})"))}");

    // +1328 = 0x00010000: cross-check; is it always this value regardless of class?
    // class flag is at +516: class 1=0x00010000, 2=0x00020000, 3=0x00030000, 4=0x00040000
    // So +1328 = 0x00010000 would be SAME as class-1 flag... but in all records including class 2/3/4?
    Console.WriteLine("\n   +516 class-flag vs +1328 (0x00010000) for non-class-1 skills:");
    int classFlagMismatch = 0;
    foreach (var (offset, skillId, catIdx, N) in records)
    {
        var classFlag = BitConverter.ToUInt32(data, offset + 516);
        var f1328     = BitConverter.ToUInt32(data, offset + 1328);
        if (classFlag != 0x00010000 && f1328 == 0x00010000)
            classFlagMismatch++;
    }
    Console.WriteLine($"   Records where classFlag != 0x00010000 but +1328 == 0x00010000: {classFlagMismatch}");
    Console.WriteLine($"   (If classFlagMismatch > 0, then +1328 is NOT a copy of the class flag)");

    // Sample +1328 per class
    var f1328ByClass = new Dictionary<uint, HashSet<uint>>();
    foreach (var (offset, skillId, catIdx, N) in records)
    {
        var classFlag = BitConverter.ToUInt32(data, offset + 516);
        var f1328     = BitConverter.ToUInt32(data, offset + 1328);
        if (!f1328ByClass.ContainsKey(classFlag)) f1328ByClass[classFlag] = new HashSet<uint>();
        f1328ByClass[classFlag].Add(f1328);
    }
    foreach (var kv in f1328ByClass.OrderBy(x=>x.Key))
        Console.WriteLine($"   classFlag=0x{kv.Key:X8} → +1328 distinct: {string.Join(", ", kv.Value.Select(v=>$"0x{v:X8}"))}");
}

// ─────────────────────────────────────────────────────────────────
// 4. mobs.scr — stride 488, 3997 records
//    +19 secondary name, f32 constants +60 (3.0), +188 (1.0),
//    variance pairs +296/+300/+304/+308
// ─────────────────────────────────────────────────────────────────
Console.WriteLine("\n══════════════════════════════════════════════");
Console.WriteLine("4. mobs.scr — secondary name and f32 constants");
Console.WriteLine("══════════════════════════════════════════════");
{
    const int stride = 488;
    var data = vfs.GetFileContent("data/script/mobs.scr").ToArray();
    int count = data.Length / stride;
    Console.WriteLine($"   File size: {data.Length}, records: {count}");

    // +19 secondary name: census distinct values (capped at 40)
    var secondaryNames = new Dictionary<string, int>();
    for (int i = 0; i < count; i++)
    {
        int nameOffset = i * stride + 19;
        int end = nameOffset;
        while (end < nameOffset + 19 && data[end] != 0) end++;
        if (end > nameOffset)
        {
            var name = cp949.GetString(data, nameOffset, end - nameOffset);
            secondaryNames.TryAdd(name, 0); secondaryNames[name]++;
        }
    }
    Console.WriteLine($"\n   +19 secondary name: {secondaryNames.Count} distinct non-empty values (of {count} records)");
    Console.WriteLine("   Top 20 most frequent:");
    foreach (var kv in secondaryNames.OrderByDescending(x => x.Value).Take(20))
        Console.WriteLine($"     '{kv.Key}' × {kv.Value}");

    // Check if secondary name correlates with mob area/zone
    // Cross-check: do mobs with the same secondary name cluster in mob-IDs?
    // (Mob IDs suggest area: 11=area1, 21=area2, etc.)
    Console.WriteLine("\n   Secondary name vs mob-ID range (first 30 records with secondary name):");
    int shown = 0;
    for (int i = 0; i < count && shown < 30; i++)
    {
        var mobId = BitConverter.ToUInt16(data, i * stride + 0);
        int nameOffset = i * stride + 19;
        int end = nameOffset;
        while (end < nameOffset + 19 && data[end] != 0) end++;
        if (end > nameOffset)
        {
            var secName = cp949.GetString(data, nameOffset, end - nameOffset);
            // Also read primary name
            int primEnd = i * stride + 2;
            while (primEnd < i * stride + 2 + 17 && data[primEnd] != 0) primEnd++;
            var primName = cp949.GetString(data, i * stride + 2, primEnd - (i * stride + 2));
            Console.WriteLine($"   mob={mobId:D5} prim='{primName}' sec='{secName}'");
            shown++;
        }
    }

    // f32 constants at +60, +188
    var f60vals = new Dictionary<float, int>();
    var f188vals = new Dictionary<float, int>();
    var f272_6vals = new Dictionary<string, int>(); // 6×f32 at +272
    var var296_vals = new Dictionary<string, int>(); // variance pair
    for (int i = 0; i < count; i++)
    {
        var f60  = BitConverter.ToSingle(data, i * stride + 60);
        var f188 = BitConverter.ToSingle(data, i * stride + 188);
        f60vals.TryAdd(f60, 0); f60vals[f60]++;
        f188vals.TryAdd(f188, 0); f188vals[f188]++;

        // variance at +296..+308
        var v296 = BitConverter.ToSingle(data, i * stride + 296);
        var v300 = BitConverter.ToSingle(data, i * stride + 300);
        var v304 = BitConverter.ToSingle(data, i * stride + 304);
        var v308 = BitConverter.ToSingle(data, i * stride + 308);
        var key = $"{v296:F4}/{v300:F4}/{v304:F4}/{v308:F4}";
        var296_vals.TryAdd(key, 0); var296_vals[key]++;
    }
    Console.WriteLine($"\n   +60 (f32) distinct: {string.Join(", ", f60vals.OrderByDescending(x=>x.Value).Select(kv=>$"{kv.Key}({kv.Value})"))}");
    Console.WriteLine($"   +188 (f32) distinct: {string.Join(", ", f188vals.OrderByDescending(x=>x.Value).Select(kv=>$"{kv.Key}({kv.Value})"))}");
    Console.WriteLine($"\n   Variance pairs +296..+308 distinct combinations:");
    foreach (var kv in var296_vals.OrderByDescending(x=>x.Value).Take(10))
        Console.WriteLine($"     {kv.Key} × {kv.Value}");

    // +272: 6×f32 "all 1.0" — verify across all records
    int allOnes272 = 0;
    for (int i = 0; i < count; i++)
    {
        bool allOne = true;
        for (int j = 0; j < 6; j++)
        {
            var v = BitConverter.ToSingle(data, i * stride + 272 + j * 4);
            if (v != 1.0f) { allOne = false; break; }
        }
        if (allOne) allOnes272++;
    }
    Console.WriteLine($"\n   +272..+295 (6×f32): all 1.0 in {allOnes272}/{count} records");

    // Cross-check: do the variance pairs correlate with mob type at +324?
    Console.WriteLine("\n   Variance pairs vs mob type (+324):");
    var typeVarMap = new Dictionary<byte, Dictionary<string, int>>();
    for (int i = 0; i < count; i++)
    {
        var mobType = data[i * stride + 324];
        var v296 = BitConverter.ToSingle(data, i * stride + 296);
        var v300 = BitConverter.ToSingle(data, i * stride + 300);
        var key = $"{v296:F4}/{v300:F4}";
        if (!typeVarMap.ContainsKey(mobType)) typeVarMap[mobType] = new Dictionary<string, int>();
        typeVarMap[mobType].TryAdd(key, 0); typeVarMap[mobType][key]++;
    }
    foreach (var kv in typeVarMap.OrderBy(x=>x.Key))
    {
        Console.WriteLine($"   mobType={kv.Key}: variance pairs {string.Join("; ", kv.Value.OrderByDescending(v=>v.Value).Take(5).Select(v=>$"{v.Key}({v.Value})"))}");
    }

    // Check +316/+320 scale factors vs mob type
    Console.WriteLine("\n   +316/+320 scale factors per mob type (+316 = size-A, +320 = size-B):");
    var scaleByType = new Dictionary<byte, List<(float sA, float sB)>>();
    for (int i = 0; i < count; i++)
    {
        var mobType = data[i * stride + 324];
        var sA = BitConverter.ToSingle(data, i * stride + 316);
        var sB = BitConverter.ToSingle(data, i * stride + 320);
        if (!scaleByType.ContainsKey(mobType)) scaleByType[mobType] = new();
        scaleByType[mobType].Add((sA, sB));
    }
    foreach (var kv in scaleByType.OrderBy(x=>x.Key))
    {
        var avA = kv.Value.Average(x => x.sA);
        var avB = kv.Value.Average(x => x.sB);
        var minA = kv.Value.Min(x => x.sA);
        var maxA = kv.Value.Max(x => x.sA);
        Console.WriteLine($"   mobType={kv.Key:D2} ({kv.Value.Count} recs): sA avg={avA:F3} [{minA:F3}..{maxA:F3}], sB avg={avB:F3}");
    }

    // +60: is 3.0 related to a speed/movement scale? Check vs boss mobs
    Console.WriteLine("\n   +60 f32 and +188 f32 for boss mobs (type=11):");
    for (int i = 0; i < Math.Min(count, 3997); i++)
    {
        if (data[i * stride + 324] == 11)
        {
            var mobId = BitConverter.ToUInt16(data, i * stride + 0);
            var f60   = BitConverter.ToSingle(data, i * stride + 60);
            var f188  = BitConverter.ToSingle(data, i * stride + 188);
            var mobLvl = BitConverter.ToInt32(data, i * stride + 244);
            Console.WriteLine($"   boss mob={mobId} lvl={mobLvl} +60={f60} +188={f188}");
            if (i > 20) break; // limit output
        }
    }
}

// ─────────────────────────────────────────────────────────────────
// 5. products.scr — stride 212, survey body fields
// ─────────────────────────────────────────────────────────────────
Console.WriteLine("\n══════════════════════════════════════════════");
Console.WriteLine("5. products.scr — body field survey (crafting recipes)");
Console.WriteLine("══════════════════════════════════════════════");
{
    const int stride = 212;
    var data = vfs.GetFileContent("data/script/products.scr").ToArray();
    int count = data.Length / stride;
    Console.WriteLine($"   File size: {data.Length}, records: {count}");

    // Known: u32 recipe id @+0, CP949 name @+4
    // Survey the 188-byte body for integer/float patterns
    Console.WriteLine("\n   First 8 records — decoded:");
    for (int i = 0; i < Math.Min(8, count); i++)
    {
        var recId = BitConverter.ToUInt32(data, i * stride + 0);
        int nameEnd = i * stride + 4;
        while (nameEnd < i * stride + 4 + 32 && data[nameEnd] != 0) nameEnd++;
        var name = cp949.GetString(data, i * stride + 4, nameEnd - (i * stride + 4));
        Console.WriteLine($"\n   Record {i}: id={recId} name='{name}'");
        // Print the body as 4-byte words
        Console.Write("   Body (u32s): ");
        for (int j = 0; j < 47; j++) // 188 bytes / 4 = 47 words
        {
            var w = BitConverter.ToUInt32(data, i * stride + 4 + j * 4); // note: body starts right after id? let's offset from 0
            // Actually body = stride - id(4) = 208 bytes remaining; but id is 4B, name goes into the body
            // Let's read the entire record as 53 words (212/4=53)
        }
        // Instead: read all 53 u32 words
        Console.WriteLine();
        Console.Write("   All 53 u32 words: ");
        var nonZeroWords = new List<string>();
        for (int j = 0; j < 53; j++)
        {
            var w = BitConverter.ToUInt32(data, i * stride + j * 4);
            if (w != 0) nonZeroWords.Add($"[{j}]={w}");
        }
        Console.WriteLine(string.Join(" ", nonZeroWords));
    }

    // Find the non-zero field positions across all records
    Console.WriteLine("\n   Non-zero field census (u32 word index → % non-zero across all records):");
    for (int wordIdx = 0; wordIdx < 53; wordIdx++)
    {
        int nonZero = 0;
        uint minV = uint.MaxValue, maxV = 0;
        var distinct = new HashSet<uint>();
        for (int i = 0; i < count; i++)
        {
            var w = BitConverter.ToUInt32(data, i * stride + wordIdx * 4);
            if (w != 0) nonZero++;
            if (w < minV) minV = w;
            if (w > maxV) maxV = w;
            if (distinct.Count < 20) distinct.Add(w);
        }
        double pct = 100.0 * nonZero / count;
        if (pct > 1.0 || nonZero > 0)
        {
            var distinctStr = distinct.Count <= 10 ? string.Join(",", distinct.OrderBy(v=>v)) : $"{distinct.Count} distinct";
            Console.WriteLine($"   word[{wordIdx:D2}] byte+{wordIdx*4:D3}: {nonZero}/{count} ({pct:F1}%) non-zero | min={minV} max={maxV} | vals=[{distinctStr}]");
        }
    }

    // Also try reading as f32 for float fields
    Console.WriteLine("\n   Float-scan (f32 in range [0.01, 1000]) on body words:");
    for (int wordIdx = 1; wordIdx < 53; wordIdx++) // skip word[0]=id
    {
        int floatLike = 0;
        for (int i = 0; i < count; i++)
        {
            var f = BitConverter.ToSingle(data, i * stride + wordIdx * 4);
            if (!float.IsNaN(f) && !float.IsInfinity(f) && f >= 0.01f && f <= 1000f && f != 0f)
                floatLike++;
        }
        if (floatLike > count / 4)
            Console.WriteLine($"   word[{wordIdx:D2}] byte+{wordIdx*4:D3}: {floatLike}/{count} plausible f32 values");
    }

    // Ingredient field hypothesis: products.scr likely stores item_id + quantity pairs
    // products.scr has 212B = 53 u32 words. id=word[0], name=4..~40B, then ingredients
    // Typical crafting: 2-6 ingredients with item_id (u32) + count (u8 or u16)
    // Let's look for item_id patterns (< 90000000 for items, or large IDs)
    Console.WriteLine("\n   Searching for item-ID-shaped values (> 1000 and < 20000000) in body words:");
    var itemIdWords = new Dictionary<int, int>(); // wordIdx -> count of item-id-shaped values
    for (int wordIdx = 1; wordIdx < 53; wordIdx++)
    {
        int hits = 0;
        for (int i = 0; i < count; i++)
        {
            var w = BitConverter.ToUInt32(data, i * stride + wordIdx * 4);
            if (w > 1000 && w < 20_000_000) hits++;
        }
        if (hits > count / 5) itemIdWords[wordIdx] = hits;
    }
    foreach (var kv in itemIdWords.OrderBy(x=>x.Key))
        Console.WriteLine($"   word[{kv.Key:D2}] byte+{kv.Key*4:D3}: {kv.Value}/{count} item-id-shaped values");

    // Show a few full records with interpretation attempt
    Console.WriteLine("\n   Detailed decode of first 3 non-zero records:");
    for (int i = 0; i < Math.Min(count, 20); i++)
    {
        var recId = BitConverter.ToUInt32(data, i * stride + 0);
        if (recId == 0) continue;
        int nameEnd = i * stride + 4;
        while (nameEnd < i * stride + 36 && data[nameEnd] != 0) nameEnd++;
        var name = cp949.GetString(data, i * stride + 4, nameEnd - (i * stride + 4));
        Console.WriteLine($"\n   rec[{i}] id={recId} name='{name}'");
        // words from offset 0
        for (int j = 0; j < 53; j++)
        {
            var w = BitConverter.ToUInt32(data, i * stride + j * 4);
            var f = BitConverter.ToSingle(data, i * stride + j * 4);
            string fstr = (!float.IsNaN(f) && !float.IsInfinity(f) && f > 0.001f && f < 1e7f) ? $" ({f:F3}f)" : "";
            if (w != 0)
                Console.WriteLine($"     [w{j:D2}@+{j*4:D3}] = {w}{fstr}");
        }
    }
}

Console.WriteLine("\n\nDone.");
