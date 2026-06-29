using Godot;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Terrain;
using MartialHeroes.Assets.Parsers.Terrain.Models;
using MartialHeroes.Assets.Vfs;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Explorer.Viewer;

public sealed record BudBuildInfo(int ObjectCount, int Textured, int Fallback, string TextureChain);

public static class BudSceneBuilder
{
    private static Shader? _swayShader;

    public static Node3D Build(BudScene scene, MappedVfsArchive? archive, BgTextureCatalog? bgCatalog)
    {
        return BuildCore(scene, archive, bgCatalog, string.Empty, true).Root;
    }

    public static Node3D BuildWorld(BudScene scene, MappedVfsArchive archive, BgTextureCatalog? bgCatalog,
        string budVfsPath)
    {
        return BuildCore(scene, archive, bgCatalog, budVfsPath, false).Root;
    }

    private static (Node3D Root, BudBuildInfo Info) BuildCore(BudScene scene, MappedVfsArchive? archive,
        BgTextureCatalog? bgCatalog, string budVfsPath, bool recenter)
    {
        var root = new Node3D { Name = "BudSceneNode" };
        var textured = 0;
        var fallback = 0;

        var buildingPool = LoadBuildingPool(archive, bgCatalog, budVfsPath, out var textureChain);

        for (var i = 0; i < scene.Objects.Length; i++)
        {
            var obj = scene.Objects[i];
            try
            {
                ImageTexture? tex = null;
                byte kind = 1;
                if (archive is not null && bgCatalog is not null)
                {
                    int slot;
                    if (buildingPool is not null && buildingPool.Length > 0)
                        slot = obj.TexId > 0 && obj.TexId <= (uint)buildingPool.Length
                            ? buildingPool[(int)obj.TexId - 1]
                            : buildingPool[0];
                    else
                        slot = (int)obj.TexId;
                    var resolved = ViewerTextures.ResolveBgSlot(archive, bgCatalog, slot);
                    kind = bgCatalog.ResolveKind(slot);

                    tex = resolved.Texture;
                    if (tex is null) fallback++;
                    else textured++;
                }
                else
                {
                    fallback++;
                }

                var inst = BuildObject(obj, i, tex, kind);
                if (inst is not null)
                    root.AddChild(inst);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[BudScene] Failed to build object[{i}]: {ex.Message}");
                fallback++;
            }
        }

        if (recenter) ApplyRecentre(scene, root);

        GD.Print(
            $"[BudScene] {scene.Objects.Length} objects — textured: {textured}, fallback: {fallback}, chain: {textureChain}");
        return (root, new BudBuildInfo(scene.Objects.Length, textured, fallback, textureChain));
    }

    private static int[]? LoadBuildingPool(MappedVfsArchive? archive, BgTextureCatalog? bgCatalog,
        string budVfsPath, out string chainDescription)
    {
        chainDescription = "direct-slot (no .bud path)";
        if (archive is null || bgCatalog is null || budVfsPath.Length == 0)
            return null;

        var mapPath = FindMapPath(archive, budVfsPath);
        if (mapPath is null)
        {
            chainDescription = "direct-slot (no sibling .map)";
            return null;
        }

        MapDescriptor descriptor;
        try
        {
            var mapBytes = archive.GetFileContent(mapPath);
            descriptor = MapDescriptorParser.Parse(mapBytes);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[BudScene] failed to parse sibling .map {mapPath}: {ex.Message}");
            chainDescription = "direct-slot (.map parse failed)";
            return null;
        }

        MapSection? buildingSection = null;
        foreach (var sec in descriptor.Sections)
            if (sec.Keyword.Equals("BUILDING", StringComparison.OrdinalIgnoreCase))
            {
                buildingSection = sec;
                break;
            }

        if (buildingSection is null || buildingSection.Textures.Length == 0)
        {
            chainDescription = $"direct-slot (no BUILDING section in {mapPath})";
            return null;
        }

        var pool = new int[buildingSection.Textures.Length];
        for (var i = 0; i < buildingSection.Textures.Length; i++)
            pool[i] = buildingSection.Textures[i].TexId;

        chainDescription = $"BUILDING pool ({pool.Length} entries, map={mapPath})";
        GD.Print($"[BudScene] BUILDING texture pool from {mapPath}: {pool.Length} entries");
        return pool;
    }

    private static void ApplyRecentre(BudScene scene, Node3D root)
    {
        if (scene.Objects.Length == 0) return;

        var minX = float.MaxValue;
        var maxX = float.MinValue;
        var minY = float.MaxValue;
        var maxY = float.MinValue;
        var minZ = float.MaxValue;
        var maxZ = float.MinValue;
        var hasVerts = false;

        foreach (var obj in scene.Objects)
        foreach (var v in obj.Vertices)
        {
            if (v.PosX < minX) minX = v.PosX;
            if (v.PosX > maxX) maxX = v.PosX;
            if (v.PosY < minY) minY = v.PosY;
            if (v.PosY > maxY) maxY = v.PosY;
            if (v.PosZ < minZ) minZ = v.PosZ;
            if (v.PosZ > maxZ) maxZ = v.PosZ;
            hasVerts = true;
        }

        if (!hasVerts) return;

        root.Position = new Vector3(
            -(minX + maxX) * 0.5f,
            -minY,
            (minZ + maxZ) * 0.5f
        );

        var halfW = (maxX - minX) * 0.5f;
        var halfD = (maxZ - minZ) * 0.5f;
        GD.Print($"[BudScene] world-space Y: raw=[{minY:F1},{maxY:F1}] -> after grounding: Y=[0.0,{maxY - minY:F1}]");
        GD.Print(
            $"[BudScene] AABB after grounding: X=[{-halfW:F1},{halfW:F1}] Y=[0.0,{maxY - minY:F1}] Z=[{-halfD:F1},{halfD:F1}]");
    }

    private static MeshInstance3D? BuildObject(BudObject obj, int index, ImageTexture? tex, byte kind)
    {
        if (obj.Vertices.Length == 0 || obj.Indices.Length == 0)
            return null;

        var vertCount = obj.Vertices.Length;
        var positions = new Vector3[vertCount];
        var normals = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];

        var minY = float.MaxValue;
        var maxY = float.MinValue;
        var minX = float.MaxValue;
        var maxX = float.MinValue;
        var minZ = float.MaxValue;
        var maxZ = float.MinValue;

        for (var v = 0; v < vertCount; v++)
        {
            var bv = obj.Vertices[v];
            var p = new Vector3(bv.PosX, bv.PosY, -bv.PosZ);
            positions[v] = p;
            normals[v] = new Vector3(bv.NormalX, bv.NormalY, -bv.NormalZ).Normalized();
            uvs[v] = new Vector2(bv.UvU, bv.UvV);

            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
            if (p.Z < minZ) minZ = p.Z;
            if (p.Z > maxZ) maxZ = p.Z;
        }

        var triCount = obj.Indices.Length / 3;
        var indices = new int[triCount * 3];
        for (var t = 0; t < triCount; t++)
        {
            var src = t * 3;
            indices[src + 0] = obj.Indices[src + 0];
            indices[src + 1] = obj.Indices[src + 2];
            indices[src + 2] = obj.Indices[src + 1];
        }

        var arrays = new Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var swaySmall = kind >= 0x0A && kind <= 0x0E;
        var swayLarge = kind >= 0x14 && kind <= 0x18;
        var solidShadow = kind == 0x02;
        var twoSided = solidShadow || swaySmall || swayLarge;
        var cull = twoSided ? BaseMaterial3D.CullModeEnum.Disabled : BaseMaterial3D.CullModeEnum.Back;

        if (tex is not null && (swaySmall || swayLarge))
        {
            var height = MathF.Max(maxY - minY, 0.001f);
            var diag = MathF.Sqrt((maxX - minX) * (maxX - minX) + (maxZ - minZ) * (maxZ - minZ));
            float rawAmp;
            float midYNorm;
            float secondaryParam;
            if (swaySmall)
            {
                if (vertCount == 9)
                {
                    var v1y = obj.Vertices[1].PosY;
                    var v4y = obj.Vertices[4].PosY;
                    var v7y = obj.Vertices[7].PosY;
                    rawAmp = MathF.Abs(v1y - v4y) * 0.1f;
                    var denom = v1y - v7y;
                    secondaryParam = MathF.Abs(denom) > 0.001f ? (v4y - v7y) / denom * 0.7f : 0.35f;
                    midYNorm = Math.Clamp((v4y - minY) / height, 0f, 1f);
                }
                else if (vertCount > 5)
                {
                    var v1y = obj.Vertices[1].PosY;
                    var v4y = obj.Vertices[4].PosY;
                    rawAmp = MathF.Abs(v1y - v4y) * 0.1f;
                    secondaryParam = 0.3f;
                    midYNorm = Math.Clamp((v4y - minY) / height, 0f, 1f);
                }
                else
                {
                    rawAmp = height * 0.1f;
                    secondaryParam = 0.3f;
                    midYNorm = 0.5f;
                }
            }
            else
            {
                rawAmp = MathF.Min(diag * 0.01f * 0.5f, 2.0f);
                secondaryParam = 0.5f;
                midYNorm = 0.5f;
            }

            var divisor = swayLarge ? 2 << (kind - 0x14) : 2 << (kind - 0x0A);
            var amp = rawAmp / divisor;
            var swayMat = new ShaderMaterial { Shader = GetSwayShader() };
            swayMat.SetShaderParameter("albedo_tex", tex);
            swayMat.SetShaderParameter("base_y", minY);
            swayMat.SetShaderParameter("inv_height", 1f / height);
            swayMat.SetShaderParameter("amp", amp);
            swayMat.SetShaderParameter("phase", index * 0.7f);
            swayMat.SetShaderParameter("mid_y_norm", midYNorm);
            swayMat.SetShaderParameter("secondary_param", secondaryParam);
            mesh.SurfaceSetMaterial(0, swayMat);
            return new MeshInstance3D { Name = $"BudObject_{index}", Mesh = mesh };
        }

        StandardMaterial3D mat;
        if (tex is not null)
        {
            mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
                CullMode = cull,
                AlbedoTexture = tex,
                AlbedoColor = new Color(2f, 2f, 2f),
                Roughness = 0.95f,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
            };
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        }
        else
        {
            mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
                CullMode = cull,
                AlbedoColor = new Color(0.6f, 0.6f, 0.6f),
                Roughness = 0.8f,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
            };
        }

        mesh.SurfaceSetMaterial(0, mat);

        return new MeshInstance3D { Name = $"BudObject_{index}", Mesh = mesh };
    }

    private static Shader GetSwayShader()
    {
        if (_swayShader is not null) return _swayShader;
        _swayShader = new Shader
        {
            Code = @"shader_type spatial;
render_mode cull_disabled, blend_mix, depth_prepass_alpha;
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform float base_y = 0.0;
uniform float inv_height = 0.0;
uniform float amp = 0.0;
uniform float phase = 0.0;
uniform float speed = 1.5;
uniform float mid_y_norm = 0.5;
uniform float secondary_param = 0.3;
void vertex() {
    float h = clamp((VERTEX.y - base_y) * inv_height, 0.0, 1.0);
    float lower_w = h * secondary_param / max(mid_y_norm, 0.0001);
    float upper_w = secondary_param + (1.0 - secondary_param) * clamp((h - mid_y_norm) / max(1.0 - mid_y_norm, 0.0001), 0.0, 1.0);
    float w = mix(lower_w, upper_w, step(mid_y_norm, h));
    float t = TIME * speed + phase;
    VERTEX.x += sin(t) * amp * w;
    VERTEX.z += sin(t * 0.8 + 1.3) * amp * w * 0.6;
}
void fragment() {
    vec4 c = texture(albedo_tex, UV);
    ALBEDO = clamp(c.rgb * 2.0, vec3(0.0), vec3(1.0));
    ALPHA = c.a;
    ROUGHNESS = 0.95;
}"
        };
        return _swayShader;
    }

    private static string? FindMapPath(MappedVfsArchive archive, string budVfsPath)
    {
        var normalised = budVfsPath.Replace('\\', '/');
        var changeExt = Path.ChangeExtension(normalised, ".map").Replace('\\', '/');
        if (archive.Contains(changeExt))
            return changeExt;

        var fileName = Path.GetFileNameWithoutExtension(normalised);
        var dir = Path.GetDirectoryName(normalised)?.Replace('\\', '/') ?? string.Empty;
        if (dir.Length > 0 && !dir.EndsWith('/'))
            dir += '/';
        var derived = dir + fileName + ".map";
        if (archive.Contains(derived))
            return derived;

        return null;
    }
}