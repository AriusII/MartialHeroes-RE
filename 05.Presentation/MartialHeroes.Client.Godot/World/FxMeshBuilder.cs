using Godot;
using MartialHeroes.Assets.Parsers.Terrain.Models;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Client.Godot.World;

internal static class FxMeshBuilder
{
    public delegate ImageTexture? FxTextureResolver(int channel, uint texIndex1Based);

    public static Node3D BuildFx1(Fx1Layer layer, FxTextureResolver? resolver)
    {
        var root = new Node3D { Name = "FxLayer_fx1" };
        for (var g = 0; g < layer.Groups.Length; g++)
        {
            var grp = layer.Groups[g];
            AddGroupVf36(root, 1, g, grp.TextureIndex1Based, grp.Vertices, grp.Indices, resolver);
        }

        return root;
    }

    public static Node3D BuildFx2(Fx2Layer layer, FxTextureResolver? resolver)
    {
        var root = new Node3D { Name = "FxLayer_fx2" };
        for (var g = 0; g < layer.Groups.Length; g++)
        {
            var grp = layer.Groups[g];
            AddGroupVf44(root, 2, g, grp.TextureIndex1Based, grp.Vertices, grp.Indices, resolver);
        }

        return root;
    }

    public static Node3D BuildFx3(Fx3Layer layer, FxTextureResolver? resolver)
    {
        var root = new Node3D { Name = "FxLayer_fx3_water" };
        for (var g = 0; g < layer.Groups.Length; g++)
        {
            var grp = layer.Groups[g];
            AddGroupVf36(root, 3, g, grp.TextureIndex1Based, grp.Vertices, grp.Indices, resolver,
                true);
        }

        return root;
    }

    public static Node3D BuildFx4(Fx4Layer layer, FxTextureResolver? resolver)
    {
        var root = new Node3D { Name = "FxLayer_fx4" };
        for (var t = 0; t < layer.Tiles.Length; t++)
        {
            var tile = layer.Tiles[t];
            AddGroupVf44(root, 4, t, 1u, tile.Vertices, tile.Indices, resolver);
        }

        return root;
    }

    public static Node3D BuildFx5(Fx5Layer layer, FxTextureResolver? resolver)
    {
        var root = new Node3D { Name = "FxLayer_fx5_water" };
        for (var s = 0; s < layer.Sections.Length; s++)
        {
            var sec = layer.Sections[s];
            AddGroupVf36(root, 5, s, 1u, sec.Vertices, sec.Indices, resolver,
                true);
        }

        return root;
    }

    public static Node3D BuildFx6(Fx6Layer layer, FxTextureResolver? resolver)
    {
        var root = new Node3D { Name = "FxLayer_fx6" };
        for (var g = 0; g < layer.Groups.Length; g++)
        {
            var grp = layer.Groups[g];
            AddGroupVf32(root, 6, g, grp.TextureIndex1Based, grp.Vertices, grp.Indices, resolver);
        }

        return root;
    }

    public static Node3D BuildFx7(Fx7Layer layer, FxTextureResolver? resolver)
    {
        var root = new Node3D { Name = "FxLayer_fx7" };
        for (var g = 0; g < layer.Groups.Length; g++)
        {
            var grp = layer.Groups[g];
            AddGroupVf32(root, 7, g, 1u, grp.Vertices, grp.Indices, resolver);
        }

        return root;
    }


    private static void AddGroupVf36(
        Node3D root, int channel, int groupIndex, uint texIndex1Based,
        FxVertex36[] verts, ushort[] indices, FxTextureResolver? resolver, bool isWater = false)
    {
        if (verts.Length == 0 || indices.Length == 0)
            return;

        var positions = new Vector3[verts.Length];
        var normals = new Vector3[verts.Length];
        var uvs = new Vector2[verts.Length];
        for (var v = 0; v < verts.Length; v++)
        {
            var fv = verts[v];
            positions[v] = new Vector3(fv.X, fv.Y, -fv.Z);
            normals[v] = new Vector3(fv.NX, fv.NY, -fv.NZ).Normalized();
            uvs[v] = new Vector2(fv.U0, fv.V0);
        }

        EmitMeshInstance(root, channel, groupIndex, texIndex1Based, positions, normals, uvs, indices,
            resolver, isWater);
    }

    private static void AddGroupVf44(
        Node3D root, int channel, int groupIndex, uint texIndex1Based,
        FxVertex44[] verts, ushort[] indices, FxTextureResolver? resolver, bool isWater = false)
    {
        if (verts.Length == 0 || indices.Length == 0)
            return;

        var positions = new Vector3[verts.Length];
        var normals = new Vector3[verts.Length];
        var uvs = new Vector2[verts.Length];
        for (var v = 0; v < verts.Length; v++)
        {
            var fv = verts[v];
            positions[v] = new Vector3(fv.X, fv.Y, -fv.Z);
            normals[v] = new Vector3(fv.NX, fv.NY, -fv.NZ).Normalized();
            uvs[v] = new Vector2(fv.U0, fv.V0);
        }

        EmitMeshInstance(root, channel, groupIndex, texIndex1Based, positions, normals, uvs, indices,
            resolver, isWater);
    }

    private static void AddGroupVf32(
        Node3D root, int channel, int groupIndex, uint texIndex1Based,
        FxVertex32[] verts, ushort[] indices, FxTextureResolver? resolver, bool isWater = false)
    {
        if (verts.Length == 0 || indices.Length == 0)
            return;

        var positions = new Vector3[verts.Length];
        var normals = new Vector3[verts.Length];
        var uvs = new Vector2[verts.Length];
        for (var v = 0; v < verts.Length; v++)
        {
            var fv = verts[v];
            positions[v] = new Vector3(fv.X, fv.Y, -fv.Z);
            normals[v] = new Vector3(fv.NX, fv.NY, -fv.NZ).Normalized();
            uvs[v] = new Vector2(fv.U0, fv.V0);
        }

        EmitMeshInstance(root, channel, groupIndex, texIndex1Based, positions, normals, uvs, indices,
            resolver, isWater);
    }


    private static void EmitMeshInstance(
        Node3D root, int channel, int groupIndex, uint texIndex1Based,
        Vector3[] positions, Vector3[] normals, Vector2[] uvs, ushort[] indices,
        FxTextureResolver? resolver, bool isWater)
    {
        ImageTexture? tex = null;
        if (resolver is not null)
            try
            {
                tex = resolver(channel, texIndex1Based);
            }
            catch (Exception ex)
            {
                GD.PrintErr(
                    $"[FxMeshBuilder] FX{channel} group[{groupIndex}] texture resolver threw " +
                    $"for texIndex={texIndex1Based}: {ex.Message}");
                tex = null;
            }

        if (tex is null)
        {
            GD.Print(
                $"[FxMeshBuilder] FX{channel} group[{groupIndex}]: texture (texIndex={texIndex1Based}) " +
                "did not resolve — skipping group (no placeholder). spec: terrain_layers.md §1.4b.");
            return;
        }

        var triCount = indices.Length / 3;
        var idx = new int[triCount * 3];
        for (var t = 0; t < triCount; t++)
        {
            var src = t * 3;
            idx[src + 0] = indices[src + 0];
            idx[src + 1] = indices[src + 2];
            idx[src + 2] = indices[src + 1];
        }

        var mesh = BuildArrayMesh(positions, normals, uvs, idx, tex, isWater);

        root.AddChild(new MeshInstance3D
        {
            Mesh = mesh,
            Name = $"Fx{channel}_group{groupIndex}_tex{texIndex1Based}"
        });

        if (isWater)
        {
            var workingMesh = BuildArrayMesh(positions, normals, uvs, idx, tex, isWater);
            root.AddChild(new MeshInstance3D
            {
                Mesh = workingMesh,
                Name = $"Fx{channel}_group{groupIndex}_tex{texIndex1Based}_working"
            });
        }
    }

    private static ArrayMesh BuildArrayMesh(
        Vector3[] positions, Vector3[] normals, Vector2[] uvs, int[] indices, ImageTexture tex, bool isWater)
    {
        var arrays = new Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var mat = new StandardMaterial3D
        {
            AlbedoTexture = tex,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
        };

        mesh.SurfaceSetMaterial(0, mat);
        return mesh;
    }
}