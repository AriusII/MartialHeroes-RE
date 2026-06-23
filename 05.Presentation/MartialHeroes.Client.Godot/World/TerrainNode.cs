using Godot;
using MartialHeroes.Assets.Parsers.Terrain;
using MartialHeroes.Assets.Parsers.Terrain.Models;
using MartialHeroes.Client.Application.World;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class TerrainNode : Node3D
{
    private const int PatchGrid = 16;

    private const int QuadsPerPatch = 4;

    private const int VertsPerPatch = QuadsPerPatch + 1;


    private readonly Dictionary<(int MapX, int MapZ), TerrainCell> _cellCache = new();


    private readonly Dictionary<(int MapX, int MapZ), MeshInstance3D> _meshNodes = new();


    public Func<int, ImageTexture?>? TextureResolver { get; set; }


    public event Action<int, int>? SectorBecameResident;


    public void OnSectorLoaded(SectorLoadedEvent evt)
    {
        try
        {
            OnSectorLoadedInternal(evt);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[TerrainNode] OnSectorLoaded error for ({evt.MapX},{evt.MapZ}): {ex.Message}");
        }
    }

    private void OnSectorLoadedInternal(SectorLoadedEvent evt)
    {
        if (evt.Payload.IsEmpty)
            return;

        RemoveSectorMesh(evt.MapX, evt.MapZ);

        var cell = TryParseTed(evt.Payload, evt.MapX, evt.MapZ);
        if (cell is null) return;

        _cellCache[(evt.MapX, evt.MapZ)] = cell;

        var mesh = TryBuildMultiSurfaceMesh(cell, TextureResolver);
        if (mesh is null) return;

        var legacyX = (evt.MapX - 10000) *
                      1024.0f;
        var legacyZ = (evt.MapZ - 10000) * 1024.0f;
        var godotX = legacyX;
        var godotZ = -legacyZ;

        var meshInst = new MeshInstance3D();
        meshInst.Mesh = mesh;
        meshInst.Position = new Vector3(godotX, 0f, godotZ);
        meshInst.Name = $"Sector_{evt.MapX}_{evt.MapZ}";

        AddChild(meshInst);
        _meshNodes[(evt.MapX, evt.MapZ)] = meshInst;

        GD.Print(
            $"[TerrainNode] Sector ({evt.MapX},{evt.MapZ}) loaded at Godot ({godotX:F0}, 0, {godotZ:F0}), {mesh.GetSurfaceCount()} surface(s).");

        SectorBecameResident?.Invoke(evt.MapX, evt.MapZ);
    }

    public void OnSectorUnloaded(SectorUnloadedEvent evt)
    {
        RemoveSectorMesh(evt.MapX, evt.MapZ);
        GD.Print($"[TerrainNode] Sector ({evt.MapX},{evt.MapZ}) unloaded.");
    }


    private static TerrainCell? TryParseTed(ReadOnlyMemory<byte> payload, int mapX, int mapZ)
    {
        try
        {
            return TedTerrainParser.Parse(payload);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[TerrainNode] Failed to parse .ted for sector ({mapX},{mapZ}): {ex.Message}");
            return null;
        }
    }

    private static ArrayMesh? TryBuildMultiSurfaceMesh(
        TerrainCell cell,
        Func<int, ImageTexture?>? textureResolver)
    {
        try
        {
            return BuildMultiSurfaceMesh(cell, textureResolver);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[TerrainNode] Multi-surface mesh build failed: {ex.Message}");
            return null;
        }
    }

    private static ArrayMesh BuildMultiSurfaceMesh(
        TerrainCell cell,
        Func<int, ImageTexture?>? textureResolver)
    {
        const int gridSize = TerrainCell.GridSize;
        const float spacing = 16.0f;
        const int patchGrid = PatchGrid;
        const int quadsPerPatch = QuadsPerPatch;
        const int vertsPerPatch = VertsPerPatch;
        const int patchVertCount = vertsPerPatch * vertsPerPatch;
        const int patchQuadCount = quadsPerPatch * quadsPerPatch;
        const int patchIndexCount = patchQuadCount * 2 * 3;

        var hasNormals = cell.Normals is { Length: >= TerrainCell.VertexCount };


        var byteToPatches = new Dictionary<byte, List<(int Pr, int Pc)>>();
        if (cell.TextureIndexGrid.Length >= patchGrid * patchGrid)
        {
            for (var pr = 0; pr < patchGrid; pr++)
            for (var pc = 0; pc < patchGrid; pc++)
            {
                var texByte = cell.TextureIndexGrid[pr * patchGrid + pc];
                if (!byteToPatches.TryGetValue(texByte, out var list))
                {
                    list = new List<(int, int)>();
                    byteToPatches[texByte] = list;
                }

                list.Add((pr, pc));
            }
        }
        else
        {
            var allPatches = new List<(int, int)>(patchGrid * patchGrid);
            for (var pr = 0; pr < patchGrid; pr++)
            for (var pc = 0; pc < patchGrid; pc++)
                allPatches.Add((pr, pc));
            byteToPatches[0] = allPatches;
        }

        var hasFlags = cell.DirectionFlags.Length >= patchGrid * patchGrid;

        var mesh = new ArrayMesh();

        foreach (var (texByte, patches) in byteToPatches)
        {
            var surfaceVertCount = patches.Count * patchVertCount;
            var surfaceIndexCount = patches.Count * patchIndexCount;

            var positions = new Vector3[surfaceVertCount];
            var normals = new Vector3[surfaceVertCount];
            var colours = new Color[surfaceVertCount];
            var uvs = new Vector2[surfaceVertCount];
            var indices = new int[surfaceIndexCount];

            var vBase = 0;
            var iBase = 0;

            foreach (var (pr, pc) in patches)
            {
                var dirFlag = hasFlags ? cell.DirectionFlags[pr * patchGrid + pc] : (byte)0;
                var flipU = (dirFlag & 0x01) != 0;
                var flipV = (dirFlag & 0x02) != 0;

                for (var lr = 0; lr < vertsPerPatch; lr++)
                {
                    var r = pr * quadsPerPatch + lr;
                    for (var lc = 0; lc < vertsPerPatch; lc++)
                    {
                        var c = pc * quadsPerPatch + lc;
                        var vi = r * gridSize + c;

                        positions[vBase + lr * vertsPerPatch + lc] = new Vector3(
                            c * spacing,
                            cell.Heights[vi],
                            -(r * spacing));

                        var s = lc * 0.25f;
                        var t = lr * 0.25f;
                        if (flipU) s = 1.0f - s;
                        if (flipV) t = 1.0f - t;
                        uvs[vBase + lr * vertsPerPatch + lc] = new Vector2(s, t);

                        if (hasNormals)
                        {
                            var n = cell.Normals[vi];
                            normals[vBase + lr * vertsPerPatch + lc] = new Vector3(n.Nx, n.Ny, -n.Nz).Normalized();
                        }
                        else
                        {
                            normals[vBase + lr * vertsPerPatch + lc] = Vector3.Up;
                        }

                        var d = cell.DiffuseColours[vi];
                        colours[vBase + lr * vertsPerPatch + lc] = new Color(d.R, d.G, d.B, d.A);
                    }
                }

                for (var lr = 0; lr < quadsPerPatch; lr++)
                for (var lc = 0; lc < quadsPerPatch; lc++)
                {
                    var vi0 = vBase + lr * vertsPerPatch + lc;
                    var vi1 = vBase + lr * vertsPerPatch + lc + 1;
                    var vi2 = vBase + (lr + 1) * vertsPerPatch + lc;
                    var vi3 = vBase + (lr + 1) * vertsPerPatch + lc + 1;

                    indices[iBase++] = vi0;
                    indices[iBase++] = vi1;
                    indices[iBase++] = vi3;

                    indices[iBase++] = vi0;
                    indices[iBase++] = vi3;
                    indices[iBase++] = vi2;
                }

                vBase += patchVertCount;
            }

            var arrays = new Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = positions;
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.Color] = colours;
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
            arrays[(int)Mesh.ArrayType.Index] = indices;

            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            var surfaceIdx = mesh.GetSurfaceCount() - 1;
            mesh.SurfaceSetMaterial(surfaceIdx, BuildSurfaceMaterial(texByte, textureResolver));
        }

        return mesh;
    }

    private static StandardMaterial3D BuildSurfaceMaterial(
        byte texByte,
        Func<int, ImageTexture?>? textureResolver)
    {
        var mat = new StandardMaterial3D();
        mat.VertexColorUseAsAlbedo = true;
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps;

        if (textureResolver is not null)
        {
            var clampedByte = texByte < 1 ? 1 : texByte;
            try
            {
                var tex = textureResolver(clampedByte);
                if (tex is not null)
                {
                    mat.AlbedoTexture = tex;
                    mat.TextureRepeat = true;
                    return mat;
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr(
                    $"[TerrainNode] TextureResolver failed for texByte={texByte} (clamped={clampedByte}): {ex.Message}");
            }
        }

        return mat;
    }


    private void RemoveSectorMesh(int mapX, int mapZ)
    {
        var key = (mapX, mapZ);
        if (_meshNodes.Remove(key, out var node)) node.QueueFree();

        _cellCache.Remove(key);
    }


    public float GetGroundHeight(float worldX, float worldZ, float fallbackY = 0f)
    {
        TryGetGroundHeight(worldX, worldZ, out var result, fallbackY);
        return result;
    }

    public bool TryGetGroundHeight(float worldX, float worldZ, out float height, float fallbackY = 0f)
    {
        var cellMapX = (int)Math.Floor(worldX / 1024.0) + 10000;
        var cellMapZ = (int)Math.Floor(worldZ / 1024.0) + 10000;

        if (!_cellCache.TryGetValue((cellMapX, cellMapZ), out var cell))
        {
            height = fallbackY;
            return false;
        }

        const float spacing = 16.0f;
        var lx = (worldX - (cellMapX - 10000) * 1024.0f) / spacing;
        var lz = (worldZ - (cellMapZ - 10000) * 1024.0f) / spacing;

        var c0 = Math.Clamp((int)Math.Floor(lx), 0, TerrainCell.GridSize - 2);
        var r0 = Math.Clamp((int)Math.Floor(lz), 0, TerrainCell.GridSize - 2);
        var fx = lx - c0;
        var fz = lz - r0;

        const int gs = TerrainCell.GridSize;
        var h = cell.Heights;
        var h00 = h[r0 * gs + c0];
        var h01 = h[r0 * gs + c0 + 1];
        var h10 = h[(r0 + 1) * gs + c0];
        var h11 = h[(r0 + 1) * gs + c0 + 1];

        if (fz <= fx)
            height = h00 + (h01 - h00) * fx + (h11 - h01) * fz;
        else
            height = h00 + (h10 - h00) * fz + (h11 - h10) * fx;

        return true;
    }
}