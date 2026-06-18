// dds-to-png — THROWAWAY harness: reads DDS from the real client VFS and saves PNG to a scratch dir.
// Supports DXT1, DXT2 (premultiplied alpha → un-premultiplied), DXT3, DXT5.
// DXT2 decoding: same block layout as DXT3 but RGBA channels are pre-multiplied by alpha.
//   To un-premultiply: for each pixel, if A > 0: R = clamp(R*255/A), G = clamp(G*255/A), B = clamp(B*255/A).
//
// NEVER committed. Never added to MartialHeroes.slnx. Output PNGs are scratch-only.
//
// Usage:
//   dotnet run -c Release --project <this dir> -- <vfs-path> <out-png-path>
//   dotnet run -c Release --project <this dir> -- data/ui/login_slice1.dds D:/scratch/login_slice1.png

using System.Text;
using MartialHeroes.Assets.Vfs;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: dds-to-png <vfs-path> <out-png>");
    Console.Error.WriteLine("   or: dds-to-png --batch <out-dir>   (decode the three login UI atlases)");
    return 1;
}

// --- Locate VFS ---
string infPath = "D:/MartialHeroesClient/data.inf";
string vfsPath = "D:/MartialHeroesClient/data/data.vfs";
foreach (string root in DefaultClientRoots())
{
    string ci = Path.Combine(root, "data.inf");
    string cv = Path.Combine(root, "data", "data.vfs");
    if (File.Exists(ci) && File.Exists(cv)) { infPath = ci; vfsPath = cv; break; }
}
Console.Error.WriteLine($"dds-to-png: mounting VFS from {infPath}");
using var archive = MappedVfsArchive.Open(infPath, vfsPath);

if (args[0] == "--batch")
{
    string outDir = args[1];
    Directory.CreateDirectory(outDir);
    string[] targets =
    [
        "data/ui/login_slice1.dds",
        "data/ui/loginwindow.dds",
        "data/ui/loginwindow_02.dds",
    ];
    foreach (string target in targets)
    {
        string stem = Path.GetFileNameWithoutExtension(target);
        string outPng = Path.Combine(outDir, stem + ".png");
        ConvertOne(archive, target, outPng);
    }
    return 0;
}

ConvertOne(archive, args[0], args[1]);
return 0;

// ---------------------------------------------------------------------------
static void ConvertOne(MappedVfsArchive archive, string vfsEntry, string outPng)
{
    if (!archive.Contains(vfsEntry))
    {
        Console.Error.WriteLine($"  ERROR: VFS entry not found: {vfsEntry}");
        return;
    }

    ReadOnlyMemory<byte> mem = archive.GetFileContent(vfsEntry);
    ReadOnlySpan<byte> span = mem.Span;

    // Parse DDS header (128 bytes fixed).
    if (span.Length < 128 || span[0] != 'D' || span[1] != 'D' || span[2] != 'S' || span[3] != ' ')
    {
        Console.Error.WriteLine($"  ERROR: {vfsEntry} does not start with 'DDS ' magic");
        return;
    }

    int height = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(span[12..]);
    int width  = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(span[16..]);
    // pitchOrLinearSize at offset 20 (4 bytes)
    // dwFlags at offset 8 (4 bytes)
    // pixelformat at offset 76 (dwSize=32):
    //   pfFlags at 76+4 = 80; fourCC at 76+8 = 84 (4 bytes)
    uint fourCC = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span[84..]);

    string fourCCStr = System.Text.Encoding.ASCII.GetString(span.Slice(84, 4));
    Console.WriteLine($"  {vfsEntry}: {width}x{height} FourCC={fourCCStr} size={mem.Length} bytes");

    ReadOnlySpan<byte> blocks = span[128..];

    using var image = fourCCStr switch
    {
        "DXT1" => DecodeDxt1(blocks, width, height),
        "DXT2" => DecodeDxt2(blocks, width, height),   // premultiplied alpha, DXT3 layout
        "DXT3" => DecodeDxt3(blocks, width, height),
        "DXT5" => DecodeDxt5(blocks, width, height),
        _ => throw new NotSupportedException($"Unsupported FourCC: {fourCCStr}")
    };

    Directory.CreateDirectory(Path.GetDirectoryName(outPng) ?? ".");
    image.SaveAsPng(outPng);
    Console.WriteLine($"  -> saved {outPng}");
    Console.WriteLine($"  WARNING: original copyrighted asset. NEVER commit this PNG.");
}

// ---------------------------------------------------------------------------
// DXT1: 4x4 blocks, 8 bytes each — two 16-bit colours + 4×4 2-bit indices.
static Image<Rgba32> DecodeDxt1(ReadOnlySpan<byte> blocks, int width, int height)
{
    var img = new Image<Rgba32>(width, height);
    int blocksW = (width  + 3) / 4;
    int blocksH = (height + 3) / 4;
    int pos = 0;

    for (int by = 0; by < blocksH; by++)
    for (int bx = 0; bx < blocksW; bx++)
    {
        ushort c0raw = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(blocks[pos..]);
        ushort c1raw = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(blocks[(pos+2)..]);
        uint indices = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(blocks[(pos+4)..]);
        pos += 8;

        Rgba32[] palette = new Rgba32[4];
        palette[0] = Rgb565ToRgba(c0raw, 255);
        palette[1] = Rgb565ToRgba(c1raw, 255);
        if (c0raw > c1raw)
        {
            palette[2] = Lerp2(palette[0], palette[1], 1, 3);
            palette[3] = Lerp2(palette[0], palette[1], 2, 3);
        }
        else
        {
            palette[2] = Lerp2(palette[0], palette[1], 1, 2);
            palette[3] = new Rgba32(0, 0, 0, 0);
        }

        for (int py = 0; py < 4; py++)
        for (int px = 0; px < 4; px++)
        {
            int ix = bx * 4 + px;
            int iy = by * 4 + py;
            if (ix < width && iy < height)
            {
                int idx = (int)((indices >> (py * 8 + px * 2)) & 3);
                img[ix, iy] = palette[idx];
            }
        }
    }
    return img;
}

// DXT3: 4x4 blocks, 16 bytes each — 8 bytes explicit alpha (4 bits/pixel) + 8 bytes colour (DXT1-like).
static Image<Rgba32> DecodeDxt3(ReadOnlySpan<byte> blocks, int width, int height)
{
    var img = new Image<Rgba32>(width, height);
    int blocksW = (width  + 3) / 4;
    int blocksH = (height + 3) / 4;
    int pos = 0;

    for (int by = 0; by < blocksH; by++)
    for (int bx = 0; bx < blocksW; bx++)
    {
        // 8 bytes: 16 4-bit alpha values packed as 2 nibbles per byte, row-major.
        ulong alphaData = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(blocks[pos..]);
        ushort c0raw = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(blocks[(pos+8)..]);
        ushort c1raw = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(blocks[(pos+10)..]);
        uint indices = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(blocks[(pos+12)..]);
        pos += 16;

        Rgba32[] colPalette = new Rgba32[4];
        colPalette[0] = Rgb565ToRgba(c0raw, 255);
        colPalette[1] = Rgb565ToRgba(c1raw, 255);
        colPalette[2] = Lerp2(colPalette[0], colPalette[1], 1, 3);
        colPalette[3] = Lerp2(colPalette[0], colPalette[1], 2, 3);

        for (int py = 0; py < 4; py++)
        for (int px = 0; px < 4; px++)
        {
            int ix = bx * 4 + px;
            int iy = by * 4 + py;
            if (ix < width && iy < height)
            {
                int alphaIdx = py * 4 + px;
                byte a4 = (byte)((alphaData >> (alphaIdx * 4)) & 0xF);
                byte alpha = (byte)(a4 | (a4 << 4)); // expand 4-bit to 8-bit
                int colIdx = (int)((indices >> (py * 8 + px * 2)) & 3);
                var col = colPalette[colIdx];
                img[ix, iy] = new Rgba32(col.R, col.G, col.B, alpha);
            }
        }
    }
    return img;
}

// DXT2: same block layout as DXT3, but RGB is premultiplied by alpha. Un-premultiply on decode.
static Image<Rgba32> DecodeDxt2(ReadOnlySpan<byte> blocks, int width, int height)
{
    var img = DecodeDxt3(blocks, width, height);
    // Un-premultiply: R = R*255/A (clamped), same for G, B. If A==0, leave black.
    for (int y = 0; y < height; y++)
    for (int x = 0; x < width;  x++)
    {
        Rgba32 p = img[x, y];
        if (p.A > 0)
        {
            img[x, y] = new Rgba32(
                (byte)Math.Min(255, p.R * 255 / p.A),
                (byte)Math.Min(255, p.G * 255 / p.A),
                (byte)Math.Min(255, p.B * 255 / p.A),
                p.A);
        }
    }
    return img;
}

// DXT5: 4x4 blocks, 16 bytes — 8 bytes interpolated alpha + 8 bytes colour.
static Image<Rgba32> DecodeDxt5(ReadOnlySpan<byte> blocks, int width, int height)
{
    var img = new Image<Rgba32>(width, height);
    int blocksW = (width  + 3) / 4;
    int blocksH = (height + 3) / 4;
    int pos = 0;

    for (int by = 0; by < blocksH; by++)
    for (int bx = 0; bx < blocksW; bx++)
    {
        byte a0 = blocks[pos];
        byte a1 = blocks[pos + 1];
        // 6 bytes = 48 bits of 3-bit indices
        ulong alphaBits = 0;
        for (int i = 0; i < 6; i++)
            alphaBits |= ((ulong)blocks[pos + 2 + i]) << (i * 8);

        ushort c0raw = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(blocks[(pos+8)..]);
        ushort c1raw = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(blocks[(pos+10)..]);
        uint indices = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(blocks[(pos+12)..]);
        pos += 16;

        byte[] alphaPalette = new byte[8];
        alphaPalette[0] = a0;
        alphaPalette[1] = a1;
        if (a0 > a1)
        {
            for (int i = 1; i <= 6; i++)
                alphaPalette[1 + i] = (byte)((a0 * (7 - i) + a1 * i) / 7);
        }
        else
        {
            for (int i = 1; i <= 4; i++)
                alphaPalette[1 + i] = (byte)((a0 * (5 - i) + a1 * i) / 5);
            alphaPalette[6] = 0;
            alphaPalette[7] = 255;
        }

        Rgba32[] colPalette = new Rgba32[4];
        colPalette[0] = Rgb565ToRgba(c0raw, 255);
        colPalette[1] = Rgb565ToRgba(c1raw, 255);
        colPalette[2] = Lerp2(colPalette[0], colPalette[1], 1, 3);
        colPalette[3] = Lerp2(colPalette[0], colPalette[1], 2, 3);

        for (int py = 0; py < 4; py++)
        for (int px = 0; px < 4; px++)
        {
            int ix = bx * 4 + px;
            int iy = by * 4 + py;
            if (ix < width && iy < height)
            {
                int alphaIdx2 = py * 4 + px;
                int aCode = (int)((alphaBits >> (alphaIdx2 * 3)) & 7);
                byte alpha = alphaPalette[aCode];
                int colIdx = (int)((indices >> (py * 8 + px * 2)) & 3);
                var col = colPalette[colIdx];
                img[ix, iy] = new Rgba32(col.R, col.G, col.B, alpha);
            }
        }
    }
    return img;
}

// ---------------------------------------------------------------------------
static Rgba32 Rgb565ToRgba(ushort raw, byte alpha)
{
    byte r = (byte)(((raw >> 11) & 0x1F) * 255 / 31);
    byte g = (byte)(((raw >>  5) & 0x3F) * 255 / 63);
    byte b = (byte)(((raw >>  0) & 0x1F) * 255 / 31);
    return new Rgba32(r, g, b, alpha);
}

static Rgba32 Lerp2(Rgba32 a, Rgba32 b, int w, int total)
{
    return new Rgba32(
        (byte)((a.R * (total - w) + b.R * w) / total),
        (byte)((a.G * (total - w) + b.G * w) / total),
        (byte)((a.B * (total - w) + b.B * w) / total),
        255);
}

// --- VFS path resolution (same order as Dev/ClientPathResolver.cs) ---
static IEnumerable<string> DefaultClientRoots()
{
    string? envDir = Environment.GetEnvironmentVariable("MH_CLIENT_DIR");
    if (!string.IsNullOrEmpty(envDir)) yield return envDir;

    // Walk up from exe location to find the Godot project's clientdata/
    string? dir = AppContext.BaseDirectory;
    while (dir != null)
    {
        string candidate = Path.Combine(dir, "05.Presentation", "MartialHeroes.Client.Godot", "clientdata");
        if (Directory.Exists(candidate)) { yield return candidate; break; }
        dir = Path.GetDirectoryName(dir);
    }

    yield return "D:/MartialHeroesClient";
}
