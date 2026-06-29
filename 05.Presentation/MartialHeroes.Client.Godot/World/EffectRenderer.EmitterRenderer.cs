using Godot;
using MartialHeroes.Assets.Parsers.Effects;
using MartialHeroes.Assets.Parsers.Mesh;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class EffectRenderer
{
    private const string BmplistLstPath = "data/effect/bmplist.lst";
    private const string XobjLstPath = "data/effect/xobj.lst";
    private readonly Dictionary<uint, XobjMeshData?> _xobjDataCache = new();

    private HashSet<string>? _bmpPool;
    private bool _bmpPoolAttempted;

    private string[]? _xobjNames;
    private bool _xobjNamesAttempted;

    private MeshInstance3D? BuildSubEffectMesh(
        SubEffectDesc se,
        Vector3 origin,
        ImageTexture?[]? textures,
        double elapsedMs,
        float scale)
    {
        var mi = new MeshInstance3D { GlobalPosition = origin };
        RebuildSubEffectMesh(mi, se, origin, elapsedMs, textures, scale);
        return mi;
    }

    private static ArrayMesh BuildBillboardQuad(
        float sizeX, float sizeY,
        Color tint,
        float uOff, float vOff,
        bool preRotate90Y)
    {
        var hw = 0.5f * Math.Abs(sizeX);
        var hh = 0.5f * Math.Abs(sizeY);
        hw = MathF.Max(hw, 0.05f);
        hh = MathF.Max(hh, 0.05f);

        var (aX, aY, bX, bY, cX, cY, dX, dY) = preRotate90Y
            ? (-hh, hw, hh, hw, hh, -hw, -hh, -hw)
            : (-hw, hh, hw, hh, hw, -hh, -hw, -hh);

        var arrays = new Array();
        arrays.Resize((int)Mesh.ArrayType.Max);

        arrays[(int)Mesh.ArrayType.Vertex] = new Vector3[]
        {
            new(aX, aY, 0f),
            new(bX, bY, 0f),
            new(cX, cY, 0f),
            new(dX, dY, 0f)
        };
        arrays[(int)Mesh.ArrayType.TexUV] = new Vector2[]
        {
            new(0f + uOff, 0f + vOff),
            new(1f + uOff, 0f + vOff),
            new(1f + uOff, 1f + vOff),
            new(0f + uOff, 1f + vOff)
        };
        arrays[(int)Mesh.ArrayType.Color] = new[]
        {
            tint,
            tint,
            tint,
            tint
        };
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 0, 2, 3 };

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private ArrayMesh? BuildMeshParticle(
        SubEffectDesc se,
        float sx, float sy, float sz,
        Color tint)
    {
        if (se.ResourceId >= XeffResourceParticleThreshold)
            return null;

        var data = ResolveXobjMesh(se.ResourceId);
        if (data is null || data.Vertices.Length == 0 || data.Indices.Length == 0)
            return null;

        var vertexCount = data.Vertices.Length;
        var positions = new Vector3[vertexCount];
        var uvs = new Vector2[vertexCount];
        var colours = new Color[vertexCount];

        for (var v = 0; v < vertexCount; v++)
        {
            var src = data.Vertices[v];
            positions[v] = new Vector3(src.PosX * sx, src.PosY * sy, src.PosZ * sz);
            uvs[v] = new Vector2(src.TexU, src.TexV);
            colours[v] = tint;
        }

        var indices = new int[data.Indices.Length];
        for (var i = 0; i < data.Indices.Length; i++)
            indices[i] = data.Indices[i];

        var arrays = new Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private XobjMeshData? ResolveXobjMesh(uint resourceId)
    {
        if (_xobjDataCache.TryGetValue(resourceId, out var cached))
            return cached;

        XobjMeshData? data = null;

        var names = EnsureXobjNames();
        if (names is not null && resourceId < (uint)names.Length)
        {
            var name = names[resourceId];
            if (!string.IsNullOrEmpty(name) && _assets is not null)
            {
                var path = $"data/effect/xobj/{name}";
                if (!_assets.Contains(path) &&
                    !name.EndsWith(".xobj", StringComparison.OrdinalIgnoreCase))
                    path = $"data/effect/xobj/{name}.xobj";

                if (_assets.Contains(path))
                    try
                    {
                        var raw = _assets.GetRaw(path);
                        if (!raw.IsEmpty)
                        {
                            data = XobjParser.ParseAsMeshParticle(raw);
                            GD.Print($"[EffectRenderer] xobj mesh-particle resolved: resource_id={resourceId} " +
                                     $"→ '{path}' ({data.Vertices.Length}v). spec: effects.md §8.2 step 9 / formats/xobj.md.");
                        }
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr(
                            $"[EffectRenderer] .xobj parse failed (resource_id={resourceId}, '{path}'): {ex.Message}");
                    }
            }
        }

        if (data is null)
            GD.Print($"[EffectRenderer] xobj.lst: no mesh for resource_id={resourceId} " +
                     "(slot miss → billboard fallback, no fabrication). spec: formats/effects.md §A.11.1.");

        _xobjDataCache[resourceId] = data;
        return data;
    }

    private string[]? EnsureXobjNames()
    {
        if (_xobjNamesAttempted) return _xobjNames;
        _xobjNamesAttempted = true;

        if (_assets is null) return null;
        var raw = _assets.GetRaw(XobjLstPath);
        if (raw.IsEmpty)
        {
            GD.Print(
                $"[EffectRenderer] xobj.lst not found ({XobjLstPath}) — mesh-particle emitters fall back to billboard.");
            return null;
        }

        try
        {
            var manifest = XobjLstParser.Parse(raw);
            var names = new string[manifest.Count];
            foreach (var entry in manifest.Entries)
                if ((uint)entry.Index < (uint)names.Length)
                    names[entry.Index] = entry.Name;
            _xobjNames = names;
            GD.Print($"[EffectRenderer] xobj.lst loaded: {manifest.Count} mesh slots (slot_index→name). " +
                     "spec: Docs/RE/formats/effects.md §A.11.1.");
        }
        catch (Exception ex)
        {
            GD.PrintErr(
                $"[EffectRenderer] xobj.lst parse failed: {ex.Message} — billboard fallback for mesh emitters.");
        }

        return _xobjNames;
    }

    private HashSet<string>? EnsureBmpPool()
    {
        if (_bmpPoolAttempted) return _bmpPool;
        _bmpPoolAttempted = true;

        if (_assets is null) return null;
        var raw = _assets.GetRaw(BmplistLstPath);
        if (raw.IsEmpty)
        {
            GD.Print(
                $"[EffectRenderer] bmplist.lst not found ({BmplistLstPath}) — texture names resolved by direct probe.");
            return null;
        }

        try
        {
            var manifest = BmplistLstParser.Parse(raw);
            var pool = new HashSet<string>(manifest.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in manifest.Entries)
                if (!string.IsNullOrEmpty(entry.Name))
                    pool.Add(entry.Name);
            _bmpPool = pool;
            GD.Print($"[EffectRenderer] bmplist.lst pool loaded: {pool.Count} effect-texture names. " +
                     "spec: Docs/RE/formats/effects.md §A.10.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EffectRenderer] bmplist.lst parse failed: {ex.Message} — direct-probe texture resolution.");
        }

        return _bmpPool;
    }


    private static StandardMaterial3D BuildEffectMaterial(
        ImageTexture texture, Color tint, BaseMaterial3D.BlendModeEnum blendMode, bool billboard)
    {
        return new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoTexture = texture,
            AlbedoColor = tint,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = blendMode,
            BillboardMode = billboard
                ? BaseMaterial3D.BillboardModeEnum.Enabled
                : BaseMaterial3D.BillboardModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
        };
    }


    private ImageTexture?[]? LoadSubEffectTextures(SubEffectDesc se)
    {
        if (_assets is null || se.TextureNames.Length == 0)
            return null;

        var pool = EnsureBmpPool();

        var result = new ImageTexture?[se.TextureNames.Length];
        for (var t = 0; t < se.TextureNames.Length; t++)
        {
            var name = se.TextureNames[t];
            if (string.IsNullOrEmpty(name)) continue;

            if (pool is not null && !pool.Contains(name))
                GD.Print($"[EffectRenderer] sub-effect texture '{name}' not in bmplist.lst pool — " +
                         "resolving via on-disk probe. spec: Docs/RE/formats/effects.md §A.10.");

            result[t] = _assets.LoadTexture($"data/effect/texture/{name}.tga");

            if (result[t] is null)
                result[t] = _assets.LoadTexture($"data/effect/texture/{name}.dds");
        }

        return result;
    }


    private void TeardownLiveEffect(LiveEffect live)
    {
        if (live.MeshInstances is not null)
            foreach (var mi in live.MeshInstances)
                if (mi is not null && IsInstanceValid(mi))
                    mi.QueueFree();

        if (live.SimNodes is not null)
            foreach (var sim in live.SimNodes)
                if (sim is not null && IsInstanceValid(sim))
                    sim.QueueFree();
    }
}