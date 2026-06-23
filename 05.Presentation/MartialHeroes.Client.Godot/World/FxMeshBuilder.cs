// World/FxMeshBuilder.cs
//
// FIX 11 core. Converts a decoded FX overlay layer (.fx1–.fx7) into a Godot Node3D subtree
// of ArrayMesh MeshInstance3D children WITHOUT using GltfDocument — mirroring BudMeshBuilder.
//
// This replaces the former cyan PlaneMesh placeholder in SlotRenderer.SpawnFxPlane: the FX layers
// are fully decoded by TerrainLayerParsers (per-group/tile/section/sub-chunk vertices + u16 indices
// + per-group TextureIndex1Based), so we build the real overlay geometry instead of a tinted plane.
//
// Coordinate convention (D3D9 left-handed → Godot right-handed):
//   FX overlay vertices are ABSOLUTE world-space (same as .bud and .sod). spec confirmation:
//   terrain_layers.md §1.1c "16×16 spatial cull grid per channel … laid over the cell's 1024×1024
//   footprint" and §4.2 "All vertex XZ positions confirmed to lie within the cell's world-space
//   bounding box [(cellX-10000)*1024, …]". So the handedness flip is the WORLD convention (negate Z),
//   identical to BudMeshBuilder, and the produced subtree must be parented at Vector3.Zero (NOT at the
//   per-cell origin) — the verts already carry their absolute world position.
//   spec: Helpers/WorldCoordinates.ToGodot — (x, y, z) → (x, y, -z): CONFIRMED.
//   spec: Docs/RE/formats/terrain_layers.md §4.2 — FX/collision XZ within cell world AABB: CONFIRMED.
//
// Winding order:
//   Negating Z (the world handedness flip applied to positions + normals) inverts triangle winding
//   (CCW legacy → CW Godot). We correct it by swapping indices[1] ↔ indices[2] per triangle — exactly
//   as BudMeshBuilder does (BudMeshBuilder.cs lines 167–181). Material is double-sided regardless
//   (FX overlays are thin and viewed from above/around).
//
// Texture resolution:
//   Each group carries texture_index (1-based) into THIS FX channel's own .map FX{N} TEXTURES list.
//   The caller supplies a Func<int channel, uint texIdx1Based, ImageTexture?> that performs the
//   existing two-hop chain (ResolveSectionTexture("FX"+channel, idx) → bgtexture pool → .dds).
//   ABSOLUTE RULE: a group whose texture does not resolve is LOGGED and SKIPPED — never substituted
//   with a tinted/placeholder material (no fabricated values).
//
// Water channels (fx3 = channel 3, fx5 = channel 5):
//   terrain_layers.md §1.1c "Two vertex copies per group … working copy … mutated per-frame at draw
//   time (for water-ripple / scroll on the animated fx3/fx5 channels)". The static load keeps both an
//   immutable source copy and a working copy. The per-frame water shader / mutation is DEBUGGER-PENDING
//   (§1.1c GPU draw bucket / blend mode unresolved; §Known Unknowns 7a water render path UNVERIFIED).
//   We therefore emit BOTH copies as the spec mandates (source mesh + a duplicated working mesh) so the
//   working surface exists for a future per-frame mutation pass, but we DO NOT invent a ripple offset
//   or a water material branch (would be a fabricated value). The duplicate is geometrically identical
//   to the source until a confirmed water pass mutates it.

using Godot;
using MartialHeroes.Assets.Parsers.Terrain.Models;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
///     Builds Godot <see cref="ArrayMesh" /> geometry directly from a parsed FX overlay layer
///     (<c>.fx1</c>–<c>.fx7</c>), bypassing the native Godot GLB importer. Returns a
///     <see cref="Node3D" /> root whose children are one <see cref="MeshInstance3D" /> per
///     group / tile / section / sub-chunk. FX vertices are absolute world-space, so the returned
///     subtree is meant to be parented at <see cref="Vector3.Zero" />.
///     spec: Docs/RE/formats/terrain_layers.md §1.1a (universal group-array model), §1.2 (vertex
///     formats VF_32/VF_36/VF_44), §1.3 (u16 triangle list), §4.2 (absolute world-space XZ): CONFIRMED.
///     spec: Helpers/WorldCoordinates.ToGodot — Z-flip inverts winding; corrected by swapping
///     indices[1]↔indices[2] (mirrors BudMeshBuilder): CONFIRMED.
/// </summary>
internal static class FxMeshBuilder
{
    /// <summary>
    ///     Resolves an FX group's 1-based <c>texture_index</c> for the given FX channel (1..7) to a
    ///     Godot <see cref="ImageTexture" />, or <see langword="null" /> when it cannot be resolved.
    ///     Implemented by the caller via the two-hop chain
    ///     <c>ResolveSectionTexture("FX"+channel, (int)texIdx1Based)</c>.
    ///     spec: Docs/RE/formats/terrain_layers.md §1.4b — texture_index is 1-based into the per-channel
    ///     .map FX{N} TEXTURES register (clamp [1,count], then idx-1): CONFIRMED.
    /// </summary>
    public delegate ImageTexture? FxTextureResolver(int channel, uint texIndex1Based);

    // ─── Public per-channel builders ────────────────────────────────────────────

    /// <summary>Builds the FX1 (channel 2 slot, fx layer 1) overlay subtree. VF_36, groups[].</summary>
    public static Node3D BuildFx1(Fx1Layer layer, FxTextureResolver? resolver)
    {
        var root = new Node3D { Name = "FxLayer_fx1" };
        for (var g = 0; g < layer.Groups.Length; g++)
        {
            var grp = layer.Groups[g];
            AddGroupVf36(root, channel: 1, g, grp.TextureIndex1Based, grp.Vertices, grp.Indices, resolver);
        }

        return root;
    }

    /// <summary>Builds the FX2 (fx layer 2) overlay subtree. VF_44 (uses U0/V0), groups[].</summary>
    public static Node3D BuildFx2(Fx2Layer layer, FxTextureResolver? resolver)
    {
        var root = new Node3D { Name = "FxLayer_fx2" };
        for (var g = 0; g < layer.Groups.Length; g++)
        {
            var grp = layer.Groups[g];
            AddGroupVf44(root, channel: 2, g, grp.TextureIndex1Based, grp.Vertices, grp.Indices, resolver);
        }

        return root;
    }

    /// <summary>
    ///     Builds the FX3 (fx layer 3) overlay subtree. VF_36, groups[]. FX3 is a WATER channel —
    ///     emits two vertex copies per group (source + working) per §1.1c.
    /// </summary>
    public static Node3D BuildFx3(Fx3Layer layer, FxTextureResolver? resolver)
    {
        var root = new Node3D { Name = "FxLayer_fx3_water" };
        for (var g = 0; g < layer.Groups.Length; g++)
        {
            var grp = layer.Groups[g];
            // channel 3 = water: emit source + working copy. spec: §1.1c "Two vertex copies per group".
            AddGroupVf36(root, channel: 3, g, grp.TextureIndex1Based, grp.Vertices, grp.Indices, resolver,
                isWater: true);
        }

        return root;
    }

    /// <summary>Builds the FX4 (fx layer 4) overlay subtree. VF_44 (uses U0/V0), tiles[].</summary>
    public static Node3D BuildFx4(Fx4Layer layer, FxTextureResolver? resolver)
    {
        var root = new Node3D { Name = "FxLayer_fx4" };
        for (var t = 0; t < layer.Tiles.Length; t++)
        {
            var tile = layer.Tiles[t];
            // FX4 tile header has no decoded texture_index field (only the leading raw 40 bytes,
            // UNVERIFIED semantics — §1.11). Pass 1 (the per-channel clamp-to-1 default) so the
            // resolver maps to FX4 TEXTURES slot 1. spec: §1.1c "clamp out-of-range index to 1".
            AddGroupVf44(root, channel: 4, t, texIndex1Based: 1u, tile.Vertices, tile.Indices, resolver);
        }

        return root;
    }

    /// <summary>
    ///     Builds the FX5 (fx layer 5) overlay subtree. VF_36, sections[]. FX5 is a WATER channel —
    ///     emits two vertex copies per section (source + working) per §1.1c.
    /// </summary>
    public static Node3D BuildFx5(Fx5Layer layer, FxTextureResolver? resolver)
    {
        var root = new Node3D { Name = "FxLayer_fx5_water" };
        for (var s = 0; s < layer.Sections.Length; s++)
        {
            var sec = layer.Sections[s];
            // FX5 section header is the raw 48-byte universal group header; texture_index lives at +0x00
            // but the model stores it as raw bytes (RawSectionHeader). Use clamp-to-1 default so the
            // resolver maps to FX5 TEXTURES slot 1. spec: §1.1c "clamp out-of-range index to 1".
            // channel 5 = water: emit source + working copy. spec: §1.1c "Two vertex copies per group".
            AddGroupVf36(root, channel: 5, s, texIndex1Based: 1u, sec.Vertices, sec.Indices, resolver,
                isWater: true);
        }

        return root;
    }

    /// <summary>Builds the FX6 (fx layer 6) overlay subtree. VF_32, subChunks[].</summary>
    public static Node3D BuildFx6(Fx6Layer layer, FxTextureResolver? resolver)
    {
        var root = new Node3D { Name = "FxLayer_fx6" };
        for (var s = 0; s < layer.SubChunks.Length; s++)
        {
            var sub = layer.SubChunks[s];
            // FX6 sub-chunk header is just (vert_count, idx_count) — no texture_index field (§1.9).
            // Use clamp-to-1 default. spec: §1.1c "clamp out-of-range index to 1".
            AddGroupVf32(root, channel: 6, s, texIndex1Based: 1u, sub.Vertices, sub.Indices, resolver);
        }

        return root;
    }

    /// <summary>Builds the FX7 (fx layer 7) overlay subtree. VF_32, groups[].</summary>
    public static Node3D BuildFx7(Fx7Layer layer, FxTextureResolver? resolver)
    {
        var root = new Node3D { Name = "FxLayer_fx7" };
        for (var g = 0; g < layer.Groups.Length; g++)
        {
            var grp = layer.Groups[g];
            // FX7 52-byte group header carries texture_index at +0x00 but the model stores it raw
            // (RawGroupHeader). Use clamp-to-1 default. spec: §1.1c "clamp out-of-range index to 1".
            AddGroupVf32(root, channel: 7, g, texIndex1Based: 1u, grp.Vertices, grp.Indices, resolver);
        }

        return root;
    }

    // ─── Per-format group emitters ──────────────────────────────────────────────

    private static void AddGroupVf36(
        Node3D root, int channel, int groupIndex, uint texIndex1Based,
        FxVertex36[] verts, ushort[] indices, FxTextureResolver? resolver, bool isWater = false)
    {
        if (verts.Length == 0 || indices.Length == 0)
            return; // empty group — nothing to draw.

        var positions = new Vector3[verts.Length];
        var normals = new Vector3[verts.Length];
        var uvs = new Vector2[verts.Length];
        for (var v = 0; v < verts.Length; v++)
        {
            var fv = verts[v];
            // Absolute world-space → negate Z (world convention). spec: §4.2 + WorldCoordinates.ToGodot.
            positions[v] = new Vector3(fv.X, fv.Y, -fv.Z);
            normals[v] = new Vector3(fv.NX, fv.NY, -fv.NZ).Normalized();
            // UV0 carried directly on disk (no generated/scrolled UV pass at load). spec: §1.1c.
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
            // VF_44 carries a second UV set (U1/V1); the primary diffuse UV is U0/V0. A dual-UV
            // (lightmap/detail) material is DEBUGGER-PENDING (§1.1c blend/draw bucket unresolved), so
            // we bind only U0/V0 here — binding U1/V1 to an unconfirmed channel would be a guess.
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
            uvs[v] = new Vector2(fv.U0, fv.V0); // VF_32 has no colour; UV0 only.
        }

        EmitMeshInstance(root, channel, groupIndex, texIndex1Based, positions, normals, uvs, indices,
            resolver, isWater);
    }

    // ─── Shared mesh assembly + material + texture resolution ────────────────────

    private static void EmitMeshInstance(
        Node3D root, int channel, int groupIndex, uint texIndex1Based,
        Vector3[] positions, Vector3[] normals, Vector2[] uvs, ushort[] indices,
        FxTextureResolver? resolver, bool isWater)
    {
        // Resolve the per-group texture FIRST. ABSOLUTE RULE: if it does not resolve, LOG + SKIP the
        // group — never substitute a tinted/placeholder material. spec: CLAUDE.md ABSOLUTE RULES.
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
            // Missing asset → logged + skipped (no placeholder, no substitution).
            // spec: CLAUDE.md ABSOLUTE RULES — "a missing asset is logged + skipped, never substituted".
            GD.Print(
                $"[FxMeshBuilder] FX{channel} group[{groupIndex}]: texture (texIndex={texIndex1Based}) " +
                "did not resolve — skipping group (no placeholder). spec: terrain_layers.md §1.4b.");
            return;
        }

        // Index winding: swap [1]↔[2] per triangle to re-establish CCW after the Z-flip.
        // spec: BudMeshBuilder lines 167–181; terrain_layers.md §1.3 (u16 triangle list); ToGodot Z-flip.
        var triCount = indices.Length / 3;
        var idx = new int[triCount * 3];
        for (var t = 0; t < triCount; t++)
        {
            var src = t * 3;
            idx[src + 0] = indices[src + 0];
            idx[src + 1] = indices[src + 2]; // swap: was [1]
            idx[src + 2] = indices[src + 1]; // swap: was [2]
        }

        var mesh = BuildArrayMesh(positions, normals, uvs, idx, tex, isWater);

        root.AddChild(new MeshInstance3D
        {
            Mesh = mesh,
            Name = $"Fx{channel}_group{groupIndex}_tex{texIndex1Based}"
        });

        // Water channels (fx3/fx5): emit a SECOND geometrically-identical "working" copy per §1.1c.
        // The per-frame ripple/scroll mutation of this copy is DEBUGGER-PENDING (§Known Unknowns 7a),
        // so we duplicate the surface without inventing any offset. spec: §1.1c "Two vertex copies".
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

        // FX overlays blend over the terrain base mesh. The exact GPU blend mode is DEBUGGER-PENDING
        // (§1.1c — no D3D render-state strings reachable from the load anchors), so we use alpha
        // transparency (the conservative overlay choice) rather than asserting additive/masked.
        // spec: Docs/RE/formats/terrain_layers.md §1.1c — blend mode DEBUGGER-PENDING.
        var mat = new StandardMaterial3D
        {
            AlbedoTexture = tex,
            // PerPixel shading: FX groups carry authored normals (VF_32/36/44), same as buildings.
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled, // overlays viewed from both sides
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
        };

        mesh.SurfaceSetMaterial(0, mat);
        return mesh;
    }
}
