// xeff-val — V4 full-corpus validator for .xeff and .xobj files
// Drives the PRODUCTION XeffParser against every .xeff in the VFS.
// Runs all .xobj files through a text-based parser.
// THROWAWAY — never add to MartialHeroes.slnx, never commit, metadata only.

using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Encoding cp949 = Encoding.GetEncoding(949);

string infPath = "C:/Users/Arius/RiderProjects/MartialHeroes/05.Presentation/MartialHeroes.Client.Godot/clientdata/data.inf";
string vfsPath = "C:/Users/Arius/RiderProjects/MartialHeroes/05.Presentation/MartialHeroes.Client.Godot/clientdata/data/data.vfs";

bool probeMode    = args.Length > 0 && args[0] == "probe";
bool manualMode   = args.Length > 0 && args[0] == "manual"; // run ComputeParsedBytes on all files
string? probePath = probeMode && args.Length > 1 ? args[1] : null;

Console.WriteLine($"xeff-val: mounting VFS from {infPath}");
using MappedVfsArchive archive = MappedVfsArchive.Open(infPath, vfsPath);
Console.WriteLine($"xeff-val: mounted {archive.GetEntries().Length:N0} entries");

if (manualMode)
{
    Console.WriteLine("\n=== MANUAL PARSE: all .xeff via ComputeParsedBytes (9-static/13-animated hybrid) ===\n");
    var xeffAllEntries = new List<VfsEntry>();
    foreach (var e in archive.GetEntries())
        if (e.Name.EndsWith(".xeff", StringComparison.Ordinal))
            xeffAllEntries.Add(e);
    Console.WriteLine($"Total .xeff: {xeffAllEntries.Count}");
    int mClean = 0, mResidual = 0, mError = 0;
    var mErrors = new List<(string, string)>();
    var mResiduals = new List<(string, int)>();
    var staticAnimLoopDist = new Dictionary<string, int>(); // track size used
    foreach (var e in xeffAllEntries)
    {
        var raw = archive.GetFileContent(e.Name);
        try
        {
            int consumed = ComputeParsedBytes(raw.Span);
            int leftover = raw.Length - consumed;
            if (leftover == 0) mClean++;
            else { mResidual++; mResiduals.Add((e.Name, leftover)); }
        }
        catch (Exception ex)
        {
            mError++;
            mErrors.Add((e.Name, ex.Message.Length > 120 ? ex.Message[..120] : ex.Message));
        }
    }
    Console.WriteLine($"  clean (zero residual): {mClean}");
    Console.WriteLine($"  non-zero residual:     {mResidual}");
    Console.WriteLine($"  errors:                {mError}");
    if (mResiduals.Count > 0) {
        Console.WriteLine($"\n  RESIDUALS ({mResiduals.Count}):");
        foreach (var (p, r) in mResiduals.Take(20)) Console.WriteLine($"    {p}: +{r}B");
    }
    if (mErrors.Count > 0) {
        Console.WriteLine($"\n  ERRORS ({mErrors.Count}):");
        foreach (var (p, msg) in mErrors.Take(20)) Console.WriteLine($"    {p}: {msg}");
    }

    // Now collect full field stats with the hybrid parser
    Console.WriteLine("\n=== FULL FIELD STATS (hybrid manual parser, all 3584 files) ===\n");
    var allEmitterType = new Dictionary<uint, int>();
    var allUnkConst    = new Dictionary<uint, int>();  // only animated
    var allFieldUnkA   = new Dictionary<uint, int>();
    var allDword2      = new Dictionary<uint, int>();
    var allAnimLoop    = new Dictionary<byte, int>();
    var allAnimBaseA   = new Dictionary<uint, int>();  // static base
    var allAnimBaseN   = new Dictionary<uint, int>();  // animated base
    var allAnimStrideS = new Dictionary<uint, int>(); // static stride (was unknown_const pos)
    var allAnimStrideA = new Dictionary<uint, int>(); // animated stride
    var allTexCount    = new Dictionary<string, int>();
    var allSubEffCount = new Dictionary<string, int>();
    int resBelow = 0, resAbove = 0;
    uint resMin = uint.MaxValue, resMax = 0;
    int totalSE = 0, staticSE = 0, animSE = 0;

    foreach (var e in xeffAllEntries)
    {
        var raw = archive.GetFileContent(e.Name);
        if (raw.Length < 8) continue;
        uint subCnt = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[4..]);
        string secBkt = subCnt switch { 0=>"0(stub)", 1=>"1", <=5=>"2-5", <=10=>"6-10", <=20=>"11-20", <=68=>"21-68", _=>"69+" };
        allSubEffCount.TryGetValue(secBkt, out int sv); allSubEffCount[secBkt] = sv + 1;

        int off = 8;
        for (int s = 0; s < (int)subCnt; s++)
        {
            if (off + 24 > raw.Length) break;
            uint et  = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[off..]);
            uint res = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[(off+4)..]);
            uint uka = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[(off+12)..]);
            uint dw2 = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[(off+16)..]);
            uint tex = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[(off+20)..]);
            off += 24;
            if (tex > 100) break; // sanity
            totalSE++;

            allEmitterType.TryGetValue(et, out int ec); allEmitterType[et] = ec + 1;
            allFieldUnkA.TryGetValue(uka, out int fa); allFieldUnkA[uka] = fa + 1;
            allDword2.TryGetValue(dw2, out int dd); allDword2[dw2] = dd + 1;
            if (res < resMin) resMin = res; if (res > resMax) resMax = res;
            if (res < 10000) resBelow++; else resAbove++;

            string texBkt = tex switch { 0=>"0", 1=>"1", <=5=>"2-5", <=10=>"6-10", <=20=>"11-20", <=41=>"21-41", _=>"42+" };
            allTexCount.TryGetValue(texBkt, out int tb); allTexCount[texBkt] = tb + 1;

            // skip name table
            off += (int)tex * 64;
            // skip 4 curves
            bool curveOk = true;
            for (int p = 0; p < 4; p++) {
                if (off + 4 > raw.Length) { curveOk = false; break; }
                uint cnt = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[off..]); off += 4;
                if (cnt > 10000) { curveOk = false; break; }
                off += (int)cnt * 4;
            }
            if (!curveOk) break;

            if (off + 1 > raw.Length) break;
            byte al = raw.Span[off];
            allAnimLoop.TryGetValue(al, out int alc); allAnimLoop[al] = alc + 1;

            if (al != 0) // animated: 13 bytes
            {
                animSE++;
                if (off + 13 > raw.Length) break;
                uint unkC  = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[(off+1)..]);
                uint strd  = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[(off+5)..]);
                uint base_ = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[(off+9)..]);
                allUnkConst.TryGetValue(unkC, out int uc); allUnkConst[unkC] = uc + 1;
                allAnimStrideA.TryGetValue(strd, out int sa); allAnimStrideA[strd] = sa + 1;
                allAnimBaseN.TryGetValue(base_, out int bn); allAnimBaseN[base_] = bn + 1;
                off += 13;
                for (int k = 0; k < (int)tex; k++) { int ksz = k == 0 ? 36 : 40; if (off + ksz > raw.Length) break; off += ksz; }
            }
            else // static: 9 bytes
            {
                staticSE++;
                if (off + 9 > raw.Length) break;
                uint strd  = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[(off+1)..]);
                uint base_ = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[(off+5)..]);
                allAnimStrideS.TryGetValue(strd, out int ss); allAnimStrideS[strd] = ss + 1;
                allAnimBaseA.TryGetValue(base_, out int ba); allAnimBaseA[base_] = ba + 1;
                off += 9;
                int sz = et == 2 ? 36 : 24;
                off += sz;
            }
        }
    }

    Console.WriteLine($"Total sub-effects (sanity-checked): {totalSE}  (animated={animSE} static={staticSE})");

    Console.WriteLine("\n--- emitter_type (all sub-effects) ---");
    foreach (var kv in allEmitterType.OrderBy(k => k.Key))
        Console.WriteLine($"  {kv.Key}={kv.Key switch{0=>"billboard",1=>"mesh",2=>"directional",_=>"UNKNOWN"}}: {kv.Value,7:N0}");

    Console.WriteLine("\n--- anim_loop distribution ---");
    foreach (var kv in allAnimLoop.OrderBy(k => k.Key))
        Console.WriteLine($"  anim_loop={kv.Key}: {kv.Value,7:N0}");

    Console.WriteLine("\n--- unknown_constant (animated track +1..+4, expected 67) ---");
    foreach (var kv in allUnkConst.OrderByDescending(k => k.Value).Take(10))
        Console.WriteLine($"  {kv.Key,5} (0x{kv.Key:X4}): {kv.Value,7:N0}  {(kv.Key==67?"<-- 67":"")}");

    Console.WriteLine("\n--- anim_stride ANIMATED top-20 ---");
    foreach (var kv in allAnimStrideA.OrderByDescending(k => k.Value).Take(20))
        Console.WriteLine($"  {kv.Key,8} ms: {kv.Value,7:N0}");

    Console.WriteLine("\n--- anim_stride STATIC (at track+1..+4, now confirmed stride) top-10 ---");
    foreach (var kv in allAnimStrideS.OrderByDescending(k => k.Value).Take(10))
        Console.WriteLine($"  {kv.Key,8} ms: {kv.Value,7:N0}");

    Console.WriteLine("\n--- anim_base_time ANIMATED (at track+9..+12) ---");
    int abN0 = allAnimBaseN.TryGetValue(0, out int abnz) ? abnz : 0;
    Console.WriteLine($"  zero-fraction: {abN0}/{animSE} ({(double)abN0/animSE*100:F1}%)  distinct={allAnimBaseN.Count}");

    Console.WriteLine("\n--- anim_base_time STATIC (at track+5..+8) ---");
    int abS0 = allAnimBaseA.TryGetValue(0, out int absz) ? absz : 0;
    Console.WriteLine($"  zero-fraction: {abS0}/{staticSE} ({(double)abS0/staticSE*100:F1}%)  distinct={allAnimBaseA.Count}");

    Console.WriteLine("\n--- field_unknown_a ---");
    int faT = allFieldUnkA.Values.Sum();
    int faZ = allFieldUnkA.TryGetValue(0, out int fazv) ? fazv : 0;
    Console.WriteLine($"  zero-fraction: {faZ}/{faT} ({(double)faZ/faT*100:F1}%)  distinct={allFieldUnkA.Count}");
    foreach (var kv in allFieldUnkA.Where(k=>k.Key!=0).OrderByDescending(k=>k.Value).Take(5))
        Console.WriteLine($"  0x{kv.Key:X8}: {kv.Value}");

    Console.WriteLine("\n--- element_dword2 ---");
    int edT = allDword2.Values.Sum();
    int edZ = allDword2.TryGetValue(0, out int edzv) ? edzv : 0;
    Console.WriteLine($"  zero-fraction: {edZ}/{edT} ({(double)edZ/edT*100:F1}%)  distinct={allDword2.Count}");
    foreach (var kv in allDword2.Where(k=>k.Key!=0).OrderByDescending(k=>k.Value).Take(5))
        Console.WriteLine($"  0x{kv.Key:X8}: {kv.Value}");

    Console.WriteLine("\n--- resource_id ---");
    Console.WriteLine($"  <10000: {resBelow}  >=10000: {resAbove}  min={resMin}  max={resMax}");

    Console.WriteLine("\n--- tex_count ---");
    foreach (var kv in allTexCount.OrderBy(k=>k.Key)) Console.WriteLine($"  {kv.Key,-8}: {kv.Value,7}");

    Console.WriteLine("\n--- sub_effect_count per file ---");
    foreach (var kv in allSubEffCount.OrderBy(k=>k.Key)) Console.WriteLine($"  {kv.Key,-10}: {kv.Value,7}");

    return 0;
}

if (probeMode)
{
    // Probe specific file or first 5 errors
    string[] targets = probePath != null ? new[] { probePath } :
        new[] {
            "data/effect/xeff/331310721.xeff",
            "data/effect/xeff/331410721.xeff",
            "data/effect/xeff/332210721.xeff",
            "data/effect/xeff/333130721.xeff",
            "data/effect/xeff/311100016.xeff",
        };
    foreach (string t in targets)
    {
        var raw = archive.GetFileContent(t);
        ProbeFile(t, raw.Span);
    }
    return 0;
}

// ============================================================================
// SECTION 1: .xeff full-corpus parse via production XeffParser
// ============================================================================
Console.WriteLine("\n=== SECTION 1: .xeff full-corpus parse (production XeffParser) ===\n");

var xeffList = new List<VfsEntry>();
foreach (var e in archive.GetEntries())
    if (e.Name.EndsWith(".xeff", StringComparison.Ordinal))
        xeffList.Add(e);

Console.WriteLine($"Total .xeff entries: {xeffList.Count:N0}");

int xeffClean = 0, xeffError = 0, xeffResidual = 0;
int xeffStub = 0;
var parseErrors = new List<(string path, string err)>();
var residuals   = new List<(string path, int residualBytes)>();

// Field distributions across ALL sub-effects
var emitterTypeCounts   = new Dictionary<uint, int>();
var unknownConstCounts  = new Dictionary<uint, int>();
var fieldUnknownACounts = new Dictionary<uint, int>();
var elementDword2Counts = new Dictionary<uint, int>();
var animBaseCounts      = new Dictionary<uint, int>(); // anim_base_time

// Per-file sub_effect_count distribution
var subEffCountBuckets = new Dictionary<string, int>();

// anim_loop distribution
var animLoopCounts = new Dictionary<byte, int>();

// resource_id stats
int resourceBelow = 0, resourceAbove = 0;
uint resourceMin = uint.MaxValue, resourceMax = 0;

// tex_count distribution
var texCountBuckets = new Dictionary<string, int>();

// anim_stride top values
var animStrideCounts = new Dictionary<uint, int>();

// curve counts
long totalAlphaKeyCount = 0;
int alphaZeroCount = 0;
int scaleXZero = 0, scaleYZero = 0, scaleZZero = 0;
int scaleXNonZ = 0, scaleYNonZ = 0, scaleZNonZ = 0;

int totalSubEffects = 0;

foreach (var e in xeffList)
{
    ReadOnlyMemory<byte> raw = archive.GetFileContent(e.Name);
    if (raw.Length < 8) { xeffError++; parseErrors.Add((e.Name, $"too short: {raw.Length}B")); continue; }

    uint subCount = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[4..]);
    if (subCount == 0) xeffStub++;

    string secBkt = subCount switch {
        0 => "0 (stub)",
        1 => "1",
        <= 5 => "2-5",
        <= 10 => "6-10",
        <= 20 => "11-20",
        <= 68 => "21-68",
        _ => "69+",
    };
    subEffCountBuckets.TryGetValue(secBkt, out int bv); subEffCountBuckets[secBkt] = bv + 1;

    try
    {
        var data = XeffParser.ParseXeff(raw);

        // Compute bytes consumed and check residual
        int parsedBytes = ComputeParsedBytes(raw.Span);
        int leftover = raw.Length - parsedBytes;
        if (leftover != 0) { xeffResidual++; residuals.Add((e.Name, leftover)); }
        else xeffClean++;

        totalSubEffects += (int)data.SubEffectCount;
        foreach (var se in data.SubEffects)
        {
            emitterTypeCounts.TryGetValue(se.EmitterType, out int ec); emitterTypeCounts[se.EmitterType] = ec + 1;
            unknownConstCounts.TryGetValue(se.UnknownConstant, out int uc); unknownConstCounts[se.UnknownConstant] = uc + 1;
            fieldUnknownACounts.TryGetValue(se.FieldUnknownA, out int fa); fieldUnknownACounts[se.FieldUnknownA] = fa + 1;
            elementDword2Counts.TryGetValue(se.ElementDword2, out int ed); elementDword2Counts[se.ElementDword2] = ed + 1;
            animBaseCounts.TryGetValue(se.AnimBaseTime, out int ab); animBaseCounts[se.AnimBaseTime] = ab + 1;
            animLoopCounts.TryGetValue(se.AnimLoop, out int al); animLoopCounts[se.AnimLoop] = al + 1;

            if (se.ResourceId < 10000) resourceBelow++;
            else resourceAbove++;
            if (se.ResourceId < resourceMin) resourceMin = se.ResourceId;
            if (se.ResourceId > resourceMax) resourceMax = se.ResourceId;

            string texBkt = se.EntryCount switch {
                0 => "0",
                1 => "1",
                <= 5 => "2-5",
                <= 10 => "6-10",
                <= 20 => "11-20",
                <= 41 => "21-41",
                _ => "42+",
            };
            texCountBuckets.TryGetValue(texBkt, out int tb); texCountBuckets[texBkt] = tb + 1;

            animStrideCounts.TryGetValue(se.AnimStride, out int as2); animStrideCounts[se.AnimStride] = as2 + 1;

            totalAlphaKeyCount += se.AlphaKeys.Length;
            if (se.AlphaKeys.Length == 0) alphaZeroCount++;
            if (se.ScaleX.Length == 0) scaleXZero++; else scaleXNonZ++;
            if (se.ScaleY.Length == 0) scaleYZero++; else scaleYNonZ++;
            if (se.ScaleZ.Length == 0) scaleZZero++; else scaleZNonZ++;
        }
    }
    catch (Exception ex)
    {
        xeffError++;
        parseErrors.Add((e.Name, ex.Message.Length > 150 ? ex.Message[..150] : ex.Message));
    }
}

Console.WriteLine($"  parse-clean (zero residual):   {xeffClean:N0}");
Console.WriteLine($"  residual bytes (non-zero):     {xeffResidual:N0}");
Console.WriteLine($"  parse errors (exception):      {xeffError:N0}");
Console.WriteLine($"  stub files (sub_count=0):      {xeffStub:N0}");
Console.WriteLine($"  total sub-effects processed:   {totalSubEffects:N0}");

if (parseErrors.Count > 0)
{
    Console.WriteLine($"\n  ERROR SAMPLE ({Math.Min(parseErrors.Count, 30)} of {parseErrors.Count}):");
    foreach (var (p, msg) in parseErrors.Take(30))
        Console.WriteLine($"    {p}: {msg}");
}

if (residuals.Count > 0)
{
    Console.WriteLine($"\n  RESIDUALS ({residuals.Count}):");
    foreach (var (p, r) in residuals.Take(20))
        Console.WriteLine($"    {p}: +{r}B residual");
}

Console.WriteLine("\n--- sub_effect_count distribution (per file) ---");
PrintSorted(subEffCountBuckets);

Console.WriteLine("\n--- emitter_type distribution (per sub-effect) ---");
foreach (var kv in emitterTypeCounts.OrderBy(k => k.Key))
{
    string label = kv.Key switch { 0 => "0=billboard", 1 => "1=mesh-particle", 2 => "2=directional", _ => $"{kv.Key}=UNKNOWN" };
    Console.WriteLine($"  {label,-25}: {kv.Value,7:N0}");
}

Console.WriteLine("\n--- unknown_constant (track header +1, expected=67) ---");
foreach (var kv in unknownConstCounts.OrderByDescending(k => k.Value).Take(10))
    Console.WriteLine($"  value={kv.Key,5} (0x{kv.Key:X4}): {kv.Value,7:N0}  {(kv.Key == 67 ? "<-- 67 (XEFF_TRACK_UNKNOWN_CONSTANT)" : "")}");

Console.WriteLine("\n--- field_unknown_a (element_flags @ element+0x0C) ---");
int faTotal = fieldUnknownACounts.Values.Sum();
int faZero  = fieldUnknownACounts.TryGetValue(0, out int faz) ? faz : 0;
Console.WriteLine($"  zero-fraction: {faZero:N0}/{faTotal:N0} ({(double)faZero/faTotal*100:F1}%)");
Console.WriteLine($"  distinct values: {fieldUnknownACounts.Count:N0}");
Console.WriteLine($"  top-15 non-zero:");
foreach (var kv in fieldUnknownACounts.Where(k => k.Key != 0).OrderByDescending(k => k.Value).Take(15))
    Console.WriteLine($"    0x{kv.Key:X8}: {kv.Value,7:N0}");

Console.WriteLine("\n--- element_dword2 (@ element+0x10) ---");
int edTotal = elementDword2Counts.Values.Sum();
int edZero  = elementDword2Counts.TryGetValue(0, out int edz) ? edz : 0;
Console.WriteLine($"  zero-fraction: {edZero:N0}/{edTotal:N0} ({(double)edZero/edTotal*100:F1}%)");
Console.WriteLine($"  distinct values: {elementDword2Counts.Count:N0}");
Console.WriteLine($"  top-15 non-zero:");
foreach (var kv in elementDword2Counts.Where(k => k.Key != 0).OrderByDescending(k => k.Value).Take(15))
    Console.WriteLine($"    0x{kv.Key:X8}: {kv.Value,7:N0}");

Console.WriteLine("\n--- anim_base_time distribution ---");
int abTotal = animBaseCounts.Values.Sum();
int abZero  = animBaseCounts.TryGetValue(0, out int abz) ? abz : 0;
Console.WriteLine($"  zero-fraction: {abZero:N0}/{abTotal:N0} ({(double)abZero/abTotal*100:F1}%)");
Console.WriteLine($"  distinct: {animBaseCounts.Count:N0}  non-zero top-5:");
foreach (var kv in animBaseCounts.Where(k => k.Key != 0).OrderByDescending(k => k.Value).Take(5))
    Console.WriteLine($"    {kv.Key}: {kv.Value:N0}");

Console.WriteLine("\n--- anim_loop distribution ---");
foreach (var kv in animLoopCounts.OrderBy(k => k.Key))
    Console.WriteLine($"  anim_loop={kv.Key} ({(kv.Key == 0 ? "static" : "animated")}): {kv.Value,7:N0}");

Console.WriteLine("\n--- resource_id ---");
Console.WriteLine($"  <10000 (mesh-particle): {resourceBelow:N0}");
Console.WriteLine($"  >=10000 (GPU particle): {resourceAbove:N0}");
Console.WriteLine($"  min observed: {resourceMin}");
Console.WriteLine($"  max observed: {resourceMax}");

Console.WriteLine("\n--- tex_count distribution ---");
PrintSorted(texCountBuckets);

Console.WriteLine("\n--- anim_stride top-20 values ---");
foreach (var kv in animStrideCounts.OrderByDescending(k => k.Value).Take(20))
    Console.WriteLine($"  {kv.Key,8} ms: {kv.Value,7:N0}");

Console.WriteLine("\n--- alpha curve (pass 1) ---");
Console.WriteLine($"  zero-entry (count=0): {alphaZeroCount:N0} / {totalSubEffects:N0}");
Console.WriteLine($"  total alpha key entries: {totalAlphaKeyCount:N0}");

Console.WriteLine("\n--- scale curves (passes 2/3/4) zero vs nonzero ---");
Console.WriteLine($"  scaleX: zero={scaleXZero:N0}  nonzero={scaleXNonZ:N0}");
Console.WriteLine($"  scaleY: zero={scaleYZero:N0}  nonzero={scaleYNonZ:N0}");
Console.WriteLine($"  scaleZ: zero={scaleZZero:N0}  nonzero={scaleZNonZ:N0}");

// ============================================================================
// SECTION 2: .xobj full-corpus parse (ASCII text)
// ============================================================================
Console.WriteLine("\n\n=== SECTION 2: .xobj full-corpus parse (ASCII text) ===\n");

var xobjList = new List<VfsEntry>();
foreach (var e in archive.GetEntries())
    if (e.Name.EndsWith(".xobj", StringComparison.Ordinal))
        xobjList.Add(e);

Console.WriteLine($"Total .xobj entries: {xobjList.Count:N0}");

int xobjClean = 0, xobjError = 0;
var xobjErrorList = new List<(string path, string err)>();
var slotIds    = new List<int>();
var faceCounts = new List<int>();
var vertCounts = new List<int>();

foreach (var e in xobjList)
{
    ReadOnlyMemory<byte> raw = archive.GetFileContent(e.Name);
    string text = cp949.GetString(raw.Span);
    try
    {
        var tokens = text.Split(new char[]{' ', '\t', '\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2) throw new Exception("too few tokens");
        int slotId    = int.Parse(tokens[0]);
        int faceCount = int.Parse(tokens[1]);
        int idxTokens = faceCount * 3;
        if (tokens.Length < 2 + idxTokens + 1) throw new Exception($"truncated: need {2+idxTokens+1} for indices+vertcount, got {tokens.Length}");
        int vertCount = int.Parse(tokens[2 + idxTokens]);
        int expected  = 2 + idxTokens + 1 + vertCount * 8;
        if (tokens.Length < expected) throw new Exception($"truncated vertex data: need {expected}, got {tokens.Length}");
        int leftover  = tokens.Length - expected;
        if (leftover != 0) throw new Exception($"trailing tokens: {leftover}");
        slotIds.Add(slotId);
        faceCounts.Add(faceCount);
        vertCounts.Add(vertCount);
        xobjClean++;
    }
    catch (Exception ex)
    {
        xobjError++;
        xobjErrorList.Add((e.Name, ex.Message.Length > 120 ? ex.Message[..120] : ex.Message));
    }
}

Console.WriteLine($"  parse-clean (zero residual):  {xobjClean:N0}");
Console.WriteLine($"  parse errors:                 {xobjError:N0}");

if (xobjErrorList.Count > 0)
{
    Console.WriteLine($"\n  ERRORS:");
    foreach (var (p, msg) in xobjErrorList) Console.WriteLine($"    {p}: {msg}");
}

if (slotIds.Count > 0)
{
    Console.WriteLine($"\n  slot_id: min={slotIds.Min()}  max={slotIds.Max()}  distinct={slotIds.Distinct().Count()}");
    Console.WriteLine($"  face_count: min={faceCounts.Min()}  max={faceCounts.Max()}  avg={faceCounts.Average():F1}");
    Console.WriteLine($"  vert_count: min={vertCounts.Min()}  max={vertCounts.Max()}  avg={vertCounts.Average():F1}");
}

Console.WriteLine("\n  All .xobj entries:");
foreach (var e in xobjList)
    Console.WriteLine($"    {e.Name,-60}  size={e.DataSize,6:N0}");

// slot_id vs filename prefix check
Console.WriteLine("\n  slot_id vs filename prefix check:");
foreach (var e in xobjList)
{
    var raw = archive.GetFileContent(e.Name);
    var tokens = cp949.GetString(raw.Span).Split(new char[]{' ','\t','\r','\n'}, StringSplitOptions.RemoveEmptyEntries);
    if (tokens.Length < 1) continue;
    string stem = Path.GetFileNameWithoutExtension(e.Name);
    var m = System.Text.RegularExpressions.Regex.Match(stem, @"\((\d+)\)");
    string fileNum = m.Success ? m.Groups[1].Value : "N/A";
    Console.WriteLine($"    {stem}: file_slot_id={tokens[0]}  filename_num={fileNum}  match={tokens[0]==fileNum}");
}

Console.WriteLine("\nxeff-val: done.");

// ============================================================================
// Helpers
// ============================================================================

static void PrintSorted(Dictionary<string, int> d)
{
    foreach (var kv in d.OrderBy(k => k.Key))
        Console.WriteLine($"  {kv.Key,-20}: {kv.Value,7:N0}");
}

static int ComputeParsedBytes(ReadOnlySpan<byte> span)
{
    if (span.Length < 8) return 0;
    uint subCount = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
    int offset = 8;
    for (int s = 0; s < (int)subCount; s++)
    {
        if (offset + 24 > span.Length) throw new InvalidDataException($"bloc {s}: truncated fixed head");
        uint emitterType = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        uint texCount    = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset+20)..]);
        offset += 24;
        long nameBytes = (long)texCount * 64;
        if (offset + nameBytes > span.Length) throw new InvalidDataException($"bloc {s}: truncated name table");
        offset += (int)nameBytes;
        for (int p = 0; p < 4; p++)
        {
            if (offset + 4 > span.Length) throw new InvalidDataException($"bloc {s}: curve{p} count truncated");
            uint cnt = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            offset += 4;
            if (offset + (long)cnt * 4 > span.Length) throw new InvalidDataException($"bloc {s}: curve{p} data truncated");
            offset += (int)cnt * 4;
        }
        if (offset + 1 > span.Length) throw new InvalidDataException($"bloc {s}: track header anim_loop truncated");
        byte animLoop = span[offset];
        int trackSz = animLoop != 0 ? 13 : 9;
        if (offset + trackSz > span.Length) throw new InvalidDataException($"bloc {s}: track header truncated");
        offset += trackSz;
        if (animLoop != 0)
        {
            for (int k = 0; k < (int)texCount; k++)
            {
                int kfSz = k == 0 ? 36 : 40;
                if (offset + kfSz > span.Length) throw new InvalidDataException($"bloc {s}: kf{k} truncated");
                offset += kfSz;
            }
        }
        else
        {
            int sz = emitterType == 2 ? 36 : 24;
            if (offset + sz > span.Length) throw new InvalidDataException($"bloc {s}: static-state truncated");
            offset += sz;
        }
    }
    return offset;
}

static void ProbeFile(string path, ReadOnlySpan<byte> span)
{
    Console.WriteLine($"\n=== PROBE: {path} ({span.Length} bytes) ===");
    if (span.Length < 8) { Console.WriteLine("  TOO SHORT"); return; }
    uint effectId = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
    uint subCount = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
    Console.WriteLine($"  effect_id=0x{effectId:X8}  sub_effect_count={subCount}");

    int offset = 8;
    for (int s = 0; s < (int)Math.Min(subCount, 20u); s++)
    {
        if (offset + 24 > span.Length) { Console.WriteLine($"  [bloc {s}] TRUNCATED at fixed head @0x{offset:X4}"); break; }
        uint emitterType = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        uint resourceId  = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset+4)..]);
        uint animFlag    = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset+8)..]);
        uint unknownA    = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset+12)..]);
        uint dword2      = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset+16)..]);
        uint texCount    = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset+20)..]);
        Console.WriteLine($"  [bloc {s}] @0x{offset:X4}: emitter={emitterType} res={resourceId} anim_flag={animFlag} unkn_a=0x{unknownA:X8} dw2=0x{dword2:X8} tex_count={texCount}");
        offset += 24;
        if (texCount > 1000000) { Console.WriteLine($"    IMPLAUSIBLE tex_count — stopping"); break; }
        long nameBytes = (long)texCount * 64;
        if (offset + nameBytes > span.Length) { Console.WriteLine($"    name table TRUNCATED need {nameBytes}B at 0x{offset:X4} (only {span.Length-offset}B left)"); break; }
        if (texCount > 0) {
            var n = span.Slice(offset, 64); int ni = n.IndexOf((byte)0); if (ni < 0) ni = 64;
            Console.WriteLine($"    name[0]=\"{System.Text.Encoding.ASCII.GetString(n[..ni])}\"");
        }
        offset += (int)nameBytes;
        for (int p = 0; p < 4; p++)
        {
            if (offset + 4 > span.Length) { Console.WriteLine($"    curve{p} count TRUNCATED"); break; }
            uint cnt = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            if (cnt > 10000) { Console.WriteLine($"    curve{p} count={cnt} IMPLAUSIBLE"); break; }
            Console.WriteLine($"    curve{p} count={cnt}");
            offset += 4;
            if (offset + (long)cnt * 4 > span.Length) { Console.WriteLine($"    curve{p} data TRUNCATED"); break; }
            offset += (int)cnt * 4;
        }
        // HYPOTHESIS: track header size depends on anim_loop:
        //   static (anim_loop=0): 9 bytes (no unknown_constant)
        //   animated (anim_loop!=0): 13 bytes (with unknown_constant)
        // We peek at byte [offset] to get anim_loop first.
        if (offset + 1 > span.Length) { Console.WriteLine($"    track header TRUNCATED @0x{offset:X4}"); break; }
        byte animLoopPeek = span[offset];
        int trackHdrSize = animLoopPeek != 0 ? 13 : 9;
        if (offset + trackHdrSize > span.Length) { Console.WriteLine($"    track header TRUNCATED @0x{offset:X4}"); break; }
        byte animLoop = animLoopPeek;
        uint animStride, animBase;
        if (trackHdrSize == 13)
        {
            animStride = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset+5)..]);
            animBase   = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset+9)..]);
        }
        else
        {
            animStride = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset+1)..]);
            animBase   = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset+5)..]);
        }
        Console.WriteLine($"    track({trackHdrSize}B): anim_loop={animLoop} stride={animStride} base={animBase} @0x{offset:X4}");
        offset += trackHdrSize;
        if (animLoop != 0) {
            for (int k = 0; k < (int)texCount; k++) {
                int kfSz = k == 0 ? 36 : 40;
                if (offset + kfSz > span.Length) { Console.WriteLine($"    kf{k} TRUNCATED"); break; }
                offset += kfSz;
            }
            Console.WriteLine($"    animated kfs done, now @0x{offset:X4}");
        } else {
            int sz = emitterType == 2 ? 36 : 24;
            if (offset + sz > span.Length) { Console.WriteLine($"    static TRUNCATED need {sz}B at 0x{offset:X4}"); break; }
            offset += sz;
            Console.WriteLine($"    static consumed {sz}B, now @0x{offset:X4}");
        }
    }
    int residual = span.Length - offset;
    Console.WriteLine($"  FINAL: offset=0x{offset:X4} file_len=0x{span.Length:X4} residual={residual}");
}

return 0;
