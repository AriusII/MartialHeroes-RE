
using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.DataTables;
using MartialHeroes.Assets.Parsers.Effects;
using MartialHeroes.Assets.Parsers.Mesh;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Assets.Parsers.Terrain;
using MartialHeroes.Assets.Parsers.Texture;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Assets.Parsers.World;

namespace MartialHeroes.Tools.VfsExplorer;

internal enum ConvertKind
{
    None,
    GltfStaticMesh,
    GltfSkinnedMesh,
    GltfBudScene,
    GltfTerrain,
    GltfCollision,
    Png,
    ImagePassthrough,
    AudioPassthrough,
    XeffJson
}

internal sealed class CapabilityEntry
{
    public required string Extension { get; init; }
    public required string Name { get; init; }
    public required string Spec { get; init; }
    public Func<string, ReadOnlyMemory<byte>, IReadOnlyList<string>>? Decode { get; init; }
    public ConvertKind Convert { get; init; } = ConvertKind.None;
    public string ConvertOutExt { get; init; } = "";
}

internal static class FormatRegistry
{

    private const uint DdsMagic = 0x20534444;

    private const uint BaniMagic = 0x494E4142;

    private const uint XeffAntiMagic = 0x46464558;

    private const int SoundTableDataSize = 256 * 48;

    private const string BinMapOptionKey = ".bin#map_option";
    private const string BinFogKey = ".bin#fog";
    private const string BinMaterialKey = ".bin#material";
    private const string BinLightKey = ".bin#light";
    private const string BinStarDomeKey = ".bin#stardome";
    private const string BinCloudDomeKey = ".bin#clouddome";
    private const string BinCloudCycleKey = ".bin#cloud_cycle";
    private const string BinPointLightKey = ".bin#point_light";
    private const string BinWindKey = ".bin#wind";
    private const string BinWeatherKey = ".bin#weather";
    private const string BinRegionTableKey = ".bin#regiontable";
    private const string BinCompanionKey = ".bin#companion";

    private const int WeatherBinSize = 240;

        private static readonly Dictionary<string, CapabilityEntry> ByExt = BuildTable();

        public static IEnumerable<CapabilityEntry> All =>
        ByExt.Values.OrderBy(e => e.Extension, StringComparer.Ordinal);

        public static CapabilityEntry? Lookup(string vfsPath, ReadOnlyMemory<byte> content)
    {
        var ext = ExtOf(vfsPath);

        if (ext == ".eff")
        {
            var isSoundTable = vfsPath.Contains("/soundtable", StringComparison.Ordinal);
            return ByExt.TryGetValue(isSoundTable ? ".eff#sound" : ".eff#shape", out var effEntry)
                ? effEntry
                : null;
        }

        if (ext == ".bin")
        {
            var key = ResolveBinKey(vfsPath, content);
            return ByExt.GetValueOrDefault(key);
        }

        return ByExt.GetValueOrDefault(ext);
    }

        public static string ExtOf(string name)
    {
        var dot = name.LastIndexOf('.');
        var slash = name.LastIndexOf('/');
        if (dot < 0 || dot < slash) return "(none)";
        return name[dot..].ToLowerInvariant();
    }

        private static string ResolveBinKey(string vfsPath, ReadOnlyMemory<byte> content)
    {
        var file = vfsPath[(vfsPath.LastIndexOf('/') + 1)..];

        if (file.StartsWith("map_option", StringComparison.Ordinal)) return BinMapOptionKey;
        if (file.StartsWith("cloud_cycle", StringComparison.Ordinal)) return BinCloudCycleKey;
        if (file.StartsWith("clouddome", StringComparison.Ordinal)) return BinCloudDomeKey;
        if (file.StartsWith("point_light", StringComparison.Ordinal)) return BinPointLightKey;
        if (file.StartsWith("stardome", StringComparison.Ordinal)) return BinStarDomeKey;
        if (file.StartsWith("material", StringComparison.Ordinal)) return BinMaterialKey;
        if (file.StartsWith("weather", StringComparison.Ordinal)) return BinWeatherKey;
        if (file.StartsWith("light", StringComparison.Ordinal)) return BinLightKey;
        if (file.StartsWith("wind", StringComparison.Ordinal)) return BinWindKey;
        if (file.StartsWith("fog", StringComparison.Ordinal)) return BinFogKey;

        if (file.StartsWith("regiontable", StringComparison.Ordinal)) return BinRegionTableKey;
        if (file.StartsWith("region", StringComparison.Ordinal)) return BinCompanionKey;
        if (file.StartsWith("map", StringComparison.Ordinal)) return BinCompanionKey;

        return ".bin";
    }

        public static string RunConvert(
        CapabilityEntry entry, string vfsPath, ReadOnlyMemory<byte> content,
        VfsBndResolver resolveBnd, Stream outStream)
    {
        switch (entry.Convert)
        {
            case ConvertKind.GltfStaticMesh:
                GltfConverter.WriteGlb(XobjParser.Parse(content), outStream);
                return "xobj → GLB (static mesh)";

            case ConvertKind.GltfSkinnedMesh:
            {
                var mesh = SknParser.Parse(content);

                var skel = mesh.IdB != 0 ? resolveBnd(mesh.IdB) : null;
                GltfConverter.WriteGlb(mesh, skel, outStream);
                return skel is null
                    ? "skn → GLB (rigid; no skeleton)"
                    : $"skn → GLB (skinned; bnd g{mesh.IdB}, {skel.Bones.Length} bones)";
            }

            case ConvertKind.GltfBudScene:
                BudSceneGltfConverter.WriteGlb(TerrainSceneParser.Parse(content), outStream);
                return "bud → GLB (object scene)";

            case ConvertKind.GltfTerrain:
                TerrainGltfConverter.WriteGlb(TedTerrainParser.Parse(content), outStream);
                return "ted → GLB (terrain cell)";

            case ConvertKind.GltfCollision:
                CollisionLayerGltfConverter.WriteGlb(TerrainLayerParsers.ParseUpOrExd(content), outStream);
                return "collision → GLB (triangle list)";

            case ConvertKind.Png:
                PngConverter.WritePng(TextureDetector.Detect(content), outStream);
                return "dds → PNG (DXT decode)";

            case ConvertKind.ImagePassthrough:
            {
                var img = AssetPassthrough.PassthroughImage(content, vfsPath);
                outStream.Write(img.Bytes.Span);
                return $"image passthrough ({img.Format} {img.Width}x{img.Height})";
            }

            case ConvertKind.AudioPassthrough:
            {
                var au = AssetPassthrough.PassthroughAudio(content);
                outStream.Write(au.Bytes.Span);
                return $"audio passthrough ({au.Format})";
            }

            case ConvertKind.XeffJson:
                outStream.Write(XeffJsonConverter.WriteJsonBytes(XeffParser.ParseXeff(content)));
                return "xeff → JSON";

            default:
                return "(no converter)";
        }
    }

    private static Dictionary<string, CapabilityEntry> BuildTable()
    {
        var t = new Dictionary<string, CapabilityEntry>(StringComparer.Ordinal);

        void Add(string ext, string name, string spec,
            Func<string, ReadOnlyMemory<byte>, IReadOnlyList<string>>? decode = null,
            ConvertKind convert = ConvertKind.None, string outExt = "")
        {
            t[ext] = new CapabilityEntry
            {
                Extension = ext, Name = name, Spec = spec,
                Decode = decode, Convert = convert, ConvertOutExt = outExt
            };
        }

        Add(".skn", "Skinned mesh", "Docs/RE/formats/mesh.md",
            DecodeSkn, ConvertKind.GltfSkinnedMesh, ".glb");
        Add(".bnd", "Bind-pose skeleton", "Docs/RE/formats/mesh.md", DecodeBnd);
        Add(".xobj", "ASCII static mesh", "Docs/RE/formats/mesh.md",
            DecodeXobj, ConvertKind.GltfStaticMesh, ".glb");
        Add(".mot", "Skeletal animation clip", "Docs/RE/formats/animation.md", DecodeMot);

        Add(".ted", "Terrain cell (heights/textures)", "Docs/RE/formats/terrain.md",
            DecodeTed, ConvertKind.GltfTerrain, ".glb");
        Add(".bud", "Static-object cell scene", "Docs/RE/formats/terrain_scene.md",
            DecodeBud, ConvertKind.GltfBudScene, ".glb");
        Add(".map", "Map descriptor (texture/section table)", "Docs/RE/formats/terrain.md", DecodeMap);
        Add(".sod", "Collision blob (XZ wall segments)", "Docs/RE/formats/terrain.md", DecodeSod);
        Add(".up", "Collision triangle layer", "Docs/RE/formats/terrain_layers.md",
            DecodeCollision, ConvertKind.GltfCollision, ".glb");
        Add(".exd", "Collision triangle layer", "Docs/RE/formats/terrain_layers.md",
            DecodeCollision, ConvertKind.GltfCollision, ".glb");

        Add(".arr", "Spawn array (npc 28B / mob 20B)", "Docs/RE/formats/npc_spawns.md", DecodeArr);

        Add(".dds", "DirectDraw Surface texture", "Docs/RE/formats/texture.md",
            DecodeDds, ConvertKind.Png, ".png");
        Add(".tga", "Truevision TGA texture", "Docs/RE/formats/texture.md",
            null, ConvertKind.ImagePassthrough, ".tga");
        Add(".bmp", "Windows bitmap", "Docs/RE/formats/texture.md",
            DecodeImage, ConvertKind.ImagePassthrough, ".bmp");
        Add(".png", "PNG texture", "Docs/RE/formats/texture.md",
            DecodeImage, ConvertKind.ImagePassthrough, ".png");

        Add(".ogg", "OGG Vorbis audio", "Docs/RE/formats/sound_tables.md",
            DecodeAudio, ConvertKind.AudioPassthrough, ".ogg");

        Add(".xeff", "Particle effect descriptor", "Docs/RE/formats/effects.md",
            DecodeXeff, ConvertKind.XeffJson, ".json");

        Add(".eff#sound", "Per-area sound table (.eff variant)", "Docs/RE/formats/sound_tables.md",
            DecodeSoundTable);
        Add(".eff#shape", "Effect-object shape geometry", "Docs/RE/formats/effects.md", DecodeEffShape);

        Add(".bgm", "Background-music sound table", "Docs/RE/formats/sound_tables.md", DecodeSoundTable);
        Add(".bge", "Background-ambient sound table", "Docs/RE/formats/sound_tables.md", DecodeSoundTable);
        Add(".wlk", "Walk-sound table", "Docs/RE/formats/sound_tables.md", DecodeSoundTable);
        Add(".run", "Run-sound table", "Docs/RE/formats/sound_tables.md", DecodeSoundTable);

        Add(".xdb", "Message catalogue (msg.xdb 516B records)", "Docs/RE/formats/misc_data.md", DecodeXdb);
        Add(".csv", "Items CSV (CP949)", "Docs/RE/formats/config_tables.md", DecodeItemsCsv);
        Add(".do", "Per-class stance icon table (116B)", "Docs/RE/formats/ui_manifests.md", DecodeDo);

        Add(".vsh", "D3D9 vertex shader source", "Docs/RE/formats/shaders.md", DecodeShader);
        Add(".psh", "D3D9 pixel shader source", "Docs/RE/formats/shaders.md", DecodeShader);

        Add(BinMapOptionKey, "Per-area master flags (map_option*.bin)", "Docs/RE/formats/environment_bins.md",
            DecodeBinMapOption);
        Add(BinFogKey, "Per-area fog parameters (fog*.bin)", "Docs/RE/formats/environment_bins.md", DecodeBinFog);
        Add(BinMaterialKey, "Sun/sky material colour table (material*.bin)", "Docs/RE/formats/environment_bins.md",
            DecodeBinMaterial);
        Add(BinLightKey, "Sky-lighting keyframes (light*.bin)", "Docs/RE/formats/environment_bins.md", DecodeBinLight);
        Add(BinStarDomeKey, "Star colour grid (stardome*.bin)", "Docs/RE/formats/environment_bins.md",
            DecodeBinStarDome);
        Add(BinCloudDomeKey, "Cloud-dome colour grid (clouddome*.bin)", "Docs/RE/formats/environment_bins.md",
            DecodeBinCloudDome);
        Add(BinCloudCycleKey, "Cloud animation schedule (cloud_cycle*.bin)", "Docs/RE/formats/environment_bins.md",
            DecodeBinCloudCycle);

        Add(BinPointLightKey, "Per-area point-light array (point_light*.bin)", "Docs/RE/formats/terrain_layers.md",
            DecodeBinPointLight);
        Add(BinWindKey, "Foliage-sway keyframes (wind*.bin)", "Docs/RE/formats/terrain_layers.md", DecodeBinWind);

        Add(BinWeatherKey, "Weather parameters (weather*.bin) — PARTIAL", "Docs/RE/formats/environment_bins.md",
            DecodeBinWeather);

        Add(BinRegionTableKey, "Per-area sub-zone label table (regiontable*.bin)", "Docs/RE/formats/misc_data.md",
            DecodeBinRegionTable);

        Add(BinCompanionKey, "Per-area region/map companion .bin (layout UNVERIFIED)", "Docs/RE/formats/misc_data.md",
            DecodeBinCompanion);

        Add(".lst", "Per-area cell manifest (area census; cell keys)", "Docs/RE/formats/area_inventory.md", DecodeLst);

        return t;
    }

    private static IReadOnlyList<string> DecodeSkn(string path, ReadOnlyMemory<byte> raw)
    {
        var m = SknParser.Parse(raw);
        return
        [
            $"id_a={m.IdA}  id_b(skeleton)={m.IdB}  name=\"{Safe(m.Name)}\"",
            $"vertices={m.Positions.Length:N0}  faces={m.FaceCount:N0}  corners={m.Corners.Length:N0}",
            $"weights={m.Weights.Length:N0}  " +
            (m.IdB == 0 ? "rigid (no skeleton)" : $"binds to data/char/bind/g{m.IdB}.bnd")
        ];
    }

    private static IReadOnlyList<string> DecodeBnd(string path, ReadOnlyMemory<byte> raw)
    {
        var s = BndParser.Parse(raw);
        var roots = 0;
        foreach (var b in s.Bones)
            if (b.IsRoot)
                roots++;
        return
        [
            $"actor_id={s.ActorId}  name=\"{Safe(s.ActorName)}\"",
            $"bones={s.Bones.Length:N0}  roots={roots}"
        ];
    }

    private static IReadOnlyList<string> DecodeXobj(string path, ReadOnlyMemory<byte> raw)
    {
        var m = XobjParser.Parse(raw);
        return
        [
            $"vertices={m.Positions.Length:N0}  uvs={m.Uvs.Length:N0}  " +
            $"indices={m.Indices.Length:N0}  triangles={m.Indices.Length / 3:N0}"
        ];
    }

    private static IReadOnlyList<string> DecodeMot(string path, ReadOnlyMemory<byte> raw)
    {
        if (raw.Length >= 4 &&
            BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[..4]) == BaniMagic)

            return ["variant=BANI (dead/unloadable magic 'BANI'); not parsed"];

        var clip = AnimationParser.Parse(raw);
        if (clip is null)
            return ["unparseable .mot (AnimationParser returned null)"];

        var real = clip.FrameCount > 0 && clip.Tracks.Length > 0;
        return
        [
            $"id_a={clip.IdA}  id_b={clip.IdB}  name=\"{Safe(clip.Name)}\"",
            $"frame_count={clip.FrameCount}  tracks={clip.Tracks.Length}  " +
            $"duration={clip.FrameCount * 0.1f:F1}s  {(real ? "REAL clip" : "stub")}"
        ];
    }

    private static IReadOnlyList<string> DecodeTed(string path, ReadOnlyMemory<byte> raw)
    {
        var c = TedTerrainParser.Parse(raw);
        return
        [

            $"height_samples={c.Heights.Length:N0}  normals={c.Normals.Length:N0}",
            $"texture_index_grid={c.TextureIndexGrid.Length:N0}  " +
            $"direction_flags={c.DirectionFlags.Length:N0}  diffuse_colours={c.DiffuseColours.Length:N0}"
        ];
    }

    private static IReadOnlyList<string> DecodeBud(string path, ReadOnlyMemory<byte> raw)
    {
        var s = TerrainSceneParser.Parse(raw);
        long verts = 0, idx = 0;
        foreach (var o in s.Objects)
        {
            verts += o.Vertices.Length;
            idx += o.Indices.Length;
        }

        return
        [
            $"objects={s.Objects.Length:N0}  total_vertices={verts:N0}  total_triangles={idx / 3:N0}"
        ];
    }

    private static IReadOnlyList<string> DecodeMap(string path, ReadOnlyMemory<byte> raw)
    {
        var d = MapDescriptorParser.Parse(raw);
        var lines = new List<string> { $"sections={d.Sections.Length}" };
        foreach (var sec in d.Sections)
            lines.Add($"  section \"{Safe(sec.Keyword)}\"  textures={sec.Textures.Length}");
        return lines;
    }

    private static IReadOnlyList<string> DecodeSod(string path, ReadOnlyMemory<byte> raw)
    {
        var b = SodBlobParser.Parse(raw);
        return
        [

            $"solid_count={b.SolidCount}  solids={b.Solids.Length:N0}  " +
            $"quad_groups={b.QuadCounts.Length:N0}"
        ];
    }

    private static IReadOnlyList<string> DecodeCollision(string path, ReadOnlyMemory<byte> raw)
    {
        var tl = TerrainLayerParsers.ParseUpOrExd(raw);

        return [$"collision triangle layer (.up/.exd)  triangles={tl.Triangles.Length:N0}"];
    }

    private static IReadOnlyList<string> DecodeArr(string path, ReadOnlyMemory<byte> raw)
    {

        var name = path[(path.LastIndexOf('/') + 1)..];
        if (name.StartsWith("npc", StringComparison.Ordinal))
        {
            var arr = NpcSpawnParser.Parse(raw);
            return [$"npc spawn array  records={arr.Records.Length:N0}  (28B stride)"];
        }

        if (name.StartsWith("mob", StringComparison.Ordinal))
        {
            var recs = MobSpawnParser.Parse(raw);
            return [$"mob spawn array  records={recs.Length:N0}  (20B stride)"];
        }

        return [$"spawn array  {raw.Length:N0} bytes (name does not start with npc/mob — ambiguous stride)"];
    }

    private static IReadOnlyList<string> DecodeDds(string path, ReadOnlyMemory<byte> raw)
    {

        const int MinHeader = 0x58;
        if (raw.Length < MinHeader ||
            BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[..4]) != DdsMagic)
            return ["not a DDS file (magic mismatch or truncated)"];

        var h = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span.Slice(0x0C, 4));
        var w = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span.Slice(0x10, 4));
        var fourccRaw = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span.Slice(0x54, 4));
        var fourcc = fourccRaw == 0
            ? "uncompressed RGBA"
            : Encoding.ASCII.GetString(BitConverter.GetBytes(fourccRaw)).TrimEnd('\0');
        var npot = !IsPow2(w) || !IsPow2(h);
        return [$"DDS  {w}x{h}  format={fourcc}{(npot ? "  *** non-power-of-2 ***" : "")}"];
    }

    private static IReadOnlyList<string> DecodeImage(string path, ReadOnlyMemory<byte> raw)
    {
        try
        {
            var img = AssetPassthrough.PassthroughImage(raw, path);
            return [$"{img.Format}  {img.Width}x{img.Height}"];
        }
        catch (NotSupportedException ex)
        {
            return [$"unrecognised image ({ex.Message})"];
        }
    }

    private static IReadOnlyList<string> DecodeAudio(string path, ReadOnlyMemory<byte> raw)
    {
        try
        {
            var au = AssetPassthrough.PassthroughAudio(raw);
            return [$"{au.Format}  {raw.Length:N0} bytes"];
        }
        catch (NotSupportedException ex)
        {
            return [$"unrecognised audio ({ex.Message})"];
        }
    }

    private static IReadOnlyList<string> DecodeXeff(string path, ReadOnlyMemory<byte> raw)
    {
        if (raw.Length >= 4 &&
            BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[..4]) == XeffAntiMagic)
            return ["invalid .xeff (anti-magic sentinel 0x46464558)"];

        var fx = XeffParser.ParseXeff(raw);

        return [$"effect_id={fx.EffectId}  sub_effects={fx.SubEffects.Length:N0}"];
    }

    private static IReadOnlyList<string> DecodeEffShape(string path, ReadOnlyMemory<byte> raw)
    {

        if (raw.Length < 4) return ["truncated .eff shape"];
        var indexCount = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[..4]);
        return [$"effect-object shape  index_count={indexCount:N0}  triangles={indexCount / 3:N0}"];
    }

    private static IReadOnlyList<string> DecodeSoundTable(string path, ReadOnlyMemory<byte> raw)
    {
        if (raw.Length < SoundTableDataSize)
            return [$"sound table too short ({raw.Length:N0} < {SoundTableDataSize} bytes)"];

        var nonNull = 0;
        var span = raw.Span[..SoundTableDataSize];
        for (var i = 0; i < 256; i++)
            if (BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(i * 48, 4)) != 0)
                nonNull++;
        return [$"sound table  entries=256  non-null={nonNull}  (48B stride)"];
    }

    private static IReadOnlyList<string> DecodeXdb(string path, ReadOnlyMemory<byte> raw)
    {
        var cat = MsgXdbParser.Parse(raw);
        return [$"message catalogue  records={cat.Records.Count:N0}  (516B stride)"];
    }

    private static IReadOnlyList<string> DecodeItemsCsv(string path, ReadOnlyMemory<byte> raw)
    {
        var rows = ItemsCsvParser.Parse(raw);
        return [$"items CSV  rows={rows.Length:N0}  (CP949)"];
    }

    private static IReadOnlyList<string> DecodeDo(string path, ReadOnlyMemory<byte> raw)
    {
        var tbl = DoStanceParser.Parse(raw);
        return
        [

            $"stance icon table  records={tbl.TotalRecordCount:N0}  trailing_bytes={tbl.TrailingByteCount}"
        ];
    }

    private static IReadOnlyList<string> DecodeShader(string path, ReadOnlyMemory<byte> raw)
    {
        var src = ShaderContainerParser.Parse(raw);

        return [$"D3D9 {src.ShaderType} shader  source_chars={src.SourceText.Length:N0}  (ASCII)"];
    }

    private static IReadOnlyList<string> DecodeBinMapOption(string path, ReadOnlyMemory<byte> raw)
    {
        var o = EnvironmentBinParsers.ParseMapOption(raw);

        return
        [
            $"map_option  is_dungeon={o.IsDungeon}  sight_fix={o.SightDistance}  indoor={o.IndoorFlag}",
            $"lensflare={o.LensFlareEnable}  stardome={o.StarDomeEnable}  clouddome={o.CloudDomeEnable}  " +
            $"sun={o.SunEnable}  moon={o.MoonEnable}  skybox={o.SkyboxEnable}"
        ];
    }

    private static IReadOnlyList<string> DecodeBinFog(string path, ReadOnlyMemory<byte> raw)
    {
        var f = EnvironmentBinParsers.ParseFog(raw);

        return
        [
            $"fog  start_dist={f.StartDist:F3}  end_dist={f.EndDist:F3}  " +
            $"data_load_flag={f.DataLoadFlag}  colour_keyframes={f.FogColors.Length}"
        ];
    }

    private static IReadOnlyList<string> DecodeBinMaterial(string path, ReadOnlyMemory<byte> raw)
    {
        var m = EnvironmentBinParsers.ParseMaterial(raw);

        return
        [
            $"material colour table  keyframes={m.ColorTable.Length}  values_per_keyframe={MaterialBin.ValuesPerKeyframe}"
        ];
    }

    private static IReadOnlyList<string> DecodeBinLight(string path, ReadOnlyMemory<byte> raw)
    {
        var l = EnvironmentBinParsers.ParseLight(raw);

        return
        [
            $"light  directional_keyframes={l.DirectionalKeyframes.Length}  ambient_keyframes={l.AmbientKeyframes.Length}",
            $"fog_distance_scalars={l.FogDistanceScalars.Length}  secondary_fog_scalars={l.SecondaryFogScalars.Length}",
            $"fallback_light scale={l.FallbackScale:F1} dir=({l.FallbackDirX:F1},{l.FallbackDirY:F1},{l.FallbackDirZ:F1})"
        ];
    }

    private static IReadOnlyList<string> DecodeBinStarDome(string path, ReadOnlyMemory<byte> raw)
    {
        var s = EnvironmentBinParsers.ParseStarDome(raw);

        return
        [
            $"stardome  keyframes={s.StarColors.Length}  stars_per_keyframe={StarDomeBin.StarsPerKeyframe}  (12-frame cycle)"
        ];
    }

    private static IReadOnlyList<string> DecodeBinCloudDome(string path, ReadOnlyMemory<byte> raw)
    {
        var c = EnvironmentBinParsers.ParseCloudDome(raw);

        return
        [
            $"clouddome  layers=2  keyframes={c.Layer1Colors.Length}  vertices_per_keyframe={CloudDomeBin.VerticesPerKeyframe}"
        ];
    }

    private static IReadOnlyList<string> DecodeBinCloudCycle(string path, ReadOnlyMemory<byte> raw)
    {
        var c = EnvironmentBinParsers.ParseCloudCycle(raw);

        return [$"cloud_cycle  day_patterns={c.Rows.Length}  (10 × 7 u8 schedule)"];
    }

    private static IReadOnlyList<string> DecodeBinPointLight(string path, ReadOnlyMemory<byte> raw)
    {
        var p = TerrainLayerParsers.ParsePointLightBin(raw);
        var enabled = 0;
        foreach (var r in p.Records)
            if (r.EnabledFlag == 0)
                enabled++;

        return [$"point_light  proximity_radius={p.ProximityRadius}  records={p.Records.Length}  enabled={enabled}"];
    }

    private static IReadOnlyList<string> DecodeBinWind(string path, ReadOnlyMemory<byte> raw)
    {
        var w = TerrainLayerParsers.ParseWindBin(raw);

        return [$"wind  count={w.Count}  flag2={w.Flag2}  (24-byte keyframe records; fields UNVERIFIED)"];
    }

    private static IReadOnlyList<string> DecodeBinWeather(string path, ReadOnlyMemory<byte> raw)
    {

        var isRain = path.Contains("_rain", StringComparison.Ordinal);
        var variant = isRain ? "weather_rain" : "weather";
        if (raw.Length != WeatherBinSize)
            return [$"{variant}  {raw.Length:N0} bytes (expected {WeatherBinSize}; body not decoded — PARTIAL)"];

        var allZero = true;
        foreach (var b in raw.Span)
            if (b != 0)
            {
                allZero = false;
                break;
            }

        return
        [
            $"{variant}  {WeatherBinSize} bytes  {(allZero ? "all-zero (inactive)" : "non-zero body present")}  (body not decoded — PARTIAL)"
        ];
    }

    private static IReadOnlyList<string> DecodeBinRegionTable(string path, ReadOnlyMemory<byte> raw)
    {
        var recs = RegionTableParser.Parse(raw);

        return [$"regiontable  sub_zone_records={recs.Length}  (32B stride)"];
    }

    private static IReadOnlyList<string> DecodeBinCompanion(string path, ReadOnlyMemory<byte> raw)
    {

        var file = path[(path.LastIndexOf('/') + 1)..];
        var kind = file.StartsWith("map", StringComparison.Ordinal)
            ? "map<NNN>.bin (fixed 520B)"
            : "region<NNN>.bin";
        return [$"{kind}  {raw.Length:N0} bytes  (companion .bin; layout UNVERIFIED — misc_data.md §7.2)"];
    }

    private static IReadOnlyList<string> DecodeLst(string path, ReadOnlyMemory<byte> raw)
    {

        var file = path[(path.LastIndexOf('/') + 1)..];
        var isAreaManifest = path.Contains("/dat/", StringComparison.Ordinal) &&
                             file.StartsWith("d", StringComparison.Ordinal);

        if (raw.Length < 4)
            return [".lst too short for a u32 count prefix"];

        if (!isAreaManifest)
        {

            var hdrCount = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[..4]);
            return
            [
                $"generic .lst (not a d<NNN>.lst area manifest)  leading_u32={hdrCount:N0}  size={raw.Length:N0} bytes",
                "  (effect/index list — different layout; not the area-inventory cell manifest)"
            ];
        }

        var m = LstManifestParser.Parse(raw);

        long byFormula = (raw.Length - 4) / 4;
        var distinct = new HashSet<uint>();
        foreach (var e in m.Entries) distinct.Add(e.Key);
        var dupes = m.Entries.Length - distinct.Count;
        return
        [
            $"area manifest  cell_entries={m.Entries.Length}  distinct_keys={distinct.Count}" +
            (dupes > 0 ? $"  duplicate_keys={dupes}" : ""),
            $"size cross-check (file_size-4)/4 = {byFormula}  " +
            (byFormula == m.Entries.Length ? "(matches count field)" : "(MISMATCH vs count field)")
        ];
    }

    private static bool IsPow2(uint v)
    {
        return v > 0 && (v & (v - 1)) == 0;
    }

    private static string Safe(string s)
    {
        const int cap = 48;
        var sb = new StringBuilder(Math.Min(s.Length, cap));
        foreach (var c in s)
        {
            if (sb.Length >= cap)
            {
                sb.Append('…');
                break;
            }

            sb.Append(char.IsControl(c) ? '.' : c);
        }

        return sb.ToString();
    }
}

internal delegate Skeleton? VfsBndResolver(uint idB);
