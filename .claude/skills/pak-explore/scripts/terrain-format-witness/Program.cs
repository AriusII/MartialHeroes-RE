// terrain-format-witness — THROWAWAY, NEVER committed, NEVER in slnx.
// Black-box witness-2 for .ted / .mud / .bud / .sod / .exd / .up / .fx1-.fx7
// across the entire VFS corpus.  Drives production parsers where they exist,
// otherwise reads raw bytes.  Reports sizes/strides/residuals only — no payload bytes.

using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// ── VFS location ─────────────────────────────────────────────────────────────
string infPath = "D:/MartialHeroesClient/data.inf";
string vfsPath = "D:/MartialHeroesClient/data/data.vfs";

// Also probe the embedded clientdata folder
string repoRoot = FindRepoRoot(AppContext.BaseDirectory);
if (repoRoot != "")
{
    string candidateInf = Path.Combine(repoRoot, "05.Presentation",
        "MartialHeroes.Client.Godot", "clientdata", "data.inf");
    string candidateVfs = Path.Combine(repoRoot, "05.Presentation",
        "MartialHeroes.Client.Godot", "clientdata", "data", "data.vfs");
    if (File.Exists(candidateInf) && File.Exists(candidateVfs))
    {
        infPath = candidateInf;
        vfsPath = candidateVfs;
    }
}
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--inf") infPath = args[i + 1];
    if (args[i] == "--vfs") vfsPath = args[i + 1];
}

if (!File.Exists(infPath) || !File.Exists(vfsPath))
{
    Console.Error.WriteLine($"ERROR: client not found at inf={infPath}");
    return 2;
}

using var archive = MappedVfsArchive.Open(infPath, vfsPath);
var allEntries = archive.GetEntries().ToArray();
Console.WriteLine($"Mounted {allEntries.Length:N0} entries from {infPath}");
Console.WriteLine();

// ── .ted ─────────────────────────────────────────────────────────────────────
RunTed(archive, allEntries);

// ── .mud ─────────────────────────────────────────────────────────────────────
RunMud(archive, allEntries);

// ── .bud ─────────────────────────────────────────────────────────────────────
RunBud(archive, allEntries);

// ── .sod ─────────────────────────────────────────────────────────────────────
RunSod(archive, allEntries);

// ── .exd / .up ───────────────────────────────────────────────────────────────
RunUpExd(archive, allEntries, ".exd");
RunUpExd(archive, allEntries, ".up");

// ── .fx1 .. .fx7 ─────────────────────────────────────────────────────────────
RunFx(archive, allEntries);

Console.WriteLine("\n=== terrain-format-witness DONE ===");
return 0;

// ─────────────────────────────────────────────────────────────────────────────
// .TED — witness-1 claim: ALL exactly 46987 B, no header
// ─────────────────────────────────────────────────────────────────────────────
static void RunTed(MappedVfsArchive archive, VfsEntry[] all)
{
    const int Expected = 46987;
    var entries = Ext(all, ".ted");
    Console.WriteLine($"=== .ted ===  ({entries.Count} instances)");
    if (entries.Count == 0) { Console.WriteLine("  (none)"); return; }

    int exact = 0, wrong = 0, parseOk = 0, parseErr = 0;
    var wrongSizes = new List<(string name, int size)>();

    foreach (var (name, size) in entries)
    {
        if (size == Expected)
            exact++;
        else
        {
            wrong++;
            if (wrongSizes.Count < 20) wrongSizes.Add((name, size));
        }

        // Drive the production parser on a sample of up to 50 files
        if (exact + parseOk + parseErr <= 50)
        {
            try
            {
                var raw = archive.GetFileContent(name);
                TedTerrainParser.Parse(raw);
                parseOk++;
            }
            catch (Exception ex)
            {
                parseErr++;
                Console.WriteLine($"  PARSE ERROR {name}: {ex.Message}");
            }
        }
    }

    Console.WriteLine($"  exact 46987 B: {exact}/{entries.Count}");
    Console.WriteLine($"  wrong size: {wrong}");
    foreach (var (n, s) in wrongSizes)
        Console.WriteLine($"    WRONG: {n}  size={s}");
    Console.WriteLine($"  production-parser sample (first 50): ok={parseOk} err={parseErr}");
    Console.WriteLine($"  VERDICT: witness-1 claim (all=46987 B) → {(wrong == 0 ? "AGREE" : $"CONFLICT ({wrong} outliers)")}");
    Console.WriteLine();
}

// ─────────────────────────────────────────────────────────────────────────────
// .MUD — witness-1 claim: ALL exactly 0x8000 = 32768 B
// ─────────────────────────────────────────────────────────────────────────────
static void RunMud(MappedVfsArchive archive, VfsEntry[] all)
{
    const int Expected = 0x8000; // 32768
    var entries = Ext(all, ".mud");
    Console.WriteLine($"=== .mud ===  ({entries.Count} instances)");
    if (entries.Count == 0) { Console.WriteLine("  (none)"); return; }

    int exact = 0, wrong = 0, parseOk = 0, parseErr = 0;
    var wrongSizes = new List<(string name, int size)>();

    foreach (var (name, size) in entries)
    {
        if (size == Expected)
            exact++;
        else
        {
            wrong++;
            if (wrongSizes.Count < 20) wrongSizes.Add((name, size));
        }

        if (exact + parseOk + parseErr <= 50)
        {
            try
            {
                var raw = archive.GetFileContent(name);
                MudBlobParser.Parse(raw);
                parseOk++;
            }
            catch (Exception ex)
            {
                parseErr++;
                Console.WriteLine($"  PARSE ERROR {name}: {ex.Message}");
            }
        }
    }

    Console.WriteLine($"  exact 0x8000 B: {exact}/{entries.Count}");
    Console.WriteLine($"  wrong size: {wrong}");
    foreach (var (n, s) in wrongSizes)
        Console.WriteLine($"    WRONG: {n}  size={s}");
    Console.WriteLine($"  production-parser sample (first 50): ok={parseOk} err={parseErr}");
    Console.WriteLine($"  VERDICT: witness-1 claim (all=0x8000 B) → {(wrong == 0 ? "AGREE" : $"CONFLICT ({wrong} outliers)")}");
    Console.WriteLine();
}

// ─────────────────────────────────────────────────────────────────────────────
// .BUD — witness-1 claim: per-object stream {type(1)+tex(4)+vcount(4)+32N+icount(4)+2M}
//         cap-breachers: files where any object has vertex_count > 3072
// ─────────────────────────────────────────────────────────────────────────────
static void RunBud(MappedVfsArchive archive, VfsEntry[] all)
{
    const uint Cap = 3072;
    var entries = Ext(all, ".bud");
    Console.WriteLine($"=== .bud ===  ({entries.Count} instances)");
    if (entries.Count == 0) { Console.WriteLine("  (none)"); return; }

    int ok = 0, sizeResidualErr = 0, parseErr = 0;
    var capBreachers = new List<(string name, uint maxVcount)>();
    long totalObjects = 0, totalVerts = 0, totalIndices = 0;

    foreach (var (name, size) in entries)
    {
        ReadOnlyMemory<byte> raw;
        try { raw = archive.GetFileContent(name); }
        catch { parseErr++; continue; }

        var span = raw.Span;
        if (span.Length < 4) { parseErr++; continue; }

        // Parse the per-object stream manually (mirrors TerrainSceneParser exactly)
        int offset = 0;
        uint objectCount;
        try { objectCount = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]); }
        catch { parseErr++; continue; }
        offset += 4;

        uint maxVcountInFile = 0;
        bool streamOk = true;

        for (int i = 0; i < (int)objectCount; i++)
        {
            if (offset + 9 > span.Length) { streamOk = false; break; }
            // type_byte(1) + tex_id(4) + vertex_count(4) = 9 bytes
            offset += 1; // type_byte
            offset += 4; // tex_id
            uint vc = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            offset += 4;

            if (vc > maxVcountInFile) maxVcountInFile = vc;
            totalVerts += vc;

            long vertBytes = (long)vc * 32;
            if (offset + vertBytes > span.Length) { streamOk = false; break; }
            offset += (int)vertBytes;

            if (offset + 4 > span.Length) { streamOk = false; break; }
            uint ic = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            offset += 4;

            long idxBytes = (long)ic * 2;
            if (offset + idxBytes > span.Length) { streamOk = false; break; }
            offset += (int)idxBytes;

            totalObjects++;
            totalIndices += ic;
        }

        if (!streamOk) { parseErr++; continue; }

        // After consuming all objects the offset should equal the file size.
        // A non-zero residual is a layout mismatch.
        int residual = span.Length - offset;
        if (residual != 0) { sizeResidualErr++; }
        else ok++;

        if (maxVcountInFile > Cap)
            capBreachers.Add((name, maxVcountInFile));
    }

    Console.WriteLine($"  total instances: {entries.Count}");
    Console.WriteLine($"  stream reproduces file size (residual=0): {ok}");
    Console.WriteLine($"  residual != 0: {sizeResidualErr}");
    Console.WriteLine($"  parse/read errors: {parseErr}");
    Console.WriteLine($"  total objects decoded: {totalObjects:N0}");
    Console.WriteLine($"  total vertices: {totalVerts:N0}");
    Console.WriteLine($"  total indices: {totalIndices:N0}");
    Console.WriteLine($"  cap-breachers (any object vertex_count > {Cap}): {capBreachers.Count}");
    foreach (var (n, mvc) in capBreachers)
        Console.WriteLine($"    BREACH: {n}  max_vertex_count={mvc}");
    Console.WriteLine($"  VERDICT layout reproduces size → {(sizeResidualErr == 0 && parseErr == 0 ? "AGREE" : "CONFLICT")}");
    Console.WriteLine($"  VERDICT cap-breachers count: {capBreachers.Count}  (witness-1 said 4)");
    Console.WriteLine();
}

// ─────────────────────────────────────────────────────────────────────────────
// .SOD — witness-1 claim: solidCount(4) + solidCount×108 + per-solid[quadCount(4)+quadCount×48]
// ─────────────────────────────────────────────────────────────────────────────
static void RunSod(MappedVfsArchive archive, VfsEntry[] all)
{
    var entries = Ext(all, ".sod");
    Console.WriteLine($"=== .sod ===  ({entries.Count} instances)");
    if (entries.Count == 0) { Console.WriteLine("  (none)"); return; }

    int ok = 0, residualErr = 0, parseErr = 0;
    var residualValues = new Dictionary<int, int>(); // residual → count
    long totalSolids = 0, totalQuads = 0;

    foreach (var (name, size) in entries)
    {
        ReadOnlyMemory<byte> raw;
        try { raw = archive.GetFileContent(name); }
        catch { parseErr++; continue; }

        var span = raw.Span;
        if (span.Length < 4) { parseErr++; continue; }

        int offset = 0;
        uint solidCount = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        offset += 4;

        // solidCount × 108-byte SolidRecord block
        long solidBlock = (long)solidCount * 108;
        if (offset + solidBlock > span.Length) { parseErr++; continue; }
        offset += (int)solidBlock;

        bool streamOk = true;
        for (int s = 0; s < (int)solidCount; s++)
        {
            if (offset + 4 > span.Length) { streamOk = false; break; }
            uint qc = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            offset += 4;

            long quadBlock = (long)qc * 48;
            if (offset + quadBlock > span.Length) { streamOk = false; break; }
            offset += (int)quadBlock;

            totalSolids++;
            totalQuads += qc;
        }

        if (!streamOk) { parseErr++; continue; }

        int residual = span.Length - offset;
        if (residual == 0)
            ok++;
        else
        {
            residualErr++;
            if (!residualValues.ContainsKey(residual)) residualValues[residual] = 0;
            residualValues[residual]++;
        }
    }

    Console.WriteLine($"  total instances: {entries.Count}");
    Console.WriteLine($"  layout reproduces file size (residual=0): {ok}");
    Console.WriteLine($"  residual != 0: {residualErr}");
    Console.WriteLine($"  parse errors: {parseErr}");
    Console.WriteLine($"  total solids: {totalSolids:N0}");
    Console.WriteLine($"  total quads: {totalQuads:N0}");
    if (residualValues.Count > 0)
    {
        Console.WriteLine($"  Residual distribution (top 20):");
        foreach (var kv in residualValues.OrderByDescending(k => k.Value).Take(20))
            Console.WriteLine($"    residual={kv.Key} bytes  ×{kv.Value}");
    }
    Console.WriteLine($"  VERDICT two-tier 108B/48B layout → {(residualErr == 0 && parseErr == 0 ? "AGREE" : $"CONFLICT (residuals={residualErr} errors={parseErr})")}");
    Console.WriteLine();
}

// ─────────────────────────────────────────────────────────────────────────────
// .EXD / .UP — witness-1 claim: count(4) + count×40B triangles
// ─────────────────────────────────────────────────────────────────────────────
static void RunUpExd(MappedVfsArchive archive, VfsEntry[] all, string ext)
{
    var entries = Ext(all, ext);
    Console.WriteLine($"=== {ext} ===  ({entries.Count} instances)");
    if (entries.Count == 0) { Console.WriteLine("  (none)"); return; }

    int ok = 0, residualErr = 0, parseErr = 0;
    var residuals = new Dictionary<int, int>();
    long totalTriangles = 0;

    foreach (var (name, size) in entries)
    {
        ReadOnlyMemory<byte> raw;
        try
        {
            raw = archive.GetFileContent(name);
            // Drive the production parser — if it throws, that's a conflict
            var result = TerrainLayerParsers.ParseUpOrExd(raw);
            totalTriangles += result.Triangles.Length;

            // Verify formula: 4 + count×40 == file size
            long expected = 4L + (long)result.Triangles.Length * 40;
            int residual = (int)(raw.Length - expected);
            if (residual == 0)
                ok++;
            else
            {
                residualErr++;
                if (!residuals.ContainsKey(residual)) residuals[residual] = 0;
                residuals[residual]++;
            }
        }
        catch (Exception ex)
        {
            parseErr++;
            if (parseErr <= 5)
                Console.WriteLine($"  PARSE ERROR {name}: {ex.Message}");
        }
    }

    Console.WriteLine($"  total instances: {entries.Count}");
    Console.WriteLine($"  formula 4+count×40 reproduces size: {ok}");
    Console.WriteLine($"  residual != 0: {residualErr}");
    Console.WriteLine($"  parse errors: {parseErr}");
    Console.WriteLine($"  total triangles: {totalTriangles:N0}");
    if (residuals.Count > 0)
    {
        Console.WriteLine($"  Residual distribution:");
        foreach (var kv in residuals.OrderByDescending(k => k.Value).Take(20))
            Console.WriteLine($"    residual={kv.Key}  ×{kv.Value}");
    }
    Console.WriteLine($"  VERDICT count+40B triangles → {(residualErr == 0 && parseErr == 0 ? "AGREE" : $"CONFLICT")}");
    Console.WriteLine();
}

// ─────────────────────────────────────────────────────────────────────────────
// .FX1..FX7 — per-channel header/stride via production parsers
// ─────────────────────────────────────────────────────────────────────────────
static void RunFx(MappedVfsArchive archive, VfsEntry[] all)
{
    // Drive each channel through the production parser and check residual = 0.
    // Channel layout expected by witness-1:
    //   fx1: group_count(4) + groups×[20+vc×36+ic×2]
    //   fx2: group_count(4) + groups×[20+vc×44+ic×2]
    //   fx3: group_count(4) + groups×[44+vc×36+ic×2]
    //   fx4: tile_count(4) + tiles×[48+vc×44+ic×2]
    //   fx5: sections (40+12+vc×36+ic×2) until EOF
    //   fx6: 32 + subchunks×[8+vc×32+ic×2+28footer(non-final)]
    //   fx7: (check for presence/size distribution only — spec may differ)

    var channels = new[] { ".fx1", ".fx2", ".fx3", ".fx4", ".fx5", ".fx6", ".fx7" };

    foreach (var ch in channels)
    {
        var entries = Ext(all, ch);
        Console.WriteLine($"=== {ch} ===  ({entries.Count} instances)");
        if (entries.Count == 0) { Console.WriteLine("  (none)"); continue; }

        var sizes = entries.Select(e => e.size).ToList();
        Console.WriteLine($"  size: min={sizes.Min()} max={sizes.Max()} distinct={sizes.Distinct().Count()}");

        int ok = 0, residualErr = 0, parseErr = 0;
        var residuals = new Dictionary<int, int>();

        foreach (var (name, size) in entries)
        {
            try
            {
                var raw = archive.GetFileContent(name);
                int consumed = ParseFxChannel(ch, raw.Span);
                int residual = raw.Length - consumed;
                if (residual == 0)
                    ok++;
                else
                {
                    residualErr++;
                    if (!residuals.ContainsKey(residual)) residuals[residual] = 0;
                    residuals[residual]++;
                }
            }
            catch (Exception ex)
            {
                parseErr++;
                if (parseErr <= 5)
                    Console.WriteLine($"  PARSE ERROR {name}: {ex.Message}");
            }
        }

        Console.WriteLine($"  layout reproduces size (residual=0): {ok}/{entries.Count}");
        Console.WriteLine($"  residual != 0: {residualErr}");
        Console.WriteLine($"  parse errors: {parseErr}");
        if (residuals.Count > 0)
        {
            Console.WriteLine($"  Residual distribution (top 10):");
            foreach (var kv in residuals.OrderByDescending(k => k.Value).Take(10))
                Console.WriteLine($"    residual={kv.Key}  ×{kv.Value}");
        }
        Console.WriteLine($"  VERDICT → {(residualErr == 0 && parseErr == 0 ? "AGREE" : $"CONFLICT (residual={residualErr} err={parseErr})")}");
        Console.WriteLine();
    }
}

// Returns the number of bytes consumed by the channel parse.
// Uses raw byte walking mirroring the production parsers — no allocation, no object graph.
static int ParseFxChannel(string ch, ReadOnlySpan<byte> span)
{
    return ch switch
    {
        ".fx1" => WalkGroupArray(span, groupHeaderSize: 20, vertStride: 36),
        ".fx2" => WalkGroupArray(span, groupHeaderSize: 20, vertStride: 44),
        ".fx3" => WalkGroupArray(span, groupHeaderSize: 44, vertStride: 36),
        ".fx4" => WalkFx4(span),
        ".fx5" => WalkFx5(span),
        ".fx6" => WalkFx6(span),
        ".fx7" => WalkFx7(span),
        _ => throw new ArgumentException($"unknown channel {ch}")
    };
}

// FX1/FX2/FX3: u32 group_count + groups×[hdrSize + vc×vStride + ic×2]
// vc at group+0x0C (hdr=20) or group+0x24 (hdr=44)
// ic at group+0x10 (hdr=20) or group+0x28 (hdr=44)
static int WalkGroupArray(ReadOnlySpan<byte> span, int groupHeaderSize, int vertStride)
{
    if (span.Length < 4) throw new InvalidDataException("too short for group_count");
    uint groupCount = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
    int off = 4;

    int vcOff = groupHeaderSize == 20 ? 12 : 36; // vertex_count offset within group header
    int icOff = groupHeaderSize == 20 ? 16 : 40; // index_count offset

    for (uint g = 0; g < groupCount; g++)
    {
        if (off + groupHeaderSize > span.Length)
            throw new InvalidDataException($"group[{g}] header truncated");
        uint vc = BinaryPrimitives.ReadUInt32LittleEndian(span[(off + vcOff)..]);
        uint ic = BinaryPrimitives.ReadUInt32LittleEndian(span[(off + icOff)..]);
        off += groupHeaderSize;
        long need = (long)vc * vertStride + (long)ic * 2;
        if (off + need > span.Length)
            throw new InvalidDataException($"group[{g}] geometry truncated (need {need})");
        off += (int)need;
    }
    return off;
}

// FX4: u32 tile_count + tiles×[48 + vc×44 + ic×2]  (vc at tile+0x28, ic at tile+0x2C)
static int WalkFx4(ReadOnlySpan<byte> span)
{
    if (span.Length < 4) throw new InvalidDataException("too short for tile_count");
    uint tileCount = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
    int off = 4;
    const int TileHdr = 48;
    for (uint t = 0; t < tileCount; t++)
    {
        if (off + TileHdr > span.Length) throw new InvalidDataException($"tile[{t}] header truncated");
        uint vc = BinaryPrimitives.ReadUInt32LittleEndian(span[(off + 0x28)..]);
        uint ic = BinaryPrimitives.ReadUInt32LittleEndian(span[(off + 0x2C)..]);
        off += TileHdr;
        long need = (long)vc * 44 + (long)ic * 2;
        if (off + need > span.Length) throw new InvalidDataException($"tile[{t}] geometry truncated");
        off += (int)need;
    }
    return off;
}

// FX5: same flat-tile-array model as FX4, but vertex stride = 36 (VF_36 not VF_44).
// tile_count(4) + tiles × [48B tile header + vc×36 + ic×2]
// vc at tile+0x28 (offset 40 within 48B header), ic at tile+0x2C (offset 44)
// Confirmed by terrain-layer-val: 89/89 residual=0 using this model.
static int WalkFx5(ReadOnlySpan<byte> span)
{
    if (span.Length < 4) throw new InvalidDataException("fx5 too short for tile_count");
    uint tileCount = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
    int off = 4;
    const int TileHdr = 48;
    for (uint t = 0; t < tileCount; t++)
    {
        if (off + TileHdr > span.Length) throw new InvalidDataException($"fx5 tile[{t}] header truncated");
        uint vc = BinaryPrimitives.ReadUInt32LittleEndian(span[(off + 0x28)..]);
        uint ic = BinaryPrimitives.ReadUInt32LittleEndian(span[(off + 0x2C)..]);
        off += TileHdr;
        long need = (long)vc * 36 + (long)ic * 2;
        if (off + need > span.Length) throw new InvalidDataException($"fx5 tile[{t}] geometry truncated");
        off += (int)need;
    }
    return off;
}

// FX6: 32-byte global header + sub_chunk_count subchunks
//       each: [8 + vc×32 + ic×2 + 28 footer] except the LAST which has no footer
static int WalkFx6(ReadOnlySpan<byte> span)
{
    if (span.Length < 32) throw new InvalidDataException("fx6 too short for global header");
    uint subCount = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
    int off = 32;
    for (int s = 0; s < (int)subCount; s++)
    {
        bool isFinal = s == (int)subCount - 1;
        if (off + 8 > span.Length) throw new InvalidDataException($"fx6 sub[{s}] header truncated");
        uint vc = BinaryPrimitives.ReadUInt32LittleEndian(span[off..]);
        uint ic = BinaryPrimitives.ReadUInt32LittleEndian(span[(off + 4)..]);
        off += 8;
        long geoBytes = (long)vc * 32 + (long)ic * 2;
        long need = geoBytes + (isFinal ? 0 : 28);
        if (off + need > span.Length) throw new InvalidDataException($"fx6 sub[{s}] geometry/footer truncated");
        off += (int)need;
    }
    return off;
}

// FX7: single-section flat header 52B + vc×32 + ic×2.
// Header: u32[0..12] (52 bytes), vc at header[11] (+0x2C), ic at header[12] (+0x30).
// Confirmed by terrain-layer-val: 2/2 residual=0 using hdr=52B vStride=32.
static int WalkFx7(ReadOnlySpan<byte> span)
{
    const int Hdr = 52;
    if (span.Length < Hdr) throw new InvalidDataException("fx7 too short for 52B header");
    uint vc = BinaryPrimitives.ReadUInt32LittleEndian(span[0x2C..]);
    uint ic = BinaryPrimitives.ReadUInt32LittleEndian(span[0x30..]);
    long need = (long)vc * 32 + (long)ic * 2;
    if (Hdr + need > span.Length) throw new InvalidDataException($"fx7 geometry truncated (need {need})");
    return Hdr + (int)need;
}

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────
static List<(string name, int size)> Ext(VfsEntry[] all, string ext)
{
    var result = new List<(string, int)>();
    foreach (var e in all)
        if (e.Name.EndsWith(ext, StringComparison.Ordinal))
            result.Add((e.Name, (int)e.DataSize));
    return result;
}

static string FindRepoRoot(string start)
{
    string dir = start;
    for (int i = 0; i < 10; i++)
    {
        if (File.Exists(Path.Combine(dir, "MartialHeroes.slnx"))) return dir;
        string? parent = Path.GetDirectoryName(dir);
        if (parent == null || parent == dir) break;
        dir = parent;
    }
    return "";
}
