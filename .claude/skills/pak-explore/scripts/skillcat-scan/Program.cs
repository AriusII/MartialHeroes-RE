// skillcat-scan v3 — decode the 116-byte (.do) per-class stance skill record.
// The legacy loader reads fixed 116-byte (0x74) records and copies each verbatim into two
// in-memory maps (keyed by instanceKey @ +0x00 and by slot index @ +0x08); the hotbar/skill-grid
// builders read the icon (srcX, srcY) pair from the in-memory copy at +24/+28 (= on-disk
// +0x18/+0x1C). spec: Docs/RE/formats/ui_manifests.md §2.7.
using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Encoding cp949 = Encoding.GetEncoding(949);

string infPath = "D:/MartialHeroesClient/data.inf", vfsPath = "D:/MartialHeroesClient/data/data.vfs";
foreach (string root in DefaultClientRoots())
{
    string ci = Path.Combine(root, "data.inf"), cv = Path.Combine(root, "data", "data.vfs");
    if (File.Exists(ci) && File.Exists(cv)) { infPath = ci; vfsPath = cv; break; }
}
using MappedVfsArchive archive = MappedVfsArchive.Open(infPath, vfsPath);
Console.WriteLine($"Mounted {archive.GetEntries().Length:N0} entries");

const int Stride = 0x74; // 116
const int Cell = 23, Sheet = 512, MaxCoord = Sheet - Cell;

string[] doFiles = {
    "data/script/musajung.do","data/script/musasa.do","data/script/musama.do",
    "data/script/assasinjung.do","data/script/wizardjung.do","data/script/monkjung.do",
};

foreach (string path in doFiles)
{
    if (!archive.Contains(path)) { Console.WriteLine($"\n{path}: MISSING"); continue; }
    ReadOnlyMemory<byte> mem = archive.GetFileContent(path);
    ReadOnlySpan<byte> raw = mem.Span;
    int nrec = raw.Length / Stride;
    Console.WriteLine($"\n========== {path} ==========");
    Console.WriteLine($"  size={raw.Length:N0}  stride={Stride}  records={nrec}  tail={raw.Length % Stride}");

    // Decode every record's candidate fields per the IDA-derived offsets.
    //  dword0 (+0x00): map-A key (seq)        | dword2 (+0x08): map-B key
    //  dword3 (+0x0C): looks like skillRef 1001 in column dump
    //  +0x18 (24): icon srcX (u16)            | +0x1C (28): icon srcY (u16)   [engine reads these]
    Console.WriteLine($"  {"rec",3} {"d0(keyA)",10} {"d1",6} {"d2(keyB)",9} {"d3",6} {"d4",5} {"+0x14",6} {"sX@+0x18",8} {"sY@+0x1C",8} {"+0x20",6} {"+0x24",6}  cell  valid?");
    int validCnt = 0, total = 0; var dX = new HashSet<int>(); var dY = new HashSet<int>();
    var rows = new List<(int rec, uint d0, uint d2, uint d3, short sx, short sy)>();
    for (int i = 0; i < nrec; i++)
    {
        ReadOnlySpan<byte> r = raw.Slice(i * Stride, Stride);
        uint d0 = BinaryPrimitives.ReadUInt32LittleEndian(r[0x00..0x04]);
        uint d1 = BinaryPrimitives.ReadUInt32LittleEndian(r[0x04..0x08]);
        uint d2 = BinaryPrimitives.ReadUInt32LittleEndian(r[0x08..0x0C]);
        uint d3 = BinaryPrimitives.ReadUInt32LittleEndian(r[0x0C..0x10]);
        uint d4 = BinaryPrimitives.ReadUInt32LittleEndian(r[0x10..0x14]);
        ushort f14 = BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(0x14, 2));
        short sx = BinaryPrimitives.ReadInt16LittleEndian(r.Slice(0x18, 2));
        short sy = BinaryPrimitives.ReadInt16LittleEndian(r.Slice(0x1C, 2));
        ushort f20 = BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(0x20, 2));
        ushort f24 = BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(0x24, 2));
        if (d0 == 0 && d2 == 0 && sx == 0 && sy == 0) continue; // empty
        total++;
        bool ok = sx >= 0 && sx <= MaxCoord && sy >= 0 && sy <= MaxCoord && (sx != 0 || sy != 0);
        if (ok) { validCnt++; dX.Add(sx); dY.Add(sy); }
        rows.Add((i, d0, d2, d3, sx, sy));
        if (i < 24)
            Console.WriteLine($"  {i,3} {d0,10} {d1,6} {d2,9} {d3,6} {d4,5} {f14,6} {sx,8} {sy,8} {f20,6} {f24,6}  ({sx/Cell},{sy/Cell})  {(ok?"yes":"NO")}");
    }
    Console.WriteLine($"  ... [{nrec} records total]");
    Console.WriteLine($"  non-empty={total}  icon-valid(0..{MaxCoord})={validCnt} ({(total>0?100.0*validCnt/total:0):F1}%)  distinctX={dX.Count} distinctY={dY.Count}");
    if (dX.Count > 0)
    {
        var xs = dX.OrderBy(v => v).ToList(); var ys = dY.OrderBy(v => v).ToList();
        Console.WriteLine($"  srcX set: {string.Join(",", xs.Take(40))}");
        Console.WriteLine($"  srcY set: {string.Join(",", ys.Take(40))}");
        Console.WriteLine($"  srcX all %{Cell}==0? {xs.All(v => v % Cell == 0)}   srcY all %{Cell}==0? {ys.All(v => v % Cell == 0)}");
        // check the grid formula hypothesis: srcX steps 81? Actually print sorted unique (x,y) pairs:
    }

    // Distinct (srcX,srcY) pairs and whether each maps to a unique skill ref
    Console.WriteLine($"  --- first 16 records: keyA, keyB, d3(skillRef?), (srcX,srcY) ---");
    foreach (var row in rows.Take(16))
        Console.WriteLine($"    rec{row.rec,3}: keyA=0x{row.d0:X8} keyB=0x{row.d2:X8} d3={row.d3,6} icon=({row.sx,3},{row.sy,3})");
}

Console.WriteLine("\nDone.");
return 0;

static IEnumerable<string> DefaultClientRoots()
{
    string? env = Environment.GetEnvironmentVariable("MH_CLIENT_DIR");
    if (!string.IsNullOrWhiteSpace(env)) yield return env;
    for (DirectoryInfo? d = new(AppContext.BaseDirectory); d != null; d = d.Parent)
        yield return Path.Combine(d.FullName, "05.Presentation", "MartialHeroes.Client.Godot", "clientdata");
    yield return "D:/MartialHeroesClient";
}
