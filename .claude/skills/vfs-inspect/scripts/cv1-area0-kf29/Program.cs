// CV-1 CAMPAIGN 9 — Area-0 keyframe-29 (14:30) environment values extractor
// THROWAWAY — NEVER commit, NEVER add to slnx.
// Reads data through production EnvironmentBinParsers (same path the shipping client uses).
using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

string inf = "C:/Users/Arius/RiderProjects/MartialHeroes/05.Presentation/MartialHeroes.Client.Godot/clientdata/data.inf";
string vfs = "C:/Users/Arius/RiderProjects/MartialHeroes/05.Presentation/MartialHeroes.Client.Godot/clientdata/data/data.vfs";
using var archive = MappedVfsArchive.Open(inf, vfs);

const int KF = 29; // keyframe 29 = 14:30 (52200 s / 1800 = 29.0 exactly, frac = 0)

// ─────────────────────────────────────────────────────────────────────────────
// 1. light0.bin — Section A (Directional) and Section B (Ambient) keyframe 29
// ─────────────────────────────────────────────────────────────────────────────

Console.WriteLine("=== LIGHT0.BIN  keyframe 29 (14:30) ===");
{
    var rawLight = archive.GetFileContent("data/sky/dat/light0.bin");
    var span     = rawLight.Span;
    Console.WriteLine($"  VFS path: data/sky/dat/light0.bin  size: {rawLight.Length} bytes");

    // Section A: offset 0x0000, stride 48 bytes/keyframe
    // KF 29 section A starts at: 0x0000 + 29*48 = 0x0000 + 1392 = 0x0570
    int offA = 0x0000 + KF * 48;
    Console.WriteLine($"\n--- Section A: Directional light  (section base=0x0000, kf29 byte offset=0x{offA:X4} = {offA}) ---");

    float[] dirCA = ReadFloat4(span, offA);        // color_A at +0x00
    float[] dirCB = ReadFloat4(span, offA + 0x10); // color_B at +0x10
    float[] dirCC = ReadFloat4(span, offA + 0x20); // color_C at +0x20 (unread)

    Console.WriteLine($"  color_A (diffuse RGBA, float [0,1]) @ +0x{offA:X4}:");
    Console.WriteLine($"    R={dirCA[0]:F6}  G={dirCA[1]:F6}  B={dirCA[2]:F6}  A={dirCA[3]:F6}");
    Console.WriteLine($"  color_B (specular RGBA) @ +0x{offA + 0x10:X4}:");
    Console.WriteLine($"    R={dirCB[0]:F6}  G={dirCB[1]:F6}  B={dirCB[2]:F6}  A={dirCB[3]:F6}");
    Console.WriteLine($"  color_C (present-but-unread) @ +0x{offA + 0x20:X4}:");
    Console.WriteLine($"    R={dirCC[0]:F6}  G={dirCC[1]:F6}  B={dirCC[2]:F6}  A={dirCC[3]:F6}");

    // Verify raw bytes for audit
    Console.Write("  raw hex color_A: ");
    for (int i = offA; i < offA + 16; i++) Console.Write($"{span[i]:X2} ");
    Console.WriteLine();

    // Section B: base 0x0930, stride 48 bytes/keyframe
    // KF 29: 0x0930 + 29*48 = 0x0930 + 1392 = 0x0930 + 0x0570 = 0x0EA0
    int offB = 0x0930 + KF * 48;
    Console.WriteLine($"\n--- Section B: Ambient light  (section base=0x0930, kf29 byte offset=0x{offB:X4} = {offB}) ---");

    float[] ambCA = ReadFloat4(span, offB);
    float[] ambCB = ReadFloat4(span, offB + 0x10);
    float[] ambCC = ReadFloat4(span, offB + 0x20);

    Console.WriteLine($"  color_A (ambient RGBA, float [0,1]) @ +0x{offB:X4}:");
    Console.WriteLine($"    R={ambCA[0]:F6}  G={ambCA[1]:F6}  B={ambCA[2]:F6}  A={ambCA[3]:F6}");
    Console.WriteLine($"  color_B (secondary) @ +0x{offB + 0x10:X4}:");
    Console.WriteLine($"    R={ambCB[0]:F6}  G={ambCB[1]:F6}  B={ambCB[2]:F6}  A={ambCB[3]:F6}");
    Console.WriteLine($"  color_C (unread) @ +0x{offB + 0x20:X4}:");
    Console.WriteLine($"    R={ambCC[0]:F6}  G={ambCC[1]:F6}  B={ambCC[2]:F6}  A={ambCC[3]:F6}");

    // Section C: fog-distance scalar for kf29
    // base 0x1260, stride 4 bytes
    int offC = 0x1260 + KF * 4;
    float fogScalarC = BinaryPrimitives.ReadSingleLittleEndian(span[offC..]);
    Console.WriteLine($"\n--- Section C: Fog-distance scalar (base=0x1260, kf29 @ 0x{offC:X4}) ---");
    Console.WriteLine($"  fog_distance_scalar = {fogScalarC:F6}  (world units; fog_range = s*3.0 = {fogScalarC * 3.0f:F4})");

    // Section D: secondary fog scalar for kf29
    int offD = 0x1320 + KF * 4;
    float fogScalarD = BinaryPrimitives.ReadSingleLittleEndian(span[offD..]);
    Console.WriteLine($"\n--- Section D: Secondary fog scalar (base=0x1320, kf29 @ 0x{offD:X4}) ---");
    Console.WriteLine($"  secondary_fog_scalar = {fogScalarD:F6}");

    // Fallback light
    Console.WriteLine($"\n--- Fallback directional light (0x14B0 / 5296) ---");
    float fbScale = BinaryPrimitives.ReadSingleLittleEndian(span[0x14B0..]);
    float fbX     = BinaryPrimitives.ReadSingleLittleEndian(span[0x14B4..]);
    float fbY     = BinaryPrimitives.ReadSingleLittleEndian(span[0x14B8..]);
    float fbZ     = BinaryPrimitives.ReadSingleLittleEndian(span[0x14BC..]);
    Console.WriteLine($"  scale={fbScale:F4}  dir=({fbX:F4}, {fbY:F4}, {fbZ:F4})");
    float mag = MathF.Sqrt(fbX*fbX + fbY*fbY + fbZ*fbZ);
    Console.WriteLine($"  normalized=({fbX/mag:F6}, {fbY/mag:F6}, {fbZ/mag:F6})");

    // Cross-check via production parser
    Console.WriteLine("\n--- Cross-check via EnvironmentBinParsers.ParseLight ---");
    var parsed = EnvironmentBinParsers.ParseLight(rawLight);
    var pDir29 = parsed.DirectionalKeyframes[KF];
    var pAmb29 = parsed.AmbientKeyframes[KF];
    Console.WriteLine($"  parsed Dir kf29 colorA: R={pDir29.ColorA[0]:F6} G={pDir29.ColorA[1]:F6} B={pDir29.ColorA[2]:F6} A={pDir29.ColorA[3]:F6}");
    Console.WriteLine($"  parsed Amb kf29 colorA: R={pAmb29.ColorA[0]:F6} G={pAmb29.ColorA[1]:F6} B={pAmb29.ColorA[2]:F6} A={pAmb29.ColorA[3]:F6}");
    Console.WriteLine($"  parsed FogDistanceScalars[29] = {parsed.FogDistanceScalars[KF]:F6}");
    Console.WriteLine($"  parsed FallbackDir = ({parsed.FallbackDirX:F4}, {parsed.FallbackDirY:F4}, {parsed.FallbackDirZ:F4})");

    // Dump all 48 keyframes for section A to cross-check against .txt
    Console.WriteLine("\n--- Section A all 48 keyframes (diffuse colorA R/G/B) ---");
    Console.WriteLine("  kf  time  R        G        B");
    for (int k = 0; k < 48; k++)
    {
        int o = 0x0000 + k * 48;
        float r = BinaryPrimitives.ReadSingleLittleEndian(span[o..]);
        float g = BinaryPrimitives.ReadSingleLittleEndian(span[(o+4)..]);
        float b = BinaryPrimitives.ReadSingleLittleEndian(span[(o+8)..]);
        int hh = k / 2, mm = (k % 2) * 30;
        string marker = k == KF ? " <<< KF29 14:30" : "";
        Console.WriteLine($"  {k,2}  {hh:D2}:{mm:D2}  {r,8:F4}  {g,8:F4}  {b,8:F4}{marker}");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 2. fog0.bin — keyframe 29
// ─────────────────────────────────────────────────────────────────────────────

Console.WriteLine("\n=== FOG0.BIN  keyframe 29 (14:30) ===");
{
    var rawFog = archive.GetFileContent("data/sky/dat/fog0.bin");
    var span   = rawFog.Span;
    Console.WriteLine($"  VFS path: data/sky/dat/fog0.bin  size: {rawFog.Length} bytes");

    float startDist = BinaryPrimitives.ReadSingleLittleEndian(span[0x00..]);
    float endDist   = BinaryPrimitives.ReadSingleLittleEndian(span[0x04..]);
    uint  dataFlag  = BinaryPrimitives.ReadUInt32LittleEndian(span[0x08..]);
    Console.WriteLine($"  start_dist @ 0x00 = {startDist:F6}");
    Console.WriteLine($"  end_dist   @ 0x04 = {endDist:F6}");
    Console.WriteLine($"  data_load_flag @ 0x08 = {dataFlag}  (0=synthesise from LUT, 1=read verbatim)");

    // KF 29 fog colour: base 0x0C + KF*4
    int offKf29 = 0x0C + KF * 4;
    byte b29 = span[offKf29];
    byte g29 = span[offKf29 + 1];
    byte r29 = span[offKf29 + 2];
    byte a29 = span[offKf29 + 3];
    Console.WriteLine($"\n  fog_colors[29] @ 0x{offKf29:X2} (kf29 = 14:30):");
    Console.WriteLine($"    BGRA bytes: B={b29}  G={g29}  R={r29}  A={a29}");
    Console.WriteLine($"    As [0,1] floats (port step /255): R={r29/255f:F4}  G={g29/255f:F4}  B={b29/255f:F4}  A={a29/255f:F4}");

    // Print all 48 fog colours for cross-check
    Console.WriteLine("\n  All 48 fog colours (R G B BGRA-byte order stored as B G R A):");
    Console.WriteLine("  kf  time  B    G    R    A   [as float R/G/B]");
    for (int k = 0; k < 48; k++)
    {
        int o = 0x0C + k * 4;
        byte bv = span[o]; byte gv = span[o+1]; byte rv = span[o+2]; byte av = span[o+3];
        int hh = k / 2; int mm = (k % 2) * 30;
        string marker = k == KF ? " <<< KF29 14:30" : "";
        Console.WriteLine($"  {k,2}  {hh:D2}:{mm:D2}  {bv,3}  {gv,3}  {rv,3}  {av,3}   [{rv/255f:F3},{gv/255f:F3},{bv/255f:F3}]{marker}");
    }

    // Cross-check via production parser
    Console.WriteLine("\n--- Cross-check via EnvironmentBinParsers.ParseFog ---");
    var parsed = EnvironmentBinParsers.ParseFog(rawFog);
    var fc29 = parsed.FogColors[KF];
    Console.WriteLine($"  parsed kf29: B={fc29.B} G={fc29.G} R={fc29.R} A={fc29.A}");
    Console.WriteLine($"  (data_load_flag=0 means these in-file bytes are NOT used by the client;");
    Console.WriteLine($"   the fog colour is synthesised from the material LUT at runtime)");
}

// ─────────────────────────────────────────────────────────────────────────────
// 3. material0.bin — keyframe 29 (all 51 f32)
// ─────────────────────────────────────────────────────────────────────────────

Console.WriteLine("\n=== MATERIAL0.BIN  keyframe 29 (14:30) ===");
{
    var rawMat = archive.GetFileContent("data/sky/dat/material0.bin");
    var span   = rawMat.Span;
    Console.WriteLine($"  VFS path: data/sky/dat/material0.bin  size: {rawMat.Length} bytes");

    // KF 29 row starts at: 29 * 204 = 5916 = 0x171C
    int rowOff = KF * 204;
    Console.WriteLine($"  keyframe 29 row byte offset: {rowOff} (0x{rowOff:X4})  (= 29 * 204)");

    // Read all 51 f32
    float[] row = new float[51];
    for (int i = 0; i < 51; i++)
        row[i] = BinaryPrimitives.ReadSingleLittleEndian(span[(rowOff + i * 4)..]);

    Console.WriteLine("\n  Per-index decoded values (spec: environment_bins.md §3.2):");
    Console.WriteLine($"  idx   field                   value");
    string[] names =
    [
        /* 0*/ "sky_haze R",
        /* 1*/ "sky_haze G",
        /* 2*/ "sky_haze B",
        /* 3*/ "sky_haze A",
        /* 4*/ "sun_color R",
        /* 5*/ "sun_color G",
        /* 6*/ "sun_color B",
        /* 7*/ "sun_color A",
        /* 8*/ "[8] PROPOSED/unknown",
        /* 9*/ "[9] PROPOSED/unknown",
        /*10*/ "[10] PROPOSED/unknown",
        /*11*/ "[11] PROPOSED/unknown",
        /*12*/ "secondary_sky_color R",
        /*13*/ "secondary_sky_color G",
        /*14*/ "secondary_sky_color B",
        /*15*/ "secondary_sky_color A",
        /*16*/ "[16] PROPOSED/unknown",
        /*17*/ "cloud_color_A R",
        /*18*/ "cloud_color_A G",
        /*19*/ "cloud_color_A B",
        /*20*/ "cloud_color_A A",
        /*21*/ "cloud_color_B R",
        /*22*/ "cloud_color_B G",
        /*23*/ "cloud_color_B B",
        /*24*/ "cloud_color_B A",
        /*25*/ "[25] PROPOSED/unknown",
        /*26*/ "[26] PROPOSED/unknown",
        /*27*/ "[27] PROPOSED/unknown",
        /*28*/ "[28] PROPOSED/unknown",
        /*29*/ "ambient_sky_color R",
        /*30*/ "ambient_sky_color G",
        /*31*/ "ambient_sky_color B",
        /*32*/ "ambient_sky_color A",
        /*33*/ "[33] PROPOSED/unknown",
        /*34*/ "emissive_sky R",
        /*35*/ "emissive_sky G",
        /*36*/ "emissive_sky B",
        /*37*/ "[37] PROPOSED/unknown",
        /*38*/ "specular_sky R",
        /*39*/ "specular_sky G",
        /*40*/ "specular_sky B",
        /*41*/ "[41] PROPOSED/unknown",
        /*42*/ "[42] PROPOSED/unknown",
        /*43*/ "[43] PROPOSED/unknown",
        /*44*/ "[44] PROPOSED/unknown",
        /*45*/ "[45] PROPOSED/unknown",
        /*46*/ "[46] PROPOSED/unknown",
        /*47*/ "[47] PROPOSED/unknown",
        /*48*/ "[48] PROPOSED/unknown",
        /*49*/ "[49] PROPOSED/unknown",
        /*50*/ "[50] PROPOSED/unknown",
    ];

    for (int i = 0; i < 51; i++)
    {
        int byteOff = rowOff + i * 4;
        Console.WriteLine($"  [{i,2}]  0x{byteOff:X4}  {names[i],-30}  {row[i]:F6}");
    }

    // Cross-check: are all 48 kf identical for area 0? (spec §3.3 notes static table)
    bool allSame = true;
    for (int k = 0; k < 48; k++)
    {
        for (int i = 0; i < 51; i++)
        {
            float v = BinaryPrimitives.ReadSingleLittleEndian(span[(k * 204 + i * 4)..]);
            if (Math.Abs(v - row[i]) > 1e-7f) { allSame = false; break; }
        }
        if (!allSame) break;
    }
    Console.WriteLine($"\n  All 48 keyframes identical? {allSame}  (spec §3.3: area 0 static table)");

    // Cross-check via production parser
    Console.WriteLine("\n--- Cross-check via EnvironmentBinParsers.ParseMaterial ---");
    var parsed = EnvironmentBinParsers.ParseMaterial(rawMat);
    float[] parsedRow = parsed.ColorTable[KF];
    bool match = true;
    for (int i = 0; i < 51; i++) if (Math.Abs(parsedRow[i] - row[i]) > 1e-7f) { match = false; break; }
    Console.WriteLine($"  Raw vs parsed row match: {match}");
    Console.WriteLine($"  parsed[29][4] sun_color_R = {parsedRow[4]:F6}");
}

Console.WriteLine("\nDone. CV-1 area-0 kf29 extraction complete.");

static float[] ReadFloat4(ReadOnlySpan<byte> span, int offset) =>
[
    BinaryPrimitives.ReadSingleLittleEndian(span[offset..]),
    BinaryPrimitives.ReadSingleLittleEndian(span[(offset+4)..]),
    BinaryPrimitives.ReadSingleLittleEndian(span[(offset+8)..]),
    BinaryPrimitives.ReadSingleLittleEndian(span[(offset+12)..]),
];
