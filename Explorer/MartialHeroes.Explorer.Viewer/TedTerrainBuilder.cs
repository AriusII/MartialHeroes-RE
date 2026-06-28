using Godot;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Terrain;
using MartialHeroes.Assets.Parsers.Terrain.Models;
using MartialHeroes.Assets.Vfs;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Explorer.Viewer;

public static class TedTerrainBuilder
{
    private const int GridSize = TerrainCell.GridSize;
    private const float Spacing = 16f;
    private const float UvTiling = 4f;

    private static ImageTexture? _sharedWhiteTex;
    private static Shader? _sharedMultiTexShader;

    private static ImageTexture GetOrCreateSharedWhiteTex()
    {
        if (_sharedWhiteTex is not null && _sharedWhiteTex.GetRid().IsValid)
            return _sharedWhiteTex;
        var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        img.Fill(Colors.White);
        _sharedWhiteTex = ImageTexture.CreateFromImage(img);
        return _sharedWhiteTex;
    }

    private static Shader GetOrCreateMultiTexShader(string glslCode)
    {
        if (_sharedMultiTexShader is not null && _sharedMultiTexShader.GetRid().IsValid)
            return _sharedMultiTexShader;
        _sharedMultiTexShader = new Shader { Code = glslCode };
        return _sharedMultiTexShader;
    }

    public static Node3D Build(TerrainCell c)
    {
        return Build(c, null, null, string.Empty);
    }

    public static Node3D Build(TerrainCell c, MappedVfsArchive? archive, BgTextureCatalog? bgCatalog,
        string tedVfsPath)
    {
        var mesh = BuildMesh(c, archive, bgCatalog, tedVfsPath, out var minH, out var maxH);
        var cellHalf = (GridSize - 1) * Spacing * 0.5f;

        var meshNode = new MeshInstance3D { Name = "TerrainMesh", Mesh = mesh };
        var root = new Node3D
        {
            Name = "TerrainNode",
            Position = new Vector3(-cellHalf, -minH, cellHalf)
        };
        root.AddChild(meshNode);

        GD.Print($"[Terrain] world-space Y: raw=[{minH:F1},{maxH:F1}] -> after grounding: Y=[0.0,{maxH - minH:F1}]");
        GD.Print(
            $"[Terrain] AABB after grounding: X=[{-cellHalf:F1},{cellHalf:F1}] Y=[0.0,{maxH - minH:F1}] Z=[{-cellHalf:F1},{cellHalf:F1}]");
        return root;
    }

    public static Node3D BuildWorld(TerrainCell c, MappedVfsArchive archive, BgTextureCatalog? bgCatalog,
        string tedVfsPath, Vector3 cellOriginGodot)
    {
        var mesh = BuildMesh(c, archive, bgCatalog, tedVfsPath, out _, out _);
        var meshNode = new MeshInstance3D { Name = "TerrainMesh", Mesh = mesh };
        var root = new Node3D { Name = "TerrainCell", Position = cellOriginGodot };
        root.AddChild(meshNode);
        return root;
    }

    private static ArrayMesh BuildMesh(TerrainCell c, MappedVfsArchive? archive, BgTextureCatalog? bgCatalog,
        string tedVfsPath, out float minH, out float maxH)
    {
        var vertCount = GridSize * GridSize;
        var positions = new Vector3[vertCount];
        var normals = new Vector3[vertCount];
        var colors = new Color[vertCount];
        var uvs = new Vector2[vertCount];

        for (var row = 0; row < GridSize; row++)
        for (var col = 0; col < GridSize; col++)
        {
            var i = row * GridSize + col;
            positions[i] = new Vector3(col * Spacing, c.Heights[i], -(row * Spacing));

            var nrm = c.Normals[i];
            normals[i] = new Vector3(nrm.Nx, nrm.Ny, -nrm.Nz).Normalized();

            colors[i] = i < c.DiffuseColours.Length
                ? new Color(c.DiffuseColours[i].R, c.DiffuseColours[i].G, c.DiffuseColours[i].B, c.DiffuseColours[i].A)
                : new Color(0.55f, 0.65f, 0.45f);

            var u = (float)col / (GridSize - 1) * UvTiling;
            var v = (float)row / (GridSize - 1) * UvTiling;
            uvs[i] = new Vector2(u, v);
        }

        var quads = GridSize - 1;
        var indices = new int[quads * quads * 6];
        var idx = 0;
        for (var row = 0; row < quads; row++)
        for (var col = 0; col < quads; col++)
        {
            var tl = row * GridSize + col;
            var tr = tl + 1;
            var bl = (row + 1) * GridSize + col;
            var br = bl + 1;

            indices[idx++] = tl;
            indices[idx++] = tr;
            indices[idx++] = bl;

            indices[idx++] = tr;
            indices[idx++] = br;
            indices[idx++] = bl;
        }

        var mat = BuildMaterial(c, archive, bgCatalog, tedVfsPath, ref colors);

        var arrays = new Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, mat);

        minH = c.Heights[0];
        maxH = c.Heights[0];
        foreach (var h in c.Heights)
        {
            if (h < minH) minH = h;
            if (h > maxH) maxH = h;
        }

        return mesh;
    }

    private static Material BuildMaterial(TerrainCell c, MappedVfsArchive? archive,
        BgTextureCatalog? bgCatalog, string tedVfsPath, ref Color[] colors)
    {
        if (archive is null || bgCatalog is null || tedVfsPath.Length == 0)
            return FallbackMaterial();

        var mapPath = FindMapPath(archive, tedVfsPath);
        if (mapPath is null)
        {
            GD.Print($"[Terrain] no .map found for {tedVfsPath} — vertex-colour only.");
            return FallbackMaterial();
        }

        MapDescriptor descriptor;
        try
        {
            var mapBytes = archive.GetFileContent(mapPath);
            descriptor = MapDescriptorParser.Parse(mapBytes);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Terrain] failed to parse {mapPath}: {ex.Message}");
            return FallbackMaterial();
        }

        MapSection? terrainSection = null;
        foreach (var sec in descriptor.Sections)
            if (sec.Keyword.Equals("TERRAIN", StringComparison.OrdinalIgnoreCase))
            {
                terrainSection = sec;
                break;
            }

        if (terrainSection is null || terrainSection.Textures.Length == 0)
        {
            GD.Print($"[Terrain] {mapPath} has no TERRAIN textures — vertex-colour only.");
            return FallbackMaterial();
        }

        var distinctIndices = new List<byte>();
        foreach (var b in c.TextureIndexGrid)
        {
            if (b == 0 || b > terrainSection.Textures.Length) continue;
            if (!distinctIndices.Contains(b)) distinctIndices.Add(b);
            if (distinctIndices.Count == 4) break;
        }

        if (distinctIndices.Count == 0)
        {
            GD.Print("[Terrain] no valid texture indices in grid — vertex-colour only.");
            return FallbackMaterial();
        }

        if (distinctIndices.Count == 1)
        {
            var singleTexId = terrainSection.Textures[distinctIndices[0] - 1].TexId;
            var singleResolved = ViewerTextures.ResolveBgSlot(archive, bgCatalog, singleTexId);
            if (singleResolved.Texture is null)
            {
                GD.Print($"[Terrain] texId={singleTexId} unresolved ({singleResolved.Note}) — vertex-colour only.");
                return FallbackMaterial();
            }

            GD.Print(
                $"[Terrain] single-texture: map={mapPath}, byte={distinctIndices[0]}, texId={singleTexId}, path={singleResolved.Path}");
            return new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                VertexColorUseAsAlbedo = true,
                AlbedoColor = new Color(2f, 2f, 2f),
                AlbedoTexture = singleResolved.Texture,
                Roughness = 0.95f,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
            };
        }

        var slotTextures = new ImageTexture?[4];
        var loadedCount = 0;
        for (var si = 0; si < distinctIndices.Count && si < 4; si++)
        {
            var texId = terrainSection.Textures[distinctIndices[si] - 1].TexId;
            var resolved = ViewerTextures.ResolveBgSlot(archive, bgCatalog, texId);
            if (resolved.Texture is not null)
            {
                slotTextures[si] = resolved.Texture;
                loadedCount++;
                GD.Print(
                    $"[Terrain] multi-texture slot {si}: byte={distinctIndices[si]}, texId={texId}, path={resolved.Path}");
            }
            else
            {
                GD.Print($"[Terrain] multi-texture slot {si}: texId={texId} unresolved ({resolved.Note})");
            }
        }

        if (loadedCount == 0)
        {
            GD.Print("[Terrain] multi-texture: no textures resolved — vertex-colour only.");
            return FallbackMaterial();
        }

        var whiteTex = GetOrCreateSharedWhiteTex();

        for (var vi = 0; vi < GridSize * GridSize; vi++)
        {
            var gridByte = vi < c.TextureIndexGrid.Length ? c.TextureIndexGrid[vi] : (byte)0;
            float w0 = 0f, w1 = 0f, w2 = 0f, w3 = 0f;
            for (var di = 0; di < distinctIndices.Count && di < 4; di++)
            {
                if (gridByte != distinctIndices[di]) continue;
                if (di == 0) w0 = 1f;
                else if (di == 1) w1 = 1f;
                else if (di == 2) w2 = 1f;
                else w3 = 1f;
                break;
            }

            if (w0 + w1 + w2 + w3 < 0.5f) w0 = 1f;
            colors[vi] = new Color(w0, w1, w2, w3);
        }

        var shaderCode = @"shader_type spatial;
render_mode cull_disabled;
uniform sampler2D texture_0 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D texture_1 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D texture_2 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D texture_3 : source_color, filter_linear_mipmap, repeat_enable;
void fragment() {
    vec4 t0 = texture(texture_0, UV);
    vec4 t1 = texture(texture_1, UV);
    vec4 t2 = texture(texture_2, UV);
    vec4 t3 = texture(texture_3, UV);
    ALBEDO = 2.0 * (t0.rgb * COLOR.r + t1.rgb * COLOR.g + t2.rgb * COLOR.b + t3.rgb * COLOR.a);
    ROUGHNESS = 0.95;
    SPECULAR = 0.02;
}";
        var shader = GetOrCreateMultiTexShader(shaderCode);
        var shaderMat = new ShaderMaterial { Shader = shader };
        shaderMat.SetShaderParameter("texture_0", (Variant)(slotTextures[0] ?? whiteTex));
        shaderMat.SetShaderParameter("texture_1", (Variant)(slotTextures[1] ?? whiteTex));
        shaderMat.SetShaderParameter("texture_2", (Variant)(slotTextures[2] ?? whiteTex));
        shaderMat.SetShaderParameter("texture_3", (Variant)(slotTextures[3] ?? whiteTex));

        GD.Print(
            $"[Terrain] multi-texture shader active: map={mapPath}, distinct={distinctIndices.Count}, loaded={loadedCount}");
        return shaderMat;
    }

    private static StandardMaterial3D FallbackMaterial()
    {
        return new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            VertexColorUseAsAlbedo = true,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
        };
    }

    private static string? FindMapPath(MappedVfsArchive? archive, string tedVfsPath)
    {
        if (archive is null) return null;
        var normalised = tedVfsPath.Replace('\\', '/');
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