// V8 Terrain Layer Validation Harness — THROWAWAY, never committed, never in slnx
// Validates terrain_layers.md stride/size formulas across ALL .fx*/.up/.exd instances.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

string infPath = "C:/Users/Arius/RiderProjects/MartialHeroes/05.Presentation/MartialHeroes.Client.Godot/clientdata/data.inf";
string vfsPath = "C:/Users/Arius/RiderProjects/MartialHeroes/05.Presentation/MartialHeroes.Client.Godot/clientdata/data/data.vfs";
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--inf") infPath = args[i + 1];
    if (args[i] == "--vfs") vfsPath = args[i + 1];
}

using var archive = MappedVfsArchive.Open(infPath, vfsPath);
var entriesSpan = archive.GetEntries();
var entriesArr = entriesSpan.ToArray();
Console.WriteLine($"Mounted {entriesArr.Length:N0} entries.");

var runner = new V8Runner(archive, entriesArr);
runner.Run();

// ══════════════════════════════════════════════════════════════════════════
class V8Runner
{
    private readonly MappedVfsArchive _archive;
    private readonly VfsEntry[] _entries;

    public V8Runner(MappedVfsArchive archive, VfsEntry[] entries)
    {
        _archive = archive;
        _entries = entries;
    }

    public void Run()
    {
        var extensions = new[] { ".fx1", ".fx2", ".fx3", ".fx4", ".fx5", ".fx6", ".fx7", ".up", ".exd" };
        foreach (var ext in extensions)
        {
            Console.WriteLine($"\n======== {ext} ========");
            var matched = GetEntries(ext);
            if (matched.Count == 0) { Console.WriteLine("  (no instances)"); continue; }

            var sizes = matched.Select(m => m.Size).ToList();
            Console.WriteLine($"  Count: {matched.Count}");
            Console.WriteLine($"  Total bytes: {sizes.Sum():N0}");
            Console.WriteLine($"  Size distribution: min={sizes.Min()} max={sizes.Max()} distinct={sizes.Distinct().Count()}");

            var sizeHist = sizes.GroupBy(s => s).OrderByDescending(g => g.Count()).Take(20).ToList();
            Console.WriteLine($"  Size histogram (top {sizeHist.Count}):");
            foreach (var g in sizeHist)
                Console.WriteLine($"    {g.Key,10} bytes  ×{g.Count(),5}");

            switch (ext)
            {
                case ".fx1": ValidateFx12(matched, ext, 24, 36); break;
                case ".fx2": ValidateFx12(matched, ext, 24, 44); break;
                case ".fx3": ValidateFx3(matched); break;
                case ".fx4": ValidateFx4(matched); break;
                case ".fx5": ValidateFx5(matched); break;
                case ".fx6": ValidateFx6(matched); break;
                case ".fx7": ValidateFx7(matched); break;
                case ".up":  ValidateUpExd(matched, ".up"); break;
                case ".exd": ValidateUpExd(matched, ".exd"); break;
            }
        }

        // sky/dat files
        RunSkyDat();
    }

    private List<(string Name, int Size)> GetEntries(string ext)
    {
        var result = new List<(string, int)>();
        foreach (var e in _entries)
            if (e.Name.EndsWith(ext, StringComparison.Ordinal))
                result.Add((e.Name, (int)e.DataSize));
        return result;
    }

    // FX1 / FX2: 24-byte header
    private void ValidateFx12(List<(string Name, int Size)> files, string ext, int hdrSize, int vStride)
    {
        int ok = 0, bad = 0;
        var residuals = new Hist();
        var meshCounts = new Hist(); var idxCounts = new Hist();
        var unk1 = new Hist(); var unk2 = new Hist(); var renderState = new Hist();

        foreach (var (name, size) in files)
        {
            var b = _archive.GetFileContent(name).Span;
            if (b.Length < hdrSize) { bad++; continue; }
            uint u1  = ReadU32(b, 4);
            uint u2  = ReadU32(b, 8);
            uint rs  = ReadU32(b, 12);
            uint mc  = ReadU32(b, 16);
            uint ic  = ReadU32(b, 20);
            int expected = hdrSize + (int)mc * vStride + (int)ic * 2;
            int residual = size - expected;
            residuals.Add(residual); meshCounts.Add((int)mc); idxCounts.Add((int)ic);
            unk1.Add((int)u1); unk2.Add((int)u2); renderState.Add((int)rs);
            if (residual == 0) ok++; else bad++;
        }

        Console.WriteLine($"\n  Stride check (hdr={hdrSize} vStride={vStride}): OK={ok} BAD={bad}");
        residuals.Print("  residuals");
        unk1.Print("  unk1 @0x04");
        unk2.Print("  unk2 @0x08");
        renderState.Print("  render_state @0x0C");
        meshCounts.Print("  mesh_count @0x10", 10);
        idxCounts.Print("  index_count @0x14", 10);
    }

    // FX3: 48-byte header
    private void ValidateFx3(List<(string Name, int Size)> files)
    {
        int ok = 0, bad = 0;
        var residuals = new Hist();
        var renderState = new Hist();
        var fields = new Hist[10]; // u1..u8 + mesh + idx
        for (int i = 0; i < 10; i++) fields[i] = new Hist();
        var f4floats = new List<float>();

        foreach (var (name, size) in files)
        {
            var b = _archive.GetFileContent(name).Span;
            if (b.Length < 48) { bad++; continue; }
            // 0x00=typeTag 0x04=u1 0x08=u2 0x0C=rs 0x10=u3 0x14=u4(float) 0x18=u5 0x1C=u6 0x20=u7 0x24=u8 0x28=mc 0x2C=ic
            fields[0].Add((int)ReadU32(b, 4));   // u1
            fields[1].Add((int)ReadU32(b, 8));   // u2
            renderState.Add((int)ReadU32(b, 12));
            fields[2].Add((int)ReadU32(b, 16));  // u3
            f4floats.Add(BitsToFloat(ReadU32(b, 20))); // u4 as float
            fields[3].Add((int)ReadU32(b, 24));  // u5
            fields[4].Add((int)ReadU32(b, 28));  // u6
            fields[5].Add((int)ReadU32(b, 32));  // u7
            fields[6].Add((int)ReadU32(b, 36));  // u8
            uint mc = ReadU32(b, 40); uint ic = ReadU32(b, 44);
            fields[7].Add((int)mc); fields[8].Add((int)ic);
            int expected = 48 + (int)mc * 36 + (int)ic * 2;
            int residual = size - expected;
            residuals.Add(residual);
            if (residual == 0) ok++; else bad++;
        }

        Console.WriteLine($"\n  Stride check (hdr=48 vStride=36): OK={ok} BAD={bad}");
        residuals.Print("  residuals");
        fields[0].Print("  unk1 @0x04");
        fields[1].Print("  unk2 @0x08");
        renderState.Print("  render_state @0x0C");
        fields[2].Print("  unk3 @0x10");
        var f4dist = f4floats.GroupBy(f => f).OrderByDescending(g => g.Count()).Take(5);
        Console.WriteLine("  unk4_float @0x14: " + string.Join(", ", f4dist.Select(g => $"{g.Key:G6}×{g.Count()}")));
        fields[3].Print("  unk5 @0x18");
        fields[4].Print("  unk6 @0x1C");
        fields[5].Print("  unk7 @0x20");
        fields[6].Print("  unk8 @0x24");
        fields[7].Print("  mesh_count @0x28", 10);
        fields[8].Print("  index_count @0x2C", 10);
    }

    // FX4: flat tile array
    private void ValidateFx4(List<(string Name, int Size)> files)
    {
        int ok = 0, bad = 0;
        Console.WriteLine($"\n  FX4 flat-tile-array validation (vStride=44):");
        foreach (var (name, size) in files)
        {
            var b = _archive.GetFileContent(name).Span;
            if (b.Length < 4) { bad++; continue; }
            uint tileCount = ReadU32(b, 0);
            int pos = 4; int computedSize = 4;
            bool ok2 = true;
            for (int t = 0; t < (int)tileCount && ok2; t++)
            {
                if (pos + 48 > b.Length) { ok2 = false; break; }
                // tile header: 40B metadata + vCount@+40 + iCount@+44
                uint vc = ReadU32(b, pos + 40);
                uint ic = ReadU32(b, pos + 44);
                int tileSz = 48 + (int)vc * 44 + (int)ic * 2;
                Console.WriteLine($"    tile[{t}]: vc={vc} ic={ic} tileSz={tileSz}");
                // Print the 40B metadata as u32 and f32
                Console.Write($"      meta_u32: ");
                for (int i = 0; i < 10; i++) Console.Write($"{ReadU32(b, pos + i * 4),12} ");
                Console.WriteLine();
                Console.Write($"      meta_f32: ");
                for (int i = 0; i < 10; i++) Console.Write($"{BitsToFloat(ReadU32(b, pos + i * 4)),12:G6} ");
                Console.WriteLine();
                computedSize += tileSz;
                pos += tileSz;
            }
            int residual = size - computedSize;
            Console.WriteLine($"    {name}: size={size} tileCount={tileCount} computed={computedSize} residual={residual} {(residual == 0 ? "OK" : "MISMATCH")}");
            if (residual == 0) ok++; else bad++;
        }
        Console.WriteLine($"  OK={ok} BAD={bad}");
    }

    // FX5: flat tile array, vStride=36
    private void ValidateFx5(List<(string Name, int Size)> files)
    {
        int ok = 0, bad = 0, parseFail = 0;
        var residuals = new Hist();
        var tileCounts = new Hist();
        // Histogram each 48B tile-header field (as u32) for tile[0]
        var hdrHist = new Hist[12];
        for (int i = 0; i < 12; i++) hdrHist[i] = new Hist();
        // Also collect as float for fields 3..9 (direction/bounding box candidates)
        var floatSamples = new List<float>[12];
        for (int i = 0; i < 12; i++) floatSamples[i] = new List<float>();

        foreach (var (name, size) in files)
        {
            var b = _archive.GetFileContent(name).Span;
            if (b.Length < 4) { parseFail++; continue; }
            uint tileCount = ReadU32(b, 0);
            tileCounts.Add((int)tileCount);
            int pos = 4; int computedSize = 4; bool good = true;
            for (int t = 0; t < (int)tileCount && good; t++)
            {
                if (pos + 48 > b.Length) { good = false; break; }
                if (t == 0) // sample first tile's header
                {
                    for (int fi = 0; fi < 12; fi++)
                    {
                        uint rawVal = ReadU32(b, pos + fi * 4);
                        hdrHist[fi].Add((int)rawVal);
                        floatSamples[fi].Add(BitsToFloat(rawVal));
                    }
                }
                uint vc = ReadU32(b, pos + 40);
                uint ic = ReadU32(b, pos + 44);
                int tileSz = 48 + (int)vc * 36 + (int)ic * 2;
                computedSize += tileSz;
                pos += tileSz;
            }
            int residual = size - computedSize;
            residuals.Add(residual);
            if (good && residual == 0) ok++; else bad++;
        }

        Console.WriteLine($"\n  FX5 flat-tile-array validation (vStride=36): OK={ok} BAD={bad} parseFail={parseFail}");
        residuals.Print("  residuals");
        tileCounts.Print("  tile_count", 20);
        for (int fi = 0; fi < 12; fi++)
        {
            var distinctInts = hdrHist[fi].TopN(5);
            var distinctFloats = floatSamples[fi].GroupBy(f => f).OrderByDescending(g => g.Count()).Take(3);
            Console.WriteLine($"  tile[0] hdr[{fi}] @+{fi*4:X2}: u32={string.Join("|", distinctInts)} f32={string.Join("|", distinctFloats.Select(g => $"{g.Key:G6}×{g.Count()}"))}");
        }
    }

    // FX6: GlobalHeader(32B) + N sub-chunks
    private void ValidateFx6(List<(string Name, int Size)> files)
    {
        int ok = 0, bad = 0;
        var subCounts = new Hist();
        var gHdr = new Hist[8];
        for (int i = 0; i < 8; i++) gHdr[i] = new Hist();
        // Per-sub-chunk footer field histograms (fields e=+0x10, f=+0x14)
        var ftrE = new Hist(); var ftrF = new Hist();

        foreach (var (name, size) in files)
        {
            var b = _archive.GetFileContent(name).Span;
            if (b.Length < 32) { bad++; continue; }
            uint subCount = ReadU32(b, 0);
            subCounts.Add((int)subCount);
            for (int fi = 0; fi < 8; fi++) gHdr[fi].Add((int)ReadU32(b, fi * 4));

            int expected = 32 + (int)(subCount > 0 ? subCount - 1 : 0) * 736 + 708;
            int residual = size - expected;
            if (residual != 0) { bad++; Console.WriteLine($"    MISMATCH {name}: size={size} subCount={subCount} expected={expected} residual={residual}"); }
            else ok++;

            // Walk sub-chunks
            int pos = 32;
            for (int sc = 0; sc < (int)subCount; sc++)
            {
                if (pos + 8 > b.Length) break;
                uint vc = ReadU32(b, pos); uint ic = ReadU32(b, pos + 4);
                bool isFinal = sc == (int)subCount - 1;
                pos += 8 + (int)vc * 32 + (int)ic * 2;
                if (!isFinal && pos + 28 <= b.Length)
                {
                    // footer: a=+0 b=+4 c=+8 d=+12(float) e=+16 f=+20 g=+24
                    ftrE.Add((int)ReadU32(b, pos + 16));
                    ftrF.Add((int)ReadU32(b, pos + 20));
                    pos += 28;
                }
            }
        }

        Console.WriteLine($"\n  FX6 validation: OK={ok} BAD={bad}");
        subCounts.Print("  sub_chunk_count", 10);
        for (int fi = 0; fi < 8; fi++)
        {
            string label = fi switch {
                0 => "sub_chunk_count",
                1 => "version @0x04",
                2 => "unk1 @0x08",
                3 => "unk2 @0x0C",
                4 => "unk3_float @0x10",
                5 => "unk4 @0x14",
                6 => "unk5 @0x18",
                7 => "unk6 @0x1C",
                _ => $"hdr[{fi}]"
            };
            gHdr[fi].Print($"  GlobalHdr {label}", 8);
        }
        ftrE.Print("  footer_e @+0x10 (varies)", 10);
        ftrF.Print("  footer_f @+0x14 (varies)", 10);
    }

    // FX7: single-tile, vStride=32
    private void ValidateFx7(List<(string Name, int Size)> files)
    {
        int ok = 0, bad = 0;
        var residuals = new Hist();
        var hdrRaw = new List<uint>[13];
        for (int i = 0; i < 13; i++) hdrRaw[i] = new List<uint>();

        foreach (var (name, size) in files)
        {
            var b = _archive.GetFileContent(name).Span;
            if (b.Length < 52) { bad++; continue; }
            for (int fi = 0; fi < 13; fi++) hdrRaw[fi].Add(ReadU32(b, fi * 4));
            uint vc = ReadU32(b, 44); uint ic = ReadU32(b, 48);
            int expected = 52 + (int)vc * 32 + (int)ic * 2;
            int residual = size - expected;
            residuals.Add(residual);
            Console.WriteLine($"    {name}: size={size} vc={vc} ic={ic} expected={expected} residual={residual}");
            if (residual == 0) ok++; else bad++;
        }
        Console.WriteLine($"\n  FX7 validation (hdr=52B vStride=32): OK={ok} BAD={bad}");
        residuals.Print("  residuals");
        Console.WriteLine("  All header fields (u32 | f32):");
        for (int fi = 0; fi < 13; fi++)
        {
            var distinct = hdrRaw[fi].Distinct().ToList();
            Console.WriteLine($"    [{fi}] @+{fi*4:X2}: " + string.Join(", ", distinct.Select(v => $"u32={v} f32={BitsToFloat(v):G6}")));
        }
    }

    // .up / .exd: 4B count + N*40B records
    private void ValidateUpExd(List<(string Name, int Size)> files, string ext)
    {
        int ok = 0, bad = 0;
        var residuals = new Hist();
        var triCounts = new Hist();
        long totalTri = 0, flatCount = 0, nonFlatCount = 0;

        foreach (var (name, size) in files)
        {
            var b = _archive.GetFileContent(name).Span;
            if (b.Length < 4) { bad++; continue; }
            uint tc = ReadU32(b, 0);
            triCounts.Add((int)tc);
            totalTri += tc;
            int expected = 4 + (int)tc * 40;
            int residual = size - expected;
            residuals.Add(residual);
            if (residual == 0) ok++; else bad++;

            for (int t = 0; t < (int)tc; t++)
            {
                int off = 4 + t * 40;
                if (off + 40 > b.Length) break;
                float v1y = BitsToFloat(ReadU32(b, off + 4));
                float v2y = BitsToFloat(ReadU32(b, off + 16));
                float v3y = BitsToFloat(ReadU32(b, off + 28));
                float ph  = BitsToFloat(ReadU32(b, off + 36));
                if (Math.Abs(v1y - ph) < 0.01f && Math.Abs(v2y - ph) < 0.01f && Math.Abs(v3y - ph) < 0.01f)
                    flatCount++;
                else
                    nonFlatCount++;
            }
        }

        Console.WriteLine($"\n  Stride check (hdr=4 recStride=40): OK={ok} BAD={bad}");
        residuals.Print("  residuals");
        triCounts.Print("  triangle_count", 20);
        Console.WriteLine($"  Total triangles: {totalTri:N0}");
        Console.WriteLine($"  plane_height == all vertex Y (flat geometry): {flatCount}");
        Console.WriteLine($"  plane_height != vertex Y (non-flat):          {nonFlatCount}");
    }

    private void RunSkyDat()
    {
        Console.WriteLine($"\n======== sky/dat bins ========");
        var skyAll = new List<(string Name, int Size)>();
        foreach (var e in _entries)
            if (e.Name.StartsWith("data/sky/dat/", StringComparison.Ordinal))
                skyAll.Add((e.Name, (int)e.DataSize));
        Console.WriteLine($"  Total sky/dat entries: {skyAll.Count}");

        foreach (var g in skyAll.GroupBy(e => {
            var n = Path.GetFileName(e.Name);
            if (n.StartsWith("light", StringComparison.Ordinal) && !n.StartsWith("point", StringComparison.Ordinal)) return "light*.bin";
            if (n.StartsWith("point_light", StringComparison.Ordinal)) return "point_light*.bin";
            if (n.StartsWith("wind", StringComparison.Ordinal)) return "wind*.bin";
            return "other";
        }).OrderBy(g => g.Key))
        {
            var list = g.ToList();
            Console.WriteLine($"\n  {g.Key}: {list.Count} files");
            var szs = list.Select(e => e.Size).ToList();
            Console.WriteLine($"    sizes: min={szs.Min()} max={szs.Max()} distinct={szs.Distinct().Count()}");
            foreach (var sg in szs.GroupBy(s => s).OrderByDescending(x => x.Count()))
                Console.WriteLine($"      {sg.Key,8} bytes ×{sg.Count()}");

            if (g.Key == "light*.bin")
            {
                foreach (var e in list)
                    Console.WriteLine($"    {e.Name}: {e.Size} bytes {(e.Size == 5312 ? "OK=5312" : $"MISMATCH expected 5312")}");
            }
            if (g.Key == "wind*.bin")
            {
                foreach (var e in list)
                {
                    var b = _archive.GetFileContent(e.Name).Span;
                    if (b.Length < 8) { Console.WriteLine($"    {e.Name} TOO_SHORT"); continue; }
                    uint count = ReadU32(b, 0); uint flag2 = ReadU32(b, 4);
                    int exp2 = (int)(8 + count * 24);
                    Console.WriteLine($"    {e.Name}: size={e.Size} count={count} flag2={flag2} expected={exp2} {(e.Size == exp2 ? "OK" : "MISMATCH")}");
                }
            }
            if (g.Key == "point_light*.bin")
            {
                foreach (var e in list)
                {
                    var b = _archive.GetFileContent(e.Name).Span;
                    if (b.Length < 8) { Console.WriteLine($"    {e.Name} TOO_SHORT"); continue; }
                    uint intScale = ReadU32(b, 0); uint count = ReadU32(b, 4);
                    int exp2 = (int)(8 + count * 60);
                    Console.WriteLine($"    {e.Name}: size={e.Size} intScale={intScale} count={count} expected={exp2} {(e.Size == exp2 ? "OK" : "MISMATCH")}");
                }
            }
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadU32(ReadOnlySpan<byte> b, int off) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b[off..]);

    private static float BitsToFloat(uint bits)
    {
        unsafe { return *(float*)&bits; }
    }
}

// ── Histogram helper ────────────────────────────────────────────────────────
class Hist
{
    private readonly Dictionary<int, int> _d = new();
    public void Add(int v) { _d.TryGetValue(v, out int c); _d[v] = c + 1; }
    public void Print(string label, int top = 15)
    {
        var sorted = _d.OrderByDescending(kv => kv.Value).Take(top).ToList();
        Console.WriteLine($"{label} (distinct={_d.Count}): " + string.Join(", ", sorted.Select(kv => $"{kv.Key}×{kv.Value}")));
    }
    public IEnumerable<string> TopN(int n) =>
        _d.OrderByDescending(kv => kv.Value).Take(n).Select(kv => $"{kv.Key}×{kv.Value}");
}
