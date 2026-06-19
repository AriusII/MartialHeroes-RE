// World/BudMeshBuilder.cs
//
// Converts a BudScene (Assets.Parsers) into Godot ArrayMesh nodes WITHOUT using GltfDocument.
// This avoids the native Godot GLB importer that crashes on our generated GLBs.
//
// Each BudObject becomes one MeshInstance3D child of the returned Node3D root.
//
// Coordinate conventions (D3D9 left-handed → Godot right-handed):
//   Negate Z (WORLD convention). BUD vertices are pre-baked into ABSOLUTE world-space, so
//   they must use the same world handedness flip as TerrainNode and the character node —
//   negate Z — NOT the mesh-local negate-X convention. Negating X on absolute coordinates
//   mirrors every building across the world X axis, placing them ~1000+ units away from the
//   terrain they belong to (the gray-world bug: BUD landed at (-X, +Z) instead of (+X, -Z)).
//   spec: Docs/RE/formats/terrain_scene.md §Coordinate system — "positions are pre-baked
//         into absolute world-space": CONFIRMED.
//   spec: Helpers/WorldCoordinates.ToGodot — (x, y, z) -> (x, y, -z).
//
// Winding order:
//   The spec confirms CCW front faces (terrain_scene.md §3.2.4). Negating Z (the world handedness
//   flip applied to both positions and normals) inverts the triangle winding: CCW legacy → CW
//   Godot. We correct this by swapping indices[1] ↔ indices[2] per triangle (re-establishes CCW
//   in Godot space). The material is also rendered double-sided (CullMode.Disabled) because thin
//   architectural props are often viewed from both sides.
//   spec: Docs/RE/formats/terrain_scene.md §Index array — "CONFIRMED CCW front faces."
//   spec: Helpers/WorldCoordinates.ToGodot — (x, y, z) → (x, y, -z). CONFIRMED.
//
// Per-vertex colour:
//   BudVertex is exactly 8 × f32 (pos XYZ, normal XYZ, uv UV = 32 bytes on disk).
//   The .bud format carries NO per-vertex colour channel.
//   spec: Docs/RE/formats/terrain_scene.md §Vertex record (32 bytes) — 8 fields, none is colour:
//         CONFIRMED.
//   Therefore no vertex-colour modulation is applied here.
//
// Material strategy:
//   Buildings are 3D structures with authored unit normals; dynamic shading (ShadingModeEnum
//   PerPixel, the Godot default) is the correct choice — it provides depth via the
//   DirectionalLight3D key light and the ambient term in WorldEnvironment.
//
//   CullMode is set to Disabled (double-sided) because BUD props are thin architectural
//   surfaces that can be viewed from both sides; single-sided culling would silently hide
//   faces that the artist intended to be visible.
//
//   When no texture resolves (offline or missing asset), a modest emission is added to the
//   neutral grey fallback so the placeholder is clearly visible and not near-black regardless
//   of light angle. Emission is NOT applied when a real texture is present — the texture
//   speaks for itself under dynamic lighting. This is an empirical visual safety net, not a
//   spec value.

using Godot;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// Builds Godot <see cref="ArrayMesh"/> geometry directly from a parsed <see cref="BudScene"/>,
/// bypassing the native Godot GLB importer (<c>GltfDocument.AppendFromBuffer</c>).
///
/// Returns a <see cref="Node3D"/> root node whose children are one <see cref="MeshInstance3D"/>
/// per <see cref="BudObject"/>. An optional <see cref="ImageTexture"/> resolver may be supplied
/// to apply diffuse textures; if null or if the resolver returns null, a neutral grey material
/// with a small emission boost is applied so placeholder geometry is never near-black.
///
/// spec: Docs/RE/formats/terrain_scene.md §Vertex record (32 bytes): CONFIRMED.
/// spec: Docs/RE/formats/terrain_scene.md §Index array — u16 indices, triangle list, CCW front faces: CONFIRMED.
/// spec: Helpers/WorldCoordinates.ToGodot — Z-flip inverts winding; corrected by swapping indices[1]↔indices[2]: CONFIRMED.
/// </summary>
public static class BudMeshBuilder
{
    /// <summary>
    /// Builds a scene graph from all objects in <paramref name="scene"/>.
    ///
    /// <paramref name="textureResolver"/> is called with a 1-based tex_id; it may return null,
    /// in which case the mesh renders with a neutral untextured material (slightly emissive grey).
    ///
    /// spec: Docs/RE/formats/terrain_scene.md §Object header — tex_id u32 @ +0x01: PARTIAL.
    /// </summary>
    public static Node3D Build(BudScene scene, Func<uint, ImageTexture?>? textureResolver = null)
    {
        var root = new Node3D { Name = "BudSceneNode" };

        for (int i = 0; i < scene.Objects.Length; i++)
        {
            BudObject obj = scene.Objects[i];

            try
            {
                MeshInstance3D? inst = BuildObject(obj, i, textureResolver);
                if (inst is not null)
                    root.AddChild(inst);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[BudMeshBuilder] Failed to build BudObject[{i}] (texId={obj.TexId}): {ex.Message}");
                // Continue with remaining objects — a single bad object must not abort the scene.
            }
        }

        return root;
    }

    // -------------------------------------------------------------------------
    // Per-object mesh construction
    // -------------------------------------------------------------------------

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

        int vertCount = obj.Vertices.Length;
        var positions = new Vector3[vertCount];
        var normals = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];

        for (int v = 0; v < vertCount; v++)
        {
            BudVertex bv = obj.Vertices[v];

            // Handedness flip for ABSOLUTE world-space geometry: negate Z (world convention).
            // spec: Helpers/WorldCoordinates.ToGodot — (x, y, z) -> (x, y, -z).
            // spec: Docs/RE/formats/terrain_scene.md §Vertex record — pos_x @ +0x00: CONFIRMED.
            positions[v] = new Vector3(bv.PosX, bv.PosY, -bv.PosZ);

            // Normals: negate Z for the same handedness flip.
            // The stored normals are unit-length outward-facing surface normals.
            // spec: Docs/RE/formats/terrain_scene.md §Vertex record — normal_x @ +0x0C: CONFIRMED.
            // spec: terrain_scene.md §3.2.2 — "magnitude 1.0 to within 1e-7": CONFIRMED.
            normals[v] = new Vector3(bv.NormalX, bv.NormalY, -bv.NormalZ).Normalized();

            // UV coordinates are passed through directly.
            // The spec notes observed UVs in the range ~24–29 (tiled world-scale, not normalised
            // [0,1]). These tile the diffuse texture across the building surface, which is the
            // correct intent for D3D9 WRAP sampling. Godot's default REPEAT addressing handles
            // this identically.
            // spec: Docs/RE/formats/terrain_scene.md §Vertex record — uv_u @ +0x18, uv_v @ +0x1C: CONFIRMED.
            // spec: terrain_scene.md §3.2.2 — "tiled world-scale floats, may exceed [0,1]": CONFIRMED.
            uvs[v] = new Vector2(bv.UvU, bv.UvV);
        }

        // Build index array — swap indices 1 and 2 per triangle to correct winding after the Z-flip.
        //
        // The spec confirms CCW winding for front faces (terrain_scene.md §3.2.4 "CONFIRMED CCW").
        // In legacy D3D9 (left-handed), CCW is front-facing; Godot (right-handed) also treats CCW
        // as front-facing. HOWEVER, negating Z (the world handedness flip applied to positions and
        // normals) inverts the triangle winding: a CCW legacy triangle becomes CW in Godot.
        //
        // Without a winding correction the "front" and "back" faces swap. With CullMode.Disabled
        // the geometry still renders from both sides, but Godot's Forward Plus renderer computes the
        // geometric face normal from the winding for shading (normal-map, shadow, deferred G-buffer
        // face direction checks). If the geometric face normal is inverted relative to the stored
        // vertex normal, per-pixel shading on top faces (normals pointing +Y) can produce near-zero
        // irradiance for a sun coming from above (the geometric normal shadow test fires negatively
        // for the "back" winding). This causes top/roof faces to render near-black even though
        // CullMode.Disabled keeps them geometrically visible.
        //
        // FIX: swap the two latter indices of each triangle (indices[1] ↔ indices[2]) to re-invert
        // the winding back to CCW in Godot space. This realigns the geometric face normal with the
        // stored per-vertex normal and restores correct irradiance on top faces.
        //
        // spec: Docs/RE/formats/terrain_scene.md §Index array — "CONFIRMED counter-clockwise (CCW)
        //   for front faces." §Vertex record — Z negation (world convention, port-side): CONFIRMED.
        // spec: Helpers/WorldCoordinates.ToGodot — (x, y, z) → (x, y, -z): CONFIRMED.
        int triCount = obj.Indices.Length / 3;
        var indices = new int[triCount * 3];
        for (int t = 0; t < triCount; t++)
        {
            int src = t * 3;
            int dst = t * 3;
            // Swap indices 1 and 2 to correct CCW winding after the Z-flip.
            // Before flip-Z: CCW in legacy (D3D9 left-handed) = front face.
            // After flip-Z:  CCW becomes CW in Godot (right-handed) = back face.
            // Swap re-establishes CCW = front face in Godot space.
            // spec: terrain_scene.md §3.2.4 — CCW front faces; WorldCoordinates — negate Z. CONFIRMED.
            indices[dst + 0] = obj.Indices[src + 0];
            indices[dst + 1] = obj.Indices[src + 2]; // swap: was [1]
            indices[dst + 2] = obj.Indices[src + 1]; // swap: was [2]
        }

        // Assemble ArrayMesh.
        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        // -------------------------------------------------------------------------
        // Material
        //
        // ShadingMode: PerPixel (Godot default / dynamic lit). Buildings are 3D
        //   structures with authored normals; dynamic shading is correct. The
        //   DirectionalLight3D key light + WorldEnvironment ambient will illuminate
        //   them with proper depth.  We do NOT use Unshaded here (unlike TerrainNode
        //   which bakes lighting into vertex colours).
        //
        // CullMode: Disabled (double-sided). BUD props are thin surfaces that can be
        //   viewed from either side; disable back-face culling so no face disappears
        //   from any camera angle.
        //
        // TextureFilter: LinearWithMipmaps — reduces aliasing on the tiled UVs.
        //
        // Fallback (no texture): emit a modest amount of the neutral grey so the
        //   placeholder geometry is clearly visible even if the key light hits it at a
        //   shallow angle. Emission value 0.25 is empirical — enough to read on screen,
        //   not so bright it overwhelms a real scene.
        //   When a real texture is present, NO emission is added; the texture reads under
        //   the normal PBR light/ambient path without artificial brightening.
        // -------------------------------------------------------------------------
        var mat = new StandardMaterial3D();
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel; // dynamic lit, depth from normals
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled; // double-sided — thin arch. surfaces
        mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps;

        bool hasTexture = false;

        if (textureResolver is not null)
        {
            try
            {
                ImageTexture? tex = textureResolver(obj.TexId);
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
        }

        if (!hasTexture)
        {
            // Neutral grey placeholder: visible under dynamic lighting.
            // Add a small emission so it never goes near-black at grazing light angles.
            // Emission value is empirical (visual safety net, not a spec constant).
            mat.AlbedoColor = new Color(0.6f, 0.6f, 0.6f, 1f);
            mat.EmissionEnabled = true;
            mat.Emission = new Color(0.15f, 0.15f, 0.15f, 1f); // empirical: modest floor, ~0.25 effective
        }

        mesh.SurfaceSetMaterial(0, mat);

        return new MeshInstance3D
        {
            Mesh = mesh,
            Name = $"BudObject_{objIndex}_tex{obj.TexId}",
        };
    }
}