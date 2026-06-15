// THROWAWAY harness — xdb-scan — V6 Campaign VFS-MASTERY
// Never commit, never add to solution.
// Decodes all .xdb tables and produces per-field distribution stats.

using System.Text;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var cp949 = Encoding.GetEncoding(949);

string inf = @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata\data.inf";
string vfs = @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata\data\data.vfs";

// Allow overrides via args
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--inf") inf = args[i + 1];
    if (args[i] == "--vfs") vfs = args[i + 1];
}

using var archive = MappedVfsArchive.Open(inf, vfs);

Console.WriteLine("=== XDB SCAN — V6 Campaign VFS-MASTERY ===");
Console.WriteLine();

// ── actor_size.xdb ─────────────────────────────────────────────────────────
ScanActorSize(archive);

// ── buff_icon_position.xdb ─────────────────────────────────────────────────
ScanBuffIconPosition(archive);

// ── effectscale.xdb ────────────────────────────────────────────────────────
ScanEffectScale(archive);

// ── vehicle.xdb ────────────────────────────────────────────────────────────
ScanVehicle(archive);

// ── creature_item.xdb ──────────────────────────────────────────────────────
ScanCreatureItem(archive);

// ── msg.xdb ────────────────────────────────────────────────────────────────
ScanMsgXdb(archive, cp949);

return;

// ============================================================
static void ScanActorSize(MappedVfsArchive archive)
{
    const string path = "data/script/actor_size.xdb";
    const int stride = 12;
    var data = archive.GetFileContent(path);
    int fileSize = data.Length;
    int residual = fileSize % stride;
    int count = fileSize / stride;

    Console.WriteLine($"── actor_size.xdb ──────────────────────────────────────");
    Console.WriteLine($"  file_size={fileSize}  stride={stride}  records={count}  residual={residual}");

    var span = data.Span;
    float minA = float.MaxValue, maxA = float.MinValue;
    float minB = float.MaxValue, maxB = float.MinValue;
    int zeroA = 0, zeroB = 0;
    var distinctA = new HashSet<float>();
    var distinctB = new HashSet<float>();

    for (int i = 0; i < count; i++)
    {
        int off = i * stride;
        uint id = ReadU32(span, off);
        float a = ReadF32(span, off + 4);
        float b = ReadF32(span, off + 8);

        if (a == 0f) zeroA++;
        if (b == 0f) zeroB++;
        if (a < minA) minA = a;
        if (a > maxA) maxA = a;
        if (b < minB) minB = b;
        if (b > maxB) maxB = b;
        distinctA.Add(a);
        distinctB.Add(b);

        Console.WriteLine($"    rec[{i:D2}] actor_kind_id={id}  scale_a={a:F4}  scale_b={b:F4}");
    }
    Console.WriteLine($"  scale_a: distinct={distinctA.Count}  min={minA:F4}  max={maxA:F4}  zero={zeroA}/{count}");
    Console.WriteLine($"  scale_b: distinct={distinctB.Count}  min={minB:F4}  max={maxB:F4}  zero={zeroB}/{count}");
    Console.WriteLine();
}

static void ScanBuffIconPosition(MappedVfsArchive archive)
{
    const string path = "data/script/buff_icon_position.xdb";
    const int stride = 12;
    var data = archive.GetFileContent(path);
    int fileSize = data.Length;
    int residual = fileSize % stride;
    int count = fileSize / stride;

    Console.WriteLine($"── buff_icon_position.xdb ──────────────────────────────");
    Console.WriteLine($"  file_size={fileSize}  stride={stride}  records={count}  residual={residual}");

    var span = data.Span;
    uint minId = uint.MaxValue, maxId = 0;
    var distinctX = new Dictionary<uint, int>();
    var distinctY = new Dictionary<uint, int>();
    var distinctId = new HashSet<uint>();

    // check contiguity
    bool allContiguous = true;
    uint prevId = 0;

    for (int i = 0; i < count; i++)
    {
        int off = i * stride;
        uint buffId = ReadU32(span, off);
        uint spriteX = ReadU32(span, off + 4);
        uint spriteY = ReadU32(span, off + 8);

        if (buffId < minId) minId = buffId;
        if (buffId > maxId) maxId = buffId;
        distinctId.Add(buffId);
        IncrDict(distinctX, spriteX);
        IncrDict(distinctY, spriteY);

        if (i > 0 && buffId != prevId + 1) allContiguous = false;
        prevId = buffId;
    }

    // Print all records (only 134)
    for (int i = 0; i < count; i++)
    {
        int off = i * stride;
        uint buffId = ReadU32(span, off);
        uint spriteX = ReadU32(span, off + 4);
        uint spriteY = ReadU32(span, off + 8);
        Console.WriteLine($"    rec[{i:D3}] buff_id={buffId}  sprite_x={spriteX}  sprite_y={spriteY}");
    }

    Console.WriteLine($"  buff_id: min={minId}  max={maxId}  distinct={distinctId.Count}  contiguous={allContiguous}");
    Console.WriteLine($"  sprite_x: distinct={distinctX.Count}  values={string.Join(",", distinctX.Keys.OrderBy(k => k))}");
    Console.WriteLine($"  sprite_y: distinct={distinctY.Count}  values={string.Join(",", distinctY.Keys.OrderBy(k => k))}");
    Console.WriteLine();
}

static void ScanEffectScale(MappedVfsArchive archive)
{
    const string path = "data/script/effectscale.xdb";
    const int stride = 8;
    var data = archive.GetFileContent(path);
    int fileSize = data.Length;
    int residual = fileSize % stride;
    int count = fileSize / stride;

    Console.WriteLine($"── effectscale.xdb ─────────────────────────────────────");
    Console.WriteLine($"  file_size={fileSize}  stride={stride}  records={count}  residual={residual}");

    var span = data.Span;
    for (int i = 0; i < count; i++)
    {
        int off = i * stride;
        uint key = ReadU32(span, off);
        float scale = ReadF32(span, off + 4);
        uint hi16 = key >> 16;
        uint lo16 = key & 0xFFFF;
        Console.WriteLine($"    rec[{i}] effect_key=0x{key:X8}  hi16=0x{hi16:X4}  lo16=0x{lo16:X4}  scale_factor={scale:F4}");
    }
    Console.WriteLine();
}

static void ScanVehicle(MappedVfsArchive archive)
{
    const string path = "data/script/vehicle.xdb";
    const int stride = 52;
    var data = archive.GetFileContent(path);
    int fileSize = data.Length;
    int residual = fileSize % stride;
    int count = fileSize / stride;

    Console.WriteLine($"── vehicle.xdb ─────────────────────────────────────────");
    Console.WriteLine($"  file_size={fileSize}  stride={stride}  records={count}  residual={residual}");

    var span = data.Span;
    var tagAValues = new Dictionary<uint, int>();
    var tagBValues = new Dictionary<uint, int>();
    bool tagBConstant = true;
    uint firstTagB = ReadU32(span, 12);

    for (int i = 0; i < count; i++)
    {
        int off = i * stride;
        uint vehicleId = ReadU32(span, off);
        uint itemId = ReadU32(span, off + 4);
        uint tagA = ReadU32(span, off + 8);
        uint tagB = ReadU32(span, off + 12);
        float p0 = ReadF32(span, off + 16);
        float p1 = ReadF32(span, off + 20);
        float p2 = ReadF32(span, off + 24);
        float p3 = ReadF32(span, off + 28);
        float p4 = ReadF32(span, off + 32);
        float p5 = ReadF32(span, off + 36);
        float p6 = ReadF32(span, off + 40);
        float p7 = ReadF32(span, off + 44);
        float p8 = ReadF32(span, off + 48);

        IncrDict(tagAValues, tagA);
        IncrDict(tagBValues, tagB);
        if (tagB != firstTagB) tagBConstant = false;

        Console.WriteLine($"    rec[{i:D2}] id={vehicleId} item={itemId} tagA=0x{tagA:X8} tagB=0x{tagB:X8} p0={p0:F2} p1={p1:F2} p2={p2:F2} p3={p3:F2} p4={p4:F2} p5={p5:F2} p6={p6:F2} p7={p7:F2} p8={p8:F2}");
    }

    Console.WriteLine($"  tag_a: distinct={tagAValues.Count}  values: {string.Join(", ", tagAValues.Select(kv => $"0x{kv.Key:X8}×{kv.Value}"))}");
    Console.WriteLine($"  tag_b: constant={tagBConstant}  value=0x{firstTagB:X8}  distinct={tagBValues.Count}");
    Console.WriteLine();
}

static void ScanCreatureItem(MappedVfsArchive archive)
{
    const string path = "data/script/creature_item.xdb";
    const int stride = 48;
    var data = archive.GetFileContent(path);
    int fileSize = data.Length;
    int residual = fileSize % stride;
    int count = fileSize / stride;

    Console.WriteLine($"── creature_item.xdb ───────────────────────────────────");
    Console.WriteLine($"  file_size={fileSize}  stride={stride}  records={count}  residual={residual}");

    var span = data.Span;

    uint minKey = uint.MaxValue, maxKey = 0;
    uint minItem = uint.MaxValue, maxItem = 0;
    var distinctRadius = new Dictionary<float, int>();
    var distinctProb = new Dictionary<float, int>();
    int probNonZero = 0;
    int f0NonZero = 0, f1NonZero = 0, f2NonZero = 0, f3NonZero = 0, f4NonZero = 0, f5NonZero = 0;
    int flag0Ones = 0, flag1Ones = 0, flag2Ones = 0, flag3Ones = 0;
    var probConst100 = 0;
    var distinctProbU32 = new Dictionary<uint, int>();

    for (int i = 0; i < count; i++)
    {
        int off = i * stride;
        uint ck = ReadU32(span, off);
        uint item = ReadU32(span, off + 4);
        float af0 = ReadF32(span, off + 8);
        float af1 = ReadF32(span, off + 12);
        float af2 = ReadF32(span, off + 16);
        float af3 = ReadF32(span, off + 20);
        float af4 = ReadF32(span, off + 24);
        float af5 = ReadF32(span, off + 28);
        float radius = ReadF32(span, off + 32);
        float probF = ReadF32(span, off + 36);
        byte fl0 = span[off + 40];
        byte fl1 = span[off + 41];
        byte fl2 = span[off + 42];
        byte fl3 = span[off + 43];
        uint probU = ReadU32(span, off + 44);

        if (ck < minKey) minKey = ck;
        if (ck > maxKey) maxKey = ck;
        if (item < minItem) minItem = item;
        if (item > maxItem) maxItem = item;
        IncrDictF(distinctRadius, radius);
        IncrDictF(distinctProb, probF);
        if (probF != 0f) probNonZero++;
        if (af0 != 0f) f0NonZero++;
        if (af1 != 0f) f1NonZero++;
        if (af2 != 0f) f2NonZero++;
        if (af3 != 0f) f3NonZero++;
        if (af4 != 0f) f4NonZero++;
        if (af5 != 0f) f5NonZero++;
        if (fl0 == 1) flag0Ones++;
        if (fl1 == 1) flag1Ones++;
        if (fl2 == 1) flag2Ones++;
        if (fl3 == 1) flag3Ones++;
        if (probU == 100) probConst100++;
        IncrDict(distinctProbU32, probU);
    }

    Console.WriteLine($"  creature_key: min={minKey}  max={maxKey}  span={maxKey - minKey}");
    Console.WriteLine($"  item_id: min={minItem}  max={maxItem}");
    Console.WriteLine($"  attach_f0..f5 non-zero: {f0NonZero} {f1NonZero} {f2NonZero} {f3NonZero} {f4NonZero} {f5NonZero} (of {count})");
    Console.WriteLine($"  scale_or_radius: distinct values: {string.Join(", ", distinctRadius.Select(kv => $"{kv.Key:F1}×{kv.Value}"))}");
    Console.WriteLine($"  attach_prob_f32: non-zero={probNonZero}/{count}  distinct={distinctProb.Count}");
    Console.WriteLine($"  attach_prob_f32 values: {string.Join(", ", distinctProb.Keys.OrderBy(k => k).Select(k => $"{k:F2}×{distinctProb[k]}"))}");
    Console.WriteLine($"  flag_0: ones={flag0Ones}/{count}  zeros={(count - flag0Ones)}/{count}");
    Console.WriteLine($"  flag_1: ones={flag1Ones}/{count}  zeros={(count - flag1Ones)}/{count}");
    Console.WriteLine($"  flag_2: ones={flag2Ones}/{count}  zeros={(count - flag2Ones)}/{count}");
    Console.WriteLine($"  flag_3: ones={flag3Ones}/{count}  zeros={(count - flag3Ones)}/{count}");
    Console.WriteLine($"  probability_u32: const-100={probConst100}/{count}  distinct_values={distinctProbU32.Count}");
    if (distinctProbU32.Count > 1)
        Console.WriteLine($"  probability_u32 values: {string.Join(", ", distinctProbU32.Select(kv => $"{kv.Key}×{kv.Value}"))}");
    Console.WriteLine();
}

static void ScanMsgXdb(MappedVfsArchive archive, Encoding cp949)
{
    const string path = "data/script/msg.xdb";
    const int stride = 516;
    var data = archive.GetFileContent(path);
    int fileSize = data.Length;
    int residual = fileSize % stride;
    int count = fileSize / stride;

    Console.WriteLine($"── msg.xdb ─────────────────────────────────────────────");
    Console.WriteLine($"  file_size={fileSize}  stride={stride}  records={count}  residual={residual}");

    var span = data.Span;

    // Validate ordering is ascending
    int orderViolations = 0;
    uint prevId2 = 0;
    uint minId = uint.MaxValue, maxId = 0;
    int emptyStrings = 0;
    int maxStrLen = 0;
    bool anyFullBuffer = false;

    for (int i = 0; i < count; i++)
    {
        int off = i * stride;
        uint id = ReadU32(span, off);

        if (id < minId) minId = id;
        if (id > maxId) maxId = id;
        if (i > 0 && id < prevId2) orderViolations++;
        prevId2 = id;

        // Find null terminator in the 512-byte string buffer
        int nullPos = -1;
        for (int j = 0; j < 512; j++)
        {
            if (span[off + 4 + j] == 0)
            {
                nullPos = j;
                break;
            }
        }
        if (nullPos == -1)
        {
            anyFullBuffer = true;
            nullPos = 512;
        }
        if (nullPos == 0) emptyStrings++;
        if (nullPos > maxStrLen) maxStrLen = nullPos;
    }

    Console.WriteLine($"  caption_id: min={minId}  max={maxId}");
    Console.WriteLine($"  order_violations={orderViolations} (0=ascending order confirmed)");
    Console.WriteLine($"  empty_strings={emptyStrings}/{count}");
    Console.WriteLine($"  max_string_bytes={maxStrLen}");
    Console.WriteLine($"  any_full_512_buffer={anyFullBuffer}");

    // Spot-check known caption IDs from spec
    int[] knownIds = [
        4001, 4002, 4021, 4022, 4023, 4024,
        2206, 14001, 14002, 46001, 46002, 48001, 48003, 48004, 48005, 63030
    ];
    Console.WriteLine($"  spot-check known caption IDs (existence only, no text):");
    foreach (int checkId in knownIds)
    {
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            int off = i * stride;
            uint id = ReadU32(span, off);
            if (id == (uint)checkId) { found = true; break; }
            if (id > (uint)checkId) break; // ascending order
        }
        Console.WriteLine($"    caption_id={checkId}: {(found ? "FOUND" : "MISSING")}");
    }
    Console.WriteLine();
}

// ─── helpers ─────────────────────────────────────────────────────────────────

static uint ReadU32(ReadOnlySpan<byte> s, int off)
    => (uint)(s[off] | (s[off + 1] << 8) | (s[off + 2] << 16) | (s[off + 3] << 24));

static float ReadF32(ReadOnlySpan<byte> s, int off)
{
    uint bits = ReadU32(s, off);
    return System.Runtime.CompilerServices.Unsafe.As<uint, float>(ref bits);
}

static void IncrDict<T>(Dictionary<T, int> d, T k) where T : notnull
{
    d.TryGetValue(k, out int c);
    d[k] = c + 1;
}

static void IncrDictF(Dictionary<float, int> d, float k)
{
    d.TryGetValue(k, out int c);
    d[k] = c + 1;
}
