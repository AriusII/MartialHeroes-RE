using Godot;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Client.Presentation.Helpers;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Explorer.Viewer;

public static class XobjMeshBuilder
{
    public static MeshInstance3D Build(StaticMesh m)
    {
        var vertCount = m.Positions.Length;
        var positions = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];

        for (var v = 0; v < vertCount; v++)
        {
            var p = m.Positions[v];
            var (gx, gy, gz) = WorldCoordinates.ToGodot(p.X, p.Y, p.Z);
            positions[v] = new Vector3(gx, gy, gz);

            if (v < m.Uvs.Length)
            {
                var uv = m.Uvs[v];
                uvs[v] = new Vector2(uv.X, uv.Y);
            }
        }

        var triCount = m.Indices.Length / 3;
        var indices = new int[triCount * 3];
        for (var t = 0; t < triCount; t++)
        {
            var src = t * 3;
            indices[src + 0] = m.Indices[src + 0];
            indices[src + 1] = m.Indices[src + 2];
            indices[src + 2] = m.Indices[src + 1];
        }

        var arrays = new Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var arrayMesh = new ArrayMesh();
        arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoColor = new Color(0.85f, 0.83f, 0.78f),
            EmissionEnabled = true,
            Emission = new Color(0.12f, 0.11f, 0.09f),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
        };
        arrayMesh.SurfaceSetMaterial(0, mat);

        return new MeshInstance3D { Name = "XobjMesh", Mesh = arrayMesh };
    }
}