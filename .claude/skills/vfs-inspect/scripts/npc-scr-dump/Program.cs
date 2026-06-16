// npc-scr-dump — THROWAWAY harness: decode data/script/npc.scr fixed-404-byte records.
// NOT a solution member. Never committed. Run: dotnet run -c Release --project <this dir>

using System;
using System.IO;
using System.Text;
using MartialHeroes.Assets.Vfs;

// CP949 (Korean) — mandatory for all client text.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var cp949 = Encoding.GetEncoding(949);

// --- Resolve client path (mirrors Dev/ClientPathResolver order) ---
static string? FindClient()
{
    var env = Environment.GetEnvironmentVariable("MH_CLIENT_DIR");
    if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;

    var cfg = "client_dir.cfg";
    if (File.Exists(cfg)) { var line = File.ReadAllText(cfg).Trim(); if (Directory.Exists(line)) return line; }

    var godotLocal = Path.Combine(
        "C:/Users/Arius/RiderProjects/MartialHeroes",
        "05.Presentation/MartialHeroes.Client.Godot/clientdata");
    if (Directory.Exists(godotLocal)) return godotLocal;

    if (Directory.Exists("D:/MartialHeroesClient")) return "D:/MartialHeroesClient";
    return null;
}

var clientDir = FindClient();
if (clientDir == null) { Console.Error.WriteLine("ERROR: client not found"); return 1; }

var infPath = Path.Combine(clientDir, "data.inf");
var vfsPath = Path.Combine(clientDir, "data/data.vfs");

if (!File.Exists(infPath) || !File.Exists(vfsPath))
{ Console.Error.WriteLine($"ERROR: inf or vfs missing under {clientDir}"); return 1; }

Console.WriteLine($"Mounted: {infPath}");

using var archive = MappedVfsArchive.Open(infPath, vfsPath);

const string NPC_PATH = "data/script/npc.scr";
if (!archive.Contains(NPC_PATH)) { Console.Error.WriteLine($"ERROR: {NPC_PATH} not in VFS"); return 1; }

var data = archive.GetFileContent(NPC_PATH);
int fileSize = data.Length;
const int STRIDE = 404; // 0x194 — hypothesis under test

Console.WriteLine($"File: {NPC_PATH}");
Console.WriteLine($"Size: {fileSize} bytes");
Console.WriteLine($"STRIDE = {STRIDE} (0x{STRIDE:X3}): multiple? {fileSize % STRIDE == 0} (mod={fileSize % STRIDE})");

int recordCount = fileSize / STRIDE;
Console.WriteLine($"Record count: {recordCount}");
Console.WriteLine();

// Helper: decode a 64-byte CP949 string field (NUL-padded).
static string DecodeField(ReadOnlySpan<byte> raw, Encoding enc)
{
    // Trim at first NUL.
    int len = raw.IndexOf((byte)0);
    if (len < 0) len = raw.Length;
    if (len == 0) return "(empty/nul)";
    // Engine treats first byte == '0' (0x30) as empty sentinel.
    if (raw[0] == 0x30) return "(EMPTY sentinel '0')";
    return enc.GetString(raw[..len]);
}

// Helper: hex dump a span.
static string HexDump(ReadOnlySpan<byte> b)
{
    var sb = new StringBuilder();
    foreach (var x in b) sb.Append($"{x:X2} ");
    return sb.ToString().TrimEnd();
}

var span = data.Span;

// --- SANITY SCAN: first 20 records — key + field-0 ---
Console.WriteLine("=== FIRST 20 RECORDS: key + field-0 (name) ===");
int scanLimit = Math.Min(20, recordCount);
for (int i = 0; i < scanLimit; i++)
{
    int offset = i * STRIDE;
    int key = BitConverter.ToInt32(span.Slice(offset, 4));
    var f0 = DecodeField(span.Slice(offset + 20, 64), cp949);
    Console.WriteLine($"  rec[{i,4}]  key={key,6}  field0={f0}");
}
Console.WriteLine();

// --- FIND RECORDS WITH KEY 1,2,3,4 ---
Console.WriteLine("=== CLASS RECORDS (key = 1,2,3,4) ===");
for (int key = 1; key <= 4; key++)
{
    // Scan all records for this key.
    int foundIdx = -1;
    for (int i = 0; i < recordCount; i++)
    {
        int off = i * STRIDE;
        int k = BitConverter.ToInt32(span.Slice(off, 4));
        if (k == key) { foundIdx = i; break; }
    }
    if (foundIdx < 0) { Console.WriteLine($"  key={key}: NOT FOUND"); continue; }

    int recOff = foundIdx * STRIDE;
    int recKey = BitConverter.ToInt32(span.Slice(recOff, 4));

    // Header tail: bytes [4..19] = 16 bytes.
    var headerTail = span.Slice(recOff + 4, 16);

    // Six string fields at [20..83], [84..147], [148..211], [212..275], [276..339], [340..403].
    string[] fields = new string[6];
    for (int fi = 0; fi < 6; fi++)
        fields[fi] = DecodeField(span.Slice(recOff + 20 + fi * 64, 64), cp949);

    Console.WriteLine($"--- key={recKey} (record index {foundIdx}) ---");
    Console.WriteLine($"  Header[4..19] hex : {HexDump(headerTail)}");
    Console.WriteLine($"  field[0] +0x14  : {fields[0]}");
    Console.WriteLine($"  field[1] +0x54  : {fields[1]}");
    Console.WriteLine($"  field[2] +0x94  : {fields[2]}");
    Console.WriteLine($"  field[3] +0xD4  : {fields[3]}");
    Console.WriteLine($"  field[4] +0x114 : {fields[4]}");
    Console.WriteLine($"  field[5] +0x154 : {fields[5]}");
    Console.WriteLine();
}

// --- Also show keys right around 1-4 to see neighbouring records ---
Console.WriteLine("=== EXTENDED SCAN: records 0..9 full dump ===");
for (int i = 0; i < Math.Min(10, recordCount); i++)
{
    int off = i * STRIDE;
    int k = BitConverter.ToInt32(span.Slice(off, 4));
    var f0 = DecodeField(span.Slice(off + 20, 64), cp949);
    var f1 = DecodeField(span.Slice(off + 84, 64), cp949);
    var f2 = DecodeField(span.Slice(off + 148, 64), cp949);
    Console.WriteLine($"  rec[{i}] key={k}: f0={f0} | f1={f1} | f2={f2}");
}

return 0;
