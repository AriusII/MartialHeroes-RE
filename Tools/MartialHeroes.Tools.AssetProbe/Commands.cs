// Commands — the five asset-dump subcommands of assetprobe.
//
// Each subcommand:
//   1. Resolves the VFS path argument and loads raw bytes via MappedVfsArchive.
//   2. Parses with the production layer-03 parser (no reinventing, no duplication).
//   3. Writes a JSON or CSV dump to an EXTERNAL, guarded output path.
//   4. Prints a one-line summary to stdout for quick visual inspection.
//
// Output guard (GuardOutputPath) refuses to write inside the repo tree so extracted
// client data can never accidentally land in a git-tracked file.
//
// Every wire-significant constant (field offset, stride, block size) cites its spec.

using System.Globalization;
using System.Text;
using System.Text.Json;
using MartialHeroes.Assets.Parsers.Effects;
using MartialHeroes.Assets.Parsers.Mesh;
using MartialHeroes.Assets.Parsers.Terrain;
using MartialHeroes.Assets.Vfs;

namespace MartialHeroes.Tools.AssetProbe;

internal static class Commands
{
    // ════════════════════════════════════════════════════════════════════════════
    // mot-frames <vfs-path> [--track N] --out <file>
    // Per-track keyframe rows: pos.x/y/z, rot.x/y/z/w at the 28-byte stride.
    // spec: Docs/RE/formats/animation.md §Keyframe record — 28 bytes, little-endian.
    // ════════════════════════════════════════════════════════════════════════════
    public static int MotFrames(MappedVfsArchive archive, string[] args)
    {
        var vfsPath = FirstPositional(args);
        if (vfsPath is null)
        {
            Console.Error.WriteLine(
                "mot-frames: needs <vfs-path>. e.g. mot-frames data/char/1.mot --out D:/dump/1.csv");
            return 2;
        }

        vfsPath = NormPath(vfsPath);
        var outFile = GetFlag(args, "--out");
        int? trackFilter = null;
        var trackStr = GetFlag(args, "--track");
        if (trackStr is not null && int.TryParse(trackStr, out var tf))
            trackFilter = tf;

        if (!Require(archive, vfsPath)) return 1;
        if (!RequireOut(outFile, out var guardedOut)) return 3;

        var raw = archive.GetFileContent(vfsPath);

        // AnimationParser.Parse returns null for the BANI variant.
        // spec: Docs/RE/formats/animation.md §BANI variant — parser returns null: CONFIRMED.
        var clip = AnimationParser.Parse(raw);
        if (clip is null)
        {
            Console.Error.WriteLine(
                $"mot-frames: '{vfsPath}' is the BANI variant — not parseable by the shipping client loader.");
            return 1;
        }

        Console.WriteLine($"mot-frames: id_a={clip.IdA} id_b={clip.IdB} name=\"{clip.Name}\"");
        Console.WriteLine($"  frame_count={clip.FrameCount} track_count={clip.Tracks.Length}");
        // Fixed frame rate: 10 fps. spec: Docs/RE/formats/animation.md §Timing: CONFIRMED.
        Console.WriteLine($"  duration={clip.FrameCount * 0.1f:F2}s  (10 fps fixed)");

        var dir = Path.GetDirectoryName(Path.GetFullPath(guardedOut));
        if (dir is not null) Directory.CreateDirectory(dir);

        using var sw = new StreamWriter(guardedOut, false, Encoding.UTF8);

        // CSV header
        // spec: Docs/RE/formats/animation.md §Keyframe record — translation XYZ + rotation XYZW: CONFIRMED.
        sw.WriteLine("track_idx,bone_id,key_idx,pos_x,pos_y,pos_z,rot_x,rot_y,rot_z,rot_w");

        var totalRows = 0;
        for (var t = 0; t < clip.Tracks.Length; t++)
        {
            if (trackFilter.HasValue && t != trackFilter.Value) continue;

            var track = clip.Tracks[t];
            for (var k = 0; k < track.Keyframes.Length; k++)
            {
                var kf = track.Keyframes[k];
                // Keyframe record: translation XYZ @ sub-offset 0/4/8, rotation XYZW @ 12/16/20/24.
                // spec: Docs/RE/formats/animation.md §Keyframe record — 28 bytes: CONFIRMED.
                sw.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{t},{track.BoneId},{k},{kf.Translation.X},{kf.Translation.Y},{kf.Translation.Z}," +
                    $"{kf.Rotation.X},{kf.Rotation.Y},{kf.Rotation.Z},{kf.Rotation.W}"));
                totalRows++;
            }
        }

        Console.WriteLine($"  wrote {totalRows:N0} keyframe rows → {Path.GetFullPath(guardedOut)}");
        WarnNeverCommit();
        return 0;
    }

    // ════════════════════════════════════════════════════════════════════════════
    // bnd-matrices <vfs-path> --out <file>
    // Per-bone: name (as self_id), parent index, bind-local transform.
    // spec: Docs/RE/formats/mesh.md §Format: .bnd
    // spec: Docs/RE/specs/skinning.md §Bind-pose matrix derivation
    // ════════════════════════════════════════════════════════════════════════════
    public static int BndMatrices(MappedVfsArchive archive, string[] args)
    {
        var vfsPath = FirstPositional(args);
        if (vfsPath is null)
        {
            Console.Error.WriteLine(
                "bnd-matrices: needs <vfs-path>. e.g. bnd-matrices data/char/bind/g1.bnd --out D:/dump/g1.json");
            return 2;
        }

        vfsPath = NormPath(vfsPath);
        var outFile = GetFlag(args, "--out");

        if (!Require(archive, vfsPath)) return 1;
        if (!RequireOut(outFile, out var guardedOut)) return 3;

        var raw = archive.GetFileContent(vfsPath);
        var skel = BndParser.Parse(raw);

        Console.WriteLine($"bnd-matrices: actor_id={skel.ActorId} name=\"{skel.ActorName}\"");
        Console.WriteLine($"  bone_count={skel.Bones.Length}");

        // Build JSON output.
        // Per-bone layout: self_id, parent_id, local translation XYZ, local rotation XYZW.
        // spec: Docs/RE/formats/mesh.md §BndBone on-disk record (36 bytes): CONFIRMED.
        // spec: Docs/RE/formats/mesh.md §Bone array — self_id @ +0, parent_id @ +4, local_translation @ +8, local_rotation @ +20: CONFIRMED.
        var bones = new object[skel.Bones.Length];
        for (var i = 0; i < skel.Bones.Length; i++)
        {
            var bone = skel.Bones[i];
            // parent index: find the bone whose self_id (low byte) matches this bone's parent_id (low byte).
            // spec: Docs/RE/formats/mesh.md §Bone addressing — low byte used for matching: CONFIRMED.
            var parentIdx = -1;
            var parentLowByte = (byte)(bone.ParentId & 0xFF);
            var selfLowByte = (byte)(bone.SelfId & 0xFF);
            if (!bone.IsRoot)
                for (var j = 0; j < skel.Bones.Length; j++)
                    if (j != i && (byte)(skel.Bones[j].SelfId & 0xFF) == parentLowByte)
                    {
                        parentIdx = j;
                        break;
                    }

            bones[i] = new
            {
                bone_idx = i,
                self_id = bone.SelfId,
                self_id_low = selfLowByte,
                parent_id = bone.ParentId,
                parent_id_low = parentLowByte,
                parent_idx = parentIdx,
                is_root = bone.IsRoot,
                // Local bind-pose translation (XYZ).
                // spec: Docs/RE/formats/mesh.md §BndBone — local_translation @ +8: CONFIRMED.
                local_translation = new { x = bone.Translation.X, y = bone.Translation.Y, z = bone.Translation.Z },
                // Local bind-pose rotation quaternion (XYZW, scalar W last).
                // spec: Docs/RE/formats/mesh.md §BndBone — local_rotation @ +20 (XYZW): CONFIRMED.
                local_rotation = new
                    { x = bone.Rotation.X, y = bone.Rotation.Y, z = bone.Rotation.Z, w = bone.Rotation.W }
            };
        }

        var doc = new
        {
            source = vfsPath,
            actor_id = skel.ActorId,
            actor_name = skel.ActorName,
            bone_count = skel.Bones.Length,
            // spec: Docs/RE/formats/mesh.md §Format: .bnd — binary bind-pose skeleton
            spec = "Docs/RE/formats/mesh.md",
            bones
        };

        var dir = Path.GetDirectoryName(Path.GetFullPath(guardedOut));
        if (dir is not null) Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(guardedOut, json, Encoding.UTF8);

        Console.WriteLine($"  wrote {skel.Bones.Length} bones → {Path.GetFullPath(guardedOut)}");
        WarnNeverCommit();
        return 0;
    }

    // ════════════════════════════════════════════════════════════════════════════
    // skn-weights <vfs-path> --out <file>
    // Header id_a/id_b; per-vertex influence triples (vertexIdx, boneId, weight).
    // spec: Docs/RE/formats/mesh.md §Format: .skn
    // spec: Docs/RE/formats/skn.md
    // ════════════════════════════════════════════════════════════════════════════
    public static int SknWeights(MappedVfsArchive archive, string[] args)
    {
        var vfsPath = FirstPositional(args);
        if (vfsPath is null)
        {
            Console.Error.WriteLine(
                "skn-weights: needs <vfs-path>. e.g. skn-weights data/char/0.skn --out D:/dump/0.csv");
            return 2;
        }

        vfsPath = NormPath(vfsPath);
        var outFile = GetFlag(args, "--out");

        if (!Require(archive, vfsPath)) return 1;
        if (!RequireOut(outFile, out var guardedOut)) return 3;

        var raw = archive.GetFileContent(vfsPath);
        var mesh = SknParser.Parse(raw);

        Console.WriteLine($"skn-weights: id_a={mesh.IdA} id_b={mesh.IdB} name=\"{mesh.Name}\"");
        Console.WriteLine(
            $"  vertex_count={mesh.Positions.Length} face_count={mesh.FaceCount} weight_count={mesh.Weights.Length}");

        var dir = Path.GetDirectoryName(Path.GetFullPath(guardedOut));
        if (dir is not null) Directory.CreateDirectory(dir);

        using var sw = new StreamWriter(guardedOut, false, Encoding.UTF8);

        // CSV header: per-weight influence record.
        // spec: Docs/RE/formats/mesh.md §Weight record — vertexIndex, boneIndex, weight (12 bytes): CONFIRMED.
        sw.WriteLine(
            $"# source={vfsPath} id_a={mesh.IdA} id_b={mesh.IdB} vertex_count={mesh.Positions.Length} weight_count={mesh.Weights.Length}");
        sw.WriteLine("vertex_idx,bone_idx,weight");

        foreach (var w in mesh.Weights)
            // Weight record: vertex_index u32, bone_index u32, weight f32.
            // spec: Docs/RE/formats/mesh.md §Weight record — 12 bytes: CONFIRMED.
            sw.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{w.VertexIndex},{w.BoneIndex},{w.Weight}"));

        Console.WriteLine($"  wrote {mesh.Weights.Length:N0} weight rows → {Path.GetFullPath(guardedOut)}");
        WarnNeverCommit();
        return 0;
    }

    // ════════════════════════════════════════════════════════════════════════════
    // ted-blocks <vfs-path> --out <file>
    // The five .ted blocks: each block's offset/size + key fields, the
    // TextureIndexGrid bytes (256 u8), and the DirectionFlags (256 u8).
    // spec: Docs/RE/formats/terrain.md §5. Terrain geometry blob — .ted
    // ════════════════════════════════════════════════════════════════════════════
    public static int TedBlocks(MappedVfsArchive archive, string[] args)
    {
        var vfsPath = FirstPositional(args);
        if (vfsPath is null)
        {
            Console.Error.WriteLine(
                "ted-blocks: needs <vfs-path>. e.g. ted-blocks data/map000/110001.ted --out D:/dump/110001.json");
            return 2;
        }

        vfsPath = NormPath(vfsPath);
        var outFile = GetFlag(args, "--out");

        if (!Require(archive, vfsPath)) return 1;
        if (!RequireOut(outFile, out var guardedOut)) return 3;

        var raw = archive.GetFileContent(vfsPath);
        var cell = TedTerrainParser.Parse(raw);

        Console.WriteLine($"ted-blocks: {vfsPath} ({raw.Length:N0} bytes)");

        // Block layout constants (all CONFIRMED from spec).
        // spec: Docs/RE/formats/terrain.md §5.2 — five sequential fixed-size blocks: CONFIRMED.
        const int HeightmapOffset = 0;
        const int HeightmapSize = 4225 * 4; // spec: §5.2 Block 1 — 4225 f32le: CONFIRMED.
        const int NormalsOffset = HeightmapOffset + HeightmapSize; // 16900
        const int NormalsSize = 4225 * 3; // spec: §5.2 Block 2 — 4225 RGB u8×3: CONFIRMED.
        const int LookupOffset = NormalsOffset + NormalsSize; // 29575
        const int LookupSize = 256; // spec: §5.2 Block 3 — 256 u8: CONFIRMED.
        const int DirectionOffset = LookupOffset + LookupSize; // 29831
        const int DirectionSize = 256; // spec: §5.2 Block 4 — 256 u8: CONFIRMED.
        const int DiffuseOffset = DirectionOffset + DirectionSize; // 30087
        const int DiffuseSize = 4225 * 4; // spec: §5.2 Block 5 — 4225 RGBA u8×4: CONFIRMED.
        const int TotalSize = DiffuseOffset + DiffuseSize; // 46987

        Console.WriteLine($"  file_size={raw.Length} (expected {TotalSize})");
        Console.WriteLine($"  height_count={cell.Heights.Length} normal_count={cell.Normals.Length}");
        Console.WriteLine(
            $"  texture_index_grid_len={cell.TextureIndexGrid.Length} direction_flags_len={cell.DirectionFlags.Length}");
        Console.WriteLine($"  diffuse_colour_count={cell.DiffuseColours.Length}");

        // Distinct texture index values (1-based raw bytes).
        // spec: Docs/RE/formats/terrain.md §5.6 Block 3 — "u8, 1-based, max observed=11": CONFIRMED.
        var distinctTex = new SortedSet<byte>(cell.TextureIndexGrid);
        Console.WriteLine($"  distinct_texture_indices: [{string.Join(",", distinctTex)}]");

        // Distinct direction flag values (0-3).
        // spec: Docs/RE/formats/terrain.md §5.7 Block 4 — "u8, observed 0-3": CONFIRMED.
        var distinctDir = new SortedSet<byte>(cell.DirectionFlags);
        Console.WriteLine($"  distinct_direction_flags: [{string.Join(",", distinctDir)}]");

        // Build JSON doc.
        var doc = new
        {
            source = vfsPath,
            file_size = raw.Length,
            // spec: Docs/RE/formats/terrain.md §5.1 — "Total file size: 46 987 bytes": CONFIRMED.
            spec = "Docs/RE/formats/terrain.md",
            blocks = new[]
            {
                new
                {
                    name = "Block1_Heightmap", offset = HeightmapOffset, size = HeightmapSize,
                    vertex_count = cell.Heights.Length,
                    height_min = cell.Heights.Min(), height_max = cell.Heights.Max()
                },
                new
                {
                    name = "Block2_Normals", offset = NormalsOffset, size = NormalsSize,
                    vertex_count = cell.Normals.Length,
                    height_min = 0f, height_max = 0f
                },
                new
                {
                    name = "Block3_TextureIndexGrid", offset = LookupOffset, size = LookupSize,
                    vertex_count = 256,
                    height_min = 0f, height_max = 0f
                },
                new
                {
                    name = "Block4_DirectionFlags", offset = DirectionOffset, size = DirectionSize,
                    vertex_count = 256,
                    height_min = 0f, height_max = 0f
                },
                new
                {
                    name = "Block5_DiffuseRGBA", offset = DiffuseOffset, size = DiffuseSize,
                    vertex_count = cell.DiffuseColours.Length,
                    height_min = 0f, height_max = 0f
                }
            },
            // TextureIndexGrid: 256 raw u8 bytes.
            // spec: Docs/RE/formats/terrain.md §5.6 Block 3 — stored RAW, no idx-1 decrement in parser: CONFIRMED.
            texture_index_grid = cell.TextureIndexGrid,
            // DirectionFlags: 256 raw u8 bytes.
            // spec: Docs/RE/formats/terrain.md §5.7 Block 4 — values 0-3: CONFIRMED.
            direction_flags = cell.DirectionFlags,
            height_min = cell.Heights.Min(),
            height_max = cell.Heights.Max(),
            distinct_texture_indices = distinctTex.ToArray(),
            distinct_direction_flags = distinctDir.ToArray()
        };

        var dir = Path.GetDirectoryName(Path.GetFullPath(guardedOut));
        if (dir is not null) Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(guardedOut, json, Encoding.UTF8);

        Console.WriteLine($"  wrote → {Path.GetFullPath(guardedOut)}");
        WarnNeverCommit();
        return 0;
    }

    // ════════════════════════════════════════════════════════════════════════════
    // xeff-emitters <vfs-path> --out <file>
    // Per-emitter raw fields including resource_id (GPU flag when ≥ 10000) and the
    // 52-byte particle sub-records (if the file is particleEmitter.eff).
    // spec: Docs/RE/formats/effects.md §Section A (.xeff) and §Section E (particleEmitter.eff)
    // ════════════════════════════════════════════════════════════════════════════
    public static int XeffEmitters(MappedVfsArchive archive, string[] args)
    {
        var vfsPath = FirstPositional(args);
        if (vfsPath is null)
        {
            Console.Error.WriteLine(
                "xeff-emitters: needs <vfs-path>. e.g. xeff-emitters data/effect/particle/particleemitter.eff --out D:/dump/pe.json");
            return 2;
        }

        vfsPath = NormPath(vfsPath);
        var outFile = GetFlag(args, "--out");

        if (!Require(archive, vfsPath)) return 1;
        if (!RequireOut(outFile, out var guardedOut)) return 3;

        var raw = archive.GetFileContent(vfsPath);
        var ext = Path.GetExtension(vfsPath).ToLowerInvariant();
        var stem = Path.GetFileNameWithoutExtension(vfsPath).ToLowerInvariant();

        // Dispatch: particleEmitter.eff → ParticleEmitterParser; *.xeff → XeffParser.
        // spec: Docs/RE/formats/effects.md §Disambiguation — particleEmitter.eff vs. .xeff: CONFIRMED.
        // spec: Docs/RE/formats/effects.md §E.1 — path data/effect/particle/particleEmitter.eff: CONFIRMED.
        var isParticleEmitter = stem == "particleemitter" && ext == ".eff";

        var dir = Path.GetDirectoryName(Path.GetFullPath(guardedOut));
        if (dir is not null) Directory.CreateDirectory(dir);

        if (isParticleEmitter)
        {
            // particleEmitter.eff: GPU particle emitter descriptor table.
            // Entry = 28-byte header + num_frames × 52-byte sub-records + 64-byte texture name.
            // spec: Docs/RE/formats/effects.md §E.2 File layout: CONFIRMED.
            var table = ParticleEmitterParser.Parse(raw);

            Console.WriteLine($"xeff-emitters (particleEmitter.eff): {table.Entries.Length} entries");

            var entries = table.Entries.Select(e => new
            {
                entry_id = e.EntryId,
                // resource_id >= 10000 flags GPU particle dispatch in .xeff elements.
                // spec: Docs/RE/formats/effects.md §A.14 XEFF_RESOURCE_PARTICLE_THRESHOLD = 10000: CONFIRMED.
                is_gpu_particle = e.EntryId >= 10000,
                num_frames = e.NumFrames,
                // spec: Docs/RE/formats/effects.md §E.2.1 — sprite_size_x/y f32 @ 0x08/0x0C: HIGH.
                sprite_size_x = e.SpriteSizeX,
                sprite_size_y = e.SpriteSizeY,
                // spec: Docs/RE/formats/effects.md §E.2.1 — max_particles u32 @ 0x10: HIGH.
                max_particles = e.MaxParticles,
                texture_name = e.TextureName,
                // 52-byte sub-records fully decoded (19 typed fields) — per-particle spawn+Euler descriptor.
                // spec: Docs/RE/formats/effects.md §E.2.2 — sub-record stride 52 bytes, fields CODE-CONFIRMED.
                sub_records = e.SubRecords.Select((sr, idx) => new
                {
                    idx,
                    // +0x00..+0x06 four u16 timer/size fields. spec: Docs/RE/formats/effects.md §E.2.2.
                    life_bonus = sr.LifeBonus,
                    lifetime = sr.Lifetime,
                    spawn_delay = sr.SpawnDelay,
                    size_init = sr.SizeInit,
                    // +0x08..+0x0B RGBA8 (genuine initial alpha, NOT a sentinel). spec: §E.2.2.
                    color_r = sr.ColorR,
                    color_g = sr.ColorG,
                    color_b = sr.ColorB,
                    color_a = sr.ColorA,
                    // +0x0C..+0x18 spawn position xyz + size_rate (f32). spec: §E.2.2.
                    spawn_pos_x = sr.SpawnPosX,
                    spawn_pos_y = sr.SpawnPosY,
                    spawn_pos_z = sr.SpawnPosZ,
                    size_rate = sr.SizeRate,
                    // +0x1C..+0x22 four SIGNED i16 colour-rate fields. spec: §E.2.2.
                    color_r_rate = sr.ColorRRate,
                    color_g_rate = sr.ColorGRate,
                    color_b_rate = sr.ColorBRate,
                    color_a_rate = sr.ColorARate,
                    // +0x24..+0x30 velocity xyz + damping (f32). spec: §E.2.2.
                    velocity_x = sr.VelocityX,
                    velocity_y = sr.VelocityY,
                    velocity_z = sr.VelocityZ,
                    velocity_damp = sr.VelocityDamp
                }).ToArray()
            }).ToArray();

            var doc = new
            {
                source = vfsPath,
                kind = "particleEmitter.eff",
                entry_count = table.Entries.Length,
                spec = "Docs/RE/formats/effects.md §Section E",
                entries
            };

            var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(guardedOut, json, Encoding.UTF8);
            Console.WriteLine($"  wrote {table.Entries.Length} entries → {Path.GetFullPath(guardedOut)}");
        }
        else if (ext == ".xeff")
        {
            // .xeff: particle effect descriptor.
            // Header: 8 bytes (effect_id u32 + sub_effect_count u32).
            // spec: Docs/RE/formats/effects.md §A.2 File Header (8 bytes): VERIFIED.
            var xeff = XeffParser.ParseXeff(raw);

            Console.WriteLine(
                $"xeff-emitters (.xeff): effect_id={xeff.EffectId} sub_effect_count={xeff.SubEffectCount}");

            var subEffects = xeff.SubEffects.Select((se, idx) => new
            {
                block_idx = idx,
                // emitter_type: 0=billboard, 1=mesh-particle, 2=directional-billboard.
                // spec: Docs/RE/formats/effects.md §A.4.0 — emitter_type u32 @ element+0x00: CONFIRMED.
                emitter_type = se.EmitterType,
                // resource_id: <10000=shared mesh; >=10000=GPU particle id.
                // spec: Docs/RE/formats/effects.md §A.4.0 — resource_id u32 @ element+0x04: CONFIRMED.
                // spec: Docs/RE/formats/effects.md §A.14 XEFF_RESOURCE_PARTICLE_THRESHOLD = 10000: CONFIRMED.
                resource_id = se.ResourceId,
                is_gpu_particle = se.ResourceId >= 10000,
                anim_flag = se.AnimFlag,
                field_unknown_a = se.FieldUnknownA,
                element_dword2 = se.ElementDword2,
                entry_count = se.EntryCount,
                texture_names = se.TextureNames,
                // Track header.
                // spec: Docs/RE/formats/effects.md §A.4.3 — anim_loop u8 @ +0, anim_stride u32 @ +1, anim_base_time u32 @ +5: CONFIRMED.
                anim_loop = se.AnimLoop,
                anim_stride_ms = se.AnimStride,
                anim_base_time_ms = se.AnimBaseTime,
                // Curve passes (alpha + diffuse R/G/B).
                // spec: Docs/RE/formats/effects.md §A.4.2 — four curve passes: CONFIRMED.
                alpha_key_count = se.AlphaKeys.Length,
                alpha_keys = se.AlphaKeys,
                diffuse_r_keys = se.DiffuseR,
                diffuse_g_keys = se.DiffuseG,
                diffuse_b_keys = se.DiffuseB,
                // Keyframes.
                // spec: Docs/RE/formats/effects.md §A.4.4 — 40-byte animated frame / 24-36 byte static: CONFIRMED.
                keyframe_count = se.Keyframes.Length,
                keyframes = se.Keyframes.Select(kf => new
                {
                    kf_index = kf.KfIndex,
                    vel_x = kf.VelocityX,
                    vel_y = kf.VelocityY,
                    vel_z = kf.VelocityZ,
                    size_x = kf.SizeX,
                    size_y = kf.SizeY,
                    size_z = kf.SizeZ,
                    rot_x_deg = kf.RotXDeg,
                    rot_y_deg = kf.RotYDeg,
                    rot_z_deg = kf.RotZDeg
                }).ToArray()
            }).ToArray();

            var doc = new
            {
                source = vfsPath,
                kind = ".xeff",
                effect_id = xeff.EffectId,
                sub_effect_count = xeff.SubEffectCount,
                spec = "Docs/RE/formats/effects.md §Section A",
                sub_effects = subEffects
            };

            var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(guardedOut, json, Encoding.UTF8);
            Console.WriteLine($"  wrote {xeff.SubEffectCount} sub-effects → {Path.GetFullPath(guardedOut)}");
        }
        else
        {
            Console.Error.WriteLine(
                $"xeff-emitters: unsupported extension '{ext}'. Expected .xeff or particleemitter.eff.");
            return 2;
        }

        WarnNeverCommit();
        return 0;
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Shared helpers
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Refuses to write into the repo tree. Only explicit external paths are accepted.
    ///     Mirrors the GuardOutputPath pattern from VfsExplorer/Commands.cs.
    /// </summary>
    private static bool GuardOutputPath(string outPath, out string reason)
    {
        reason = "";
        string full;
        try
        {
            full = Path.GetFullPath(outPath);
        }
        catch (Exception ex)
        {
            reason = $"invalid path ({ex.Message})";
            return false;
        }

        var repoRoot = RepoRootFinder.Find();
        if (repoRoot is not null)
        {
            var rootFull = Path.GetFullPath(repoRoot);
            var candidate = Path.GetDirectoryName(full) ?? full;
            if (IsUnder(candidate, rootFull))
            {
                reason = $"path is inside the repository tree ({rootFull}). Dumps must stay OUTSIDE the repo.";
                return false;
            }
        }

        if (full.Replace('\\', '/').Contains("/.git/", StringComparison.OrdinalIgnoreCase))
        {
            reason = "path is inside a .git directory.";
            return false;
        }

        return true;
    }

    private static bool IsUnder(string candidate, string root)
    {
        var c = NormalizeDir(candidate);
        var r = NormalizeDir(root);
        return c.Equals(r, StringComparison.OrdinalIgnoreCase) ||
               c.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               c.StartsWith(r + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDir(string p)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));
    }

    private static bool Require(MappedVfsArchive archive, string vfsPath)
    {
        if (archive.Contains(vfsPath)) return true;
        Console.Error.WriteLine($"assetprobe: no entry '{vfsPath}' in the VFS.");
        return false;
    }

    private static bool RequireOut(string? outFile, out string guardedOut)
    {
        guardedOut = "";
        if (outFile is null)
        {
            Console.Error.WriteLine("assetprobe: --out <file> is required.");
            return false;
        }

        if (!GuardOutputPath(outFile, out var reason))
        {
            Console.Error.WriteLine($"assetprobe: refusing to write '{outFile}': {reason}");
            return false;
        }

        guardedOut = outFile;
        return true;
    }

    private static void WarnNeverCommit()
    {
        Console.WriteLine("  WARNING: output is extracted client data. Never commit it to the repo.");
    }

    private static string NormPath(string p)
    {
        return p.Replace('\\', '/').ToLowerInvariant();
    }

    private static string? FirstPositional(string[] args)
    {
        foreach (var a in args)
            if (!a.StartsWith("--", StringComparison.Ordinal))
                return a;
        return null;
    }

    private static string? GetFlag(string[] args, string flag)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == flag)
                return args[i + 1];
        return null;
    }
}