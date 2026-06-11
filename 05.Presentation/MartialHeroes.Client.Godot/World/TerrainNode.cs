using Godot;
using MartialHeroes.Assets.Mapping;
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
/// <see cref="TedTerrainParser"/>) → GLB bytes (via <see cref="TerrainGltfConverter"/>) →
/// Godot <see cref="ArrayMesh"/>. The mesh is built from the heightmap (POSITION) and diffuse
/// colour (COLOR_0) arrays; no formula, no gameplay authority.
///
/// Coordinate conventions:
///   The GLB produced by <see cref="TerrainGltfConverter"/> uses right-handed Y-up with X
///   negated (legacy left-handed D3D9 → glTF flip). The sector's world-space offset (derived
///   from mapX / mapZ) is applied as the node's local position so each sector mesh sits at the
///   correct position in the scene.
///
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
    // Sector mesh registry: (mapX, mapZ) → MeshInstance3D
    // -------------------------------------------------------------------------

    private readonly Dictionary<(int MapX, int MapZ), MeshInstance3D> _meshNodes = new();

    // -------------------------------------------------------------------------
    // Event handlers (called from GameLoop._Process, main thread)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reacts to a <see cref="SectorLoadedEvent"/>: parses the .ted bytes, converts to a Godot
    /// <see cref="ArrayMesh"/>, and adds a <see cref="MeshInstance3D"/> to the scene.
    /// If the payload is empty (cell not in area manifest), nothing is added.
    /// spec: Docs/RE/formats/terrain.md §5 (.ted blob); §1.4 (world-space bounds).
    /// </summary>
    public void OnSectorLoaded(SectorLoadedEvent evt)
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

        // Build the Godot ArrayMesh directly from the TerrainCell (no GLB round-trip needed
        // for the runtime mesh; TerrainGltfConverter is used for offline GLB export).
        // The ArrayMesh is built from POSITION (heights) and COLOR (diffuse) arrays.
        ArrayMesh? mesh = TryBuildMesh(cell);
        if (mesh is null)
        {
            return;
        }

        // Compute the sector's world-space origin and apply Godot coordinate conventions.
        // spec: Docs/RE/formats/terrain.md §1.4 — world-space bounds from (mapX, mapZ).
        // worldX_min = (mapX - 10000) × 1024.0  — CONFIRMED (spec §1.4).
        // worldZ_min = (mapZ - 10000) × 1024.0  — CONFIRMED (spec §1.4).
        // Godot convention: negate Z (legacy left-handed → right-handed).
        // spec: WorldCoordinates.ToGodot.
        float
            legacyX = (evt.MapX - 10000) *
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
    ///     X = c × 16.0 (columns → X axis — axis orientation UNVERIFIED, convention consistent with
    ///         TerrainGltfConverter).
    ///     Y = Heights[vi] (direct f32 world-space Y — scale UNVERIFIED per spec §5.3).
    ///     Z = r × 16.0 (rows → Z axis).
    ///   No handedness flip needed here: the mesh is local to the sector node; the node's position
    ///   carries the world-space offset with the Godot Z flip already applied.
    ///
    ///   COLOR: 4225 RGBA bytes from block 5 (diffuse colour map), interpreted as Color.
    ///   INDICES: 64×64×2×3 = 24576 indices, CCW winding (consistent with TerrainGltfConverter).
    ///
    /// spec: Docs/RE/formats/terrain.md §5.1 — "65×65 vertex grid, 64×64 quads, vertex spacing
    ///       16.0 world units": CONFIRMED.
    /// spec: Docs/RE/formats/terrain.md §5.2 Block 1 — heights f32le: CONFIRMED.
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
            var colours = new Color[vertCount];

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

                    // Diffuse colour from block 5. The parser now decodes block 5 into
                    // normalized (R,G,B,A) floats per vertex (on-disk u8 × 0.5 effective value).
                    // spec: Docs/RE/formats/terrain.md §5.2 Block 5 — CONFIRMED.
                    var d = cell.DiffuseColours[vi];
                    colours[vi] = new Color(d.R, d.G, d.B, d.A);
                }
            }

            // Build index array — CCW triangulation after handedness consideration.
            // The mesh is in local right-handed space (no global X-flip here since the
            // node offset handles the handedness via the negated Z origin).
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
            arrays[(int)Mesh.ArrayType.Color] = colours;
            arrays[(int)Mesh.ArrayType.Index] = indices;

            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            // Assign a simple vertex-colour material so the diffuse colours are visible.
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