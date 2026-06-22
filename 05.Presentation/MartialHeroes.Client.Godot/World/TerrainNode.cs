using Godot;
using MartialHeroes.Assets.Parsers.Terrain;
using MartialHeroes.Assets.Parsers.Terrain.Models;
using MartialHeroes.Client.Application.World;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
///     Manages the set of resident terrain sector meshes. Listens for <see cref="SectorLoadedEvent" />
///     and <see cref="SectorUnloadedEvent" /> (forwarded by <see cref="GameLoop" />) and adds/removes
///     <see cref="MeshInstance3D" /> nodes for each sector.
///     PASSIVE: zero game logic. Converts raw .ted bytes → <see cref="TerrainCell" /> (via
///     <see cref="TedTerrainParser" />) → Godot <see cref="ArrayMesh" />. The mesh is built as a
///     multi-surface ArrayMesh: one surface per distinct texture byte value in TextureIndexGrid,
///     so every 4×4-quad patch shows its correct ground texture.
///     Texture support (multi-surface, per-patch):
///     The .ted cell carries a 16×16 TextureIndexGrid (Block 3) where each byte is a 1-based index
///     into the per-cell TEXTURES list, and a 16×16 DirectionFlags (Block 4) carrying per-patch
///     UV mirror bits. Patches are grouped by texture byte; one ArrayMesh surface is emitted per
///     distinct byte. Each surface receives its own StandardMaterial3D with the resolved texture.
///     spec: Docs/RE/formats/terrain.md §5.6 Block 3 — "u8, 1-based, 16×16": CONFIRMED.
///     spec: Docs/RE/formats/terrain.md §5.7 Block 4 — bit 0x01=U-flip, bit 0x02=V-flip: CONFIRMED.
///     Coordinate conventions:
///     World-space origin bias:
///     worldX_min = (mapX - 10000) × 1024
///     worldZ_min = (mapZ - 10000) × 1024
///     spec: Docs/RE/formats/terrain.md §1.4 per-cell world-space bounds.
///     Godot Z convention (handedness flip): negate Z.
///     spec: WorldCoordinates.ToGodot — negate Z to convert legacy left-handed → Godot right-handed.
///     spec: Docs/RE/formats/terrain.md §5 (.ted geometry blob — 65×65 vertices, 64×64 quads).
///     spec: Docs/RE/formats/terrain.md §1.4 (per-cell world-space bounds).
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
    // Sector mesh registry: (mapX, mapZ) → MeshInstance3D
    // -------------------------------------------------------------------------

    private readonly Dictionary<(int MapX, int MapZ), MeshInstance3D> _meshNodes = new();

    // -------------------------------------------------------------------------
    // Optional texture resolver (set by RealWorldRenderer when VFS is available)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Optional texture resolver: given a tex_id (from the .map TEXTURES block), returns a
    ///     Godot <see cref="ImageTexture" /> or null. Called once per distinct texture byte value.
    ///     Set by <see cref="RealWorldRenderer" /> when real VFS assets are available.
    ///     When null, the mesh renders with vertex colours only (offline / synthetic mode).
    ///     spec: Docs/RE/formats/terrain.md §3.5 TEXTURES directive — tex_id indexed array.
    /// </summary>
    public Func<int, ImageTexture?>? TextureResolver { get; set; }

    // -------------------------------------------------------------------------
    // Sector-resident notification
    //
    // Fired on the Godot main thread immediately after a new cell enters _cellCache,
    // so subscribers (e.g. NpcRenderer) can snap actor Y values without polling.
    // Parameters: (mapX, mapZ) — the biased cell coordinates of the newly resident sector.
    //
    // spec: Docs/RE/formats/terrain.md §1.4 — origin bias 10000, cell size 1024 wu: CONFIRMED.
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Raised on the Godot main thread each time a new terrain sector becomes resident
    ///     (its <see cref="TerrainCell" /> enters the height-lookup cache).
    ///     Subscribers receive the biased (<paramref name="mapX" />, <paramref name="mapZ" />) of the
    ///     newly loaded cell and can call <see cref="TryGetGroundHeight" /> immediately — the cell is
    ///     guaranteed to be in the cache at the point the delegate runs.
    ///     Usage: wire this in the orchestrator (e.g. <see cref="RealWorldRenderer" />) to re-ground
    ///     actors that were spawned before their cell's heightmap was available.
    ///     spec: Docs/RE/formats/terrain.md §5.4 — Heights[] are direct world-space Y values: CONFIRMED.
    /// </summary>
    public event Action<int, int>? SectorBecameResident;

    // -------------------------------------------------------------------------
    // Event handlers (called from GameLoop._Process, main thread)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Reacts to a <see cref="SectorLoadedEvent" />: parses the .ted bytes, converts to a Godot
    ///     multi-surface <see cref="ArrayMesh" />, and adds a <see cref="MeshInstance3D" />.
    ///     If the payload is empty (cell not in area manifest), nothing is added.
    ///     spec: Docs/RE/formats/terrain.md §5 (.ted blob); §1.4 (world-space bounds).
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
            // Cell absent from the area manifest — no visual to add.
            // spec: Docs/RE/formats/terrain.md §1.2 (absent key → no data).
            return;

        // Evict any stale node for this key (can happen on re-load after eviction).
        RemoveSectorMesh(evt.MapX, evt.MapZ);

        var cell = TryParseTed(evt.Payload, evt.MapX, evt.MapZ);
        if (cell is null) return;

        // Cache the cell for ground-height queries before building the mesh.
        // spec: Docs/RE/formats/terrain.md §5.4 — Heights[] are direct world-space Y values.
        _cellCache[(evt.MapX, evt.MapZ)] = cell;

        // Build the Godot multi-surface ArrayMesh directly from the TerrainCell.
        var mesh = TryBuildMultiSurfaceMesh(cell, TextureResolver);
        if (mesh is null) return;

        // Compute the sector's world-space origin and apply Godot coordinate conventions.
        // spec: Docs/RE/formats/terrain.md §1.4 — world-space bounds from (mapX, mapZ).
        // worldX_min = (mapX - 10000) × 1024.0  — CONFIRMED (spec §1.4).
        // worldZ_min = (mapZ - 10000) × 1024.0  — CONFIRMED (spec §1.4).
        // Godot convention: negate Z (legacy left-handed → right-handed).
        // spec: WorldCoordinates.ToGodot.
        var legacyX = (evt.MapX - 10000) *
                      1024.0f; // spec: terrain.md §1.4, origin bias 10000, cell size 1024. CONFIRMED.
        var legacyZ = (evt.MapZ - 10000) * 1024.0f; // spec: terrain.md §1.4. CONFIRMED.
        var godotX = legacyX;
        var godotZ = -legacyZ; // spec: WorldCoordinates.ToGodot — negate Z.

        var meshInst = new MeshInstance3D();
        meshInst.Mesh = mesh;
        meshInst.Position = new Vector3(godotX, 0f, godotZ);
        meshInst.Name = $"Sector_{evt.MapX}_{evt.MapZ}";

        AddChild(meshInst);
        _meshNodes[(evt.MapX, evt.MapZ)] = meshInst;

        GD.Print(
            $"[TerrainNode] Sector ({evt.MapX},{evt.MapZ}) loaded at Godot ({godotX:F0}, 0, {godotZ:F0}), {mesh.GetSurfaceCount()} surface(s).");

        // Notify subscribers that this cell's heightmap is now queryable via TryGetGroundHeight.
        // The cell is already in _cellCache above, so subscribers can call it immediately.
        // Called on the Godot main thread (we are inside GameLoop._Process → DispatchEvent).
        // spec: Docs/RE/formats/terrain.md §5.4 — Heights[] resident once _cellCache is updated: CONFIRMED.
        SectorBecameResident?.Invoke(evt.MapX, evt.MapZ);
    }

    /// <summary>
    ///     Reacts to a <see cref="SectorUnloadedEvent" />: removes the mesh node for the evicted sector.
    ///     spec: Docs/RE/formats/terrain.md §9.3 (eviction: Chebyshev distance > 2).
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
    ///     Parses the raw .ted bytes into a <see cref="TerrainCell" />. Returns null on parse failure
    ///     (too short, corrupt) so the sector is silently skipped without crashing the scene.
    ///     spec: Docs/RE/formats/terrain.md §5.2 (block layout — total 46987 bytes: CONFIRMED).
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
    ///     Builds a multi-surface Godot <see cref="ArrayMesh" /> from a parsed <see cref="TerrainCell" />.
    ///     Surface grouping strategy:
    ///     Patches (16×16 grid entries from TextureIndexGrid) are grouped by their raw texture byte
    ///     value. All patches sharing the same byte value contribute geometry to a single surface,
    ///     so a cell with N distinct byte values produces N surfaces. This avoids a material switch
    ///     per patch while keeping the UV/flip logic correct per patch.
    ///     Per-patch vertex layout:
    ///     Each patch owns a 5×5 local vertex grid (VertsPerPatch=5) covering its 4×4 quads. A patch
    ///     at (pr, pc) spans global grid rows [pr*4 .. pr*4+4] and columns [pc*4 .. pc*4+4].
    ///     spec: terrain.md §5.6 — "4×4 quads per patch axis, 5×5 vertices per patch": CONFIRMED.
    ///     The last patch column (pc=15) reaches global col 64 (pc*4+4 = 64), which is valid since
    ///     the grid is 65×65 (cols 0..64). Likewise for patch row 15 reaching global row 64. There
    ///     is no boundary clip needed: the global grid index r*65+c with r,c ≤ 64 is always valid.
    ///     UV per patch:
    ///     Local vertex offsets within a patch run 0..4 on each axis. UV fraction = offset × 0.25,
    ///     giving the five steps 0.0, 0.25, 0.5, 0.75, 1.0 over the patch's [0,1] UV square.
    ///     spec: terrain.md §5.7 — "f = vertex_offset × 0.25 (0.0, 0.25, 0.5, 0.75, 1.0)": CONFIRMED.
    ///     DirectionFlags UV mirror:
    ///     For each patch, DirectionFlags[pr*16+pc] is read; bit 0x01 = flip U (S-mirror),
    ///     bit 0x02 = flip V (T-mirror). When set, the corresponding UV component becomes 1 − f.
    ///     spec: terrain.md §5.7 — "bit 0x01 s_flip, bit 0x02 t_flip": CONFIRMED.
    ///     The global Uv1Scale hack is dropped; UV is patch-local 0..1 with Repeat addressing.
    ///     Positions / Normals / Colours:
    ///     Reuse the same formula as the previous single-surface builder. Each patch vertex
    ///     (pr*4+lr, pc*4+lc) maps to global index vi = r*65+c. The local-space Z is negated to
    ///     match Godot handedness.
    ///     spec: terrain.md §5.1 — spacing 16.0 wu: CONFIRMED. §5.5 — normals Nx,Ny,-Nz: CONFIRMED.
    ///     spec: terrain.md §5.8 — diffuse RGBA: CONFIRMED.
    ///     Material:
    ///     StandardMaterial3D: Unshaded + VertexColorUseAsAlbedo + CullMode.Disabled +
    ///     LinearWithMipmaps + Repeat UV + AlbedoTexture = resolved or null (no AlbedoTexture when
    ///     the resolver returns null — no synthetic fallback colour).
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

        var hasNormals = cell.Normals is { Length: >= TerrainCell.VertexCount };

        // ---- Step 1: group patches by texture byte ----
        // Build a mapping from texture byte → list of (patchRow, patchCol) pairs.
        // spec: terrain.md §5.6 — "TextureIndexGrid row=Z, col=X, 1-based": CONFIRMED.

        var byteToPatches = new Dictionary<byte, List<(int Pr, int Pc)>>();
        if (cell.TextureIndexGrid.Length >= patchGrid * patchGrid)
        {
            for (var pr = 0; pr < patchGrid; pr++)
            for (var pc = 0; pc < patchGrid; pc++)
            {
                // spec: terrain.md §5.6 — grid index = patchRow*16+patchCol: CONFIRMED.
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
            // Malformed cell: fall back to treating the entire cell as one group with byte 0.
            var allPatches = new List<(int, int)>(patchGrid * patchGrid);
            for (var pr = 0; pr < patchGrid; pr++)
            for (var pc = 0; pc < patchGrid; pc++)
                allPatches.Add((pr, pc));
            byteToPatches[0] = allPatches;
        }

        // ---- Step 2: check whether DirectionFlags are available ----
        // spec: terrain.md §5.7 — "Block 4 DirectionFlags, 16×16, values 0-3": CONFIRMED.
        var hasFlags = cell.DirectionFlags.Length >= patchGrid * patchGrid;

        // ---- Step 3: emit one surface per texture-byte group ----
        var mesh = new ArrayMesh();

        foreach (var (texByte, patches) in byteToPatches)
        {
            var surfaceVertCount = patches.Count * patchVertCount; // 25 verts per patch
            var surfaceIndexCount = patches.Count * patchIndexCount; // 96 indices per patch

            var positions = new Vector3[surfaceVertCount];
            var normals = new Vector3[surfaceVertCount];
            var colours = new Color[surfaceVertCount];
            var uvs = new Vector2[surfaceVertCount];
            var indices = new int[surfaceIndexCount];

            var vBase = 0; // running vertex write cursor
            var iBase = 0; // running index write cursor

            foreach (var (pr, pc) in patches)
            {
                // Determine UV flip flags for this patch.
                // spec: terrain.md §5.7 — bit 0x01=s_flip (U-mirror), bit 0x02=t_flip (V-mirror): CONFIRMED.
                var dirFlag = hasFlags ? cell.DirectionFlags[pr * patchGrid + pc] : (byte)0;
                var flipU = (dirFlag & 0x01) != 0; // spec: terrain.md §5.7 — 0x01 s_flip. CONFIRMED.
                var flipV = (dirFlag & 0x02) != 0; // spec: terrain.md §5.7 — 0x02 t_flip. CONFIRMED.

                // Emit the 5×5 vertices for this patch.
                // Global grid rows [pr*4 .. pr*4+4], cols [pc*4 .. pc*4+4].
                // spec: terrain.md §5.6 — "4×4 quads per patch; global row = pr*4+lr, col = pc*4+lc": CONFIRMED.
                for (var lr = 0; lr < vertsPerPatch; lr++)
                {
                    var r = pr * quadsPerPatch + lr; // global row [0..64]
                    for (var lc = 0; lc < vertsPerPatch; lc++)
                    {
                        var c = pc * quadsPerPatch + lc; // global col [0..64]
                        var vi = r * gridSize + c; // global vertex index

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
                        // Note: UV rows go along Z (row index), UV cols along X (col index).
                        // The patch texture tiles in S=column-direction (X), T=row-direction (Z).
                        // Map: S=lc*0.25, T=lr*0.25 so texture is axis-aligned with the quad grid.
                        var s = lc * 0.25f; // local col offset 0..4 → 0.0..1.0 (S axis = X)
                        var t = lr * 0.25f; // local row offset 0..4 → 0.0..1.0 (T axis = Z)
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
                for (var lr = 0; lr < quadsPerPatch; lr++)
                for (var lc = 0; lc < quadsPerPatch; lc++)
                {
                    var vi0 = vBase + lr * vertsPerPatch + lc; // TL
                    var vi1 = vBase + lr * vertsPerPatch + lc + 1; // TR
                    var vi2 = vBase + (lr + 1) * vertsPerPatch + lc; // BL
                    var vi3 = vBase + (lr + 1) * vertsPerPatch + lc + 1; // BR

                    // Winding mirrors the single-surface builder: Z was negated, so
                    // parity was flipped → use TL,TR,BR / TL,BR,BL to keep faces upward.
                    indices[iBase++] = vi0; // tri 0: TL, TR, BR
                    indices[iBase++] = vi1;
                    indices[iBase++] = vi3;

                    indices[iBase++] = vi0; // tri 1: TL, BR, BL
                    indices[iBase++] = vi3;
                    indices[iBase++] = vi2;
                }

                vBase += patchVertCount;
            }

            // Assemble the surface arrays.
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

    /// <summary>
    ///     Builds the StandardMaterial3D for one surface (one texture byte group).
    ///     When <paramref name="textureResolver" /> is non-null and returns a texture, the material
    ///     uses that texture as AlbedoTexture with Repeat addressing and LinearWithMipmaps filtering.
    ///     UV is patch-local 0..1 (no Uv1Scale multiplier needed — Repeat wrapping handles tiling).
    ///     VertexColorUseAsAlbedo modulates by the .ted diffuse colour baked into vertex colours.
    ///     Unshaded because the legacy terrain bakes its lighting into the vertex colours.
    ///     When the resolver returns null (texture absent from VFS), no AlbedoTexture is set on the
    ///     material — the surface renders with vertex colours only (no synthetic debug colour).
    ///     spec: terrain.md §5.6 — tex_id (1-based) resolve chain: CONFIRMED.
    ///     spec: terrain.md §5.7 — UV is patch-local 0..1, Repeat wrap, no global scale: CONFIRMED.
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

        if (textureResolver is not null)
        {
            // Any byte < 1 is clamped up to 1 before the -1 indexing (renders slot 1 / texlist[0]).
            // spec: Docs/RE/formats/terrain.md §5.6 — "a byte below 1 is clamped up to 1 … renders
            //   slot 1; the '0 = no-texture sentinel' reading is REFUTED … RENDER-domain, before the -1."
            // spec: Docs/RE/formats/terrain.md §5.9 — clamp < 1 to 1 at render bind; CONFIRMED.
            var clampedByte = texByte < 1 ? 1 : texByte;
            try
            {
                // spec: terrain.md §5.6 — 1-based byte indexes the per-cell TEXTURES list: CONFIRMED.
                var tex = textureResolver(clampedByte);
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
                GD.PrintErr(
                    $"[TerrainNode] TextureResolver failed for texByte={texByte} (clamped={clampedByte}): {ex.Message}");
                // Resolution failed — return the material without an AlbedoTexture (vertex colours only).
            }
        }

        // No resolver wired or resolution returned null/failed — no AlbedoTexture set.
        // The material renders with vertex colours only (faithful: untextured, not a debug colour).
        return mat;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void RemoveSectorMesh(int mapX, int mapZ)
    {
        var key = (mapX, mapZ);
        if (_meshNodes.Remove(key, out var node)) node.QueueFree();

        // Evict the height cache for this cell alongside the mesh node.
        _cellCache.Remove(key);
    }

    // -------------------------------------------------------------------------
    // Ground-height API (bilinear sample of the resident .ted heightmap)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Returns the terrain Y (world-space height) at the given legacy world position
    ///     by per-triangle plane interpolation of the resident sector's .ted heightmap.
    ///     spec: Docs/RE/formats/terrain.md §5.4a — PER-TRIANGLE PLANE interpolation; CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/terrain-streaming.md §6.5 — CYCLE 12 correction (bilinear is wrong).
    ///     <para>
    ///         Coordinate convention: <paramref name="worldX" /> and <paramref name="worldZ" /> are
    ///         <b>legacy</b> world coordinates (not Godot Godot-space). The caller passes the
    ///         original (un-negated) legacy Z. The returned Y is a direct world-space value — no
    ///         scale or coordinate flip is applied to it.
    ///         spec: Docs/RE/formats/terrain.md §5.4 — "Values are direct world-space Y coordinates".
    ///     </para>
    ///     <para>
    ///         Grid layout: row = Z axis, col = X axis; spacing 16 wu; 65×65 vertices per cell.
    ///         spec: Docs/RE/formats/terrain.md §5.2 — "rows are constant-Z slices": CONFIRMED.
    ///         spec: Docs/RE/formats/terrain.md §5.1 — vertex spacing 16.0 wu: CONFIRMED.
    ///     </para>
    ///     <para>
    ///         Cell key formula:
    ///         cellMapX = floor(worldX / 1024) + 10000
    ///         cellMapZ = floor(worldZ / 1024) + 10000
    ///         spec: Docs/RE/formats/terrain.md §1.4 — world-space bounds, origin bias 10000: CONFIRMED.
    ///         Note: §2 of terrain.md describes an adjusted-axis correction for negative coordinates.
    ///         We reproduce that here using <see cref="Math.Floor" /> which already implements correct
    ///         floor-division for negatives — no explicit adjustment branch is needed.
    ///     </para>
    ///     <para>
    ///         When the queried cell is not resident, returns the <paramref name="fallbackY" />
    ///         value (default 0f). Callers that want to know whether a cell was actually sampled
    ///         should use <see cref="TryGetGroundHeight" /> instead.
    ///     </para>
    /// </summary>
    /// <param name="worldX">Legacy world-space X coordinate (not negated).</param>
    /// <param name="worldZ">Legacy world-space Z coordinate (not negated).</param>
    /// <param name="fallbackY">
    ///     Value returned when the cell is not resident. Defaults to 0f.
    ///     Pass the known flat-terrain height (e.g. 26f) for a graceful fallback.
    /// </param>
    /// <returns>World-space terrain Y at (worldX, worldZ), or <paramref name="fallbackY" />.</returns>
    public float GetGroundHeight(float worldX, float worldZ, float fallbackY = 0f)
    {
        TryGetGroundHeight(worldX, worldZ, out var result, fallbackY);
        return result;
    }

    /// <summary>
    ///     Attempts to sample the terrain height at the given legacy world position using
    ///     per-triangle plane interpolation.
    ///     Returns <see langword="true" /> when the sector is resident and the sample succeeded;
    ///     <see langword="false" /> when the cell is not loaded (height is set to
    ///     <paramref name="fallbackY" />).
    ///     Algorithm (per-triangle plane interpolation):
    ///     1. Convert world XZ to fractional quad coordinates within the cell.
    ///     lx = (worldX - cellOriginX) / 16,  lz = (worldZ - cellOriginZ) / 16
    ///     c0 = floor(lx),  r0 = floor(lz),  fx = lx - c0,  fz = lz - r0
    ///     2. Determine which of the two triangles the point falls in (\\-diagonal split,
    ///     matching the mesh winding in BuildMultiSurfaceMesh):
    ///     if fz &lt;= fx  → Triangle A (TL, TR, BR):  y = h00 + (h01-h00)·fx + (h11-h01)·fz
    ///     else           → Triangle B (TL, BL, BR):  y = h00 + (h10-h00)·fz + (h11-h10)·fx
    ///     (These are the plane equations evaluated at (fx, fz), giving exact vertex values at corners.)
    ///     3. The block-4 DirectionFlags drive the UV/texture orientation (§5.7); the split diagonal
    ///     also follows those flags. Since §5.7 is PARTIAL on which flag bit re-selects the
    ///     triangulation diagonal (vs. only re-orienting the texture UV), the \\-diagonal is used
    ///     consistently, matching the current mesh winding. PARTIAL noted below.
    ///     spec: Docs/RE/formats/terrain.md §5.4a — PER-TRIANGLE PLANE interpolation; CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/terrain-streaming.md §6.5 — CYCLE 12 correction; bilinear is WRONG.
    ///     spec: Docs/RE/formats/terrain.md §5.7 — block-4 diagonal flags; split interaction PARTIAL.
    ///     spec: Docs/RE/formats/terrain.md §5.2 — row=Z, col=X: CONFIRMED.
    ///     spec: Docs/RE/formats/terrain.md §5.4 — Heights[] direct world-space Y: CONFIRMED.
    ///     spec: Docs/RE/formats/terrain.md §1.4 — cell size 1024, origin bias 10000: CONFIRMED.
    /// </summary>
    /// <param name="worldX">Legacy world-space X (not negated).</param>
    /// <param name="worldZ">Legacy world-space Z (not negated).</param>
    /// <param name="height">Sampled terrain Y on success; <paramref name="fallbackY" /> on miss.</param>
    /// <param name="fallbackY">Value written to <paramref name="height" /> when cell not resident.</param>
    /// <returns><see langword="true" /> if the cell was resident and height was sampled.</returns>
    public bool TryGetGroundHeight(float worldX, float worldZ, out float height, float fallbackY = 0f)
    {
        // --- Step 1: map world position to (mapX, mapZ) cell key ---
        // spec: terrain.md §1.4 — origin bias 10000, cell size 1024 wu: CONFIRMED.
        // Math.Floor correctly handles negative worldX/worldZ (floor-division semantics).
        var cellMapX = (int)Math.Floor(worldX / 1024.0) + 10000;
        var cellMapZ = (int)Math.Floor(worldZ / 1024.0) + 10000;

        if (!_cellCache.TryGetValue((cellMapX, cellMapZ), out var cell))
        {
            height = fallbackY;
            return false;
        }

        // --- Step 2: compute fractional local coordinates within the cell ---
        // Local position within cell in grid units (0..64 range for interior points).
        // spec: terrain.md §5.1 — spacing 16.0 wu: CONFIRMED.
        // spec: terrain.md §1.4 — worldX_min = (mapX - 10000) × 1024: CONFIRMED.
        const float spacing = 16.0f; // spec: terrain.md §5.1 — 1024/64 = 16.0. CONFIRMED.
        var lx = (worldX - (cellMapX - 10000) * 1024.0f) / spacing;
        var lz = (worldZ - (cellMapZ - 10000) * 1024.0f) / spacing;

        // --- Step 3: find the quad corner indices ---
        // c0/r0: integer grid column/row of the top-left quad corner.
        // Clamp to [0,63] so the +1 neighbour is always valid (max grid index is 64).
        // spec: terrain.md §5.1 — 65×65 vertices, 64×64 quads: CONFIRMED.
        var c0 = Math.Clamp((int)Math.Floor(lx), 0, TerrainCell.GridSize - 2); // [0, 63]
        var r0 = Math.Clamp((int)Math.Floor(lz), 0, TerrainCell.GridSize - 2); // [0, 63]
        var fx = lx - c0; // fractional X within quad [0,1]
        var fz = lz - r0; // fractional Z within quad [0,1]

        // --- Step 4: per-triangle plane interpolation ---
        // Read the four corner heights.
        // spec: terrain.md §5.2 — row=Z, col=X, row-major index = row*65+col: CONFIRMED.
        // spec: terrain.md §5.4 — Heights[] direct world-space Y, no scale: CONFIRMED.
        // spec: terrain.md §5.4a — PER-TRIANGLE PLANE interpolation (not bilinear): CODE-CONFIRMED.
        // spec: terrain-streaming.md §6.5 — CYCLE 12 correction: bilinear is incorrect; use per-triangle.
        const int gs = TerrainCell.GridSize; // 65
        var h = cell.Heights;
        var h00 = h[r0 * gs + c0]; // TL: (r0,   c0)
        var h01 = h[r0 * gs + c0 + 1]; // TR: (r0,   c0+1)
        var h10 = h[(r0 + 1) * gs + c0]; // BL: (r0+1, c0)
        var h11 = h[(r0 + 1) * gs + c0 + 1]; // BR: (r0+1, c0+1)

        // Diagonal split: \\-diagonal (TL→BR), consistent with mesh winding in BuildMultiSurfaceMesh.
        // spec: terrain.md §5.7 — block-4 flags drive the diagonal; flag↔diagonal interaction is PARTIAL.
        // Triangle A (TL, TR, BR): contains points where fz <= fx.
        //   Plane: y = h00 + (h01 - h00) * fx + (h11 - h01) * fz
        // Triangle B (TL, BL, BR): contains points where fz > fx.
        //   Plane: y = h00 + (h10 - h00) * fz + (h11 - h10) * fx
        // Both formulas give exact vertex values at their respective corners.
        if (fz <= fx)
            // Triangle A — TL, TR, BR
            height = h00 + (h01 - h00) * fx + (h11 - h01) * fz;
        else
            // Triangle B — TL, BL, BR
            height = h00 + (h10 - h00) * fz + (h11 - h10) * fx;

        return true;
    }
}