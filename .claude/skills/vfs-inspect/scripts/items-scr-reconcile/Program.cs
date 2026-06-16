// THROWAWAY harness — items.scr MODEL A vs MODEL B reconciliation
// Tests MODEL A: fixed 548-byte (0x224) blocks with optional trailing 8-byte effect entries
// (effect_count u8 at record-offset 0x220).
// NOT in MartialHeroes.slnx, never committed, never git add.

using System.Text;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var cp949 = Encoding.GetEncoding(949);

// VFS path resolution
var infPath = @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata\data.inf";
var vfsPath = @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata\data\data.vfs";
if (!File.Exists(infPath)) { infPath = @"D:\MartialHeroesClient\data.inf"; vfsPath = @"D:\MartialHeroesClient\data\data.vfs"; }

Console.WriteLine($"Mounting VFS from {infPath}");
using var vfs = MappedVfsArchive.Open(infPath, vfsPath);

var ib = vfs.GetFileContent("data/script/items.scr").ToArray();
int fileSize = ib.Length;
Console.WriteLine($"items.scr size = {fileSize:N0} bytes (0x{fileSize:X})");
Console.WriteLine();

const int RECORD_STRIDE = 0x224; // 548 bytes
const int EFFECT_COUNT_OFFSET = 0x220; // u8 effect_count within each 548-byte block

// ═══════════════════════════════════════════════════════════════════════
// MODEL A WALK
// ═══════════════════════════════════════════════════════════════════════
Console.WriteLine("════════════════════════════════════════════════════════════════");
Console.WriteLine("MODEL A WALK: fixed 548-byte blocks + 8*effect_count trailing");
Console.WriteLine("════════════════════════════════════════════════════════════════");

int pos = 0;
int recordCount = 0;
int badNameCount = 0;     // records where byte[0x00] is not a plausible name start
int badEffectCount = 0;   // records where effect_count > 30 (implausibly large)
int zeroNameCount = 0;    // records where the entire name field is zero (blank record)
long totalBytesConsumed = 0;

// For first 10 records: detailed decode
Console.WriteLine("\nFirst 10 records (detailed):");
Console.WriteLine($"{"#",-5} {"offset",-10} {"name@+0x00",-40} {"uid@+0x34",-14} {"effCnt@+0x220",-14} {"consumed",-10}");
Console.WriteLine(new string('-', 110));

var detailBuffer = new List<(int recNo, int offset, string name, uint uid, byte effCount, int stride)>();

while (pos + RECORD_STRIDE <= fileSize)
{
    // Read the 548-byte block
    int blockStart = pos;

    // --- FIELD 1: Name at +0x00 (CP949, up to 52 bytes before uid @0x34) ---
    // IDA spec says name @+0x00; let's read as null-padded CP949 up to 0x34 boundary
    // First byte plausibility check
    byte nameByte0 = ib[pos];
    bool isAsciiPrintable = nameByte0 >= 0x20 && nameByte0 <= 0x7E;
    bool isCP949LeadByte = nameByte0 >= 0x81 && nameByte0 <= 0xFE;
    bool isNul = nameByte0 == 0x00;
    bool nameOk = isAsciiPrintable || isCP949LeadByte || isNul;

    // Read name as null-terminated CP949 within the 0x34-byte window
    int nameEnd = pos;
    int nameLimit = pos + 0x34; // name occupies +0x00 through +0x33 (52 bytes before uid)
    while (nameEnd < nameLimit && ib[nameEnd] != 0) nameEnd++;
    string name = nameEnd > pos ? cp949.GetString(ib, pos, nameEnd - pos) : "(empty)";

    // --- FIELD 2: uid at +0x34 (u32 LE) ---
    uint uid = BitConverter.ToUInt32(ib, pos + 0x34);

    // --- FIELD 3: effect_count at +0x220 (u8) ---
    byte effectCount = ib[pos + EFFECT_COUNT_OFFSET];
    bool effectCountSane = effectCount <= 30; // sanity: no item has >30 effect entries

    int totalStride = RECORD_STRIDE + 8 * effectCount;

    // Sanity checks
    if (!nameOk) badNameCount++;
    if (!effectCountSane) badEffectCount++;
    if (isNul) zeroNameCount++;

    // Detailed output for first 10
    if (recordCount < 10)
    {
        detailBuffer.Add((recordCount, blockStart, name, uid, effectCount, totalStride));
    }

    recordCount++;
    totalBytesConsumed += totalStride;
    pos += totalStride;
}

// Print first 10 detail
foreach (var (recNo, offset, name, uid, effCount, stride) in detailBuffer)
{
    string nameTrunc = name.Length > 38 ? name[..38] : name;
    Console.WriteLine($"{recNo,-5} 0x{offset:X8}  {nameTrunc,-40} 0x{uid:X8}    effCnt={effCount,-6}  stride={stride}");
}

Console.WriteLine();
Console.WriteLine($"Walk stopped at offset 0x{pos:X} ({pos:N0})");
int leftover = fileSize - pos;
Console.WriteLine($"Leftover bytes: {leftover} (0x{leftover:X})");
Console.WriteLine($"Total records read: {recordCount}");
Console.WriteLine($"Total bytes consumed by walk: {totalBytesConsumed:N0}");
Console.WriteLine();

// ═══════════════════════════════════════════════════════════════════════
// INVARIANT REPORT
// ═══════════════════════════════════════════════════════════════════════
Console.WriteLine("════════════════════════════════════════════════════════════════");
Console.WriteLine("MODEL A INVARIANT CHECKS");
Console.WriteLine("════════════════════════════════════════════════════════════════");

bool eofClean = leftover == 0;
bool namesOk = badNameCount == 0;
bool effectsOk = badEffectCount == 0;

Console.WriteLine($"(a) CP949/ASCII name at +0x00 for ALL records:  {(namesOk ? "PASS" : "FAIL")} ({badNameCount} bad name bytes, {zeroNameCount} zero/empty names)");
Console.WriteLine($"(b) effect_count@+0x220 sane (<=30) for ALL:   {(effectsOk ? "PASS" : "FAIL")} ({badEffectCount} records with count > 30)");
Console.WriteLine($"(c) Walk lands EXACTLY on EOF:                  {(eofClean ? "PASS" : "FAIL")} ({leftover} leftover bytes)");
Console.WriteLine($"(d) Total record count:                         {recordCount}");
Console.WriteLine();

bool modelAHolds = eofClean && effectsOk;
if (modelAHolds)
{
    Console.WriteLine("VERDICT: MODEL A HOLDS — fixed 548+8N walk lands exactly on EOF with all effect_count sane");
}
else
{
    Console.WriteLine("VERDICT: MODEL A FAILS — see failure details below");
    if (!eofClean)
    {
        Console.WriteLine($"  EOF miss: {leftover} bytes leftover (file not evenly consumed)");
        // Compute what stride WOULD give clean EOF
        if (recordCount > 0)
        {
            long consumed548Only = (long)recordCount * RECORD_STRIDE;
            long avgEffectTrail = (totalBytesConsumed - consumed548Only);
            Console.WriteLine($"  If zero effect_count throughout: consumed = {consumed548Only:N0}, leftover = {fileSize - consumed548Only}");
        }
    }
    if (!effectsOk)
        Console.WriteLine($"  {badEffectCount} records have effect_count > 30 (insane trailing array size)");
}
Console.WriteLine();

// ═══════════════════════════════════════════════════════════════════════
// EXTRA: if MODEL A holds, do a quick field cross-check at fixed offsets
// ═══════════════════════════════════════════════════════════════════════
if (modelAHolds)
{
    Console.WriteLine("════════════════════════════════════════════════════════════════");
    Console.WriteLine("FIXED-OFFSET FIELD SAMPLING (MODEL A confirmed)");
    Console.WriteLine("════════════════════════════════════════════════════════════════");
    Console.WriteLine("Sampling 4 item families to verify stats at fixed offsets within 548-byte block:");
    Console.WriteLine();

    // Re-walk and sample: first record per uid-range bucket
    // uid ranges hint at item category (from earlier analysis, items have uid in 0x0B..0x0F range)
    var samples = new List<(int recNo, int offset, string name, uint uid, byte effCnt)>();
    int samplePos = 0;
    int sampleRec = 0;

    // Collect ALL records for cross-family comparison (full file walk)
    var allRecs = new List<(int offset, string name, uint uid, byte effCnt)>();
    samplePos = 0;
    sampleRec = 0;
    while (samplePos + RECORD_STRIDE <= fileSize)
    {
        byte[] nameBytes = ib.AsSpan(samplePos, 0x34).ToArray();
        int ne = 0;
        while (ne < nameBytes.Length && nameBytes[ne] != 0) ne++;
        string nm = ne > 0 ? cp949.GetString(nameBytes, 0, ne) : "";
        uint u = BitConverter.ToUInt32(ib, samplePos + 0x34);
        byte ec = ib[samplePos + EFFECT_COUNT_OFFSET];
        allRecs.Add((samplePos, nm, u, ec));
        samplePos += RECORD_STRIDE + 8 * ec;
        sampleRec++;
    }

    Console.WriteLine($"Records sampled: {allRecs.Count}");
    Console.WriteLine();

    // Show uid distribution
    var uidBuckets = allRecs
        .GroupBy(r => r.uid >> 28)
        .OrderBy(g => g.Key)
        .Select(g => (highNibble: g.Key, count: g.Count()))
        .ToList();
    Console.WriteLine("UID high-nibble distribution (family indicator):");
    foreach (var (hn, cnt) in uidBuckets)
        Console.WriteLine($"  uid >> 28 == 0x{hn:X}: {cnt} records");
    Console.WriteLine();

    // Cross-enchant analysis: find a family with similar base names
    // First, find records that have the same name prefix (e.g. first 6 chars)
    var nameGroups = allRecs
        .Where(r => r.name.Length >= 4)
        .GroupBy(r => r.name.Length > 6 ? r.name[..6] : r.name)
        .Where(g => g.Count() >= 3)
        .OrderByDescending(g => g.Count())
        .Take(3)
        .ToList();

    Console.WriteLine("Largest name-prefix families (likely enchant series):");
    foreach (var ng in nameGroups)
        Console.WriteLine($"  '{ng.Key}' — {ng.Count()} records");
    Console.WriteLine();

    // For the largest family, show fixed-offset fields across variants
    if (nameGroups.Count > 0)
    {
        var fam = nameGroups[0].OrderBy(r => r.name).Take(8).ToList();
        Console.WriteLine($"Fixed-offset field comparison within family '{nameGroups[0].Key}' (first 8 variants):");
        Console.WriteLine($"{"Variant name",-30} {"uid",-12} {"effCnt",-8} {"+0x084 u32",-14} {"+0x088 u32",-14} {"+0x0B8 u32",-14} {"+0x0C0 u32",-14} {"+0x100 u32",-14} {"+0x108 u32",-14}");
        Console.WriteLine(new string('-', 150));
        foreach (var (off, nm, u, ec) in fam)
        {
            uint f084 = off + 0x084 + 4 <= fileSize ? BitConverter.ToUInt32(ib, off + 0x084) : 0;
            uint f088 = off + 0x088 + 4 <= fileSize ? BitConverter.ToUInt32(ib, off + 0x088) : 0;
            uint f0b8 = off + 0x0B8 + 4 <= fileSize ? BitConverter.ToUInt32(ib, off + 0x0B8) : 0;
            uint f0c0 = off + 0x0C0 + 4 <= fileSize ? BitConverter.ToUInt32(ib, off + 0x0C0) : 0;
            uint f100 = off + 0x100 + 4 <= fileSize ? BitConverter.ToUInt32(ib, off + 0x100) : 0;
            uint f108 = off + 0x108 + 4 <= fileSize ? BitConverter.ToUInt32(ib, off + 0x108) : 0;
            string nmT = nm.Length > 28 ? nm[..28] : nm;
            Console.WriteLine($"{nmT,-30} 0x{u:X8}  {ec,-8} 0x{f084:X8}  0x{f088:X8}  0x{f0b8:X8}  0x{f0c0:X8}  0x{f100:X8}  0x{f108:X8}");
        }
        Console.WriteLine();
    }

    // Show offset 0x34 (uid), 0x00 (name), 0x220 (effect_count) invariants across all 2000
    int nonZeroNames = allRecs.Count(r => r.name.Length > 0);
    int saneEffects = allRecs.Count(r => r.effCnt <= 30);
    Console.WriteLine($"Across all {allRecs.Count} sampled records:");
    Console.WriteLine($"  Records with non-empty name at +0x00: {nonZeroNames}/{allRecs.Count}");
    Console.WriteLine($"  Records with effect_count <= 30:      {saneEffects}/{allRecs.Count}");
    Console.WriteLine();

    // Effect count distribution
    var ecDist = allRecs.GroupBy(r => r.effCnt).OrderBy(g => g.Key).ToList();
    Console.WriteLine("effect_count distribution (top 15):");
    foreach (var g in ecDist.Take(15))
        Console.WriteLine($"  effect_count={g.Key,3}: {g.Count()} records");
    Console.WriteLine();

    // Cross-family stats at fixed offsets
    // UID high-byte distribution
    var uidHighByte = allRecs
        .GroupBy(r => r.uid >> 24)
        .OrderBy(g => g.Key)
        .Select(g => (high: g.Key, cnt: g.Count()))
        .ToList();
    Console.WriteLine("UID high-byte distribution (uid >> 24):");
    foreach (var (hb, cnt) in uidHighByte)
        Console.WriteLine($"  0x{hb:X2}: {cnt,6} records");
    Console.WriteLine();

    // Find one record per uid-high-byte family
    Console.WriteLine("Per-UID-family fixed-offset field sampling (one record each):");
    Console.WriteLine($"{"uid>>24",-10} {"name",-30} {"+0x038 u32",-12} {"+0x084 u32",-12} {"+0x0B8 u32",-12} {"+0x0EC u32",-12} {"+0x150 u32",-12} {"+0x1C0 u32",-12}");
    Console.WriteLine(new string('-', 130));
    var seen = new HashSet<uint>();
    foreach (var (off, nm, u, ec) in allRecs)
    {
        uint fam2 = u >> 24;
        if (!seen.Add(fam2)) continue;
        uint f038 = off + 0x038 + 4 <= fileSize ? BitConverter.ToUInt32(ib, off + 0x038) : 0;
        uint f084 = off + 0x084 + 4 <= fileSize ? BitConverter.ToUInt32(ib, off + 0x084) : 0;
        uint f0b8 = off + 0x0B8 + 4 <= fileSize ? BitConverter.ToUInt32(ib, off + 0x0B8) : 0;
        uint f0ec = off + 0x0EC + 4 <= fileSize ? BitConverter.ToUInt32(ib, off + 0x0EC) : 0;
        uint f150 = off + 0x150 + 4 <= fileSize ? BitConverter.ToUInt32(ib, off + 0x150) : 0;
        uint f1c0 = off + 0x1C0 + 4 <= fileSize ? BitConverter.ToUInt32(ib, off + 0x1C0) : 0;
        string nmT = nm.Length > 28 ? nm[..28] : nm;
        Console.WriteLine($"0x{fam2:X2}       {nmT,-30} 0x{f038:X8} 0x{f084:X8} 0x{f0b8:X8} 0x{f0ec:X8} 0x{f150:X8} 0x{f1c0:X8}");
    }
    Console.WriteLine();

    // Cross-category stats: sample records from different regions of the file to catch all uid families
    // Sample every 500th record
    Console.WriteLine("Cross-file stratified sample (every 500th record) — fixed offsets:");
    Console.WriteLine($"{"#",-6} {"uid",-12} {"name",-30} {"+0x034 uid",-12} {"+0x080 u32",-12} {"+0x0A4 u32",-12} {"+0x0A8 u32",-12} {"+0x0AC u32",-12} {"+0x200 u32",-12} {"+0x21C u32",-12} {"effCnt",-8}");
    Console.WriteLine(new string('-', 155));
    for (int si = 0; si < allRecs.Count; si += 500)
    {
        var (off2, nm2, u2, ec2) = allRecs[si];
        uint f034 = BitConverter.ToUInt32(ib, off2 + 0x034);
        uint f080 = off2 + 0x080 + 4 <= fileSize ? BitConverter.ToUInt32(ib, off2 + 0x080) : 0;
        uint f0a4 = off2 + 0x0A4 + 4 <= fileSize ? BitConverter.ToUInt32(ib, off2 + 0x0A4) : 0;
        uint f0a8 = off2 + 0x0A8 + 4 <= fileSize ? BitConverter.ToUInt32(ib, off2 + 0x0A8) : 0;
        uint f0ac = off2 + 0x0AC + 4 <= fileSize ? BitConverter.ToUInt32(ib, off2 + 0x0AC) : 0;
        uint f200 = off2 + 0x200 + 4 <= fileSize ? BitConverter.ToUInt32(ib, off2 + 0x200) : 0;
        uint f21c = off2 + 0x21C + 4 <= fileSize ? BitConverter.ToUInt32(ib, off2 + 0x21C) : 0;
        string nm2T = nm2.Length > 28 ? nm2[..28] : nm2;
        Console.WriteLine($"{si,-6} 0x{u2:X8} {nm2T,-30} 0x{f034:X8} 0x{f080:X8} 0x{f0a4:X8} 0x{f0a8:X8} 0x{f0ac:X8} 0x{f200:X8} 0x{f21c:X8} {ec2}");
    }
    Console.WriteLine();
}
else
{
    // MODEL A failed — try to determine what stride DOES work
    Console.WriteLine("════════════════════════════════════════════════════════════════");
    Console.WriteLine("MODEL A REFUTED — probing alternative strides");
    Console.WriteLine("════════════════════════════════════════════════════════════════");

    // Check: does 548 flat (no trailing) give clean EOF?
    if (fileSize % RECORD_STRIDE == 0)
    {
        Console.WriteLine($"FLAT STRIDE {RECORD_STRIDE}: fileSize / {RECORD_STRIDE} = {fileSize / RECORD_STRIDE} records EXACTLY — clean!");
    }
    else
    {
        Console.WriteLine($"FLAT STRIDE {RECORD_STRIDE}: {fileSize} % {RECORD_STRIDE} = {fileSize % RECORD_STRIDE} (NOT clean)");
    }

    // Check a few other strides
    int[] candidates = [548, 556, 512, 520, 524, 536, 544, 552, 560, 576, 596, 608, 640];
    foreach (int c in candidates)
    {
        if (fileSize % c == 0)
            Console.WriteLine($"  Stride {c} (0x{c:X3}): {fileSize / c} records — CLEAN EOF");
        else
            Console.WriteLine($"  Stride {c} (0x{c:X3}): leftover {fileSize % c}");
    }
}

Console.WriteLine();
Console.WriteLine("Done — items_scr_reconcile harness complete.");
