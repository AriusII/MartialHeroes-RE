// THROWAWAY harness — Campaign 8 Phase V lane V2
// Validates .scr catalogue specs for: items.scr, citems.scr, skills.scr, mobs.scr, npcs.scr
// NOT in MartialHeroes.slnx, never committed, never git add.
//
// Validates:
//   items.scr  : stride = 548 + N*8 (N = effect_count u8 @+0x220), 90937 records
//   citems.scr : stride = 1052, 512 records, 6×81-byte desc paragraphs from +0x0E4
//   skills.scr : stride = 1504 + N*8, ~194 real records  (variable-stride)
//   mobs.scr   : stride = 488 flat, 3997 records
//   npcs.scr   : stride = 1916 flat, counts records
//
// Per-file outputs: stride-division result, per-field distributions for UNVERIFIED fields,
// HOLDS / VARIES / STILL-OPAQUE verdicts.

using System.Runtime.CompilerServices;
using System.Text;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var cp949 = Encoding.GetEncoding(949);

var infPath = @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata\data.inf";
var vfsPath = @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata\data\data.vfs";
if (!File.Exists(infPath))
{
    infPath = @"D:\MartialHeroesClient\data.inf";
    vfsPath = @"D:\MartialHeroesClient\data\data.vfs";
}

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine($"=== SCR CATALOGUE VALIDATOR — Campaign 8 Phase V lane V2 ===");
Console.WriteLine($"VFS: {infPath}");
Console.WriteLine();

using var vfs = MappedVfsArchive.Open(infPath, vfsPath);

// ─── Helper functions ───────────────────────────────────────────────────
static uint U32(ReadOnlySpan<byte> b, int off) => off + 4 <= b.Length ? BitConverter.ToUInt32(b.Slice(off, 4)) : 0u;
static int I32(ReadOnlySpan<byte> b, int off) => off + 4 <= b.Length ? BitConverter.ToInt32(b.Slice(off, 4)) : 0;
static ushort U16(ReadOnlySpan<byte> b, int off) => off + 2 <= b.Length ? BitConverter.ToUInt16(b.Slice(off, 2)) : (ushort)0;
static float F32(ReadOnlySpan<byte> b, int off) => off + 4 <= b.Length ? BitConverter.ToSingle(b.Slice(off, 4)) : 0f;
static string Str(ReadOnlySpan<byte> b, int off, int maxLen, Encoding enc)
{
    int end = off;
    int limit = Math.Min(off + maxLen, b.Length);
    while (end < limit && b[end] != 0) end++;
    if (end == off) return "";
    return enc.GetString(b.Slice(off, end - off));
}
static bool IsPlausibleFloat(float f) => !float.IsNaN(f) && !float.IsInfinity(f) && f >= -1e7f && f <= 1e7f;

static string DistSummary<T>(Dictionary<T, int> dist) where T : notnull
{
    int total = dist.Values.Sum();
    var sorted = dist.OrderByDescending(kv => kv.Value).Take(10);
    return string.Join(", ", sorted.Select(kv => $"{kv.Key}×{kv.Value} ({100.0*kv.Value/total:F1}%)"));
}

// ═══════════════════════════════════════════════════════════════════════
// 1. items.scr
// ═══════════════════════════════════════════════════════════════════════
Console.WriteLine("════════════════════════════════════════════════════════");
Console.WriteLine("1. items.scr");
Console.WriteLine("════════════════════════════════════════════════════════");

var itemsBytes = vfs.GetFileContent("data/script/items.scr").ToArray();
Console.WriteLine($"File size: {itemsBytes.Length:N0} bytes (0x{itemsBytes.Length:X})");

const int ITEMS_FIXED = 0x224;   // 548
const int ITEMS_EC_OFFSET = 0x220; // effect_count u8

{
    int pos = 0;
    int recs = 0;
    int badEff = 0;
    var effectDist = new Dictionary<byte, int>();
    var uidHighDist = new Dictionary<uint, int>();
    int nonZeroName = 0;
    int zeroNameRecs = 0;

    // Fields to track from fixed block (items_scr.md §1.4)
    // +0x034 item_uid CONFIRMED; +0x080 template_ref UNVERIFIED; +0x084 template_ref_b UNVERIFIED
    // +0x0A4 f32 stat UNVERIFIED; +0x0B8 item_type_tag UNVERIFIED; +0x200 seq_ref UNVERIFIED; +0x21C rare_nonzero UNVERIFIED
    int nonZeroField080 = 0, nonZeroField084 = 0, nonZeroField0A4 = 0, nonZeroField0B8 = 0,
        nonZeroField200 = 0, nonZeroField21C = 0;
    var typeTags = new Dictionary<uint, int>();
    var f0A4Dist = new Dictionary<string, int>();  // bucketed as float
    int zeroDesc = 0;

    while (pos + ITEMS_FIXED <= itemsBytes.Length)
    {
        var block = new ReadOnlySpan<byte>(itemsBytes, pos, ITEMS_FIXED);
        byte ec = block[ITEMS_EC_OFFSET];
        if (!effectDist.TryAdd(ec, 1)) effectDist[ec]++;
        if (ec > 30) badEff++;

        // Name
        bool hasName = block[0] != 0;
        if (hasName) nonZeroName++;
        else zeroNameRecs++;

        // uid high nibble
        uint uid = U32(block, 0x034);
        uint hiByte = uid >> 24;
        if (!uidHighDist.TryAdd(hiByte, 1)) uidHighDist[hiByte]++;

        // UNVERIFIED fields
        uint f080 = U32(block, 0x080); if (f080 != 0) nonZeroField080++;
        uint f084 = U32(block, 0x084); if (f084 != 0) nonZeroField084++;
        float fa4 = F32(block, 0x0A4);
        if (fa4 != 0f) nonZeroField0A4++;
        // bucket the float
        string fa4Bucket = IsPlausibleFloat(fa4) ? $"{fa4:G5}" : "NaN/Inf";
        if (!f0A4Dist.TryAdd(fa4Bucket, 1)) f0A4Dist[fa4Bucket]++;

        uint f0B8 = U32(block, 0x0B8); if (f0B8 != 0) nonZeroField0B8++;
        if (!typeTags.TryAdd(f0B8, 1)) typeTags[f0B8]++;

        uint f200 = U32(block, 0x200); if (f200 != 0) nonZeroField200++;
        uint f21C = U32(block, 0x21C); if (f21C != 0) nonZeroField21C++;

        // Check desc at +0x038
        if (block.Length > 0x038 && block[0x038] == 0) zeroDesc++;

        int stride = ITEMS_FIXED + 8 * ec;
        pos += stride;
        recs++;
    }

    int leftover = itemsBytes.Length - pos;
    Console.WriteLine($"Walk result: {recs} records, leftover = {leftover} bytes");
    Console.WriteLine($"Stride: 548 + 8*effect_count — EOF clean: {(leftover == 0 ? "YES (CONFIRMED)" : $"NO — {leftover} bytes residual")}");
    Console.WriteLine($"Bad effect_count (>30): {badEff}");
    Console.WriteLine();

    Console.WriteLine("--- effect_count distribution (top 8): ---");
    foreach (var kv in effectDist.OrderByDescending(x => x.Value).Take(8))
        Console.WriteLine($"  effect_count={kv.Key}: {kv.Value} records ({100.0*kv.Value/recs:F2}%)");
    Console.WriteLine();

    Console.WriteLine("--- item_uid high-byte distribution (uid>>24): ---");
    foreach (var kv in uidHighDist.OrderBy(x => x.Key))
        Console.WriteLine($"  0x{kv.Key:X2}: {kv.Value} records");
    Console.WriteLine();

    Console.WriteLine("--- UNVERIFIED field non-zero counts ---");
    Console.WriteLine($"  +0x080 template_ref:    {nonZeroField080}/{recs} non-zero — {(nonZeroField080 > 0 ? "VARIES" : "ALWAYS ZERO")}");
    Console.WriteLine($"  +0x084 template_ref_b:  {nonZeroField084}/{recs} non-zero — {(nonZeroField084 > 0 ? "VARIES" : "ALWAYS ZERO")}");
    Console.WriteLine($"  +0x0A4 f32 stat:        {nonZeroField0A4}/{recs} non-zero — {(nonZeroField0A4 > 0 ? "VARIES" : "ALWAYS ZERO")}");
    Console.WriteLine($"  +0x0B8 item_type_tag:   {nonZeroField0B8}/{recs} non-zero — {(nonZeroField0B8 > 0 ? "VARIES" : "ALWAYS ZERO")}");
    Console.WriteLine($"  +0x200 seq_ref:         {nonZeroField200}/{recs} non-zero — {(nonZeroField200 > 0 ? "VARIES" : "ALWAYS ZERO")}");
    Console.WriteLine($"  +0x21C rare_nonzero:    {nonZeroField21C}/{recs} non-zero — {(nonZeroField21C > 0 ? "VARIES" : "ALWAYS ZERO")}");
    Console.WriteLine($"  Records with non-empty name @+0x000: {nonZeroName}/{recs}");
    Console.WriteLine($"  Records with zero desc @+0x038: {zeroDesc}/{recs}");
    Console.WriteLine();

    Console.WriteLine("--- +0x0B8 item_type_tag top-15 distinct values: ---");
    foreach (var kv in typeTags.OrderByDescending(x => x.Value).Take(15))
        Console.WriteLine($"  0x{kv.Key:X8}: {kv.Value} records");
    Console.WriteLine();

    Console.WriteLine("--- +0x0A4 f32 stat top-15 values: ---");
    foreach (var kv in f0A4Dist.OrderByDescending(x => x.Value).Take(15))
        Console.WriteLine($"  {kv.Key}: {kv.Value} records");
    Console.WriteLine();

    // Probe reserved bytes +0x221..+0x223
    int nonZeroPad = 0;
    pos = 0;
    while (pos + ITEMS_FIXED <= itemsBytes.Length)
    {
        var block = new ReadOnlySpan<byte>(itemsBytes, pos, ITEMS_FIXED);
        byte ec = block[ITEMS_EC_OFFSET];
        if (block[0x221] != 0 || block[0x222] != 0 || block[0x223] != 0) nonZeroPad++;
        pos += ITEMS_FIXED + 8 * ec;
    }
    Console.WriteLine($"--- +0x221..+0x223 padding zero check: {nonZeroPad} records have non-zero padding ---");
    Console.WriteLine($"  Verdict: {(nonZeroPad == 0 ? "HOLDS — always zero" : $"VARIES — {nonZeroPad} non-zero")}");
    Console.WriteLine();
}

// ═══════════════════════════════════════════════════════════════════════
// 2. citems.scr
// ═══════════════════════════════════════════════════════════════════════
Console.WriteLine("════════════════════════════════════════════════════════");
Console.WriteLine("2. citems.scr");
Console.WriteLine("════════════════════════════════════════════════════════");

var citemsBytes = vfs.GetFileContent("data/script/citems.scr").ToArray();
Console.WriteLine($"File size: {citemsBytes.Length:N0} bytes (0x{citemsBytes.Length:X})");

const int CITEMS_STRIDE = 1052; // 0x41C

{
    int fileSize = citemsBytes.Length;
    int recs = fileSize / CITEMS_STRIDE;
    int leftover = fileSize % CITEMS_STRIDE;

    Console.WriteLine($"Stride 1052: fileSize / 1052 = {recs} records, leftover = {leftover}");
    Console.WriteLine($"Stride-division: {(leftover == 0 ? "CONFIRMED — exact divisor" : $"FAILS — {leftover} residual")}");
    Console.WriteLine();

    // Per-field tracking
    int slotSeqOk = 0;        // slot_index sequential 1..N
    int nameNonEmpty = 0;
    int pad30Zero = 0;
    int pad34Zero = 0;
    int unk36_nonzero = 0;
    var unk36Dist = new Dictionary<ushort, int>();
    int priceNonZero = 0;
    var priceDist = new Dictionary<string, int>();
    int slotSeq2Nonzero = 0;
    int pad40Zero = 0;
    int itemUidNonzero = 0;
    int flag4C_nonone = 0;
    // desc_para paragraph fill counts
    int[] paraFilled = new int[6];
    // remainder 0x2CA..0x41B non-zero
    int remNonZero = 0;

    for (int r = 0; r < recs; r++)
    {
        int off = r * CITEMS_STRIDE;
        var rec = new ReadOnlySpan<byte>(citemsBytes, off, CITEMS_STRIDE);

        // +0x00 slot_index
        uint slotIdx = U32(rec, 0x00);
        if (slotIdx == (uint)(r + 1)) slotSeqOk++;

        // +0x04 item_name
        string nm = Str(rec, 0x04, 48, cp949);
        if (nm.Length > 0) nameNonEmpty++;

        // pad_30 = bytes 0x30..0x33
        if (rec[0x30] == 0 && rec[0x31] == 0 && rec[0x32] == 0 && rec[0x33] == 0) pad30Zero++;

        // pad_34 = bytes 0x34..0x35
        if (rec[0x34] == 0 && rec[0x35] == 0) pad34Zero++;

        // unknown_36
        ushort unk36 = U16(rec, 0x36);
        if (unk36 != 0) unk36_nonzero++;
        if (!unk36Dist.TryAdd(unk36, 1)) unk36Dist[unk36]++;

        // +0x38 cash_price_nx
        uint price = U32(rec, 0x38);
        if (price != 0) priceNonZero++;
        // bucket price into coarse ranges
        string pBucket = price == 0 ? "0" : price <= 500 ? "1-500" : price <= 2000 ? "501-2000" : price <= 5000 ? "2001-5000" : ">5000";
        if (!priceDist.TryAdd(pBucket, 1)) priceDist[pBucket]++;

        // +0x3C slot_seq_2
        uint slotSeq2 = U32(rec, 0x3C);
        if (slotSeq2 != 0) slotSeq2Nonzero++;

        // pad_40: bytes 0x40..0x47 all zero?
        bool p40z = true;
        for (int i = 0x40; i < 0x48; i++) if (rec[i] != 0) { p40z = false; break; }
        if (p40z) pad40Zero++;

        // +0x48 item_uid
        uint itmUid = U32(rec, 0x48);
        if (itmUid != 0) itemUidNonzero++;

        // +0x4C flag_4C: spec says value 1 in all observed records
        uint flag4C = U32(rec, 0x4C);
        if (flag4C != 1) flag4C_nonone++;

        // 6 desc paragraphs: 0x0E4 + i*81
        for (int pi = 0; pi < 6; pi++)
        {
            int paraOff = 0x0E4 + pi * 81;
            if (paraOff + 81 <= CITEMS_STRIDE && rec[paraOff] != 0)
                paraFilled[pi]++;
        }

        // remainder 0x2CA..0x41B
        bool hasRem = false;
        for (int i = 0x2CA; i < CITEMS_STRIDE; i++)
            if (rec[i] != 0) { hasRem = true; break; }
        if (hasRem) remNonZero++;
    }

    Console.WriteLine("--- Per-field validation ---");
    Console.WriteLine($"  +0x00 slot_index sequential (1..{recs}): {slotSeqOk}/{recs} CONFIRMED={slotSeqOk==recs}");
    Console.WriteLine($"  +0x04 item_name non-empty: {nameNonEmpty}/{recs}");
    Console.WriteLine($"  +0x30 pad zero: {pad30Zero}/{recs}");
    Console.WriteLine($"  +0x34 pad zero: {pad34Zero}/{recs}");
    Console.WriteLine($"  +0x36 unknown_36 non-zero: {unk36_nonzero}/{recs} → VERDICT: {(unk36_nonzero > 0 ? "VARIES" : "ALWAYS ZERO")}");
    Console.WriteLine($"  +0x38 cash_price_nx non-zero: {priceNonZero}/{recs}");
    Console.WriteLine($"  +0x3C slot_seq_2 non-zero: {slotSeq2Nonzero}/{recs}");
    Console.WriteLine($"  +0x40 pad_40 all-zero: {pad40Zero}/{recs}");
    Console.WriteLine($"  +0x48 item_uid non-zero: {itemUidNonzero}/{recs}");
    Console.WriteLine($"  +0x4C flag_4C != 1: {flag4C_nonone}/{recs} → VERDICT: {(flag4C_nonone == 0 ? "HOLDS — always 1" : "VARIES")}");
    Console.WriteLine();

    Console.WriteLine("--- +0x36 unknown_36 distribution (top 10): ---");
    foreach (var kv in unk36Dist.OrderByDescending(x => x.Value).Take(10))
        Console.WriteLine($"  0x{kv.Key:X4}: {kv.Value} records");
    Console.WriteLine();

    Console.WriteLine("--- +0x38 cash_price_nx distribution (coarse): ---");
    foreach (var kv in priceDist.OrderBy(x => x.Key))
        Console.WriteLine($"  range '{kv.Key}': {kv.Value} records");
    Console.WriteLine();

    Console.WriteLine("--- 6×81-byte desc paragraph fill counts: ---");
    for (int pi = 0; pi < 6; pi++)
        Console.WriteLine($"  desc_para[{pi}] (@0x{(0x0E4 + pi*81):X3}): {paraFilled[pi]}/{recs} non-empty");
    Console.WriteLine();

    Console.WriteLine($"--- Remainder +0x2CA..+0x41B non-zero: {remNonZero}/{recs} records ---");
    // Check specific offsets in remainder for non-trivial structure
    var remDist = new Dictionary<string, int>();
    for (int r2 = 0; r2 < recs; r2++)
    {
        int off = r2 * CITEMS_STRIDE;
        var rec = new ReadOnlySpan<byte>(citemsBytes, off, CITEMS_STRIDE);
        // Sample a few u32s in the remainder
        uint r2CA = U32(rec, 0x2CA);
        uint r2CE = U32(rec, 0x2CE);
        uint r300 = U32(rec, 0x300);
        uint r380 = U32(rec, 0x380);
        uint r400 = U32(rec, 0x400);
        string key = $"2CA:{r2CA:X} 2CE:{r2CE:X} 300:{r300:X} 380:{r380:X} 400:{r400:X}";
        if (!remDist.TryAdd(key, 1)) remDist[key]++;
    }
    Console.WriteLine("  Remainder field samples (top 5 distinct patterns):");
    foreach (var kv in remDist.OrderByDescending(x => x.Value).Take(5))
        Console.WriteLine($"    {kv.Key}: {kv.Value} records");
    Console.WriteLine();

    // Validate desc paragraphs don't overflow their 81-byte buffers
    int overflows = 0;
    for (int r2 = 0; r2 < recs; r2++)
    {
        int off = r2 * CITEMS_STRIDE;
        for (int pi = 0; pi < 6; pi++)
        {
            int paraOff = off + 0x0E4 + pi * 81;
            // Check that the 81st byte is NUL (buffer boundary)
            if (citemsBytes[paraOff + 80] != 0) overflows++;
        }
    }
    Console.WriteLine($"--- Desc paragraph buffer overflow check (byte 80 of each para == 0): {overflows} overflows ---");
    Console.WriteLine($"  Verdict: {(overflows == 0 ? "HOLDS — no overflows" : $"OVERFLOW — {overflows} paragraphs")}");
    Console.WriteLine();
}

// ═══════════════════════════════════════════════════════════════════════
// 3. skills.scr
// ═══════════════════════════════════════════════════════════════════════
Console.WriteLine("════════════════════════════════════════════════════════");
Console.WriteLine("3. skills.scr");
Console.WriteLine("════════════════════════════════════════════════════════");

var skillsBytes = vfs.GetFileContent("data/script/skills.scr").ToArray();
Console.WriteLine($"File size: {skillsBytes.Length:N0} bytes (0x{skillsBytes.Length:X})");

const int SKILLS_FIXED = 1504; // 0x5E0
const int SKILLS_EC_OFFSET = 1500; // approximate — trailing count is at end of record

{
    int fileSize = skillsBytes.Length;
    // Test flat stride first
    int flatDiv = fileSize % SKILLS_FIXED;
    Console.WriteLine($"Flat stride {SKILLS_FIXED}: fileSize % {SKILLS_FIXED} = {flatDiv}");
    Console.WriteLine($"Flat-stride EOF-clean: {(flatDiv == 0 ? "YES" : "NO")}");

    // Walk as variable-stride (1504 + N*8)
    int pos = 0;
    int recs = 0;
    int realRecs = 0; // plausible skill ID + plausible category
    int badTrailCount = 0;
    var trailDist = new Dictionary<byte, int>();
    var catDist = new Dictionary<uint, int>();
    var typeDist = new Dictionary<byte, int>();

    // UNVERIFIED fields from config_tables.md §2.8:
    // +260 constant 0x30000000 (CONFIRMED), +516 class flag, +520 type byte,
    // +1072 variable (in string), +1176 f32 multiplier, +1306 enum, +1328 stance bitmask
    // +1292 u16 SP cost, +1304 u16 motion index, +1372 u32 cooldown, +1412 f32 range
    int f260_hold = 0;        // how many have 0x30000000 at +260
    var f1176Dist = new Dictionary<string, int>(); // f32 at +1176 bucketed
    var f1306Dist = new Dictionary<ushort, int>(); // u16 at +1306
    var f1328Dist = new Dictionary<uint, int>();   // u32 at +1328
    var f1304Dist = new Dictionary<ushort, int>(); // motion index at +1304
    var f1372Dist = new Dictionary<uint, int>();   // cooldown
    var f1412Dist = new Dictionary<string, int>(); // range f32

    while (pos + SKILLS_FIXED <= skillsBytes.Length)
    {
        var block = new ReadOnlySpan<byte>(skillsBytes, pos, SKILLS_FIXED);

        // trailing count byte — spec says at the very end of the 1504-byte record
        // config_tables.md §2.8 says "trailing count byte present but typically 0"
        // We check last byte of the 1504-byte block
        byte trailCount = block[SKILLS_FIXED - 4]; // try offset 1500
        byte trailCount2 = block[SKILLS_FIXED - 1]; // try last byte 1503

        // Use +1500 as the trailing count
        byte ec = block[1500];
        if (!trailDist.TryAdd(ec, 1)) trailDist[ec]++;
        if (ec > 20) badTrailCount++;

        // Is this a real record?
        uint skillId = U32(block, 0);
        uint catIdx = U32(block, 4);
        bool isReal = skillId > 0 && skillId < 10_000_000 && catIdx < 300;
        if (isReal) realRecs++;

        // Category distribution
        if (isReal)
        {
            if (!catDist.TryAdd(catIdx, 1)) catDist[catIdx]++;

            // Type byte at +520
            byte typeByte = block[520];
            if (!typeDist.TryAdd(typeByte, 1)) typeDist[typeByte]++;

            // +260 constant check
            uint f260 = U32(block, 260);
            if (f260 == 0x30000000) f260_hold++;

            // +1176 f32 multiplier
            float f1176 = F32(block, 1176);
            string f1176Bucket = IsPlausibleFloat(f1176) ? $"{f1176:G3}" : "NaN/Inf";
            if (!f1176Dist.TryAdd(f1176Bucket, 1)) f1176Dist[f1176Bucket]++;

            // +1306 u16 enum
            ushort f1306 = U16(block, 1306);
            if (!f1306Dist.TryAdd(f1306, 1)) f1306Dist[f1306]++;

            // +1328 u32 stance/school bitmask
            uint f1328 = U32(block, 1328);
            if (!f1328Dist.TryAdd(f1328, 1)) f1328Dist[f1328]++;

            // +1304 u16 motion index
            ushort f1304 = U16(block, 1304);
            if (!f1304Dist.TryAdd(f1304, 1)) f1304Dist[f1304]++;

            // +1372 u32 cooldown
            uint f1372 = U32(block, 1372);
            if (!f1372Dist.TryAdd(f1372, 1)) f1372Dist[f1372]++;

            // +1412 f32 range
            float f1412 = F32(block, 1412);
            string f1412Bucket = IsPlausibleFloat(f1412) ? $"{f1412:G4}" : "NaN/Inf";
            if (!f1412Dist.TryAdd(f1412Bucket, 1)) f1412Dist[f1412Bucket]++;
        }

        int stride = SKILLS_FIXED + 8 * ec;
        pos += stride;
        recs++;
    }

    int leftover = skillsBytes.Length - pos;
    Console.WriteLine($"Variable-stride walk: {recs} records, real={realRecs}, leftover={leftover}");
    Console.WriteLine($"Variable-stride EOF-clean: {(leftover == 0 ? "YES (CONFIRMED)" : $"NO — {leftover} residual")}");
    Console.WriteLine($"Bad trail_count (>20): {badTrailCount}");
    Console.WriteLine();

    Console.WriteLine("--- trailing_count distribution (@+1500): ---");
    foreach (var kv in trailDist.OrderBy(x => x.Key))
        Console.WriteLine($"  trailing_count={kv.Key}: {kv.Value} records");
    Console.WriteLine();

    Console.WriteLine("--- skill category distribution (real records only): ---");
    foreach (var kv in catDist.OrderBy(x => x.Key))
        Console.WriteLine($"  category={kv.Key}: {kv.Value}");
    Console.WriteLine();

    Console.WriteLine("--- skill type byte (+520) distribution (real): ---");
    foreach (var kv in typeDist.OrderBy(x => x.Key))
        Console.WriteLine($"  type=0x{kv.Key:X2}: {kv.Value}");
    Console.WriteLine();

    Console.WriteLine($"--- +260 constant 0x30000000 in real records: {f260_hold}/{realRecs} → VERDICT: {(f260_hold == realRecs ? "HOLDS — constant" : "VARIES")}");
    Console.WriteLine();

    Console.WriteLine("--- +1176 f32 multiplier distribution (real, corrected-variable field): ---");
    foreach (var kv in f1176Dist.OrderByDescending(x => x.Value))
        Console.WriteLine($"  f={kv.Key}: {kv.Value}");
    Console.WriteLine($"  Distinct values: {f1176Dist.Count} → VERDICT: {(f1176Dist.Count > 1 ? "VARIES (CONFIRMED correction)" : "CONSTANT (original spec holds)")}");
    Console.WriteLine();

    Console.WriteLine("--- +1306 u16 enum distribution (real, corrected-variable field): ---");
    foreach (var kv in f1306Dist.OrderByDescending(x => x.Value))
        Console.WriteLine($"  val={kv.Key}: {kv.Value}");
    Console.WriteLine($"  Distinct values: {f1306Dist.Count} → VERDICT: {(f1306Dist.Count > 1 ? "VARIES (CONFIRMED correction)" : "CONSTANT")}");
    Console.WriteLine();

    Console.WriteLine("--- +1328 u32 stance bitmask distribution (real, corrected-variable field): ---");
    foreach (var kv in f1328Dist.OrderByDescending(x => x.Value))
        Console.WriteLine($"  0x{kv.Key:X8}: {kv.Value}");
    Console.WriteLine($"  Distinct values: {f1328Dist.Count} → VERDICT: {(f1328Dist.Count > 1 ? "VARIES (CONFIRMED correction)" : "CONSTANT")}");
    Console.WriteLine();

    Console.WriteLine("--- +1304 u16 motion index distribution (real): ---");
    foreach (var kv in f1304Dist.OrderByDescending(x => x.Value).Take(20))
        Console.WriteLine($"  val={kv.Key}: {kv.Value}");
    Console.WriteLine($"  Distinct: {f1304Dist.Count}");
    Console.WriteLine();

    Console.WriteLine("--- +1372 u32 cooldown distribution (real, top 10): ---");
    foreach (var kv in f1372Dist.OrderByDescending(x => x.Value).Take(10))
        Console.WriteLine($"  val={kv.Key}: {kv.Value}");
    Console.WriteLine();

    Console.WriteLine("--- +1412 f32 range distribution (real, top 10): ---");
    foreach (var kv in f1412Dist.OrderByDescending(x => x.Value).Take(10))
        Console.WriteLine($"  val={kv.Key}: {kv.Value}");
    Console.WriteLine();
}

// ═══════════════════════════════════════════════════════════════════════
// 4. mobs.scr
// ═══════════════════════════════════════════════════════════════════════
Console.WriteLine("════════════════════════════════════════════════════════");
Console.WriteLine("4. mobs.scr");
Console.WriteLine("════════════════════════════════════════════════════════");

var mobsBytes = vfs.GetFileContent("data/script/mobs.scr").ToArray();
Console.WriteLine($"File size: {mobsBytes.Length:N0} bytes (0x{mobsBytes.Length:X})");

const int MOBS_STRIDE = 488;

{
    int fileSize = mobsBytes.Length;
    int recs = fileSize / MOBS_STRIDE;
    int leftover = fileSize % MOBS_STRIDE;

    Console.WriteLine($"Stride {MOBS_STRIDE}: {recs} records, leftover={leftover}");
    Console.WriteLine($"Stride-division: {(leftover == 0 ? "CONFIRMED" : $"FAILS — {leftover} residual")}");
    Console.WriteLine();

    // Per-field tracking
    // Confirmed fields: +0 u16 mob_id, +2 char[] primary name, +324 u8 mob type
    // UNVERIFIED: +52 u16, +60 f32 (corrected variable), +188 f32 (corrected variable),
    //             +244 i32 mob level, +248 u32 spawn timer, +272 6×f32 (corrected variable),
    //             +296..+308 spawn-variance quads (CONFIRMED 1.0/0.95/1.05/0.95),
    //             +316 f32 scale, +320 f32 scaleB, +328 f32 40/35, +332 f32 80/30
    var mobIdDist = new Dictionary<ushort, int>();
    var field52Dist = new Dictionary<ushort, int>();
    var field60Dist = new Dictionary<string, int>();
    var field188Dist = new Dictionary<string, int>();
    var field244Dist = new Dictionary<int, int>();
    var field248Dist = new Dictionary<uint, int>();
    var typeDist = new Dictionary<byte, int>();

    // Spawn variance exact-value counters
    int spawnVar296_10 = 0;  // +296 == 1.0
    int spawnVar300_095 = 0; // +300 == 0.95
    int spawnVar304_105 = 0; // +304 == 1.05
    int spawnVar308_095 = 0; // +308 == 0.95

    var field316Dist = new Dictionary<string, int>();
    var field320Dist = new Dictionary<string, int>();
    var field328Dist = new Dictionary<string, int>();
    var field332Dist = new Dictionary<string, int>();
    var field396Dist = new Dictionary<string, int>();
    var field400Dist = new Dictionary<string, int>();
    var field444Dist = new Dictionary<string, int>();
    var field448Dist = new Dictionary<string, int>();
    var field452Dist = new Dictionary<string, int>();

    int secondaryNameNonEmpty = 0;
    var secondaryZoneDistinct = new HashSet<string>();

    for (int r = 0; r < recs; r++)
    {
        int off = r * MOBS_STRIDE;
        var rec = new ReadOnlySpan<byte>(mobsBytes, off, MOBS_STRIDE);

        ushort mobId = U16(rec, 0);
        if (!mobIdDist.TryAdd(mobId, 1)) mobIdDist[mobId]++;

        // +2 primary name, +19 secondary name (zone label)
        string secName = Str(rec, 19, 32, cp949);
        if (secName.Length > 0)
        {
            secondaryNameNonEmpty++;
            secondaryZoneDistinct.Add(secName);
        }

        // +52 u16
        ushort f52 = U16(rec, 52);
        if (!field52Dist.TryAdd(f52, 1)) field52Dist[f52]++;

        // +60 f32 (corrected variable)
        float f60 = F32(rec, 60);
        string f60b = IsPlausibleFloat(f60) ? $"{f60:G4}" : "NaN/Inf";
        if (!field60Dist.TryAdd(f60b, 1)) field60Dist[f60b]++;

        // +188 f32 (corrected variable)
        float f188 = F32(rec, 188);
        string f188b = IsPlausibleFloat(f188) ? $"{f188:G4}" : "NaN/Inf";
        if (!field188Dist.TryAdd(f188b, 1)) field188Dist[f188b]++;

        // +244 i32 mob level
        int f244 = I32(rec, 244);
        string f244key = f244 < -1 ? "<-1" : f244 == -1 ? "-1" : f244 <= 10 ? $"{f244}" : f244 <= 50 ? "11-50" : f244 <= 150 ? "51-150" : f244 <= 300 ? "151-300" : ">300";
        if (!field244Dist.TryAdd(f244, 1)) field244Dist[f244]++;

        // +248 u32 spawn timer
        uint f248 = U32(rec, 248);
        // bucket
        string f248key = f248 == 0 ? "0" : f248 <= 60 ? "1-60s" : f248 <= 300 ? "61-300s" : f248 <= 3600 ? "301-3600s" : ">3600s";
        // just track non-zero
        if (!field248Dist.TryAdd(f248, 1)) field248Dist[f248]++;

        // +324 mob type
        byte mobType = rec[324];
        if (!typeDist.TryAdd(mobType, 1)) typeDist[mobType]++;

        // Spawn variance +296..+308
        float sv296 = F32(rec, 296);
        float sv300 = F32(rec, 300);
        float sv304 = F32(rec, 304);
        float sv308 = F32(rec, 308);
        if (sv296 == 1.0f) spawnVar296_10++;
        if (sv300 == 0.95f) spawnVar300_095++;
        if (sv304 == 1.05f) spawnVar304_105++;
        if (sv308 == 0.95f) spawnVar308_095++;

        // Float fields: +316, +320, +328, +332, +396, +400, +444, +448, +452
        void AddF(Dictionary<string, int> d, float f) { string k = IsPlausibleFloat(f) ? $"{f:G5}" : "NaN/Inf"; if (!d.TryAdd(k,1)) d[k]++; }
        AddF(field316Dist, F32(rec, 316));
        AddF(field320Dist, F32(rec, 320));
        AddF(field328Dist, F32(rec, 328));
        AddF(field332Dist, F32(rec, 332));
        AddF(field396Dist, F32(rec, 396));
        AddF(field400Dist, F32(rec, 400));
        AddF(field444Dist, F32(rec, 444));
        AddF(field448Dist, F32(rec, 448));
        AddF(field452Dist, F32(rec, 452));
    }

    Console.WriteLine($"--- Mob ID range: {mobIdDist.Keys.Min()}..{mobIdDist.Keys.Max()}, {mobIdDist.Count} distinct ---");
    Console.WriteLine();

    Console.WriteLine($"--- Secondary name (+19) = zone/area label: ---");
    Console.WriteLine($"  Non-empty: {secondaryNameNonEmpty}/{recs}");
    Console.WriteLine($"  Distinct zone labels: {secondaryZoneDistinct.Count}");
    Console.WriteLine($"  VERDICT: {(secondaryZoneDistinct.Count > 1 ? "VARIES — confirmed spawn-zone label candidate" : "CONSTANT")}");
    Console.WriteLine();

    Console.WriteLine($"--- +52 u16 distribution (top 10): ---");
    foreach (var kv in field52Dist.OrderByDescending(x => x.Value).Take(10))
        Console.WriteLine($"  0x{kv.Key:X4}: {kv.Value}");
    Console.WriteLine($"  Distinct: {field52Dist.Count} → VERDICT: {(field52Dist.Count > 1 ? "VARIES" : "CONSTANT")}");
    Console.WriteLine();

    Console.WriteLine($"--- +60 f32 distribution (corrected-variable, top 10): ---");
    foreach (var kv in field60Dist.OrderByDescending(x => x.Value).Take(10))
        Console.WriteLine($"  {kv.Key}: {kv.Value}");
    Console.WriteLine($"  Distinct: {field60Dist.Count} → VERDICT: {(field60Dist.Count > 1 ? "VARIES (spec correction confirmed)" : "CONSTANT (spec correction refuted)")}");
    Console.WriteLine();

    Console.WriteLine($"--- +188 f32 distribution (corrected-variable, top 10): ---");
    foreach (var kv in field188Dist.OrderByDescending(x => x.Value).Take(10))
        Console.WriteLine($"  {kv.Key}: {kv.Value}");
    Console.WriteLine($"  Distinct: {field188Dist.Count} → VERDICT: {(field188Dist.Count > 1 ? "VARIES (spec correction confirmed)" : "CONSTANT (spec correction refuted)")}");
    Console.WriteLine();

    // mob level distribution (condensed)
    var lvlBuckets = new Dictionary<string, int>();
    foreach (var kv in field244Dist)
    {
        string bkt = kv.Key < -1 ? "<-1" : kv.Key == -1 ? "-1" : kv.Key == 0 ? "0" : kv.Key <= 10 ? "1-10" : kv.Key <= 50 ? "11-50" : kv.Key <= 150 ? "51-150" : kv.Key <= 300 ? "151-300" : ">300";
        if (!lvlBuckets.TryAdd(bkt, kv.Value)) lvlBuckets[bkt] += kv.Value;
    }
    Console.WriteLine($"--- +244 mob level distribution (bucketed): ---");
    foreach (var kv in lvlBuckets.OrderBy(x => x.Key))
        Console.WriteLine($"  level bucket '{kv.Key}': {kv.Value}");
    Console.WriteLine($"  Distinct level values: {field244Dist.Count} → VERDICT: VARIES");
    Console.WriteLine();

    var spawnTimerBuckets = new Dictionary<string, int>();
    foreach (var kv in field248Dist)
    {
        string bkt = kv.Key == 0 ? "0" : kv.Key <= 60 ? "1-60s" : kv.Key <= 300 ? "61-300s" : kv.Key <= 3600 ? "301-3600s" : ">3600s";
        if (!spawnTimerBuckets.TryAdd(bkt, kv.Value)) spawnTimerBuckets[bkt] += kv.Value;
    }
    Console.WriteLine($"--- +248 spawn timer distribution (bucketed): ---");
    foreach (var kv in spawnTimerBuckets.OrderBy(x => x.Key))
        Console.WriteLine($"  timer bucket '{kv.Key}': {kv.Value}");
    Console.WriteLine($"  Distinct timer values: {field248Dist.Count} → VERDICT: VARIES");
    Console.WriteLine();

    Console.WriteLine($"--- +324 mob type distribution: ---");
    foreach (var kv in typeDist.OrderBy(x => x.Key))
        Console.WriteLine($"  type={kv.Key}: {kv.Value}");
    Console.WriteLine();

    Console.WriteLine($"--- Spawn variance +296..+308 (spec: exactly (1.0, 0.95, 1.05, 0.95)): ---");
    Console.WriteLine($"  +296==1.0:  {spawnVar296_10}/{recs}");
    Console.WriteLine($"  +300==0.95: {spawnVar300_095}/{recs}");
    Console.WriteLine($"  +304==1.05: {spawnVar304_105}/{recs}");
    Console.WriteLine($"  +308==0.95: {spawnVar308_095}/{recs}");
    bool allSpawnVarHold = spawnVar296_10 == recs && spawnVar300_095 == recs && spawnVar304_105 == recs && spawnVar308_095 == recs;
    Console.WriteLine($"  VERDICT: {(allSpawnVarHold ? "HOLDS — all 4 spawn-variance values constant across all records" : "VARIES — spec claim not confirmed")}");
    Console.WriteLine();

    Console.WriteLine($"--- Float fields (top values per field): ---");
    void PrintF(string name, Dictionary<string, int> d, int total)
    {
        Console.Write($"  {name} top-5: ");
        Console.WriteLine(string.Join(", ", d.OrderByDescending(x => x.Value).Take(5).Select(x => $"{x.Key}×{x.Value}")));
        Console.WriteLine($"    Distinct: {d.Count} → {(d.Count > 1 ? "VARIES" : "CONSTANT")}");
    }
    PrintF("+316", field316Dist, recs);
    PrintF("+320", field320Dist, recs);
    PrintF("+328", field328Dist, recs);
    PrintF("+332", field332Dist, recs);
    PrintF("+396", field396Dist, recs);
    PrintF("+400", field400Dist, recs);
    PrintF("+444", field444Dist, recs);
    PrintF("+448", field448Dist, recs);
    PrintF("+452", field452Dist, recs);
    Console.WriteLine();
}

// ═══════════════════════════════════════════════════════════════════════
// 5. npcs.scr
// ═══════════════════════════════════════════════════════════════════════
Console.WriteLine("════════════════════════════════════════════════════════");
Console.WriteLine("5. npcs.scr");
Console.WriteLine("════════════════════════════════════════════════════════");

var npcsBytes = vfs.GetFileContent("data/script/npcs.scr").ToArray();
Console.WriteLine($"File size: {npcsBytes.Length:N0} bytes (0x{npcsBytes.Length:X})");

const int NPCS_STRIDE = 1916;

{
    int fileSize = npcsBytes.Length;
    int recs = fileSize / NPCS_STRIDE;
    int leftover = fileSize % NPCS_STRIDE;

    Console.WriteLine($"Stride {NPCS_STRIDE}: {recs} records, leftover={leftover}");
    Console.WriteLine($"Stride-division: {(leftover == 0 ? "CONFIRMED" : $"FAILS — {leftover} residual")}");
    Console.WriteLine();

    // +0 u16 NPC ID
    var idDist = new Dictionary<ushort, int>();
    // Try to detect name encoding: check +2 for CP949 vs UCS-2
    int cp949Start = 0;   // records where byte@+2 is a CP949 lead byte (0x81..0xFE)
    int ucs2Start = 0;    // records where +2/+3 looks like a UCS-2 (ASCII+0x00)
    int zeroAt2 = 0;
    var field2Dist = new Dictionary<ushort, int>(); // first 2 bytes at +2

    for (int r = 0; r < recs; r++)
    {
        int off = r * NPCS_STRIDE;
        var rec = new ReadOnlySpan<byte>(npcsBytes, off, NPCS_STRIDE);

        ushort npcId = U16(rec, 0);
        if (!idDist.TryAdd(npcId, 1)) idDist[npcId]++;

        byte b2 = rec[2];
        byte b3 = rec[3];
        if (b2 == 0 && b3 == 0) zeroAt2++;
        else if (b2 >= 0x81 && b2 <= 0xFE) cp949Start++;
        else if (b3 == 0 && b2 >= 0x20 && b2 < 0x80) ucs2Start++;

        ushort w2 = U16(rec, 2);
        if (!field2Dist.TryAdd(w2, 1)) field2Dist[w2]++;
    }

    Console.WriteLine($"--- NPC ID range: {idDist.Keys.Min()}..{idDist.Keys.Max()}, {idDist.Count} distinct ---");
    Console.WriteLine();

    Console.WriteLine($"--- Encoding probe at +2 (first byte of body): ---");
    Console.WriteLine($"  CP949 lead byte (0x81..0xFE): {cp949Start}/{recs}");
    Console.WriteLine($"  UCS-2 ASCII pattern (b2=printable, b3=0x00): {ucs2Start}/{recs}");
    Console.WriteLine($"  Zero at +2: {zeroAt2}/{recs}");
    Console.WriteLine($"  VERDICT: {(cp949Start > ucs2Start ? "LIKELY CP949" : (ucs2Start > cp949Start ? "LIKELY UCS-2" : "AMBIGUOUS — encoding STILL-OPAQUE"))}");
    Console.WriteLine();

    Console.WriteLine("--- First word @+2 top-10 distinct values: ---");
    foreach (var kv in field2Dist.OrderByDescending(x => x.Value).Take(10))
        Console.WriteLine($"  0x{kv.Key:X4}: {kv.Value}");
    Console.WriteLine();

    // Try to decode a few NPC names both ways to see which makes sense
    Console.WriteLine("--- First 5 records decoded both ways: ---");
    for (int r = 0; r < Math.Min(5, recs); r++)
    {
        int off = r * NPCS_STRIDE;
        ushort npcId = U16(npcsBytes, off);
        // CP949: read from +2 as null-terminated CP949
        string asCP949 = Str(new ReadOnlySpan<byte>(npcsBytes, off + 2, Math.Min(64, NPCS_STRIDE - 2)), 0, 64, cp949);
        // UCS-2: read from +2 as null-terminated UCS-2
        int ucs2End = 0;
        while (off + 2 + ucs2End + 1 < npcsBytes.Length && !(npcsBytes[off+2+ucs2End] == 0 && npcsBytes[off+2+ucs2End+1] == 0))
            ucs2End += 2;
        string asUCS2 = ucs2End > 0 ? Encoding.Unicode.GetString(npcsBytes, off+2, Math.Min(ucs2End, 64)) : "(empty)";

        Console.WriteLine($"  rec[{r}] id={npcId}: CP949='{asCP949}' | UCS-2='{asUCS2}'");
    }
    Console.WriteLine();

    // Check if file might also have a _newserver variant
    bool hasNewServer = false;
    try { hasNewServer = vfs.Contains("data/script_newserver/npcs.scr"); } catch { }
    Console.WriteLine($"  data/script_newserver/npcs.scr exists: {hasNewServer}");
    if (hasNewServer)
    {
        var nsBytes = vfs.GetFileContent("data/script_newserver/npcs.scr").ToArray();
        Console.WriteLine($"  New-server npcs.scr size: {nsBytes.Length:N0} bytes, recs={nsBytes.Length/NPCS_STRIDE} (leftover={nsBytes.Length%NPCS_STRIDE})");
    }
    Console.WriteLine();
}

Console.WriteLine("=== DONE ===");
