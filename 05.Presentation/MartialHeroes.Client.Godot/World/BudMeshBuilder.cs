
using Godot;
using MartialHeroes.Assets.Parsers.Terrain.Models;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Client.Godot.World;

public static class BudMeshBuilder
{
    public static Node3D Build(BudScene scene, Func<uint, ImageTexture?>? textureResolver = null)
    {
        var root = new Node3D { Name = "BudSceneNode" };

        for (var i = 0; i < scene.Objects.Length; i++)
        {
            var obj = scene.Objects[i];

            try
            {
                var inst = BuildObject(obj, i, textureResolver);
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


    private static MeshInstance3D? BuildObject(
        BudObject obj,
        int objIndex,
        Func<uint, ImageTexture?>? textureResolver)
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

        for (var v = 0; v < vertCount; v++)
        {
            var bv = obj.Vertices[v];

            positions[v] = new Vector3(bv.PosX, bv.PosY, -bv.PosZ);

            normals[v] = new Vector3(bv.NormalX, bv.NormalY, -bv.NormalZ).Normalized();

            uvs[v] = new Vector2(bv.UvU, bv.UvV);
        }

        var triCount = obj.Indices.Length / 3;
        var indices = new int[triCount * 3];
        for (var t = 0; t < triCount; t++)
        {
            var src = t * 3;
            var dst = t * 3;
            indices[dst + 0] = obj.Indices[src + 0];
            indices[dst + 1] = obj.Indices[src + 2];
            indices[dst + 2] = obj.Indices[src + 1];
        }

        var arrays = new Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var mat = new StandardMaterial3D();
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel;
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps;

        var hasTexture = false;

        if (textureResolver is not null)
            try
            {
                var tex = textureResolver(obj.TexId);
                if (tex is not null)
                {
                    mat.AlbedoTexture = tex;
                    hasTexture = true;
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[BudMeshBuilder] textureResolver threw for tex_id={obj.TexId}: {ex.Message}");
            }

        if (!hasTexture)
        {
            mat.AlbedoColor = new Color(0.6f, 0.6f, 0.6f);
            mat.EmissionEnabled = true;
            mat.Emission = new Color(0.15f, 0.15f, 0.15f);
        }

        mesh.SurfaceSetMaterial(0, mat);

        return new MeshInstance3D
        {
            Mesh = mesh,
            Name = $"BudObject_{objIndex}_tex{obj.TexId}"
        };
    }
}