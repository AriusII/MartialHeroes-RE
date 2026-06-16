// Throwaway harness — NEVER commit, NEVER add to MartialHeroes.slnx
// V1 lane: validate stat .scr files (exp / userlevel / userpoint / users)
// All numbers little-endian, all text CP949.

using System.Text;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var cp949 = Encoding.GetEncoding(949);

var inf = args.Length > 0 ? args[0] : "C:/Users/Arius/RiderProjects/MartialHeroes/05.Presentation/MartialHeroes.Client.Godot/clientdata/data.inf";
var vfs = args.Length > 1 ? args[1] : "C:/Users/Arius/RiderProjects/MartialHeroes/05.Presentation/MartialHeroes.Client.Godot/clientdata/data/data.vfs";

Console.WriteLine($"Opening VFS: {inf}");
using var archive = MappedVfsArchive.Open(inf, vfs);
Console.WriteLine("VFS opened.\n");

// ═══════════════════════════════════════════════════════════════════════════
// exp.scr — stride 20, expected 300 records
// ═══════════════════════════════════════════════════════════════════════════
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("exp.scr  (stride=20, expected 300 records)");
Console.WriteLine("═══════════════════════════════════════════════════════════");
{
    const string path = "data/script/exp.scr";
    var raw = archive.GetFileContent(path);
    int size = raw.Length;
    int stride = 20;
    int fullRecords = size / stride;
    int residual = size % stride;
    Console.WriteLine($"  File size: {size} bytes");
    Console.WriteLine($"  size / {stride} = {fullRecords} records, residual = {residual} bytes");
    Console.WriteLine($"  Stride divides exactly: {residual == 0}");

    // Per-field distribution
    var field1Values = new Dictionary<ushort, int>(); // +0 u16 level
    var field2Values = new Dictionary<ushort, int>(); // +2 u16 constant
    var primaryExpMin = uint.MaxValue; var primaryExpMax = 0u;
    var reserved8AllZero = true;
    var secondaryMin = uint.MaxValue; var secondaryMax = 0u;
    var tertiaryMin = uint.MaxValue; var tertiaryMax = 0u;
    int secondary16NonZero = 0; int secondary16FirstNonZero = -1;
    int tertiary16NonZero = 0; int tertiary16FirstNonZero = -1;

    var span = raw.Span;
    for (int i = 0; i < fullRecords; i++)
    {
        int off = i * stride;
        ushort level = BitsU16(span, off + 0);
        ushort c2    = BitsU16(span, off + 2);
        uint   prim  = BitsU32(span, off + 4);
        uint   res   = BitsU32(span, off + 8);
        uint   sec   = BitsU32(span, off + 12);
        uint   tert  = BitsU32(span, off + 16);

        field1Values[level] = field1Values.GetValueOrDefault(level) + 1;
        field2Values[c2]    = field2Values.GetValueOrDefault(c2) + 1;
        if (prim < primaryExpMin) primaryExpMin = prim;
        if (prim > primaryExpMax) primaryExpMax = prim;
        if (res != 0) reserved8AllZero = false;
        if (sec < secondaryMin) secondaryMin = sec;
        if (sec > secondaryMax) secondaryMax = sec;
        if (tert < tertiaryMin) tertiaryMin = tert;
        if (tert > tertiaryMax) tertiaryMax = tert;
        if (sec != 0) { secondary16NonZero++; if (secondary16FirstNonZero < 0) secondary16FirstNonZero = i + 1; }
        if (tert != 0) { tertiary16NonZero++; if (tertiary16FirstNonZero < 0) tertiary16FirstNonZero = i + 1; }
    }

    Console.WriteLine($"\n  +0  u16 level:    distinct={field1Values.Count}, modal={(Modal(field1Values))} (expect sequential 1..300)");
    Console.WriteLine($"  +2  u16 constant:  distinct={field2Values.Count} values: {string.Join(", ", field2Values.Keys.OrderBy(x => x).Select(k => $"{k}(×{field2Values[k]})"))}");
    Console.WriteLine($"  +4  u32 primary:   min={primaryExpMin}, max={primaryExpMax}");
    Console.WriteLine($"  +8  u32 reserved:  allZero={reserved8AllZero}");
    Console.WriteLine($"  +12 u32 secondary: min={secondaryMin}, max={secondaryMax}, nonZeroCount={secondary16NonZero}/{fullRecords}, firstNonZeroAtLevel={secondary16FirstNonZero}");
    Console.WriteLine($"  +16 u32 tertiary:  min={tertiaryMin}, max={tertiaryMax}, nonZeroCount={tertiary16NonZero}/{fullRecords}, firstNonZeroAtLevel={tertiary16FirstNonZero}");

    // Check monotone for primary
    bool primaryMono = true;
    uint prev = 0;
    for (int i = 0; i < fullRecords; i++)
    {
        uint v = BitsU32(span, i * stride + 4);
        if (v < prev) { primaryMono = false; break; }
        prev = v;
    }
    Console.WriteLine($"  primary EXP monotone non-decreasing: {primaryMono}");

    // Check secondary monotone
    int secDecreases = 0;
    uint prevSec = 0;
    for (int i = 0; i < fullRecords; i++)
    {
        uint v = BitsU32(span, i * stride + 12);
        if (v < prevSec) secDecreases++;
        prevSec = v;
    }
    Console.WriteLine($"  secondary decreasing transitions: {secDecreases} (spec says ~29)");

    int tertDecreases = 0;
    uint prevTert = 0;
    for (int i = 0; i < fullRecords; i++)
    {
        uint v = BitsU32(span, i * stride + 16);
        if (v < prevTert) tertDecreases++;
        prevTert = v;
    }
    Console.WriteLine($"  tertiary decreasing transitions: {tertDecreases} (spec says ~1)");
}

// ═══════════════════════════════════════════════════════════════════════════
// userlevel.scr — stride 60, expected 300 records
// ═══════════════════════════════════════════════════════════════════════════
Console.WriteLine();
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("userlevel.scr  (stride=60, expected 300 records)");
Console.WriteLine("═══════════════════════════════════════════════════════════");
{
    const string path = "data/script/userlevel.scr";
    var raw = archive.GetFileContent(path);
    int size = raw.Length;
    int stride = 60;
    int fullRecords = size / stride;
    int residual = size % stride;
    Console.WriteLine($"  File size: {size} bytes");
    Console.WriteLine($"  size / {stride} = {fullRecords} records, residual = {residual} bytes");
    Console.WriteLine($"  Stride divides exactly: {residual == 0}");

    var counterAValues = new Dictionary<ushort, int>();
    var counterBValues = new Dictionary<ushort, int>();
    var divisorCValues = new Dictionary<ushort, int>();
    var pad2Values = new Dictionary<ushort, int>();
    var pad10Values = new Dictionary<ushort, int>();
    var posFloatValues = new SortedDictionary<float, int>();
    var negFloatValues = new SortedDictionary<float, int>();
    var resFloatValues = new SortedDictionary<float, int>();

    var span = raw.Span;
    for (int i = 0; i < fullRecords; i++)
    {
        int off = i * stride;
        // +0 u16 level, +2 u16 pad
        ushort pad2 = BitsU16(span, off + 2);
        ushort cA   = BitsU16(span, off + 4);
        ushort cB   = BitsU16(span, off + 6);
        ushort cC   = BitsU16(span, off + 8);
        ushort pad10= BitsU16(span, off + 10);
        // 4 positive floats at +12..+27
        for (int f = 0; f < 4; f++)
        {
            float v = BitsF32(span, off + 12 + f * 4);
            posFloatValues[v] = posFloatValues.GetValueOrDefault(v) + 1;
        }
        // 4 negative floats at +28..+43
        for (int f = 0; f < 4; f++)
        {
            float v = BitsF32(span, off + 28 + f * 4);
            negFloatValues[v] = negFloatValues.GetValueOrDefault(v) + 1;
        }
        // 4 reserved floats at +44..+59
        for (int f = 0; f < 4; f++)
        {
            float v = BitsF32(span, off + 44 + f * 4);
            resFloatValues[v] = resFloatValues.GetValueOrDefault(v) + 1;
        }
        pad2Values[pad2] = pad2Values.GetValueOrDefault(pad2) + 1;
        counterAValues[cA] = counterAValues.GetValueOrDefault(cA) + 1;
        counterBValues[cB] = counterBValues.GetValueOrDefault(cB) + 1;
        divisorCValues[cC] = divisorCValues.GetValueOrDefault(cC) + 1;
        pad10Values[pad10] = pad10Values.GetValueOrDefault(pad10) + 1;
    }

    Console.WriteLine($"\n  +2  u16 pad:       distinct={pad2Values.Count} values: {string.Join(", ", pad2Values.Keys.OrderBy(x=>x).Select(k=>$"{k}(×{pad2Values[k]})"))}");
    Console.WriteLine($"  +4  u16 counter A: distinct={counterAValues.Count} values: {string.Join(", ", counterAValues.Keys.OrderBy(x=>x).Select(k=>$"{k}(×{counterAValues[k]})"))}");
    Console.WriteLine($"  +6  u16 counter B: distinct={counterBValues.Count} values: {string.Join(", ", counterBValues.Keys.OrderBy(x=>x).Select(k=>$"{k}(×{counterBValues[k]})"))}");
    Console.WriteLine($"  +8  u16 divisor C: distinct={divisorCValues.Count} values: {string.Join(", ", divisorCValues.Keys.OrderBy(x=>x).Select(k=>$"{k}(×{divisorCValues[k]})"))}");
    Console.WriteLine($"  +10 u16 pad:       distinct={pad10Values.Count} values: {string.Join(", ", pad10Values.Keys.OrderBy(x=>x).Select(k=>$"{k}(×{pad10Values[k]})"))}");
    Console.WriteLine($"  +12 positive floats: distinct={posFloatValues.Count} values: {string.Join(", ", posFloatValues.Keys.Select(k=>$"{k}(×{posFloatValues[k]})"))}");
    Console.WriteLine($"  +28 negative floats: distinct={negFloatValues.Count} values: {string.Join(", ", negFloatValues.Keys.Select(k=>$"{k}(×{negFloatValues[k]})"))}");
    Console.WriteLine($"  +44 reserved floats: distinct={resFloatValues.Count} values: {string.Join(", ", resFloatValues.Keys.Select(k=>$"{k}(×{resFloatValues[k]})"))}");

    // Mirror check: A == B?
    bool abMirror = true;
    for (int i = 0; i < fullRecords; i++)
    {
        int off = i * stride;
        if (BitsU16(span, off + 4) != BitsU16(span, off + 6)) { abMirror = false; break; }
    }
    Console.WriteLine($"  counter A == counter B in all records: {abMirror} (spec says mirrors)");

    // Check all 4 positive floats identical per record
    bool posGroupUniform = true;
    for (int i = 0; i < fullRecords; i++)
    {
        int off = i * stride;
        float f0 = BitsF32(span, off + 12);
        for (int f = 1; f < 4; f++)
            if (BitsF32(span, off + 12 + f * 4) != f0) { posGroupUniform = false; break; }
        if (!posGroupUniform) break;
    }
    Console.WriteLine($"  all 4 positive floats identical within each record: {posGroupUniform}");

    bool negGroupUniform = true;
    for (int i = 0; i < fullRecords; i++)
    {
        int off = i * stride;
        float f0 = BitsF32(span, off + 28);
        for (int f = 1; f < 4; f++)
            if (BitsF32(span, off + 28 + f * 4) != f0) { negGroupUniform = false; break; }
        if (!negGroupUniform) break;
    }
    Console.WriteLine($"  all 4 negative floats identical within each record: {negGroupUniform}");
}

// ═══════════════════════════════════════════════════════════════════════════
// userpoint.scr — stride 32, expected 301 records
// ═══════════════════════════════════════════════════════════════════════════
Console.WriteLine();
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("userpoint.scr  (stride=32, expected 301 records)");
Console.WriteLine("═══════════════════════════════════════════════════════════");
{
    const string path = "data/script/userpoint.scr";
    var raw = archive.GetFileContent(path);
    int size = raw.Length;
    int stride = 32;
    int fullRecords = size / stride;
    int residual = size % stride;
    Console.WriteLine($"  File size: {size} bytes");
    Console.WriteLine($"  size / {stride} = {fullRecords} records, residual = {residual} bytes");
    Console.WriteLine($"  Stride divides exactly: {residual == 0}");

    var const2Values = new Dictionary<ushort, int>();   // +2
    var gain1Values  = new Dictionary<ushort, int>();   // +4
    var pad6Values   = new Dictionary<ushort, int>();   // +6
    var gain2Values  = new Dictionary<ushort, int>();   // +12
    var pad14Values  = new Dictionary<ushort, int>();   // +14
    var secLowValues = new Dictionary<ushort, int>();   // +20
    var secHiValues  = new Dictionary<ushort, int>();   // +22
    var tert1Min = uint.MaxValue; var tert1Max = 0u;
    var tert2Min = uint.MaxValue; var tert2Max = 0u;
    int tert1NonZero = 0; int tert1FirstNonZero = -1;
    bool cumul1Correct = true;
    bool cumul2Correct = true;

    var span = raw.Span;
    uint runSum1 = 0, runSum2 = 0;
    for (int i = 0; i < fullRecords; i++)
    {
        int off = i * stride;
        ushort idx     = BitsU16(span, off + 0);
        ushort c2      = BitsU16(span, off + 2);
        ushort g1      = BitsU16(span, off + 4);
        ushort pad6    = BitsU16(span, off + 6);
        uint   cum1    = BitsU32(span, off + 8);
        ushort g2      = BitsU16(span, off + 12);
        ushort pad14   = BitsU16(span, off + 14);
        uint   cum2    = BitsU32(span, off + 16);
        ushort secLow  = BitsU16(span, off + 20);
        ushort secHi   = BitsU16(span, off + 22);
        uint   tert1   = BitsU32(span, off + 24);
        uint   tert2   = BitsU32(span, off + 28);

        runSum1 += g1;
        runSum2 += g2;
        if (cum1 != runSum1) cumul1Correct = false;
        if (cum2 != runSum2) cumul2Correct = false;

        const2Values[c2]   = const2Values.GetValueOrDefault(c2) + 1;
        gain1Values[g1]    = gain1Values.GetValueOrDefault(g1) + 1;
        pad6Values[pad6]   = pad6Values.GetValueOrDefault(pad6) + 1;
        gain2Values[g2]    = gain2Values.GetValueOrDefault(g2) + 1;
        pad14Values[pad14] = pad14Values.GetValueOrDefault(pad14) + 1;
        secLowValues[secLow] = secLowValues.GetValueOrDefault(secLow) + 1;
        secHiValues[secHi]   = secHiValues.GetValueOrDefault(secHi) + 1;
        if (tert1 < tert1Min) tert1Min = tert1;
        if (tert1 > tert1Max) tert1Max = tert1;
        if (tert2 < tert2Min) tert2Min = tert2;
        if (tert2 > tert2Max) tert2Max = tert2;
        if (tert1 != 0) { tert1NonZero++; if (tert1FirstNonZero < 0) tert1FirstNonZero = (int)idx; }
    }

    Console.WriteLine($"\n  +2  u16 constant:  distinct={const2Values.Count} values: {string.Join(", ", const2Values.Keys.OrderBy(x=>x).Select(k=>$"{k}(×{const2Values[k]})"))}");
    Console.WriteLine($"  +4  u16 gain1:     distinct={gain1Values.Count} values: {string.Join(", ", gain1Values.Keys.OrderBy(x=>x).Select(k=>$"{k}(×{gain1Values[k]})"))}");
    Console.WriteLine($"  +6  u16 pad:       distinct={pad6Values.Count} values: {string.Join(", ", pad6Values.Keys.OrderBy(x=>x).Select(k=>$"{k}(×{pad6Values[k]})"))}");
    Console.WriteLine($"  +8  u32 cumul1:    running-sum matches gain1 column: {cumul1Correct}");
    Console.WriteLine($"  +12 u16 gain2:     distinct={gain2Values.Count} values: {string.Join(", ", gain2Values.Keys.OrderBy(x=>x).Select(k=>$"{k}(×{gain2Values[k]})"))}");
    Console.WriteLine($"  +14 u16 pad:       distinct={pad14Values.Count} values: {string.Join(", ", pad14Values.Keys.OrderBy(x=>x).Select(k=>$"{k}(×{pad14Values[k]})"))}");
    Console.WriteLine($"  +16 u32 cumul2:    running-sum matches gain2 column: {cumul2Correct}");
    Console.WriteLine($"  +20 u16 secLow:    distinct={secLowValues.Count} values, min={secLowValues.Keys.Min()}, max={secLowValues.Keys.Max()}");
    Console.WriteLine($"  +22 u16 secHi:     distinct={secHiValues.Count} values, min={secHiValues.Keys.Min()}, max={secHiValues.Keys.Max()}");
    Console.WriteLine($"  +24 u32 tert1:     min={tert1Min}, max={tert1Max}, nonZero={tert1NonZero}/{fullRecords}, firstNonZeroIdx={tert1FirstNonZero}");
    Console.WriteLine($"  +28 u32 tert2:     min={tert2Min}, max={tert2Max}");

    // Check tert1 == tert2
    bool tertEqual = true;
    for (int i = 0; i < fullRecords; i++)
    {
        int off = i * stride;
        if (BitsU32(span, off + 24) != BitsU32(span, off + 28)) { tertEqual = false; break; }
    }
    Console.WriteLine($"  tert1 == tert2 in all records: {tertEqual}");

    // Show last 10 records gain1 values
    Console.Write("  gain1 last 10 records (keys 291..300): ");
    for (int i = Math.Max(0, fullRecords - 10); i < fullRecords; i++)
    {
        int off = i * stride;
        ushort idx = BitsU16(span, off);
        ushort g1  = BitsU16(span, off + 4);
        Console.Write($"{idx}:{g1} ");
    }
    Console.WriteLine();
}

// ═══════════════════════════════════════════════════════════════════════════
// users.scr — 4 × 124-byte blocks
// ═══════════════════════════════════════════════════════════════════════════
Console.WriteLine();
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("users.scr  (4 × 124-byte blocks = 496 bytes)");
Console.WriteLine("═══════════════════════════════════════════════════════════");
{
    const string path = "data/script/users.scr";
    var raw = archive.GetFileContent(path);
    int size = raw.Length;
    Console.WriteLine($"  File size: {size} bytes (expected 496)");
    Console.WriteLine($"  size % 124 = {size % 124}");
    Console.WriteLine($"  Stride 124 divides exactly: {size % 124 == 0}");

    var span = raw.Span;
    int blockCount = size / 124;
    for (int b = 0; b < blockCount; b++)
    {
        int off = b * 124;
        byte classId = span[off + 0];
        byte hdr1    = span[off + 1];
        byte hdr2    = span[off + 2];
        byte pad3    = span[off + 3];
        Console.WriteLine($"\n  Block {b}: classId={classId}, hdr[1]=0x{hdr1:X2}({hdr1}), hdr[2]=0x{hdr2:X2}({hdr2}), pad[3]={pad3}");

        // Triplet A at +4 (3×f32)
        float tA0 = BitsF32(span, off + 4);
        float tA1 = BitsF32(span, off + 8);
        float tA2 = BitsF32(span, off + 12);
        Console.WriteLine($"    +4  triplet A (3×f32): ({tA0}, {tA1}, {tA2})");

        // Zero group at +16 (5×f32)
        bool zeroGroup16 = true;
        for (int f = 0; f < 5; f++) if (BitsF32(span, off + 16 + f * 4) != 0.0f) zeroGroup16 = false;
        Console.WriteLine($"    +16 zero group (5×f32): allZero={zeroGroup16}");

        // Ratios at +36 (3×f32)
        float r0 = BitsF32(span, off + 36);
        float r1 = BitsF32(span, off + 40);
        float r2 = BitsF32(span, off + 44);
        Console.WriteLine($"    +36 ratio triplet 1 (3×f32): ({r0}, {r1}, {r2})");

        // Second triplet at +48
        float s0 = BitsF32(span, off + 48);
        float s1 = BitsF32(span, off + 52);
        float s2 = BitsF32(span, off + 56);
        Console.WriteLine($"    +48 ratio triplet 2 (3×f32): ({s0}, {s1}, {s2})");

        // Third triplet at +60
        float t0 = BitsF32(span, off + 60);
        float t1 = BitsF32(span, off + 64);
        float t2 = BitsF32(span, off + 68);
        Console.WriteLine($"    +60 ratio triplet 3 (3×f32): ({t0}, {t1}, {t2})");

        // Zero group at +72 (5×f32)
        bool zeroGroup72 = true;
        for (int f = 0; f < 5; f++) if (BitsF32(span, off + 72 + f * 4) != 0.0f) zeroGroup72 = false;
        Console.WriteLine($"    +72 zero group (5×f32): allZero={zeroGroup72}");

        // Class-specific multiplier group at +92 (8×f32)
        Console.Write($"    +92 class-specific multipliers (8×f32): ");
        for (int f = 0; f < 8; f++)
            Console.Write($"[{f}]={BitsF32(span, off + 92 + f * 4):G4} ");
        Console.WriteLine();
    }
}

Console.WriteLine("\nDone.");

// ═══════════════════════════════════════════════════════════════════════════
// Helpers
// ═══════════════════════════════════════════════════════════════════════════
static ushort BitsU16(ReadOnlySpan<byte> s, int off) => (ushort)(s[off] | (s[off+1] << 8));
static uint BitsU32(ReadOnlySpan<byte> s, int off) => (uint)(s[off] | (s[off+1] << 8) | (s[off+2] << 16) | (s[off+3] << 24));
static float BitsF32(ReadOnlySpan<byte> s, int off)
{
    uint bits = BitsU32(s, off);
    return System.Runtime.CompilerServices.Unsafe.As<uint, float>(ref bits);
}
static T Modal<T>(Dictionary<T, int> d) where T : notnull => d.OrderByDescending(kv => kv.Value).First().Key;
