// V7 throwaway harness — final targeted probes
// NEVER commit, NEVER add to slnx.
using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

string inf = "C:/Users/Arius/RiderProjects/MartialHeroes/05.Presentation/MartialHeroes.Client.Godot/clientdata/data.inf";
string vfs = "C:/Users/Arius/RiderProjects/MartialHeroes/05.Presentation/MartialHeroes.Client.Godot/clientdata/data/data.vfs";

using var archive = MappedVfsArchive.Open(inf, vfs);
var entries = archive.GetEntries().ToArray();

// ---- fog data_load_flag=1 with non-zero colors
Console.WriteLine("=== FOG flag=1 with non-zero colors ===");
{
    var fogPaths = entries.Where(e => e.Name.StartsWith("data/sky/dat/fog") && e.Name.EndsWith(".bin")).Select(e => e.Name).ToList();
    int cnt = 0;
    foreach (var path in fogPaths)
    {
        var fog = EnvironmentBinParsers.ParseFog(archive.GetFileContent(path));
        if (fog.DataLoadFlag != 1) continue;
        bool allZ = fog.FogColors.All(c => c.B == 0 && c.G == 0 && c.R == 0 && c.A == 0);
        if (!allZ)
        {
            cnt++;
            Console.WriteLine($"  {path}: start={fog.StartDist:F4} end={fog.EndDist:F4}");
            Console.WriteLine($"    kf0 BGRA: B={fog.FogColors[0].B} G={fog.FogColors[0].G} R={fog.FogColors[0].R} A={fog.FogColors[0].A}");
            Console.WriteLine($"    kf24 BGRA: B={fog.FogColors[24].B} G={fog.FogColors[24].G} R={fog.FogColors[24].R} A={fog.FogColors[24].A}");
            Console.WriteLine($"    distinct BGRA entries: {fog.FogColors.Distinct().Count()}");
        }
    }
    Console.WriteLine($"  Total flag=1 with non-zero colors: {cnt}");
}

// ---- Light area 0 anomaly: all kf have same alpha (0.0473 dir, 0.7871 amb) — same as other areas?
Console.WriteLine("\n=== LIGHT0 vs LIGHT1 alpha comparison ===");
{
    var light0 = EnvironmentBinParsers.ParseLight(archive.GetFileContent("data/sky/dat/light0.bin"));
    var light1 = EnvironmentBinParsers.ParseLight(archive.GetFileContent("data/sky/dat/light1.bin"));
    // Dir alpha
    var dir0 = light0.DirectionalKeyframes.Select(kf => kf.ColorA[3]).Distinct().ToList();
    var dir1 = light1.DirectionalKeyframes.Select(kf => kf.ColorA[3]).Distinct().ToList();
    Console.WriteLine($"  light0 Dir colorA alpha values: {string.Join(",", dir0.Select(v => v.ToString("F4")))}");
    Console.WriteLine($"  light1 Dir colorA alpha values: {string.Join(",", dir1.Select(v => v.ToString("F4")))}");
    var amb0 = light0.AmbientKeyframes.Select(kf => kf.ColorA[3]).Distinct().ToList();
    var amb1 = light1.AmbientKeyframes.Select(kf => kf.ColorA[3]).Distinct().ToList();
    Console.WriteLine($"  light0 Amb colorA alpha values: {string.Join(",", amb0.Select(v => v.ToString("F4")))}");
    Console.WriteLine($"  light1 Amb colorA alpha values: {string.Join(",", amb1.Select(v => v.ToString("F4")))}");
    // Is colorB also non-zero for light0?
    bool b0NonZ = light0.DirectionalKeyframes.Any(kf => kf.ColorB.Any(v => v != 0f));
    bool b1NonZ = light1.DirectionalKeyframes.Any(kf => kf.ColorB.Any(v => v != 0f));
    Console.WriteLine($"  light0 Dir colorB any non-zero: {b0NonZ}  light1: {b1NonZ}");

    // Global: how many light files have alpha != 0 in dir or amb?
    var lightPaths = entries.Where(e => e.Name.StartsWith("data/sky/dat/light") && e.Name.EndsWith(".bin")).Select(e => e.Name).ToList();
    int dirAlphaNZ = 0, ambAlphaNZ = 0;
    foreach (var path in lightPaths)
    {
        var l = EnvironmentBinParsers.ParseLight(archive.GetFileContent(path));
        if (l.DirectionalKeyframes.Any(kf => kf.ColorA[3] != 0f)) dirAlphaNZ++;
        if (l.AmbientKeyframes.Any(kf => kf.ColorA[3] != 0f)) ambAlphaNZ++;
    }
    Console.WriteLine($"  Files with dir alpha != 0: {dirAlphaNZ}/{lightPaths.Count}");
    Console.WriteLine($"  Files with amb alpha != 0: {ambAlphaNZ}/{lightPaths.Count}");
    // Show their distinct alpha values by file
    foreach (var path in lightPaths.Take(5))
    {
        var l = EnvironmentBinParsers.ParseLight(archive.GetFileContent(path));
        var da = l.DirectionalKeyframes.Select(kf => kf.ColorA[3]).Distinct().OrderBy(x => x).ToList();
        var aa = l.AmbientKeyframes.Select(kf => kf.ColorA[3]).Distinct().OrderBy(x => x).ToList();
        Console.WriteLine($"  {path}: dir_alpha=[{string.Join(",", da.Select(v => v.ToString("F4")))}]  amb_alpha=[{string.Join(",", aa.Select(v => v.ToString("F4")))}]");
    }
}

// ---- gap A: 48 bytes = one 48-byte keyframe slot = wrap-around kf48
Console.WriteLine("\n=== GAP A as a 49th keyframe slot ===");
{
    // Parse gapA as 3 float4 groups
    var light15 = archive.GetFileContent("data/sky/dat/light015.bin");
    var span15 = light15.Span;
    Console.WriteLine("  light015 Gap A (0x0900) decoded as color_A, color_B, color_C:");
    float[] gapA_cA = [BinaryPrimitives.ReadSingleLittleEndian(span15[0x0900..]), BinaryPrimitives.ReadSingleLittleEndian(span15[0x0904..]), BinaryPrimitives.ReadSingleLittleEndian(span15[0x0908..]), BinaryPrimitives.ReadSingleLittleEndian(span15[0x090C..])];
    float[] gapA_cB = [BinaryPrimitives.ReadSingleLittleEndian(span15[0x0910..]), BinaryPrimitives.ReadSingleLittleEndian(span15[0x0914..]), BinaryPrimitives.ReadSingleLittleEndian(span15[0x0918..]), BinaryPrimitives.ReadSingleLittleEndian(span15[0x091C..])];
    float[] gapA_cC = [BinaryPrimitives.ReadSingleLittleEndian(span15[0x0920..]), BinaryPrimitives.ReadSingleLittleEndian(span15[0x0924..]), BinaryPrimitives.ReadSingleLittleEndian(span15[0x0928..]), BinaryPrimitives.ReadSingleLittleEndian(span15[0x092C..])];
    Console.WriteLine($"    colorA: {gapA_cA[0]:F4} {gapA_cA[1]:F4} {gapA_cA[2]:F4} {gapA_cA[3]:F4}");
    Console.WriteLine($"    colorB: {gapA_cB[0]:F4} {gapA_cB[1]:F4} {gapA_cB[2]:F4} {gapA_cB[3]:F4}");
    Console.WriteLine($"    colorC: {gapA_cC[0]:F4} {gapA_cC[1]:F4} {gapA_cC[2]:F4} {gapA_cC[3]:F4}");
    // Compare to kf0 (first keyframe) — does gap = kf0?
    var light15full = EnvironmentBinParsers.ParseLight(light15);
    var kf0 = light15full.DirectionalKeyframes[0];
    Console.WriteLine($"    kf0 colorA: {kf0.ColorA[0]:F4} {kf0.ColorA[1]:F4} {kf0.ColorA[2]:F4} {kf0.ColorA[3]:F4}");
    // Is gapA == kf0 colorA?
    bool eqKf0 = gapA_cA.SequenceEqual(kf0.ColorA);
    Console.WriteLine($"    Gap A colorA == kf0 colorA: {eqKf0}");
    // What are gap bytes actually? All 0x01?
    Console.Write("  light015 raw gap A bytes: ");
    for (int i = 0x0900; i < 0x0930; i++) Console.Write($"{span15[i]:X2} ");
    Console.WriteLine();
    // gap bytes 01 01 01 01... => float = 1.175494e-38 (denorm near 0)
    Console.WriteLine($"  Interpretation: 0x01010101 as f32 = {BinaryPrimitives.ReadSingleLittleEndian(new byte[]{0x01,0x01,0x01,0x01}):E4}");
}

// ---- Section E pattern: every 4th byte is zero (stride 3 + pad)?
Console.WriteLine("\n=== SECTION E byte pattern ===");
{
    var light11 = archive.GetFileContent("data/sky/dat/light11.bin");
    var span = light11.Span;
    Console.Write("  light11 SectionE bytes[0..47]: ");
    for (int i = 0x13E0; i < 0x13E0 + 48; i++) Console.Write($"{span[i]:X2} ");
    Console.WriteLine();
    // What are those bytes? The non-zero relative offsets show every 4th byte is 0:
    // rel+00,01,02 non-zero; rel+03 zero; rel+04,05,06 non-zero; rel+07 zero...
    // => stride of 3 RGB values + 1 zero? Or 3-byte structs?
    // Check: decode as rgb triples
    Console.Write("  Interpreted as RGB triples + pad: ");
    for (int i = 0; i < 16; i++)
    {
        int off = 0x13E0 + i * 4;
        if (off + 4 > 0x14A8) break;
        Console.Write($"[R={span[off]:X2} G={span[off+1]:X2} B={span[off+2]:X2} p={span[off+3]:X2}] ");
    }
    Console.WriteLine();
    // Interpret as f32
    Console.Write("  As f32: ");
    for (int i = 0; i < 12; i++)
    {
        int off = 0x13E0 + i * 4;
        float v = BinaryPrimitives.ReadSingleLittleEndian(span[off..]);
        Console.Write($"[{i}]={v:F4} ");
    }
    Console.WriteLine();
}

// ---- map_option area 016 details
Console.WriteLine("\n=== MAP_OPTION 016 content ===");
{
    var mo = EnvironmentBinParsers.ParseMapOption(archive.GetFileContent("data/sky/dat/map_option016.bin"));
    Console.WriteLine($"  is_dungeon={mo.IsDungeon} sight={mo.SightDistance} lens={mo.LensFlareEnable} star={mo.StarDomeEnable} cloud={mo.CloudDomeEnable} sun={mo.SunEnable} moon={mo.MoonEnable} skybox={mo.SkyboxEnable} indoor={mo.IndoorFlag} reserved={mo.Reserved}");
    // Check the txt companion
    Console.WriteLine($"  map_option016.bin: EXISTS, fog016.bin: MISSING => area 016 defined but env data absent");
}

// ---- Section D (secondary fog) deeper stats: is 1.0 the max or are there non-near-zero?
Console.WriteLine("\n=== SECTION D secondary fog scalars distribution ===");
{
    var lightPaths = entries.Where(e => e.Name.StartsWith("data/sky/dat/light") && e.Name.EndsWith(".bin")).Select(e => e.Name).ToList();
    var allSecD = new List<float>();
    foreach (var path in lightPaths)
    {
        var l = EnvironmentBinParsers.ParseLight(archive.GetFileContent(path));
        allSecD.AddRange(l.SecondaryFogScalars);
    }
    var nonZero = allSecD.Where(v => v != 0f).ToList();
    Console.WriteLine($"  Total sec D scalars: {allSecD.Count}  non-zero: {nonZero.Count}");
    Console.WriteLine($"  min={allSecD.Min():F4} max={allSecD.Max():F4}");
    if (nonZero.Count > 0)
    {
        Console.WriteLine($"  non-zero distinct values (top 10): {string.Join(", ", nonZero.Distinct().OrderBy(x => x).Take(10).Select(v => v.ToString("F4")))}");
    }
}

// ---- stardome: varied keyframes — are some AREAS all-uniform vs SOME varied?
Console.WriteLine("\n=== STARDOME uniformity per area ===");
{
    var stardPaths = entries.Where(e => e.Name.StartsWith("data/sky/dat/stardome") && e.Name.EndsWith(".bin")).Select(e => e.Name).ToList();
    int uniformAreas = 0, mixedAreas = 0;
    foreach (var path in stardPaths)
    {
        var sd = EnvironmentBinParsers.ParseStarDome(archive.GetFileContent(path));
        bool allUniform = sd.StarColors.All(kf => kf.Distinct().Count() == 1);
        if (allUniform) uniformAreas++; else mixedAreas++;
    }
    Console.WriteLine($"  Areas where all 12 kf are uniform (all 192 stars same BGRA): {uniformAreas}/{stardPaths.Count}");
    Console.WriteLine($"  Areas with at least one varied kf: {mixedAreas}/{stardPaths.Count}");
}

Console.WriteLine("\nDone.");
