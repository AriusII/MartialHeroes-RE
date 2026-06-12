using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Application.World;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// Manages the set of resident terrain sector meshes. Listens for <see cref="SectorLoadedEvent"/>
/// and <see cref="SectorUnloadedEvent"/> (forwarded by <see cref="GameLoop"/>) and adds/removes
/// <see cref="MeshInstance3D"/> nodes for each sector.
///
/// PASSIVE: zero game logic. Converts raw .ted bytes → <see cref="TerrainCell"/> (via
/// <see cref="TedTerrainParser"/>) → Godot <see cref="ArrayMesh"/>. The mesh is built from the
/// heightmap (POSITION), diffuse colour (COLOR_0), and normals (NORMAL) arrays. A real texture
/// is applied via the optional <see cref="TextureResolver"/> if available.
///
/// Texture support:
///   The .ted cell carries a Textures array with (Flag, TexId) pairs from the .map TEXTURES block.
///   spec: Docs/RE/formats/terrain.md §3.5 TEXTURES directive. When <see cref="TextureResolver"/> is
///   non-null, <see cref="OnSectorLoaded"/> calls it with TexId[0] to retrieve a Godot ImageTexture,
///   and applies it as the albedo on top of the vertex-colour material.
///
/// Coordinate conventions:
///   World-space origin bias:
///     worldX_min = (mapX - 10000) × 1024
///     worldZ_min = (mapZ - 10000) × 1024
///   spec: Docs/RE/formats/terrain.md §1.4 per-cell world-space bounds.
///
///   Godot Z convention (handedness flip): negate Z.
///   spec: WorldCoordinates.ToGodot — negate Z to convert legacy left-handed → Godot right-handed.
///
/// spec: Docs/RE/formats/terrain.md §5 (.ted geometry blob — 65×65 vertices, 64×64 quads).
/// spec: Docs/RE/formats/terrain.md §1.4 (per-cell world-space bounds).
/// </summary>
public sealed partial class TerrainNode : Node3D
{
    // -------------------------------------------------------------------------
    // Optional texture resolver (set by RealWorldRenderer when VFS is available)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Optional texture resolver: given a tex_id (from the .map TEXTURES block), returns a
    /// Godot <see cref="ImageTexture"/> or null. Called for the first tex_id of each sector.
    ///
    /// Set by <see cref="RealWorldRenderer"/> when real VFS assets are available.
    /// When null, the mesh renders with vertex colours only (offline / synthetic mode).
    ///
    /// spec: Docs/RE/formats/terrain.md §3.5 TEXTURES directive — tex_id indexed array.
    /// </summary>
    public Func<int, ImageTexture?>? TextureResolver { get; set; }

    // -------------------------------------------------------------------------
    // Sector mesh registry: (mapX, mapZ) → MeshInstance3D
    // -------------------------------------------------------------------------

    private readonly Dictionary<(int MapX, int MapZ), MeshInstance3D> _meshNodes = new();

    // -------------------------------------------------------------------------
    // Event handlers (called from GameLoop._Process, main thread)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reacts to a <see cref="SectorLoadedEvent"/>: parses the .ted bytes, converts to a Godot
    /// <see cref="ArrayMesh"/>, applies optional texture, and adds a <see cref="MeshInstance3D"/>.
    /// If the payload is empty (cell not in area manifest), nothing is added.
    /// spec: Docs/RE/formats/terrain.md §5 (.ted blob); §1.4 (world-space bounds).
    /// </summary>
    public void OnSectorLoaded(SectorLoadedEvent evt)
    {
        // The entire sector-load path is guarded: a bad payload or mesh build failure must not
        // crash the scene — we just skip this sector silently (with a log).
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
        {
            // Cell absent from the area manifest — no visual to add.
            // spec: Docs/RE/formats/terrain.md §1.2 (absent key → no data).
            return;
        }

        // Evict any stale node for this key (can happen on re-load after eviction).
        RemoveSectorMesh(evt.MapX, evt.MapZ);

        TerrainCell? cell = TryParseTed(evt.Payload, evt.MapX, evt.MapZ);
        if (cell is null)
        {
            return;
        }

        // Build the Godot ArrayMesh directly from the TerrainCell.
        ArrayMesh? mesh = TryBuildMesh(cell);
        if (mesh is null)
        {
            return;
        }

        // Apply terrain texture using TextureIndexGrid[0] as the tex_id for the first region.
        // TextureIndexGrid is a 16×16 grid of 1-based texture indices.
        // spec: Docs/RE/formats/terrain.md §5.6 Block 3 — "u8, 1-based, 16×16": CONFIRMED.
        // The TextureResolver maps these 1-based indices to real textures via the .map TEXTURES block.
        // NOTE: The TEXTURES block in the .map associates tex_id with texture paths; full cross-
        //       referencing requires the MapDescriptor (available only in RealWorldRenderer context).
        //       Here we pass the raw 1-based index and let RealWorldRenderer wire a resolver that
        //       can translate it. Default behaviour: vertex colours only (fallback if no resolver).
        if (TextureResolver is not null && cell.TextureIndexGrid.Length > 0)
        {
            try
            {
                int texIdx = cell.TextureIndexGrid[0]; // 1-based index into the .map TEXTURES array
                ImageTexture? tex = TextureResolver(texIdx);
                if (tex is not null)
                {
                    // Override the surface material with a real texture.
                    // spec: Docs/RE/formats/terrain.md §5.6 Block 3 — tex_id[0] = dominant texture region.
                    var texMat = new StandardMaterial3D();
                    texMat.AlbedoTexture = tex;
                    texMat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps;
                    texMat.Uv1Scale = new Vector3(4f, 4f, 4f); // tiling scale for terrain texture
                    mesh.SurfaceSetMaterial(0, texMat);
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[TerrainNode] TextureResolver failed for ({evt.MapX},{evt.MapZ}): {ex.Message}");
                // Continue: the sector will render with vertex colours only.
            }
        }

        // Compute the sector's world-space origin and apply Godot coordinate conventions.
        // spec: Docs/RE/formats/terrain.md §1.4 — world-space bounds from (mapX, mapZ).
        // worldX_min = (mapX - 10000) × 1024.0  — CONFIRMED (spec §1.4).
        // worldZ_min = (mapZ - 10000) × 1024.0  — CONFIRMED (spec §1.4).
        // Godot convention: negate Z (legacy left-handed → right-handed).
        // spec: WorldCoordinates.ToGodot.
        float legacyX = (evt.MapX - 10000) *
                        1024.0f; // spec: terrain.md §1.4, origin bias 10000, cell size 1024. CONFIRMED.
        float legacyZ = (evt.MapZ - 10000) * 1024.0f; // spec: terrain.md §1.4. CONFIRMED.
        float godotX = legacyX;
        float godotZ = -legacyZ; // spec: WorldCoordinates.ToGodot — negate Z.

        var meshInst = new MeshInstance3D();
        meshInst.Mesh = mesh;
        meshInst.Position = new Vector3(godotX, 0f, godotZ);
        meshInst.Name = $"Sector_{evt.MapX}_{evt.MapZ}";

        AddChild(meshInst);
        _meshNodes[(evt.MapX, evt.MapZ)] = meshInst;

        GD.Print($"[TerrainNode] Sector ({evt.MapX},{evt.MapZ}) loaded at Godot ({godotX:F0}, 0, {godotZ:F0}).");
    }

    /// <summary>
    /// Reacts to a <see cref="SectorUnloadedEvent"/>: removes the mesh node for the evicted sector.
    /// spec: Docs/RE/formats/terrain.md §9.3 (eviction: Chebyshev distance > 2).
    /// </summary>
    public void OnSectorUnloaded(SectorUnloadedEvent evt)
    {
        RemoveSectorMesh(evt.MapX, evt.MapZ);
        GD.Print($"[TerrainNode] Sector ({evt.MapX},{evt.MapZ}) unloaded.");
    }

    // -------------------------------------------------------------------------
    // Mesh construction
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parses the raw .ted bytes into a <see cref="TerrainCell"/>. Returns null on parse failure
    /// (too short, corrupt) so the sector is silently skipped without crashing the scene.
    /// spec: Docs/RE/formats/terrain.md §5.2 (block layout — total 46987 bytes: CONFIRMED).
    /// </summary>
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

    /// <summary>
    /// Builds a Godot <see cref="ArrayMesh"/> from a parsed <see cref="TerrainCell"/>.
    ///
    /// Mesh layout:
    ///   POSITION: 65×65 = 4225 vertices, local-space XYZ.
    ///     X = c × 16.0 (columns → X axis).
    ///     Y = Heights[vi] (direct f32 world-space Y — scale UNVERIFIED per spec §5.3).
    ///     Z = r × 16.0 (rows → Z axis).
    ///   NORMAL: decoded from Block 2 (i8/127.0). Used for lighting.
    ///     spec: Docs/RE/formats/terrain.md §5.5 Block 2 — R=Nx, G=Ny, B=Nz: CONFIRMED.
    ///   COLOR: 4225 RGBA bytes from block 5 (diffuse colour map), interpreted as Color.
    ///   INDICES: 64×64×2×3 = 24576 indices, CCW winding.
    ///
    /// spec: Docs/RE/formats/terrain.md §5.1 — "65×65 vertex grid, 64×64 quads, vertex spacing
    ///       16.0 world units": CONFIRMED.
    /// spec: Docs/RE/formats/terrain.md §5.2 Block 1 — heights f32le: CONFIRMED.
    /// spec: Docs/RE/formats/terrain.md §5.5 Block 2 — normals i8/127.0: CONFIRMED.
    /// spec: Docs/RE/formats/terrain.md §5.2 Block 5 — diffuse RGBA u8×4: CONFIRMED.
    /// </summary>
    private static ArrayMesh? TryBuildMesh(TerrainCell cell)
    {
        try
        {
            // Grid constants.
            // spec: Docs/RE/formats/terrain.md §5.1 — "65×65 vertices, vertex spacing 16.0": CONFIRMED.
            const int gridSize = TerrainCell.GridSize; // 65
            const int quadSize = gridSize - 1; // 64
            const float spacing = 16.0f; // spec: terrain.md §5.1 — 1024/64 = 16.0. CONFIRMED.
            const int vertCount = TerrainCell.VertexCount; // 4225
            const int indexCount = quadSize * quadSize * 2 * 3; // 24576

            var positions = new Vector3[vertCount];
            var normals = new Vector3[vertCount];
            var colours = new Color[vertCount];
            var uvs = new Vector2[vertCount];

            // spec: Docs/RE/formats/terrain.md §5.5 Block 2 — "VertexNormals" is "Normals" in TerrainCell.
            bool hasNormals = cell.Normals is { Length: >= vertCount };

            // Build vertex arrays.
            for (int r = 0; r < gridSize; r++)
            {
                for (int c = 0; c < gridSize; c++)
                {
                    int vi = r * gridSize + c;

                    // Local-space position within the sector mesh.
                    // col → X, height → Y (direct f32 — scale UNVERIFIED per spec §5.3),
                    // row → Z. Axis orientation convention matches TerrainGltfConverter.
                    positions[vi] = new Vector3(
                        c * spacing,
                        cell.Heights[vi],
                        r * spacing);

                    // UV for texture mapping: normalized (0..1) over the cell.
                    // spec: Docs/RE/formats/terrain.md §5.1 — vertex spacing 16.0, cell 1024×1024.
                    uvs[vi] = new Vector2((float)c / (gridSize - 1), (float)r / (gridSize - 1));

                    // Normals from Block 2 (if available).
                    // spec: Docs/RE/formats/terrain.md §5.5 Block 2 — Nx=R, Ny=G, Nz=B decoded i8/127.0. CONFIRMED.
                    if (hasNormals)
                    {
                        var n = cell.Normals[vi];
                        normals[vi] = new Vector3(n.Nx, n.Ny, n.Nz).Normalized();
                    }
                    else
                    {
                        normals[vi] = Vector3.Up; // fallback flat normal
                    }

                    // Diffuse colour from block 5.
                    // spec: Docs/RE/formats/terrain.md §5.2 Block 5 — CONFIRMED.
                    var d = cell.DiffuseColours[vi];
                    colours[vi] = new Color(d.R, d.G, d.B, d.A);
                }
            }

            // Build index array — CCW triangulation.
            // Winding convention consistent with TerrainGltfConverter (CCW).
            // spec: Docs/RE/formats/terrain.md §5.1 — 64×64 quads. CONFIRMED.
            var indices = new int[indexCount];
            int idx = 0;
            for (int r = 0; r < quadSize; r++)
            {
                for (int c = 0; c < quadSize; c++)
                {
                    int vi0 = r * gridSize + c; // TL
                    int vi1 = r * gridSize + c + 1; // TR
                    int vi2 = (r + 1) * gridSize + c; // BL
                    int vi3 = (r + 1) * gridSize + c + 1; // BR

                    // CCW triangles (matches TerrainGltfConverter winding-corrected output).
                    indices[idx++] = vi0; // tri 0: TL, BR, TR
                    indices[idx++] = vi3;
                    indices[idx++] = vi1;

                    indices[idx++] = vi0; // tri 1: TL, BL, BR
                    indices[idx++] = vi2;
                    indices[idx++] = vi3;
                }
            }

            // Assemble the ArrayMesh.
            var arrays = new global::Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = positions;
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.Color] = colours;
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
            arrays[(int)Mesh.ArrayType.Index] = indices;

            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            // Default material: vertex-colour unshaded (for offline/no-texture mode).
            // If a texture is available, OnSectorLoaded will override this material.
            var mat = new StandardMaterial3D();
            mat.VertexColorUseAsAlbedo = true;
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            mesh.SurfaceSetMaterial(0, mat);

            return mesh;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[TerrainNode] Mesh build failed: {ex.Message}");
            return null;
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void RemoveSectorMesh(int mapX, int mapZ)
    {
        var key = (mapX, mapZ);
        if (_meshNodes.Remove(key, out MeshInstance3D? node))
        {
            node.QueueFree();
        }
    }
}
