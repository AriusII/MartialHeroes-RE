using Godot;
using MartialHeroes.Assets.Parsers.Terrain.Models;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Client.Godot.World;

public static class BudMeshBuilder
{
    private const string SwayShaderPath = "res://World/BudSway.gdshader";

    private static Shader? _swayShader;

    private static bool _largeSwayNoteLogged;

    private static bool _largeSwayWindFollowUpLogged;

    public static Node3D Build(
        BudScene scene,
        Func<uint, ImageTexture?>? textureResolver = null,
        Func<uint, byte>? kindResolver = null)
    {
        BudSwayClock.EnsureGlobalParam();

        var root = new Node3D { Name = "BudSceneNode" };

        for (var i = 0; i < scene.Objects.Length; i++)
        {
            var obj = scene.Objects[i];

            try
            {
                var inst = BuildObject(obj, i, textureResolver, kindResolver);
                if (inst is not null)
                    root.AddChild(inst);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[BudMeshBuilder] Failed to build BudObject[{i}] (texId={obj.TexId}): {ex.Message}");
            }
        }

        return root;
    }

    private static Shader SwayShader()
    {
        _swayShader ??= GD.Load<Shader>(SwayShaderPath);
        return _swayShader;
    }

    private static MeshInstance3D? BuildObject(
        BudObject obj,
        int objIndex,
        Func<uint, ImageTexture?>? textureResolver,
        Func<uint, byte>? kindResolver)
    {
        if (obj.Vertices.Length == 0 || obj.Indices.Length == 0)
        {
            GD.Print($"[BudMeshBuilder] BudObject[{objIndex}] has no geometry — skipping.");
            return null;
        }

        var vertCount = obj.Vertices.Length;
        var positions = new Vector3[vertCount];
        var normals = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];

        var minX = float.MaxValue;
        var maxX = float.MinValue;
        var minY = float.MaxValue;
        var maxY = float.MinValue;
        var minZ = float.MaxValue;
        var maxZ = float.MinValue;
        var normalSum = Vector3.Zero;

        for (var v = 0; v < vertCount; v++)
        {
            var bv = obj.Vertices[v];

            var p = new Vector3(bv.PosX, bv.PosY, -bv.PosZ);
            positions[v] = p;

            var n = new Vector3(bv.NormalX, bv.NormalY, -bv.NormalZ).Normalized();
            normals[v] = n;
            normalSum += n;

            uvs[v] = new Vector2(bv.UvU, bv.UvV);

            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
            if (p.Z < minZ) minZ = p.Z;
            if (p.Z > maxZ) maxZ = p.Z;
        }

        var triCount = obj.Indices.Length / 3;
        var indices = new int[obj.Indices.Length];
        for (var t = 0; t < triCount; t++)
        {
            var src = t * 3;
            indices[src + 0] = obj.Indices[src + 0];
            indices[src + 1] = obj.Indices[src + 2];
            indices[src + 2] = obj.Indices[src + 1];
        }

        var kind = kindResolver?.Invoke(obj.TexId) ?? 1;
        var swaySmall = kind >= 0x0A && kind <= 0x0E;
        var swayLarge = kind >= 0x14 && kind <= 0x18;
        var solidShadow = kind == 0x02;
        var twoSided = solidShadow || swaySmall || swayLarge;
        var isOpaque = !twoSided;

        Color[]? bendColors = null;
        var swayAmp = 0f;
        var swayDir = Vector2.Zero;
        var swayDir3 = Vector3.Zero;

        if (swayLarge)
        {
            swayAmp = ComputeLargeSwayAmplitude(kind, minX, maxX, minY, maxY, minZ, maxZ);
            if (swayAmp != 0f)
                swayDir3 = normals[0];
        }
        else if (swaySmall)
        {
            swayAmp = ComputeSmallSwayAmplitude(obj, kind, minY, maxY);
            if (swayAmp != 0f)
            {
                var horizontal = new Vector2(normalSum.X, normalSum.Z);
                if (horizontal.Length() > 1e-4f)
                    swayDir = horizontal.Normalized() * 0.5f;

                bendColors = BuildBendWeights(obj, vertCount, minY, maxY);
            }
        }

        var arrays = new Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        if (bendColors is not null)
            arrays[(int)Mesh.ArrayType.Color] = bendColors;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var hasTexture = ResolveBudTexture(obj.TexId, objIndex, textureResolver, out var tex);

        var mat = new ShaderMaterial { Shader = SwayShader() };
        mat.SetShaderParameter("has_texture", hasTexture);
        mat.SetShaderParameter("one_sided", !twoSided);
        mat.SetShaderParameter("is_opaque", isOpaque);
        mat.SetShaderParameter("sway_amp", swayAmp);
        mat.SetShaderParameter("sway_dir", swayDir);
        mat.SetShaderParameter("is_large", swayLarge);
        mat.SetShaderParameter("sway_dir3", swayDir3);
        if (hasTexture && tex is not null)
            mat.SetShaderParameter("albedo_tex", tex);

        mesh.SurfaceSetMaterial(0, mat);

        var inst = new MeshInstance3D
        {
            Mesh = mesh,
            Name = $"BudObject_{objIndex}_tex{obj.TexId}_k{kind:X2}"
        };

        ApplyDistanceCull(inst, minX, maxX, minY, maxY, minZ, maxZ);

        return inst;
    }

    private static bool ResolveBudTexture(
        uint texId,
        int objIndex,
        Func<uint, ImageTexture?>? textureResolver,
        out ImageTexture? tex)
    {
        tex = null;
        if (textureResolver is null)
        {
            GD.Print($"[BudMeshBuilder] BudObject[{objIndex}] tex_id={texId}: no resolver — untextured (white). " +
                     "spec: terrain_scene.md §6 — tex_id pool resolution.");
            return false;
        }

        try
        {
            tex = textureResolver(texId);
            if (tex is null && texId != 1)
            {
                tex = textureResolver(1u);
                if (tex is not null)
                    GD.Print($"[BudMeshBuilder] BudObject[{objIndex}] tex_id={texId} unresolved — " +
                             "clamped to pool entry 1 (always textured). spec: terrain_scene.md §6.");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[BudMeshBuilder] textureResolver threw for tex_id={texId}: {ex.Message}");
            tex = null;
        }

        if (tex is null)
            GD.PrintErr($"[BudMeshBuilder] BudObject[{objIndex}] tex_id={texId}: texture chain broke even after " +
                        "clamp-to-1 — rendering untextured (white). spec: terrain_scene.md §6 (clamp-to-1 pool).");

        return tex is not null;
    }

    private static float ComputeSmallSwayAmplitude(BudObject obj, byte kind, float minY, float maxY)
    {
        var vertCount = obj.Vertices.Length;

        float rawAmp;
        if (vertCount > 5)
        {
            var v1y = obj.Vertices[1].PosY;
            var v4y = obj.Vertices[4].PosY;
            rawAmp = MathF.Abs(v1y - v4y) * 0.1f;
        }
        else
        {
            rawAmp = (maxY - minY) * 0.1f;
        }

        var divisorS = 2 << (kind - 0x0A);
        return rawAmp / divisorS;
    }

    private static float ComputeLargeSwayAmplitude(
        byte kind,
        float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
    {
        if (!_largeSwayNoteLogged)
        {
            _largeSwayNoteLogged = true;
            GD.Print("[BudMeshBuilder] Large-object sway (kind 0x14-0x18) is the IDA-CONFIRMED distinct path: " +
                     "amplitude = min(d * 0.005, 2.0) / (2 << (kind - 0x14)), d = AABB 3D diagonal over ALL " +
                     "vertices incl. Y (degenerate axis min==max -> max += 2.0); deform = rigid whole-object " +
                     "translation along the vertex-0 normal, per-axis ratios x/y/z = 1.0/0.7/1.5 (the only " +
                     "bucket that displaces Y). spec: Docs/RE/formats/bud.md large bucket 0x14-0x18.");
        }

        if (!_largeSwayWindFollowUpLogged)
        {
            _largeSwayWindFollowUpLogged = true;
            GD.Print("[BudMeshBuilder] FOLLOW-UP: large-object phase uses the shared global bud_sway_phase clock; " +
                     "the per-object wind-speed / velSign ping-pong modulation is NOT yet wired (no fabricated " +
                     "wind values) — approximated as shared-phase x amplitude pending a dedicated RE pass.");
        }

        var dx = maxX - minX == 0f ? 2.0f : maxX - minX;
        var dy = maxY - minY == 0f ? 2.0f : maxY - minY;
        var dz = maxZ - minZ == 0f ? 2.0f : maxZ - minZ;
        var d = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

        var divisorL = 2 << (kind - 0x14);
        return MathF.Min(d * 0.005f, 2.0f) / divisorL;
    }

    private static Color[] BuildBendWeights(BudObject obj, int vertCount, float minY, float maxY)
    {
        var colors = new Color[vertCount];

        if (vertCount <= 9)
        {
            float secondaryRatio;
            if (vertCount == 9)
            {
                var v1y = obj.Vertices[1].PosY;
                var v4y = obj.Vertices[4].PosY;
                var v7y = obj.Vertices[7].PosY;
                var denom = v1y - v7y;
                secondaryRatio = MathF.Abs(denom) > 1e-4f ? (v4y - v7y) / denom * 0.7f : 0f;
            }
            else
            {
                secondaryRatio = 0.3f;
            }

            for (var v = 0; v < vertCount; v++)
            {
                var w = 0f;
                if (v <= 2)
                    w = 1f;
                else if (vertCount == 8 && v == 3)
                    w = 1f;
                else if (vertCount != 8 && v >= 3 && v <= 5)
                    w = secondaryRatio;

                colors[v] = new Color(w, 0f, 0f);
            }

            return colors;
        }

        var height = MathF.Max(maxY - minY, 1e-4f);
        for (var v = 0; v < vertCount; v++)
        {
            var p = obj.Vertices[v].PosY;
            var w = Math.Clamp((p - minY) / height, 0f, 1f);
            colors[v] = new Color(w, 0f, 0f);
        }

        return colors;
    }

    private static void ApplyDistanceCull(
        MeshInstance3D inst,
        float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
    {
        var dx = maxX - minX;
        var dz = maxZ - minZ;
        var xzDiagonal = MathF.Sqrt(dx * dx + dz * dz);
        var yExtent = maxY - minY;
        var footprint = MathF.Max(MathF.Floor(0.6f * xzDiagonal), MathF.Floor(yExtent));

        float cullDist;
        if (footprint < 8f) cullDist = 300f;
        else if (footprint < 16f) cullDist = 500f;
        else if (footprint < 32f) cullDist = 1000f;
        else if (footprint < 64f) cullDist = 1500f;
        else cullDist = 1800f;

        inst.VisibilityRangeEnd = cullDist;
        inst.VisibilityRangeEndMargin = cullDist * 0.05f;
    }
}