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

        var clip = AnimationParser.Parse(raw);
        if (clip is null)
        {
            Console.Error.WriteLine(
                $"mot-frames: '{vfsPath}' is the BANI variant — not parseable by the shipping client loader.");
            return 1;
        }

        Console.WriteLine($"mot-frames: id_a={clip.IdA} id_b={clip.IdB} name=\"{clip.Name}\"");
        Console.WriteLine($"  frame_count={clip.FrameCount} track_count={clip.Tracks.Length}");
        Console.WriteLine($"  duration={clip.FrameCount * 0.1f:F2}s  (10 fps fixed)");

        var dir = Path.GetDirectoryName(Path.GetFullPath(guardedOut));
        if (dir is not null) Directory.CreateDirectory(dir);

        using var sw = new StreamWriter(guardedOut, false, Encoding.UTF8);

        sw.WriteLine("track_idx,bone_id,key_idx,pos_x,pos_y,pos_z,rot_x,rot_y,rot_z,rot_w");

        var totalRows = 0;
        for (var t = 0; t < clip.Tracks.Length; t++)
        {
            if (trackFilter.HasValue && t != trackFilter.Value) continue;

            var track = clip.Tracks[t];
            for (var k = 0; k < track.Keyframes.Length; k++)
            {
                var kf = track.Keyframes[k];
                sw.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{t},{track.BoneId},{k},{kf.Translation.X},{kf.Translation.Y},{kf.Translation.Z}," +
                    $"{kf.Rotation.X},{kf.Rotation.Y},{kf.Rotation.Z},{kf.Rotation.W}"));
                totalRows++;
            }
        }

        Console.WriteLine($"  wrote {totalRows:N0} keyframe rows -> {Path.GetFullPath(guardedOut)}");
        WarnNeverCommit();
        return 0;
    }

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

        var bones = new object[skel.Bones.Length];
        for (var i = 0; i < skel.Bones.Length; i++)
        {
            var bone = skel.Bones[i];
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
                local_translation = new { x = bone.Translation.X, y = bone.Translation.Y, z = bone.Translation.Z },
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
            spec = "Docs/RE/formats/mesh.md",
            bones
        };

        var dir = Path.GetDirectoryName(Path.GetFullPath(guardedOut));
        if (dir is not null) Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(guardedOut, json, Encoding.UTF8);

        Console.WriteLine($"  wrote {skel.Bones.Length} bones -> {Path.GetFullPath(guardedOut)}");
        WarnNeverCommit();
        return 0;
    }

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

        sw.WriteLine(
            $"# source={vfsPath} id_a={mesh.IdA} id_b={mesh.IdB} vertex_count={mesh.Positions.Length} weight_count={mesh.Weights.Length}");
        sw.WriteLine("vertex_idx,bone_idx,weight");

        foreach (var w in mesh.Weights)
            sw.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{w.VertexIndex},{w.BoneIndex},{w.Weight}"));

        Console.WriteLine($"  wrote {mesh.Weights.Length:N0} weight rows -> {Path.GetFullPath(guardedOut)}");
        WarnNeverCommit();
        return 0;
    }

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

        const int HeightmapOffset = 0;
        const int HeightmapSize = 4225 * 4;
        const int NormalsOffset = HeightmapOffset + HeightmapSize;
        const int NormalsSize = 4225 * 3;
        const int LookupOffset = NormalsOffset + NormalsSize;
        const int LookupSize = 256;
        const int DirectionOffset = LookupOffset + LookupSize;
        const int DirectionSize = 256;
        const int DiffuseOffset = DirectionOffset + DirectionSize;
        const int DiffuseSize = 4225 * 4;
        const int TotalSize = DiffuseOffset + DiffuseSize;

        Console.WriteLine($"  file_size={raw.Length} (expected {TotalSize})");
        Console.WriteLine($"  height_count={cell.Heights.Length} normal_count={cell.Normals.Length}");
        Console.WriteLine(
            $"  texture_index_grid_len={cell.TextureIndexGrid.Length} direction_flags_len={cell.DirectionFlags.Length}");
        Console.WriteLine($"  diffuse_colour_count={cell.DiffuseColours.Length}");

        var distinctTex = new SortedSet<byte>(cell.TextureIndexGrid);
        Console.WriteLine($"  distinct_texture_indices: [{string.Join(",", distinctTex)}]");

        var distinctDir = new SortedSet<byte>(cell.DirectionFlags);
        Console.WriteLine($"  distinct_direction_flags: [{string.Join(",", distinctDir)}]");

        var doc = new
        {
            source = vfsPath,
            file_size = raw.Length,
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
            texture_index_grid = cell.TextureIndexGrid,
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

        Console.WriteLine($"  wrote -> {Path.GetFullPath(guardedOut)}");
        WarnNeverCommit();
        return 0;
    }

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

        var isParticleEmitter = stem == "particleemitter" && ext == ".eff";

        var dir = Path.GetDirectoryName(Path.GetFullPath(guardedOut));
        if (dir is not null) Directory.CreateDirectory(dir);

        if (isParticleEmitter)
        {
            var table = ParticleEmitterParser.Parse(raw);

            Console.WriteLine($"xeff-emitters (particleEmitter.eff): {table.Entries.Length} entries");

            var entries = table.Entries.Select(e => new
            {
                entry_id = e.EntryId,
                is_gpu_particle = e.EntryId >= 10000,
                num_frames = e.NumFrames,
                sprite_size_x = e.SpriteSizeX,
                sprite_size_y = e.SpriteSizeY,
                blend_additive_flag = e.BlendAdditiveFlag,
                texture_name = e.TextureName,
                sub_records = e.SubRecords.Select((sr, idx) => new
                {
                    idx,
                    life_bonus = sr.LifeBonus,
                    lifetime = sr.Lifetime,
                    spawn_delay = sr.SpawnDelay,
                    size_init = sr.SizeInit,
                    color_r = sr.ColorR,
                    color_g = sr.ColorG,
                    color_b = sr.ColorB,
                    color_a = sr.ColorA,
                    spawn_pos_x = sr.SpawnPosX,
                    spawn_pos_y = sr.SpawnPosY,
                    spawn_pos_z = sr.SpawnPosZ,
                    size_rate = sr.SizeRate,
                    color_r_rate = sr.ColorRRate,
                    color_g_rate = sr.ColorGRate,
                    color_b_rate = sr.ColorBRate,
                    color_a_rate = sr.ColorARate,
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
                spec = "Docs/RE/formats/effects.md Section E",
                entries
            };

            var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(guardedOut, json, Encoding.UTF8);
            Console.WriteLine($"  wrote {table.Entries.Length} entries -> {Path.GetFullPath(guardedOut)}");
        }
        else if (ext == ".xeff")
        {
            var xeff = XeffParser.ParseXeff(raw);

            Console.WriteLine(
                $"xeff-emitters (.xeff): effect_id={xeff.EffectId} sub_effect_count={xeff.SubEffectCount}");

            var subEffects = xeff.SubEffects.Select((se, idx) => new
            {
                block_idx = idx,
                emitter_type = se.EmitterType,
                resource_id = se.ResourceId,
                is_gpu_particle = se.ResourceId >= 10000,
                anim_flag = se.AnimFlag,
                blend_mode = se.BlendMode,
                blend_mode_kind = se.BlendModeKind.ToString(),
                element_dword2 = se.ElementDword2,
                entry_count = se.EntryCount,
                texture_names = se.TextureNames,
                anim_loop = se.AnimLoop,
                anim_stride_ms = se.AnimStride,
                anim_base_time_ms = se.AnimBaseTime,
                opacity_count = se.Opacity.Length,
                opacity = se.Opacity,
                diffuse_r_keys = se.DiffuseR,
                diffuse_g_keys = se.DiffuseG,
                diffuse_b_keys = se.DiffuseB,
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
                spec = "Docs/RE/formats/effects.md Section A",
                sub_effects = subEffects
            };

            var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(guardedOut, json, Encoding.UTF8);
            Console.WriteLine($"  wrote {xeff.SubEffectCount} sub-effects -> {Path.GetFullPath(guardedOut)}");
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