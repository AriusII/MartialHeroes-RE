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
/// <see cref="TedTerrainParser"/>) → Godot <see cref="ArrayMesh"/>. The mesh is built as a
/// multi-surface ArrayMesh: one surface per distinct texture byte value in TextureIndexGrid,
/// so every 4×4-quad patch shows its correct ground texture.
///
/// Texture support (multi-surface, per-patch):
///   The .ted cell carries a 16×16 TextureIndexGrid (Block 3) where each byte is a 1-based index
///   into the per-cell TEXTURES list, and a 16×16 DirectionFlags (Block 4) carrying per-patch
///   UV mirror bits. Patches are grouped by texture byte; one ArrayMesh surface is emitted per
///   distinct byte. Each surface receives its own StandardMaterial3D with the resolved texture.
///   spec: Docs/RE/formats/terrain.md §5.6 Block 3 — "u8, 1-based, 16×16": CONFIRMED.
///   spec: Docs/RE/formats/terrain.md §5.7 Block 4 — bit 0x01=U-flip, bit 0x02=V-flip: CONFIRMED.
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
    // Patch grid constants
    // spec: terrain.md §5.6 — "16×16 grid; 4×4 quads per patch axis; 5×5 vertices per patch": CONFIRMED.
    // -------------------------------------------------------------------------

    /// <summary>Number of patches per axis in the texture index grid.</summary>
    private const int PatchGrid = 16; // spec: terrain.md §5.6 — 16×16: CONFIRMED.

    /// <summary>Number of quads per patch axis (64 / 16 = 4).</summary>
    private const int QuadsPerPatch = 4; // spec: terrain.md §5.6 — "64/16 = 4 quads per axis per entry": CONFIRMED.

    /// <summary>Number of vertices per patch axis (4 quads + 1 = 5).</summary>
    private const int VertsPerPatch = QuadsPerPatch + 1; // 5

    // -------------------------------------------------------------------------
    // Optional texture resolver (set by RealWorldRenderer when VFS is available)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Optional texture resolver: given a tex_id (from the .map TEXTURES block), returns a
    /// Godot <see cref="ImageTexture"/> or null. Called once per distinct texture byte value.
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
    // Height-lookup cache: (mapX, mapZ) → resident TerrainCell
    //
    // Populated in OnSectorLoadedInternal, cleared in RemoveSectorMesh.
    // Only the Heights[] array is needed for the ground-height query, but we
    // cache the full TerrainCell so callers can access other fields if needed.
    // spec: Docs/RE/formats/terrain.md §5.4 (Block 1 — direct world-space Y values).
    // spec: Docs/RE/formats/terrain.md §1.4 (per-cell world-space bounds).
    // -------------------------------------------------------------------------

    private readonly Dictionary<(int MapX, int MapZ), TerrainCell> _cellCache = new();

    // -------------------------------------------------------------------------
    // Event handlers (called from GameLoop._Process, main thread)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reacts to a <see cref="SectorLoadedEvent"/>: parses the .ted bytes, converts to a Godot
    /// multi-surface <see cref="ArrayMesh"/>, and adds a <see cref="MeshInstance3D"/>.
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

        // Cache the cell for ground-height queries before building the mesh.
        // spec: Docs/RE/formats/terrain.md §5.4 — Heights[] are direct world-space Y values.
        _cellCache[(evt.MapX, evt.MapZ)] = cell;

        // Build the Godot multi-surface ArrayMesh directly from the TerrainCell.
        ArrayMesh? mesh = TryBuildMultiSurfaceMesh(cell, TextureResolver);
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

        GD.Print(
            $"[TerrainNode] Sector ({evt.MapX},{evt.MapZ}) loaded at Godot ({godotX:F0}, 0, {godotZ:F0}), {mesh.GetSurfaceCount()} surface(s).");
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
    /// Builds a multi-surface Godot <see cref="ArrayMesh"/> from a parsed <see cref="TerrainCell"/>.
    ///
    /// Surface grouping strategy:
    ///   Patches (16×16 grid entries from TextureIndexGrid) are grouped by their raw texture byte
    ///   value. All patches sharing the same byte value contribute geometry to a single surface,
    ///   so a cell with N distinct byte values produces N surfaces. This avoids a material switch
    ///   per patch while keeping the UV/flip logic correct per patch.
    ///
    /// Per-patch vertex layout:
    ///   Each patch owns a 5×5 local vertex grid (VertsPerPatch=5) covering its 4×4 quads. A patch
    ///   at (pr, pc) spans global grid rows [pr*4 .. pr*4+4] and columns [pc*4 .. pc*4+4].
    ///   spec: terrain.md §5.6 — "4×4 quads per patch axis, 5×5 vertices per patch": CONFIRMED.
    ///
    ///   The last patch column (pc=15) reaches global col 64 (pc*4+4 = 64), which is valid since
    ///   the grid is 65×65 (cols 0..64). Likewise for patch row 15 reaching global row 64. There
    ///   is no boundary clip needed: the global grid index r*65+c with r,c ≤ 64 is always valid.
    ///
    /// UV per patch:
    ///   Local vertex offsets within a patch run 0..4 on each axis. UV fraction = offset × 0.25,
    ///   giving the five steps 0.0, 0.25, 0.5, 0.75, 1.0 over the patch's [0,1] UV square.
    ///   spec: terrain.md §5.7 — "f = vertex_offset × 0.25 (0.0, 0.25, 0.5, 0.75, 1.0)": CONFIRMED.
    ///
    /// DirectionFlags UV mirror:
    ///   For each patch, DirectionFlags[pr*16+pc] is read; bit 0x01 = flip U (S-mirror),
    ///   bit 0x02 = flip V (T-mirror). When set, the corresponding UV component becomes 1 − f.
    ///   spec: terrain.md §5.7 — "bit 0x01 s_flip, bit 0x02 t_flip": CONFIRMED.
    ///   The global Uv1Scale hack is dropped; UV is patch-local 0..1 with Repeat addressing.
    ///
    /// Positions / Normals / Colours:
    ///   Reuse the same formula as the previous single-surface builder. Each patch vertex
    ///   (pr*4+lr, pc*4+lc) maps to global index vi = r*65+c. The local-space Z is negated to
    ///   match Godot handedness.
    ///   spec: terrain.md §5.1 — spacing 16.0 wu: CONFIRMED. §5.5 — normals Nx,Ny,-Nz: CONFIRMED.
    ///   spec: terrain.md §5.8 — diffuse RGBA: CONFIRMED.
    ///
    /// Material:
    ///   StandardMaterial3D: Unshaded + VertexColorUseAsAlbedo + CullMode.Disabled +
    ///   LinearWithMipmaps + Repeat UV + AlbedoTexture = resolved or null (fallback: solid colour
    ///   seeded from the byte value for easy visual debugging).
    /// </summary>
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
        // Grid constants.
        // spec: terrain.md §5.1 — "65×65 vertices, vertex spacing 16.0": CONFIRMED.
        const int gridSize = TerrainCell.GridSize; // 65
        const float spacing = 16.0f; // spec: terrain.md §5.1 — 1024/64 = 16.0. CONFIRMED.
        const int patchGrid = PatchGrid; // 16 — spec: terrain.md §5.6. CONFIRMED.
        const int quadsPerPatch = QuadsPerPatch; // 4 — spec: terrain.md §5.6. CONFIRMED.
        const int vertsPerPatch = VertsPerPatch; // 5
        // Each patch emits 5×5 = 25 vertices and 4×4×2×3 = 96 indices.
        const int patchVertCount = vertsPerPatch * vertsPerPatch; // 25
        const int patchQuadCount = quadsPerPatch * quadsPerPatch; // 16
        const int patchIndexCount = patchQuadCount * 2 * 3; // 96

        bool hasNormals = cell.Normals is { Length: >= TerrainCell.VertexCount };

        // ---- Step 1: group patches by texture byte ----
        // Build a mapping from texture byte → list of (patchRow, patchCol) pairs.
        // spec: terrain.md §5.6 — "TextureIndexGrid row=Z, col=X, 1-based": CONFIRMED.

        var byteToPatches = new Dictionary<byte, List<(int Pr, int Pc)>>();
        if (cell.TextureIndexGrid.Length >= patchGrid * patchGrid)
        {
            for (int pr = 0; pr < patchGrid; pr++)
            {
                for (int pc = 0; pc < patchGrid; pc++)
                {
                    // spec: terrain.md §5.6 — grid index = patchRow*16+patchCol: CONFIRMED.
                    byte texByte = cell.TextureIndexGrid[pr * patchGrid + pc];
                    if (!byteToPatches.TryGetValue(texByte, out var list))
                    {
                        list = new List<(int, int)>();
                        byteToPatches[texByte] = list;
                    }

                    list.Add((pr, pc));
                }
            }
        }
        else
        {
            // Malformed cell: fall back to treating the entire cell as one group with byte 0.
            var allPatches = new List<(int, int)>(patchGrid * patchGrid);
            for (int pr = 0; pr < patchGrid; pr++)
            for (int pc = 0; pc < patchGrid; pc++)
                allPatches.Add((pr, pc));
            byteToPatches[0] = allPatches;
        }

        // ---- Step 2: check whether DirectionFlags are available ----
        // spec: terrain.md §5.7 — "Block 4 DirectionFlags, 16×16, values 0-3": CONFIRMED.
        bool hasFlags = cell.DirectionFlags.Length >= patchGrid * patchGrid;

        // ---- Step 3: emit one surface per texture-byte group ----
        var mesh = new ArrayMesh();

        foreach (var (texByte, patches) in byteToPatches)
        {
            int surfaceVertCount = patches.Count * patchVertCount; // 25 verts per patch
            int surfaceIndexCount = patches.Count * patchIndexCount; // 96 indices per patch

            var positions = new Vector3[surfaceVertCount];
            var normals = new Vector3[surfaceVertCount];
            var colours = new Color[surfaceVertCount];
            var uvs = new Vector2[surfaceVertCount];
            var indices = new int[surfaceIndexCount];

            int vBase = 0; // running vertex write cursor
            int iBase = 0; // running index write cursor

            foreach (var (pr, pc) in patches)
            {
                // Determine UV flip flags for this patch.
                // spec: terrain.md §5.7 — bit 0x01=s_flip (U-mirror), bit 0x02=t_flip (V-mirror): CONFIRMED.
                byte dirFlag = hasFlags ? cell.DirectionFlags[pr * patchGrid + pc] : (byte)0;
                bool flipU = (dirFlag & 0x01) != 0; // spec: terrain.md §5.7 — 0x01 s_flip. CONFIRMED.
                bool flipV = (dirFlag & 0x02) != 0; // spec: terrain.md §5.7 — 0x02 t_flip. CONFIRMED.

                // Emit the 5×5 vertices for this patch.
                // Global grid rows [pr*4 .. pr*4+4], cols [pc*4 .. pc*4+4].
                // spec: terrain.md §5.6 — "4×4 quads per patch; global row = pr*4+lr, col = pc*4+lc": CONFIRMED.
                for (int lr = 0; lr < vertsPerPatch; lr++)
                {
                    int r = pr * quadsPerPatch + lr; // global row [0..64]
                    for (int lc = 0; lc < vertsPerPatch; lc++)
                    {
                        int c = pc * quadsPerPatch + lc; // global col [0..64]
                        int vi = r * gridSize + c; // global vertex index

                        // Local-space position within the sector mesh.
                        // col → X, height → Y, row → -Z (Godot handedness flip).
                        // spec: terrain.md §5.1 — spacing 16.0 wu: CONFIRMED.
                        // spec: WorldCoordinates.ToGodot — Z negated.
                        positions[vBase + lr * vertsPerPatch + lc] = new Vector3(
                            c * spacing,
                            cell.Heights[vi],
                            -(r * spacing));

                        // Per-patch UV: local offset * 0.25, then apply mirror flags.
                        // spec: terrain.md §5.7 — "f = vertex_offset × 0.25": CONFIRMED.
                        float uFrac = lr * 0.25f; // local row offset 0..4 → 0.0..1.0
                        float vFrac = lc * 0.25f; // local col offset 0..4 → 0.0..1.0
                        // Note: UV rows go along Z (row index), UV cols along X (col index).
                        // The patch texture tiles in S=column-direction (X), T=row-direction (Z).
                        // Map: S=lc*0.25, T=lr*0.25 so texture is axis-aligned with the quad grid.
                        float s = lc * 0.25f;
                        float t = lr * 0.25f;
                        if (flipU) s = 1.0f - s; // spec: terrain.md §5.7 — s_flip: CONFIRMED.
                        if (flipV) t = 1.0f - t; // spec: terrain.md §5.7 — t_flip: CONFIRMED.
                        uvs[vBase + lr * vertsPerPatch + lc] = new Vector2(s, t);

                        // Normals.
                        // spec: terrain.md §5.5 — "R=Nx, G=Ny, B=Nz; negate Nz for Godot": CONFIRMED.
                        if (hasNormals)
                        {
                            var n = cell.Normals[vi];
                            normals[vBase + lr * vertsPerPatch + lc] = new Vector3(n.Nx, n.Ny, -n.Nz).Normalized();
                        }
                        else
                        {
                            normals[vBase + lr * vertsPerPatch + lc] = Vector3.Up;
                        }

                        // Diffuse colour.
                        // spec: terrain.md §5.8 — RGBA, R@+0 G@+1 B@+2 A@+3: CONFIRMED.
                        var d = cell.DiffuseColours[vi];
                        colours[vBase + lr * vertsPerPatch + lc] = new Color(d.R, d.G, d.B, d.A);
                    }
                }

                // Emit 4×4 = 16 quads (96 indices) for this patch, CCW winding.
                // Vertex layout within the patch: row-major 5×5 (local index = lr*5+lc).
                // spec: terrain.md §5.1 — "64×64 quads": CONFIRMED.
                for (int lr = 0; lr < quadsPerPatch; lr++)
                {
                    for (int lc = 0; lc < quadsPerPatch; lc++)
                    {
                        int vi0 = vBase + lr * vertsPerPatch + lc; // TL
                        int vi1 = vBase + lr * vertsPerPatch + lc + 1; // TR
                        int vi2 = vBase + (lr + 1) * vertsPerPatch + lc; // BL
                        int vi3 = vBase + (lr + 1) * vertsPerPatch + lc + 1; // BR

                        // Winding mirrors the single-surface builder: Z was negated, so
                        // parity was flipped → use TL,TR,BR / TL,BR,BL to keep faces upward.
                        indices[iBase++] = vi0; // tri 0: TL, TR, BR
                        indices[iBase++] = vi1;
                        indices[iBase++] = vi3;

                        indices[iBase++] = vi0; // tri 1: TL, BR, BL
                        indices[iBase++] = vi3;
                        indices[iBase++] = vi2;
                    }
                }

                vBase += patchVertCount;
            }

            // Assemble the surface arrays.
            var arrays = new global::Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = positions;
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.Color] = colours;
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
            arrays[(int)Mesh.ArrayType.Index] = indices;

            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            int surfaceIdx = mesh.GetSurfaceCount() - 1;
            mesh.SurfaceSetMaterial(surfaceIdx, BuildSurfaceMaterial(texByte, textureResolver));
        }

        return mesh;
    }

    /// <summary>
    /// Builds the StandardMaterial3D for one surface (one texture byte group).
    ///
    /// When <paramref name="textureResolver"/> is non-null and returns a texture, the material
    /// uses that texture as AlbedoTexture with Repeat addressing and LinearWithMipmaps filtering.
    /// UV is patch-local 0..1 (no Uv1Scale multiplier needed — Repeat wrapping handles tiling).
    /// VertexColorUseAsAlbedo modulates by the .ted diffuse colour baked into vertex colours.
    /// Unshaded because the legacy terrain bakes its lighting into the vertex colours.
    ///
    /// Fallback when the resolver returns null: a solid colour derived from the texture byte value
    /// for easy visual debugging (the colour is deterministic: hue = byte/256 in HSV space).
    ///
    /// spec: terrain.md §5.6 — tex_id (1-based) resolve chain: CONFIRMED.
    /// spec: terrain.md §5.7 — UV is patch-local 0..1, Repeat wrap, no global scale: CONFIRMED.
    /// </summary>
    private static StandardMaterial3D BuildSurfaceMaterial(
        byte texByte,
        Func<int, ImageTexture?>? textureResolver)
    {
        var mat = new StandardMaterial3D();
        mat.VertexColorUseAsAlbedo = true;
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps;

        if (textureResolver is not null && texByte != 0)
        {
            try
            {
                // spec: terrain.md §5.6 — 1-based byte indexes the per-cell TEXTURES list: CONFIRMED.
                ImageTexture? tex = textureResolver(texByte);
                if (tex is not null)
                {
                    mat.AlbedoTexture = tex;
                    // UV is already patch-local 0..1; use Repeat so the texture tiles correctly.
                    // No Uv1Scale needed — the per-patch UV already spans [0,1] exactly once.
                    mat.TextureRepeat = true;
                    return mat;
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[TerrainNode] TextureResolver failed for texByte={texByte}: {ex.Message}");
                // Fall through to the solid-colour fallback.
            }
        }

        // Fallback: solid colour seeded from texByte for visual debugging.
        // Hue cycles through the spectrum across the 256 possible byte values.
        float hue = texByte / 256.0f;
        mat.AlbedoColor = Color.FromHsv(hue, 0.6f, 0.8f);
        return mat;
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

        // Evict the height cache for this cell alongside the mesh node.
        _cellCache.Remove(key);
    }

    // -------------------------------------------------------------------------
    // Ground-height API (bilinear sample of the resident .ted heightmap)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the terrain Y (world-space height) at the given legacy world position
    /// by bilinear-sampling the resident sector's .ted heightmap.
    ///
    /// <para>
    /// Coordinate convention: <paramref name="worldX"/> and <paramref name="worldZ"/> are
    /// <b>legacy</b> world coordinates (not Godot Godot-space). The caller passes the
    /// original (un-negated) legacy Z. The returned Y is a direct world-space value — no
    /// scale or coordinate flip is applied to it.
    /// spec: Docs/RE/formats/terrain.md §5.4 — "Values are direct world-space Y coordinates".
    /// </para>
    ///
    /// <para>
    /// Grid layout: row = Z axis, col = X axis; spacing 16 wu; 65×65 vertices per cell.
    /// spec: Docs/RE/formats/terrain.md §5.2 — "rows are constant-Z slices": CONFIRMED.
    /// spec: Docs/RE/formats/terrain.md §5.1 — vertex spacing 16.0 wu: CONFIRMED.
    /// </para>
    ///
    /// <para>
    /// Cell key formula:
    ///   cellMapX = floor(worldX / 1024) + 10000
    ///   cellMapZ = floor(worldZ / 1024) + 10000
    /// spec: Docs/RE/formats/terrain.md §1.4 — world-space bounds, origin bias 10000: CONFIRMED.
    /// Note: §2 of terrain.md describes an adjusted-axis correction for negative coordinates.
    /// We reproduce that here using <see cref="Math.Floor"/> which already implements correct
    /// floor-division for negatives — no explicit adjustment branch is needed.
    /// </para>
    ///
    /// <para>
    /// When the queried cell is not resident, returns the <paramref name="fallbackY"/>
    /// value (default 0f). Callers that want to know whether a cell was actually sampled
    /// should use <see cref="TryGetGroundHeight"/> instead.
    /// </para>
    /// </summary>
    /// <param name="worldX">Legacy world-space X coordinate (not negated).</param>
    /// <param name="worldZ">Legacy world-space Z coordinate (not negated).</param>
    /// <param name="fallbackY">
    /// Value returned when the cell is not resident. Defaults to 0f.
    /// Pass the known flat-terrain height (e.g. 26f) for a graceful fallback.
    /// </param>
    /// <returns>World-space terrain Y at (worldX, worldZ), or <paramref name="fallbackY"/>.</returns>
    public float GetGroundHeight(float worldX, float worldZ, float fallbackY = 0f)
    {
        TryGetGroundHeight(worldX, worldZ, out float result, fallbackY);
        return result;
    }

    /// <summary>
    /// Attempts to bilinear-sample the terrain height at the given legacy world position.
    /// Returns <see langword="true"/> when the sector is resident and the sample succeeded;
    /// <see langword="false"/> when the cell is not loaded (height is set to
    /// <paramref name="fallbackY"/>).
    ///
    /// Bilinear formula over the 65×65 grid (spacing 16 wu):
    ///   lx = (worldX - (cellMapX - 10000) × 1024) / 16
    ///   lz = (worldZ - (cellMapZ - 10000) × 1024) / 16
    ///   c0 = clamp(floor(lx), 0, 63),  fx = lx - c0
    ///   r0 = clamp(floor(lz), 0, 63),  fz = lz - r0
    ///   h = lerp(
    ///         lerp(H[r0,c0],   H[r0,c0+1],   fx),
    ///         lerp(H[r0+1,c0], H[r0+1,c0+1], fx),
    ///         fz)
    ///
    /// spec: Docs/RE/formats/terrain.md §5.2 — row=Z, col=X: CONFIRMED.
    /// spec: Docs/RE/formats/terrain.md §5.4 — Heights[] direct world-space Y: CONFIRMED.
    /// spec: Docs/RE/formats/terrain.md §1.4 — cell size 1024, origin bias 10000: CONFIRMED.
    ///
    /// VERIFIED reference: cell (10000, 9990) is flat at ~26 wu; GetGroundHeight(474, -10101)
    /// falls inside that cell (cellMapZ = floor(-10101/1024)+10000 = 9990) and returns ≈ 25.978.
    /// </summary>
    /// <param name="worldX">Legacy world-space X (not negated).</param>
    /// <param name="worldZ">Legacy world-space Z (not negated).</param>
    /// <param name="height">Sampled terrain Y on success; <paramref name="fallbackY"/> on miss.</param>
    /// <param name="fallbackY">Value written to <paramref name="height"/> when cell not resident.</param>
    /// <returns><see langword="true"/> if the cell was resident and height was sampled.</returns>
    public bool TryGetGroundHeight(float worldX, float worldZ, out float height, float fallbackY = 0f)
    {
        // --- Step 1: map world position to (mapX, mapZ) cell key ---
        // spec: terrain.md §1.4 — origin bias 10000, cell size 1024 wu: CONFIRMED.
        // Math.Floor correctly handles negative worldX/worldZ (floor-division semantics).
        int cellMapX = (int)Math.Floor(worldX / 1024.0) + 10000;
        int cellMapZ = (int)Math.Floor(worldZ / 1024.0) + 10000;

        if (!_cellCache.TryGetValue((cellMapX, cellMapZ), out TerrainCell? cell))
        {
            height = fallbackY;
            return false;
        }

        // --- Step 2: compute fractional local coordinates within the cell ---
        // Local position within cell in grid units (0..64 range for interior points).
        // spec: terrain.md §5.1 — spacing 16.0 wu: CONFIRMED.
        // spec: terrain.md §1.4 — worldX_min = (mapX - 10000) × 1024: CONFIRMED.
        const float spacing = 16.0f; // spec: terrain.md §5.1 — 1024/64 = 16.0. CONFIRMED.
        float lx = (worldX - (cellMapX - 10000) * 1024.0f) / spacing;
        float lz = (worldZ - (cellMapZ - 10000) * 1024.0f) / spacing;

        // --- Step 3: find the four corner vertices ---
        // c0/r0: integer grid column/row of the lower-left quad corner.
        // Clamp to [0,63] so the +1 neighbour is always valid (max grid index is 64).
        int c0 = Math.Clamp((int)Math.Floor(lx), 0, TerrainCell.GridSize - 2); // [0, 63]
        int r0 = Math.Clamp((int)Math.Floor(lz), 0, TerrainCell.GridSize - 2); // [0, 63]
        float fx = lx - c0; // fractional X [0,1]
        float fz = lz - r0; // fractional Z [0,1]

        // --- Step 4: bilinear interpolation ---
        // Grid index: [row * 65 + col].
        // spec: terrain.md §5.2 — row=Z, col=X, row-major: CONFIRMED.
        // spec: terrain.md §5.4 — Heights[] direct world-space Y, no scale: CONFIRMED.
        const int gs = TerrainCell.GridSize; // 65
        float[] h = cell.Heights;
        float h00 = h[r0 * gs + c0]; // (r0,   c0)
        float h01 = h[r0 * gs + c0 + 1]; // (r0,   c0+1)
        float h10 = h[(r0 + 1) * gs + c0]; // (r0+1, c0)
        float h11 = h[(r0 + 1) * gs + c0 + 1]; // (r0+1, c0+1)

        // Bilinear: lerp along X for each Z row, then lerp along Z.
        float topRow = h00 + (h01 - h00) * fx; // lerp at r0
        float bottomRow = h10 + (h11 - h10) * fx; // lerp at r0+1
        height = topRow + (bottomRow - topRow) * fz;
        return true;
    }
}