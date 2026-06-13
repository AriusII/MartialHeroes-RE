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
