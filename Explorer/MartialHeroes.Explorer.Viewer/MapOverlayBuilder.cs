using Godot;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Terrain;
using MartialHeroes.Assets.Parsers.Terrain.Models;
using MartialHeroes.Assets.Vfs;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Explorer.Viewer;

public static class MapOverlayBuilder
{
    public static Node3D? BuildFxLayers(MappedVfsArchive archive, BgTextureCatalog? bgCatalog,
        MapDescriptor descriptor, out int layersBuilt)
    {
        layersBuilt = 0;
        var root = new Node3D { Name = "FxOverlays" };

        foreach (var sec in descriptor.Sections)
        {
            if (sec.DataFile is null) continue;
            var key = sec.Keyword.ToUpperInvariant();
            if (!key.StartsWith("FX")) continue;
            if (!archive.Contains(sec.DataFile)) continue;

            ReadOnlyMemory<byte> bytes;
            try
            {
                bytes = archive.GetFileContent(sec.DataFile);
            }
            catch
            {
                continue;
            }

            var before = root.GetChildCount();
            try
            {
                BuildFxSection(root, key, bytes, archive, bgCatalog, sec.Textures);
            }
            catch (InvalidDataException ex)
            {
                GD.PrintErr($"[MapOverlay] {key} {sec.DataFile} parse failed: {ex.Message}");
                continue;
            }

            if (root.GetChildCount() > before) layersBuilt++;
        }

        if (root.GetChildCount() == 0)
        {
            root.QueueFree();
            return null;
        }

        return root;
    }

    public static Node3D? BuildCollisionWalls(MappedVfsArchive archive, MapDescriptor descriptor,
        float yLow, float yHigh, out int segmentCount)
    {
        segmentCount = 0;
        var verts = new List<Vector3>();
        var idx = new List<int>();

        foreach (var sec in descriptor.Sections)
        {
            if (sec.DataFile is null) continue;
            if (!sec.Keyword.Equals("SOLID", StringComparison.OrdinalIgnoreCase)) continue;
            if (!archive.Contains(sec.DataFile)) continue;

            SodBlob blob;
            try
            {
                blob = SodBlobParser.Parse(archive.GetFileContent(sec.DataFile));
            }
            catch (InvalidDataException ex)
            {
                GD.PrintErr($"[MapOverlay] SOLID {sec.DataFile} parse failed: {ex.Message}");
                continue;
            }

            foreach (var solid in blob.Solids)
            foreach (var quad in solid.Quads)
            {
                var a = new Vector3(quad.P0X, yLow, -quad.P0Z);
                var b = new Vector3(quad.P1X, yLow, -quad.P1Z);
                var c = new Vector3(quad.P0X, yHigh, -quad.P0Z);
                var d = new Vector3(quad.P1X, yHigh, -quad.P1Z);

                var baseI = verts.Count;
                verts.Add(a);
                verts.Add(b);
                verts.Add(c);
                verts.Add(d);

                idx.Add(baseI);
                idx.Add(baseI + 1);
                idx.Add(baseI + 2);
                idx.Add(baseI + 3);
                idx.Add(baseI);
                idx.Add(baseI + 2);
                idx.Add(baseI + 1);
                idx.Add(baseI + 3);

                segmentCount++;
            }
        }

        if (segmentCount == 0) return null;

        var arrays = new Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1f, 0.25f, 0.85f),
            VertexColorUseAsAlbedo = false
        });

        return new MeshInstance3D { Name = "CollisionWalls", Mesh = mesh };
    }

    private static void BuildFxSection(Node3D root, string key, ReadOnlyMemory<byte> bytes,
        MappedVfsArchive archive, BgTextureCatalog? bgCatalog, (int Flag, int TexId)[] textures)
    {
        switch (key)
        {
            case "FX1":
            {
                var layer = TerrainLayerParsers.ParseFx1(bytes);
                for (var g = 0; g < layer.Groups.Length; g++)
                {
                    var grp = layer.Groups[g];
                    EmitVf36(root, $"Fx1_group{g}", grp.Vertices, grp.Indices,
                        ResolveFxTex(archive, bgCatalog, textures, grp.TextureIndex1Based), true);
                }

                break;
            }
            case "FX2":
            {
                var layer = TerrainLayerParsers.ParseFx2(bytes);
                for (var g = 0; g < layer.Groups.Length; g++)
                {
                    var grp = layer.Groups[g];
                    EmitVf44(root, $"Fx2_group{g}", grp.Vertices, grp.Indices,
                        ResolveFxTex(archive, bgCatalog, textures, grp.TextureIndex1Based), false);
                }

                break;
            }
            case "FX3":
            {
                var layer = TerrainLayerParsers.ParseFx3(bytes);
                for (var g = 0; g < layer.Groups.Length; g++)
                {
                    var grp = layer.Groups[g];
                    EmitVf36(root, $"Fx3_water_group{g}", grp.Vertices, grp.Indices,
                        ResolveFxTex(archive, bgCatalog, textures, grp.TextureIndex1Based), true);
                }

                break;
            }
            case "FX4":
            {
                var layer = TerrainLayerParsers.ParseFx4(bytes);
                for (var t = 0; t < layer.Tiles.Length; t++)
                {
                    var tile = layer.Tiles[t];
                    EmitVf44(root, $"Fx4_tile{t}", tile.Vertices, tile.Indices,
                        ResolveFxTex(archive, bgCatalog, textures, 1u), false);
                }

                break;
            }
            case "FX5":
            {
                var layer = TerrainLayerParsers.ParseFx5(bytes);
                for (var s = 0; s < layer.Sections.Length; s++)
                {
                    var sec = layer.Sections[s];
                    EmitVf36(root, $"Fx5_water_section{s}", sec.Vertices, sec.Indices,
                        ResolveFxTex(archive, bgCatalog, textures, 1u), true);
                }

                break;
            }
            case "FX6":
            {
                var layer = TerrainLayerParsers.ParseFx6(bytes);
                for (var g = 0; g < layer.Groups.Length; g++)
                {
                    var grp = layer.Groups[g];
                    EmitVf32(root, $"Fx6_group{g}", grp.Vertices, grp.Indices,
                        ResolveFxTex(archive, bgCatalog, textures, grp.TextureIndex1Based), false);
                }

                break;
            }
            case "FX7":
            {
                var layer = TerrainLayerParsers.ParseFx7(bytes);
                for (var g = 0; g < layer.Groups.Length; g++)
                {
                    var grp = layer.Groups[g];
                    EmitVf32(root, $"Fx7_group{g}", grp.Vertices, grp.Indices,
                        ResolveFxTex(archive, bgCatalog, textures, 1u), false);
                }

                break;
            }
        }
    }

    private static ImageTexture? ResolveFxTex(MappedVfsArchive archive, BgTextureCatalog? bgCatalog,
        (int Flag, int TexId)[] textures, uint texIndex1Based)
    {
        if (textures.Length == 0) return null;
        if (texIndex1Based == 0 || texIndex1Based > (uint)textures.Length) return null;
        var texId = textures[(int)texIndex1Based - 1].TexId;
        return ViewerTextures.ResolveBgSlot(archive, bgCatalog, texId).Texture;
    }

    private static void EmitVf36(Node3D root, string name, FxVertex36[] verts, ushort[] indices,
        ImageTexture? tex, bool isWater)
    {
        if (verts.Length == 0 || indices.Length == 0) return;
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

        Emit(root, name, positions, normals, uvs, indices, tex, isWater);
    }

    private static void EmitVf44(Node3D root, string name, FxVertex44[] verts, ushort[] indices,
        ImageTexture? tex, bool isWater)
    {
        if (verts.Length == 0 || indices.Length == 0) return;
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

        Emit(root, name, positions, normals, uvs, indices, tex, isWater);
    }

    private static void EmitVf32(Node3D root, string name, FxVertex32[] verts, ushort[] indices,
        ImageTexture? tex, bool isWater)
    {
        if (verts.Length == 0 || indices.Length == 0) return;
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

        Emit(root, name, positions, normals, uvs, indices, tex, isWater);
    }

    private static void Emit(Node3D root, string name, Vector3[] positions, Vector3[] normals,
        Vector2[] uvs, ushort[] indices, ImageTexture? tex, bool isWater)
    {
        var triCount = indices.Length / 3;
        var idx = new int[triCount * 3];
        for (var t = 0; t < triCount; t++)
        {
            var src = t * 3;
            idx[src + 0] = indices[src + 0];
            idx[src + 1] = indices[src + 2];
            idx[src + 2] = indices[src + 1];
        }

        var arrays = new Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = idx;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, BuildFxMaterial(tex, isWater));

        root.AddChild(new MeshInstance3D { Name = name, Mesh = mesh });
    }

    private static Material BuildFxMaterial(ImageTexture? tex, bool isWater)
    {
        if (tex is not null)
            return new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                AlbedoTexture = tex,
                AlbedoColor = new Color(2f, 2f, 2f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
            };

        return new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoColor = isWater ? new Color(0.2f, 0.4f, 0.7f, 0.45f) : new Color(0.7f, 0.7f, 0.4f, 0.45f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha
        };
    }
}