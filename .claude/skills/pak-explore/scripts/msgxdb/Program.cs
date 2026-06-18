// THROWAWAY harness — analyze msg.xdb structure and content
// Never add to MartialHeroes.slnx, never commit.
using System.Text;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var cp949 = Encoding.GetEncoding(949);

string repoRoot = @"C:\Users\Arius\RiderProjects\MartialHeroes";
string infPath = Path.Combine(repoRoot, "05.Presentation/MartialHeroes.Client.Godot/clientdata/data.inf");
string vfsPath = Path.Combine(repoRoot, "05.Presentation/MartialHeroes.Client.Godot/clientdata/data/data.vfs");
if (!File.Exists(infPath)) infPath = @"D:\MartialHeroesClient\data.inf";
if (!File.Exists(vfsPath)) vfsPath = @"D:\MartialHeroesClient\data\data.vfs";

using var archive = MappedVfsArchive.Open(infPath, vfsPath);
var data = archive.GetFileContent("data/script/msg.xdb");
var span = data.Span;

const int STRIDE = 516; // 0x204
int totalRecords = data.Length / STRIDE; // 2644
Console.WriteLine($"msg.xdb: {data.Length} bytes, stride={STRIDE}, records={totalRecords}");

// Each record: 516 bytes, padded with 0xEE
// Layout observed: at the start of the first record: 01 00 00 00 20 25 73...
// The '01 00 00 00' at record 0 looks like a uint32 = 1
// But at record 1 (offset 0x204): 00 00 00 20 25 64...
// The zero-bytes before the string are null padding BEFORE the string in some records
// This suggests the structure might be:
//   offset 0: uint32 LE = msg_id (or flags), but record 0 has id=1 while record 1 has ...
// Let's check record 1 more carefully:
// offset 0x204: bytes are the non-EE run starts at 0x204 - meaning bytes at 0x201,0x202,0x203 are 0xEE
// But the run shows [000204..00020E] with content '\0\0\0 %d은\0'
// So at 0x204=0x00, 0x205=0x00, 0x206=0x00, 0x207=0x20, ...
// This means: 3 leading nulls then the string content
// For record 0: 0x000=0x01, 0x001=0x00, 0x002=0x00, 0x003=0x00, 0x004=0x20...
// That's 01 00 00 00 = uint32 LE = 1, then string at offset 4
// For record 1: 00 00 00 20... could be uint32 LE = 0x200000 = 2097152? No.
//   OR the id is at end of record? Let's check:
//   Record 1 ends at 0x407. What's before the EE fill at 0x204+9=0x20D?
// The content at 0x204..0x20D = 00 00 00 20 25 64 C0 BA 00 (9 bytes data, then EE to 0x407)
// If id is uint32 at record[0], then record1.id = uint32(0x204) = 0x200000 = bad
// SOLUTION: the id IS encoded as the record index (0-based or 1-based)
// Record 0 -> msg_id = 1 (the '01 00 00 00' at start IS part of string or a uint32 = msg_id)
// But then for records 1..2643 the ID would just be index+1?
// Let me verify by checking a few records' first 4 bytes:

Console.WriteLine("\nFirst 4 bytes of each record (checking ID pattern):");
for (int r = 0; r < 30; r++)
{
    int off = r * STRIDE;
    uint b0 = span[off];
    uint b1 = span[off+1];
    uint b2 = span[off+2];
    uint b3 = span[off+3];
    uint idLE = BitConverter.ToUInt32(span[off..(off+4)]);

    // Find the text content (non-EE bytes after first 4)
    int textOff = off;
    // skip leading bytes that might be 00 or non-0xEE
    int te = off;
    while (te < off + STRIDE && span[te] != 0xEE) te++;
    string rawText = "";
    if (te > off)
    {
        var raw = span[off..te].ToArray();
        rawText = cp949.GetString(raw).Replace("\0", "·").Replace("\r", "").Replace("\n", "\\n");
    }
    Console.WriteLine($"  r={r,4} off=0x{off:X6} bytes=[{b0:X2} {b1:X2} {b2:X2} {b3:X2}] idLE={idLE,10} text=|{rawText}|");
}

// Extract ALL messages
Console.WriteLine("\n--- All non-empty message records ---");
var messages = new List<(int slot, uint id, string text)>();
for (int r = 0; r < totalRecords; r++)
{
    int off = r * STRIDE;
    // Find text: skip until we hit non-EE or end of record
    int te = off;
    while (te < off + STRIDE && span[te] != 0xEE) te++;
    if (te > off)
    {
        var raw = span[off..te].ToArray();
        // Trim leading 4 bytes if they look like an id (uint32)
        uint idCandidate = raw.Length >= 4 ? BitConverter.ToUInt32(raw, 0) : 0;
        string text;
        int msgStart = 0;
        // The ID candidate: check if bytes 0-3 are a plausible id (< 100000)
        // and byte 4 onwards is the string
        // But record 1 showed 00 00 00 as the leading bytes, suggesting a different layout
        // Let's just take everything as string and clean up
        text = cp949.GetString(raw).Replace("\0", "").Trim();
        messages.Add((r, (uint)r + 1, text));
    }
}

Console.WriteLine($"Non-empty slots: {messages.Count} out of {totalRecords}");

// Group by ranges and show samples
Console.WriteLine("\n--- Message sampling (every 100 slots) ---");
for (int r = 0; r < totalRecords; r += 100)
{
    int off = r * STRIDE;
    int te = off;
    while (te < off + STRIDE && span[te] != 0xEE) te++;
    if (te > off)
    {
        var raw = span[off..te].ToArray();
        string text = cp949.GetString(raw).Replace("\0", "·").Trim();
        Console.WriteLine($"  slot {r,4} (id≈{r+1,4}): |{text}|");
    }
    else
    {
        Console.WriteLine($"  slot {r,4}: (empty/EE)");
    }
}

// Show all non-empty with their slot numbers to find groupings
Console.WriteLine("\n--- All filled slots with slot# and decoded text ---");
int prevSlot = -1;
foreach (var (slot, id, text) in messages)
{
    if (slot - prevSlot > 10 && prevSlot >= 0)
        Console.WriteLine($"  ... gap of {slot-prevSlot} empty slots ...");
    Console.WriteLine($"  [{slot,4}] |{text}|");
    prevSlot = slot;
}
