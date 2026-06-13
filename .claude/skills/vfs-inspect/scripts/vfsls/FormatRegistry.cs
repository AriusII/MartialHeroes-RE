// FormatRegistry — the single extension→capability table that drives the auto-dispatch
// subcommands (decode / convert / coverage). One registry, many consumers: every command that
// needs to know "which parser owns this file?" or "can this be converted?" reads it here.
//
// Design:
//   * One CapabilityEntry per known file extension. The entry carries a friendly NAME, the
//     source SPEC doc under Docs/RE/formats/, an optional DECODE delegate (raw bytes → a short
//     structured summary, via the production Assets.Parsers decoders) and an optional CONVERT
//     delegate (raw bytes + out-dir → modern interchange via Assets.Mapping).
//   * Auto-detection is extension-first. Where an extension is AMBIGUOUS (e.g. ".eff" is both a
//     sound table under data/map*/soundtable*.eff AND effect-object geometry under
//     data/effect/obj/, or ".bin" covers a dozen env/region blobs), a small magic/signature +
//     path disambiguator resolves it. NO raw copyrighted bytes are ever printed — decoders emit
//     COUNTS, dimensions, and key header fields only.
//
// Every magic/offset constant cites its source spec, per the clean-room rule.

using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;

namespace Vfsls;

/// <summary>
/// What a registry entry can do with a file of its extension.
/// </summary>
internal enum ConvertKind
{
    None,
    GltfStaticMesh,   // .xobj → GLB
    GltfSkinnedMesh,  // .skn (+ .bnd) → GLB
    GltfBudScene,     // .bud → GLB
    GltfTerrain,      // .ted → GLB
    GltfCollision,    // .up / .exd → GLB
    Png,              // .dds → PNG (DXT decode)
    ImagePassthrough, // .tga/.bmp/.png → same bytes, dims reported
    AudioPassthrough, // .ogg/.wav → same bytes, format reported
    XeffJson,         // .xeff → JSON
}

/// <summary>
/// One extension's capabilities. <see cref="Decode"/> returns a list of human-readable summary
/// lines (counts / header fields only — NEVER raw payload bytes). It may be null for formats that
/// have no structured decoder yet (the file is still extractable + hexdumpable).
/// </summary>
internal sealed class CapabilityEntry
{
    public required string Extension { get; init; }          // includes leading dot, lower-case
    public required string Name { get; init; }               // friendly format name
    public required string Spec { get; init; }               // Docs/RE/formats/<x>.md (or "" if none)
    public Func<string, ReadOnlyMemory<byte>, IReadOnlyList<string>>? Decode { get; init; }
    public ConvertKind Convert { get; init; } = ConvertKind.None;
    public string ConvertOutExt { get; init; } = "";         // e.g. ".glb", ".png", ".json"
}

/// <summary>
/// The shared registry. <see cref="Lookup"/> resolves an extension (with ambiguity handling that
/// also considers the virtual path) to its <see cref="CapabilityEntry"/>.
/// </summary>
internal static class FormatRegistry
{
    // ── Magic constants (all little-endian u32 unless noted) ────────────────────────────────
    // spec: Docs/RE/formats/texture.md §DDS — magic "DDS " (44 44 53 20): CONFIRMED.
    private const uint DdsMagic = 0x20534444;
    // spec: Docs/RE/formats/animation.md §BANI variant — magic "BANI" (42 41 4E 49): SAMPLE-VERIFIED.
    private const uint BaniMagic = 0x494E4142;
    // spec: Docs/RE/formats/effects.md §A.1 Anti-magic 0x46464558 ("XEFF" LE): CONFIRMED.
    private const uint XeffAntiMagic = 0x46464558;
    // spec: Docs/RE/formats/sound_tables.md §Overall structure — loader reads exactly 256×48 = 12288 bytes.
    private const int SoundTableDataSize = 256 * 48;

    // ── .bin env-family synthetic registry keys ─────────────────────────────────────────────
    // The bare ".bin" extension is shared by a dozen unrelated per-area blobs that carry NO magic
    // and NO version field. They are disambiguated SOLELY by the file-name PREFIX (the part before
    // the trailing area-id digits), confirmed by the fixed file size where one exists. The synthetic
    // keys below are resolved by ResolveBinKey() in Lookup().
    // spec: Docs/RE/formats/environment_bins.md §Overview — "no magic number, no version field".
    private const string BinMapOptionKey   = ".bin#map_option";
    private const string BinFogKey         = ".bin#fog";
    private const string BinMaterialKey    = ".bin#material";
    private const string BinLightKey       = ".bin#light";
    private const string BinStarDomeKey    = ".bin#stardome";
    private const string BinCloudDomeKey   = ".bin#clouddome";
    private const string BinCloudCycleKey  = ".bin#cloud_cycle";
    private const string BinPointLightKey  = ".bin#point_light";
    private const string BinWindKey        = ".bin#wind";
    private const string BinWeatherKey     = ".bin#weather";
    private const string BinRegionTableKey = ".bin#regiontable";
    private const string BinCompanionKey   = ".bin#companion"; // region<NNN>.bin / map<NNN>.bin (UNVERIFIED layout)

    // weather%d.bin / weather%d_rain.bin fixed size (PARTIAL — content not decoded).
    // spec: Docs/RE/formats/environment_bins.md §7 / §8 — "exactly 240 bytes": SAMPLE-VERIFIED (size only).
    private const int WeatherBinSize = 240;

    /// <summary>
    /// All registered entries, keyed by extension. Built once.
    /// </summary>
    private static readonly Dictionary<string, CapabilityEntry> ByExt = BuildTable();

    /// <summary>All entries (for the coverage command), in stable extension order.</summary>
    public static IEnumerable<CapabilityEntry> All =>
        ByExt.Values.OrderBy(e => e.Extension, StringComparer.Ordinal);

    /// <summary>
    /// Resolves the capability entry for a virtual path. Extension-first; for ambiguous
    /// extensions the path + leading magic bytes pick the right decoder.
    /// Returns null when the extension is not registered.
    /// </summary>
    public static CapabilityEntry? Lookup(string vfsPath, ReadOnlyMemory<byte> content)
    {
        string ext = ExtOf(vfsPath);

        // ── Ambiguity resolution ────────────────────────────────────────────────────────────
        // .eff: sound table (data/map*/soundtable*.eff) vs effect-object geometry (data/effect/obj/*.eff).
        // spec: Docs/RE/formats/sound_tables.md §CRITICAL DISAMBIGUATION — .eff path disambiguation.
        if (ext == ".eff")
        {
            bool isSoundTable = vfsPath.Contains("/soundtable", StringComparison.Ordinal);
            return ByExt.TryGetValue(isSoundTable ? ".eff#sound" : ".eff#shape", out CapabilityEntry? effEntry)
                ? effEntry
                : null;
        }

        // .bin: a dozen unrelated per-area blobs share this extension with no magic. Resolve by
        // the file-name PREFIX (everything before the trailing area-id digits), confirmed by size.
        // spec: Docs/RE/formats/environment_bins.md §Overview — "no magic number, no version field".
        if (ext == ".bin")
        {
            string key = ResolveBinKey(vfsPath, content);
            return ByExt.GetValueOrDefault(key);
        }

        return ByExt.GetValueOrDefault(ext);
    }

    /// <summary>Lower-case file extension (with dot), or "(none)".</summary>
    public static string ExtOf(string name)
    {
        int dot = name.LastIndexOf('.');
        int slash = name.LastIndexOf('/');
        if (dot < 0 || dot < slash) return "(none)";
        return name[dot..].ToLowerInvariant();
    }

    /// <summary>
    /// Maps a <c>*.bin</c> virtual path to its synthetic registry key by file-name prefix.
    /// The per-area env/region <c>.bin</c> blobs carry no magic and no version field, so the
    /// only reliable discriminator is the name prefix (the text before the trailing area-id
    /// digits); the fixed file size confirms it. Order matters: <c>cloud_cycle</c> must be tested
    /// before <c>clouddome</c>/<c>cloud</c>, and <c>point_light</c> before <c>light</c>, because
    /// the longer prefix subsumes the shorter.
    /// spec: Docs/RE/formats/environment_bins.md §Overview — file family resolved by name pattern.
    /// </summary>
    private static string ResolveBinKey(string vfsPath, ReadOnlyMemory<byte> content)
    {
        string file = vfsPath[(vfsPath.LastIndexOf('/') + 1)..]; // already lower-cased by NormPath

        // Longest-prefix-first so e.g. "point_light" wins over "light", "cloud_cycle" over "cloud".
        // spec: Docs/RE/formats/environment_bins.md §Overview — per-area filename substitution "%d".
        if (file.StartsWith("map_option", StringComparison.Ordinal))  return BinMapOptionKey;
        if (file.StartsWith("cloud_cycle", StringComparison.Ordinal)) return BinCloudCycleKey;
        if (file.StartsWith("clouddome", StringComparison.Ordinal))   return BinCloudDomeKey;
        if (file.StartsWith("point_light", StringComparison.Ordinal)) return BinPointLightKey;
        if (file.StartsWith("stardome", StringComparison.Ordinal))    return BinStarDomeKey;
        if (file.StartsWith("material", StringComparison.Ordinal))    return BinMaterialKey;
        if (file.StartsWith("weather", StringComparison.Ordinal))     return BinWeatherKey;
        if (file.StartsWith("light", StringComparison.Ordinal))       return BinLightKey;
        if (file.StartsWith("wind", StringComparison.Ordinal))        return BinWindKey;
        if (file.StartsWith("fog", StringComparison.Ordinal))         return BinFogKey;
        // regiontable<NNN>.bin (a fixed 32-byte-stride label table) vs the companion region<NNN>.bin.
        // spec: Docs/RE/formats/misc_data.md §7.2 — regiontableNNN.bin vs region<NNN>.bin / map<NNN>.bin.
        if (file.StartsWith("regiontable", StringComparison.Ordinal)) return BinRegionTableKey;
        if (file.StartsWith("region", StringComparison.Ordinal))      return BinCompanionKey;
        if (file.StartsWith("map", StringComparison.Ordinal))         return BinCompanionKey;

        // Unknown .bin family: leave unresolved (decode reports UNREGISTERED, hexdump still works).
        return ".bin";
    }

    // ── Convert dispatch (used by the `convert` command) ────────────────────────────────────

    /// <summary>
    /// Performs the <see cref="ConvertKind"/> conversion for an entry, writing the result to the
    /// open output <paramref name="outStream"/>. <paramref name="archive"/> is supplied so the
    /// skinned-mesh path can resolve the companion .bnd skeleton. Returns a short status note.
    /// </summary>
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
                SkinnedMesh mesh = SknParser.Parse(content);
                // .skn IdB → data/char/bind/g{IdB}.bnd companion skeleton.
                // spec: Docs/RE/formats/mesh.md §Header — id_b binds to a .bnd skeleton: CONFIRMED.
                Skeleton? skel = mesh.IdB != 0 ? resolveBnd(mesh.IdB) : null;
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
                ImagePassthroughResult img = AssetPassthrough.PassthroughImage(content, vfsPath);
                outStream.Write(img.Bytes.Span);
                return $"image passthrough ({img.Format} {img.Width}x{img.Height})";
            }

            case ConvertKind.AudioPassthrough:
            {
                AudioPassthroughResult au = AssetPassthrough.PassthroughAudio(content);
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

    // ── Table construction ──────────────────────────────────────────────────────────────────

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
                Decode = decode, Convert = convert, ConvertOutExt = outExt,
            };
        }

        // ── Meshes / skeletons / animation ──────────────────────────────────────────────────
        Add(".skn", "Skinned mesh", "Docs/RE/formats/mesh.md",
            DecodeSkn, ConvertKind.GltfSkinnedMesh, ".glb");
        Add(".bnd", "Bind-pose skeleton", "Docs/RE/formats/mesh.md", DecodeBnd);
        Add(".xobj", "ASCII static mesh", "Docs/RE/formats/mesh.md",
            DecodeXobj, ConvertKind.GltfStaticMesh, ".glb");
        Add(".mot", "Skeletal animation clip", "Docs/RE/formats/animation.md", DecodeMot);

        // ── Terrain / world scene ───────────────────────────────────────────────────────────
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

        // ── Spawns ──────────────────────────────────────────────────────────────────────────
        Add(".arr", "Spawn array (npc 28B / mob 20B)", "Docs/RE/formats/npc_spawns.md", DecodeArr);

        // ── Textures ────────────────────────────────────────────────────────────────────────
        Add(".dds", "DirectDraw Surface texture", "Docs/RE/formats/texture.md",
            DecodeDds, ConvertKind.Png, ".png");
        Add(".tga", "Truevision TGA texture", "Docs/RE/formats/texture.md",
            null, ConvertKind.ImagePassthrough, ".tga");
        Add(".bmp", "Windows bitmap", "Docs/RE/formats/texture.md",
            DecodeImage, ConvertKind.ImagePassthrough, ".bmp");
        Add(".png", "PNG texture", "Docs/RE/formats/texture.md",
            DecodeImage, ConvertKind.ImagePassthrough, ".png");

        // ── Audio ───────────────────────────────────────────────────────────────────────────
        Add(".ogg", "OGG Vorbis audio", "Docs/RE/formats/sound_tables.md",
            DecodeAudio, ConvertKind.AudioPassthrough, ".ogg");

        // ── Effects ─────────────────────────────────────────────────────────────────────────
        Add(".xeff", "Particle effect descriptor", "Docs/RE/formats/effects.md",
            DecodeXeff, ConvertKind.XeffJson, ".json");
        // .eff is ambiguous — split into two synthetic keys, resolved by path in Lookup().
        Add(".eff#sound", "Per-area sound table (.eff variant)", "Docs/RE/formats/sound_tables.md",
            DecodeSoundTable);
        Add(".eff#shape", "Effect-object shape geometry", "Docs/RE/formats/effects.md", DecodeEffShape);

        // ── Sound tables ────────────────────────────────────────────────────────────────────
        Add(".bgm", "Background-music sound table", "Docs/RE/formats/sound_tables.md", DecodeSoundTable);
        Add(".bge", "Background-ambient sound table", "Docs/RE/formats/sound_tables.md", DecodeSoundTable);
        Add(".wlk", "Walk-sound table", "Docs/RE/formats/sound_tables.md", DecodeSoundTable);
        Add(".run", "Run-sound table", "Docs/RE/formats/sound_tables.md", DecodeSoundTable);

        // ── Text / data tables (CP949) ──────────────────────────────────────────────────────
        Add(".xdb", "Message catalogue (msg.xdb 516B records)", "Docs/RE/formats/misc_data.md", DecodeXdb);
        Add(".csv", "Items CSV (CP949)", "Docs/RE/formats/config_tables.md", DecodeItemsCsv);
        Add(".do", "Per-class stance icon table (116B)", "Docs/RE/formats/ui_manifests.md", DecodeDo);

        // ── Shaders (D3D9 assembly source: .vsh vertex, .psh pixel) ─────────────────────────
        Add(".vsh", "D3D9 vertex shader source", "Docs/RE/formats/shaders.md", DecodeShader);
        Add(".psh", "D3D9 pixel shader source", "Docs/RE/formats/shaders.md", DecodeShader);

        // ── Per-area environment / sky .bin family (no magic; resolved by name prefix) ───────
        // spec: Docs/RE/formats/environment_bins.md — the data/sky/dat + data/map* env-bin family.
        Add(BinMapOptionKey,  "Per-area master flags (map_option*.bin)", "Docs/RE/formats/environment_bins.md", DecodeBinMapOption);
        Add(BinFogKey,        "Per-area fog parameters (fog*.bin)",        "Docs/RE/formats/environment_bins.md", DecodeBinFog);
        Add(BinMaterialKey,   "Sun/sky material colour table (material*.bin)", "Docs/RE/formats/environment_bins.md", DecodeBinMaterial);
        Add(BinLightKey,      "Sky-lighting keyframes (light*.bin)",       "Docs/RE/formats/environment_bins.md", DecodeBinLight);
        Add(BinStarDomeKey,   "Star colour grid (stardome*.bin)",          "Docs/RE/formats/environment_bins.md", DecodeBinStarDome);
        Add(BinCloudDomeKey,  "Cloud-dome colour grid (clouddome*.bin)",   "Docs/RE/formats/environment_bins.md", DecodeBinCloudDome);
        Add(BinCloudCycleKey, "Cloud animation schedule (cloud_cycle*.bin)", "Docs/RE/formats/environment_bins.md", DecodeBinCloudCycle);
        // point_light*.bin / wind*.bin are formally specified in terrain_layers.md §7–8.
        Add(BinPointLightKey, "Per-area point-light array (point_light*.bin)", "Docs/RE/formats/terrain_layers.md", DecodeBinPointLight);
        Add(BinWindKey,       "Foliage-sway keyframes (wind*.bin)",        "Docs/RE/formats/terrain_layers.md", DecodeBinWind);
        // weather*.bin / weather*_rain.bin — size confirmed, body not decoded (PARTIAL).
        Add(BinWeatherKey,    "Weather parameters (weather*.bin) — PARTIAL", "Docs/RE/formats/environment_bins.md", DecodeBinWeather);
        // regiontable<NNN>.bin — sub-zone label table (misc_data.md §7.2).
        Add(BinRegionTableKey, "Per-area sub-zone label table (regiontable*.bin)", "Docs/RE/formats/misc_data.md", DecodeBinRegionTable);
        // region<NNN>.bin / map<NNN>.bin companions — layout UNVERIFIED (misc_data.md §7.2).
        Add(BinCompanionKey,  "Per-area region/map companion .bin (layout UNVERIFIED)", "Docs/RE/formats/misc_data.md", DecodeBinCompanion);

        // ── Per-area cell manifest (.lst — the area-inventory presence signal) ──────────────
        // spec: Docs/RE/formats/area_inventory.md §1.2 — d<NNN>.lst is the authoritative area census;
        // spec: Docs/RE/formats/terrain.md §1.2 — binary layout: u32 count | count × u32 keys.
        Add(".lst", "Per-area cell manifest (area census; cell keys)", "Docs/RE/formats/area_inventory.md", DecodeLst);

        return t;
    }

    // ── Decoders — each returns SHORT structured lines, no raw payload bytes ─────────────────

    private static IReadOnlyList<string> DecodeSkn(string path, ReadOnlyMemory<byte> raw)
    {
        SkinnedMesh m = SknParser.Parse(raw);
        return
        [
            $"id_a={m.IdA}  id_b(skeleton)={m.IdB}  name=\"{Safe(m.Name)}\"",
            $"vertices={m.Positions.Length:N0}  faces={m.FaceCount:N0}  corners={m.Corners.Length:N0}",
            $"weights={m.Weights.Length:N0}  " +
            (m.IdB == 0 ? "rigid (no skeleton)" : $"binds to data/char/bind/g{m.IdB}.bnd"),
        ];
    }

    private static IReadOnlyList<string> DecodeBnd(string path, ReadOnlyMemory<byte> raw)
    {
        Skeleton s = BndParser.Parse(raw);
        int roots = 0;
        foreach (Bone b in s.Bones) if (b.IsRoot) roots++;
        return
        [
            $"actor_id={s.ActorId}  name=\"{Safe(s.ActorName)}\"",
            $"bones={s.Bones.Length:N0}  roots={roots}",
        ];
    }

    private static IReadOnlyList<string> DecodeXobj(string path, ReadOnlyMemory<byte> raw)
    {
        StaticMesh m = XobjParser.Parse(raw);
        return
        [
            $"vertices={m.Positions.Length:N0}  uvs={m.Uvs.Length:N0}  " +
            $"indices={m.Indices.Length:N0}  triangles={m.Indices.Length / 3:N0}",
        ];
    }

    private static IReadOnlyList<string> DecodeMot(string path, ReadOnlyMemory<byte> raw)
    {
        if (raw.Length >= 4 &&
            BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[..4]) == BaniMagic)
        {
            // spec: Docs/RE/formats/animation.md §BANI variant — dead/unloadable: SAMPLE-VERIFIED.
            return ["variant=BANI (dead/unloadable magic 'BANI'); not parsed"];
        }

        AnimationClip clip = AnimationParser.Parse(raw);
        bool real = clip.FrameCount > 0 && clip.Tracks.Length > 0;
        return
        [
            $"id_a={clip.IdA}  id_b={clip.IdB}  name=\"{Safe(clip.Name)}\"",
            $"frame_count={clip.FrameCount}  tracks={clip.Tracks.Length}  " +
            $"duration={clip.FrameCount * 0.1f:F1}s  {(real ? "REAL clip" : "stub")}",
        ];
    }

    private static IReadOnlyList<string> DecodeTed(string path, ReadOnlyMemory<byte> raw)
    {
        TerrainCell c = TedTerrainParser.Parse(raw);
        return
        [
            // spec: Docs/RE/formats/terrain.md — 65×65 height grid: CONFIRMED.
            $"height_samples={c.Heights.Length:N0}  normals={c.Normals.Length:N0}",
            $"texture_index_grid={c.TextureIndexGrid.Length:N0}  " +
            $"direction_flags={c.DirectionFlags.Length:N0}  diffuse_colours={c.DiffuseColours.Length:N0}",
        ];
    }

    private static IReadOnlyList<string> DecodeBud(string path, ReadOnlyMemory<byte> raw)
    {
        BudScene s = TerrainSceneParser.Parse(raw);
        long verts = 0, idx = 0;
        foreach (BudObject o in s.Objects) { verts += o.Vertices.Length; idx += o.Indices.Length; }
        return
        [
            $"objects={s.Objects.Length:N0}  total_vertices={verts:N0}  total_triangles={idx / 3:N0}",
        ];
    }

    private static IReadOnlyList<string> DecodeMap(string path, ReadOnlyMemory<byte> raw)
    {
        MapDescriptor d = MapDescriptorParser.Parse(raw);
        var lines = new List<string> { $"sections={d.Sections.Length}" };
        foreach (MapSection sec in d.Sections)
            lines.Add($"  section \"{Safe(sec.Keyword)}\"  textures={sec.Textures.Length}");
        return lines;
    }

    private static IReadOnlyList<string> DecodeSod(string path, ReadOnlyMemory<byte> raw)
    {
        SodBlob b = SodBlobParser.Parse(raw);
        return
        [
            // spec: Docs/RE/formats/terrain.md §.sod — solid record count: CONFIRMED.
            $"solid_count={b.SolidCount}  solids={b.Solids.Length:N0}  " +
            $"triangle_groups={b.TriangleCounts.Length:N0}",
        ];
    }

    private static IReadOnlyList<string> DecodeCollision(string path, ReadOnlyMemory<byte> raw)
    {
        CollisionTriangleList tl = TerrainLayerParsers.ParseUpOrExd(raw);
        // spec: Docs/RE/formats/terrain_layers.md §collision triangle list: CONFIRMED.
        return [$"collision triangle layer (.up/.exd)  triangles={tl.Triangles.Length:N0}"];
    }

    private static IReadOnlyList<string> DecodeArr(string path, ReadOnlyMemory<byte> raw)
    {
        // npc{tag}.arr = 28-byte records; mob{tag}.arr = 20-byte records.
        // spec: Docs/RE/formats/npc_spawns.md — npc 28B / mob 20B record strides: CONFIRMED.
        string name = path[(path.LastIndexOf('/') + 1)..];
        if (name.StartsWith("npc", StringComparison.Ordinal))
        {
            NpcSpawnArray arr = NpcSpawnParser.Parse(raw);
            return [$"npc spawn array  records={arr.Records.Length:N0}  (28B stride)"];
        }
        if (name.StartsWith("mob", StringComparison.Ordinal))
        {
            MobSpawnRecord[] recs = MobSpawnParser.Parse(raw);
            return [$"mob spawn array  records={recs.Length:N0}  (20B stride)"];
        }
        return [$"spawn array  {raw.Length:N0} bytes (name does not start with npc/mob — ambiguous stride)"];
    }

    private static IReadOnlyList<string> DecodeDds(string path, ReadOnlyMemory<byte> raw)
    {
        // DDS header: height @ +0x0C, width @ +0x10, fourCC @ +0x54.
        // spec: Docs/RE/formats/texture.md §DDS_HEADER layout — all fields little-endian u32.
        const int MinHeader = 0x58;
        if (raw.Length < MinHeader ||
            BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[..4]) != DdsMagic)
            return ["not a DDS file (magic mismatch or truncated)"];

        uint h = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span.Slice(0x0C, 4));
        uint w = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span.Slice(0x10, 4));
        uint fourccRaw = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span.Slice(0x54, 4));
        string fourcc = fourccRaw == 0
            ? "uncompressed RGBA"
            : Encoding.ASCII.GetString(BitConverter.GetBytes(fourccRaw)).TrimEnd('\0');
        bool npot = !IsPow2(w) || !IsPow2(h);
        return [$"DDS  {w}x{h}  format={fourcc}{(npot ? "  *** non-power-of-2 ***" : "")}"];
    }

    private static IReadOnlyList<string> DecodeImage(string path, ReadOnlyMemory<byte> raw)
    {
        try
        {
            ImagePassthroughResult img = AssetPassthrough.PassthroughImage(raw, path);
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
            AudioPassthroughResult au = AssetPassthrough.PassthroughAudio(raw);
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

        XeffData fx = XeffParser.ParseXeff(raw);
        return [$"effect_id={fx.EffectId}  elements={fx.Elements.Length:N0}"];
    }

    private static IReadOnlyList<string> DecodeEffShape(string path, ReadOnlyMemory<byte> raw)
    {
        // .eff effect-object shape: index_count u32 @0x00, then vert_count u32, 32B verts.
        // spec: Docs/RE/formats/effects.md §B.2 File Layout Overview: VERIFIED.
        if (raw.Length < 4) return ["truncated .eff shape"];
        uint indexCount = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[..4]);
        return [$"effect-object shape  index_count={indexCount:N0}  triangles={indexCount / 3:N0}"];
    }

    private static IReadOnlyList<string> DecodeSoundTable(string path, ReadOnlyMemory<byte> raw)
    {
        if (raw.Length < SoundTableDataSize)
            return [$"sound table too short ({raw.Length:N0} < {SoundTableDataSize} bytes)"];

        // 256 × 48B entries; sound_entry_id u32 @ entry+0x00, 0 = null/disabled.
        // spec: Docs/RE/formats/sound_tables.md §Per-entry layout — sound_entry_id @ +0x00: CONFIRMED.
        int nonNull = 0;
        ReadOnlySpan<byte> span = raw.Span[..SoundTableDataSize];
        for (int i = 0; i < 256; i++)
            if (BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(i * 48, 4)) != 0) nonNull++;
        return [$"sound table  entries=256  non-null={nonNull}  (48B stride)"];
    }

    private static IReadOnlyList<string> DecodeXdb(string path, ReadOnlyMemory<byte> raw)
    {
        MsgXdbCatalog cat = MsgXdbParser.Parse(raw);
        return [$"message catalogue  records={cat.Records.Count:N0}  (516B stride)"];
    }

    private static IReadOnlyList<string> DecodeItemsCsv(string path, ReadOnlyMemory<byte> raw)
    {
        ItemCsvRow[] rows = ItemsCsvParser.Parse(raw);
        return [$"items CSV  rows={rows.Length:N0}  (CP949)"];
    }

    private static IReadOnlyList<string> DecodeDo(string path, ReadOnlyMemory<byte> raw)
    {
        DoStanceTable tbl = DoStanceParser.Parse(raw);
        return
        [
            // spec: Docs/RE/formats/ui_manifests.md §2.7 — stride 0x74 (116B): SAMPLE-VERIFIED.
            $"stance icon table  records={tbl.TotalRecordCount:N0}  trailing_bytes={tbl.TrailingByteCount}",
        ];
    }

    private static IReadOnlyList<string> DecodeShader(string path, ReadOnlyMemory<byte> raw)
    {
        ShaderSource src = ShaderContainerParser.Parse(raw);
        // spec: Docs/RE/formats/shaders.md §Version Declaration Line — vs/ps token: VERIFIED.
        return [$"D3D9 {src.ShaderType} shader  source_chars={src.SourceText.Length:N0}  (ASCII)"];
    }

    // ── Per-area environment / sky .bin family ──────────────────────────────────────────────
    // All emit COUNTS and a few flag/scalar header fields only — never a colour-table dump.

    private static IReadOnlyList<string> DecodeBinMapOption(string path, ReadOnlyMemory<byte> raw)
    {
        MapOptionBin o = EnvironmentBinParsers.ParseMapOption(raw);
        // spec: Docs/RE/formats/environment_bins.md §1.1 — 10 × u32 flag vector: CONFIRMED.
        return
        [
            $"map_option  water_enable={o.WaterEnable}  water_y={o.WaterY}  sky_gate={o.SkyGate}  indoor={o.IndoorFlag}",
            $"stardome={o.StarDomeEnable}  clouddome={o.CloudDomeEnable}  lensflare={o.LensFlareEnable}  " +
            $"sun_moon={o.SunMoonEnable}  skybox={o.SkyboxEnable}",
        ];
    }

    private static IReadOnlyList<string> DecodeBinFog(string path, ReadOnlyMemory<byte> raw)
    {
        FogBin f = EnvironmentBinParsers.ParseFog(raw);
        // spec: Docs/RE/formats/environment_bins.md §2.1 — start/end dist + 48 BGRA keyframes: CONFIRMED.
        return
        [
            $"fog  start_dist={f.StartDist:F3}  end_dist={f.EndDist:F3}  " +
            $"data_load_flag={f.DataLoadFlag}  colour_keyframes={f.FogColors.Length}",
        ];
    }

    private static IReadOnlyList<string> DecodeBinMaterial(string path, ReadOnlyMemory<byte> raw)
    {
        MaterialBin m = EnvironmentBinParsers.ParseMaterial(raw);
        // spec: Docs/RE/formats/environment_bins.md §3.1 — f32[48][51] colour table: CONFIRMED.
        return
        [
            $"material colour table  keyframes={m.ColorTable.Length}  values_per_keyframe={MaterialBin.ValuesPerKeyframe}",
        ];
    }

    private static IReadOnlyList<string> DecodeBinLight(string path, ReadOnlyMemory<byte> raw)
    {
        LightBin l = EnvironmentBinParsers.ParseLight(raw);
        // spec: Docs/RE/formats/environment_bins.md §9 — sample-verified light layout: CONFIRMED.
        return
        [
            $"light  directional_keyframes={l.DirectionalKeyframes.Length}  ambient_keyframes={l.AmbientKeyframes.Length}",
            $"fog_distance_scalars={l.FogDistanceScalars.Length}  secondary_fog_scalars={l.SecondaryFogScalars.Length}",
            $"fallback_light scale={l.FallbackScale:F1} dir=({l.FallbackDirX:F1},{l.FallbackDirY:F1},{l.FallbackDirZ:F1})",
        ];
    }

    private static IReadOnlyList<string> DecodeBinStarDome(string path, ReadOnlyMemory<byte> raw)
    {
        StarDomeBin s = EnvironmentBinParsers.ParseStarDome(raw);
        // spec: Docs/RE/formats/environment_bins.md §4.1 — u8[12][192][4] BGRA grid: CONFIRMED.
        return [$"stardome  keyframes={s.StarColors.Length}  stars_per_keyframe={StarDomeBin.StarsPerKeyframe}  (12-frame cycle)"];
    }

    private static IReadOnlyList<string> DecodeBinCloudDome(string path, ReadOnlyMemory<byte> raw)
    {
        CloudDomeBin c = EnvironmentBinParsers.ParseCloudDome(raw);
        // spec: Docs/RE/formats/environment_bins.md §5.1 — two u8[12][240][4] BGRA layers: CONFIRMED.
        return
        [
            $"clouddome  layers=2  keyframes={c.Layer1Colors.Length}  vertices_per_keyframe={CloudDomeBin.VerticesPerKeyframe}",
        ];
    }

    private static IReadOnlyList<string> DecodeBinCloudCycle(string path, ReadOnlyMemory<byte> raw)
    {
        CloudCycleBin c = EnvironmentBinParsers.ParseCloudCycle(raw);
        // spec: Docs/RE/formats/environment_bins.md §6.1 — 10 day-pattern rows × 7 u8: CONFIRMED.
        return [$"cloud_cycle  day_patterns={c.Rows.Length}  (10 × 7 u8 schedule)"];
    }

    private static IReadOnlyList<string> DecodeBinPointLight(string path, ReadOnlyMemory<byte> raw)
    {
        PointLightBinData p = TerrainLayerParsers.ParsePointLightBin(raw);
        int enabled = 0;
        foreach (PointLightRecord r in p.Records) if (r.EnabledFlag == 0) enabled++;
        // spec: Docs/RE/formats/terrain_layers.md §7 — header + 60-byte records: CONFIRMED (parser).
        return [$"point_light  intensity_scale={p.IntensityScale}  records={p.Records.Length}  enabled={enabled}"];
    }

    private static IReadOnlyList<string> DecodeBinWind(string path, ReadOnlyMemory<byte> raw)
    {
        WindBinData w = TerrainLayerParsers.ParseWindBin(raw);
        // spec: Docs/RE/formats/terrain_layers.md §8 — 8-byte header + 24-byte records: CONFIRMED (header).
        return [$"wind  count={w.Count}  flag2={w.Flag2}  (24-byte keyframe records; fields UNVERIFIED)"];
    }

    private static IReadOnlyList<string> DecodeBinWeather(string path, ReadOnlyMemory<byte> raw)
    {
        // PARTIAL: size confirmed, body not decoded. Report size only + non-zero presence.
        // spec: Docs/RE/formats/environment_bins.md §7 / §8 — exactly 240 bytes; content unknown.
        bool isRain = path.Contains("_rain", StringComparison.Ordinal);
        string variant = isRain ? "weather_rain" : "weather";
        if (raw.Length != WeatherBinSize)
            return [$"{variant}  {raw.Length:N0} bytes (expected {WeatherBinSize}; body not decoded — PARTIAL)"];

        bool allZero = true;
        foreach (byte b in raw.Span) if (b != 0) { allZero = false; break; }
        return [$"{variant}  {WeatherBinSize} bytes  {(allZero ? "all-zero (inactive)" : "non-zero body present")}  (body not decoded — PARTIAL)"];
    }

    private static IReadOnlyList<string> DecodeBinRegionTable(string path, ReadOnlyMemory<byte> raw)
    {
        RegionTableRecord[] recs = RegionTableParser.Parse(raw);
        // spec: Docs/RE/formats/misc_data.md §7.2 — 32-byte sub-zone label records: SAMPLE-VERIFIED.
        return [$"regiontable  sub_zone_records={recs.Length}  (32B stride)"];
    }

    private static IReadOnlyList<string> DecodeBinCompanion(string path, ReadOnlyMemory<byte> raw)
    {
        // region<NNN>.bin (size varies) and map<NNN>.bin (fixed 520B) — layouts UNVERIFIED.
        // spec: Docs/RE/formats/misc_data.md §7.2 — companion files, layout open: UNVERIFIED.
        string file = path[(path.LastIndexOf('/') + 1)..];
        string kind = file.StartsWith("map", StringComparison.Ordinal) ? "map<NNN>.bin (fixed 520B)" : "region<NNN>.bin";
        return [$"{kind}  {raw.Length:N0} bytes  (companion .bin; layout UNVERIFIED — misc_data.md §7.2)"];
    }

    // ── Per-area cell manifest (.lst) — the area-inventory presence signal ──────────────────

    private static IReadOnlyList<string> DecodeLst(string path, ReadOnlyMemory<byte> raw)
    {
        // Only the per-area cell manifest form (data/map<NNN>/dat/d<NNN>.lst) is the area-inventory
        // presence signal. Other .lst files (e.g. data/effect/*.lst index/bmp lists) share the
        // extension but have a different layout — don't claim them as area manifests.
        // spec: Docs/RE/formats/area_inventory.md §1 — d<NNN>.lst is the authoritative area census.
        string file = path[(path.LastIndexOf('/') + 1)..];
        bool isAreaManifest = path.Contains("/dat/", StringComparison.Ordinal) &&
                              file.StartsWith("d", StringComparison.Ordinal);

        if (raw.Length < 4)
            return [".lst too short for a u32 count prefix"];

        if (!isAreaManifest)
        {
            // Generic count-prefixed list: report the leading u32 only (no cell-key interpretation).
            uint hdrCount = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[..4]);
            return
            [
                $"generic .lst (not a d<NNN>.lst area manifest)  leading_u32={hdrCount:N0}  size={raw.Length:N0} bytes",
                "  (effect/index list — different layout; not the area-inventory cell manifest)",
            ];
        }

        LstManifest m = LstManifestParser.Parse(raw);
        // Census-style summary: cell count, the (file_size-4)/4 cross-check, and duplicate keys.
        // spec: Docs/RE/formats/area_inventory.md §1.2 — cell_count = (file_size - 4) / 4: CONFIRMED.
        // spec: Docs/RE/formats/area_inventory.md §4.1 — area 0 has a duplicate cell key: SAMPLE-VERIFIED.
        long byFormula = (raw.Length - 4) / 4;
        var distinct = new HashSet<uint>();
        foreach (LstCellEntry e in m.Entries) distinct.Add(e.Key);
        int dupes = m.Entries.Length - distinct.Count;
        return
        [
            $"area manifest  cell_entries={m.Entries.Length}  distinct_keys={distinct.Count}" +
            (dupes > 0 ? $"  duplicate_keys={dupes}" : ""),
            $"size cross-check (file_size-4)/4 = {byFormula}  " +
            (byFormula == m.Entries.Length ? "(matches count field)" : "(MISMATCH vs count field)"),
        ];
    }

    // ── small helpers ───────────────────────────────────────────────────────────────────────

    private static bool IsPow2(uint v) => v > 0 && (v & (v - 1)) == 0;

    // Render a control-stripped, length-capped echo of a decoded NAME field (not a payload dump).
    private static string Safe(string s)
    {
        const int cap = 48;
        var sb = new StringBuilder(Math.Min(s.Length, cap));
        foreach (char c in s)
        {
            if (sb.Length >= cap) { sb.Append('…'); break; }
            sb.Append(char.IsControl(c) ? '.' : c);
        }
        return sb.ToString();
    }
}

/// <summary>
/// Resolves a companion <c>.bnd</c> skeleton for a skinned mesh, by its <c>id_b</c>.
/// Returns null when the skeleton is not present in the VFS.
/// </summary>
internal delegate Skeleton? VfsBndResolver(uint idB);
