// World/BudMeshBuilder.cs
//
// Converts a BudScene (Assets.Parsers) into Godot ArrayMesh nodes WITHOUT using GltfDocument.
// This avoids the native Godot GLB importer that crashes on our generated GLBs.
//
// Each BudObject becomes one MeshInstance3D child of the returned Node3D root.
//
// Coordinate conventions (D3D9 left-handed → Godot right-handed):
//   Negate X: legacy uses a left-handed coordinate system where X points right.
//   spec: Docs/RE/formats/terrain_scene.md §Coordinate system — "positions are pre-baked
//         into absolute world-space": CONFIRMED.
//   spec: WorldCoordinates.ToGodot — negate X for D3D9→Godot handedness flip.
//   NOTE: TerrainNode and BudSceneGltfConverter both negate X (not Z) for BUD geometry.
//         This is consistent with the D3D9 left-handed world where the X axis is mirrored.
//
// Winding order:
//   The on-disk index array is CW (D3D9 default). We swap to CCW for Godot by reversing
//   each triangle's vertex order (indices[i+1] ↔ indices[i+2]).
//   spec: Docs/RE/formats/terrain_scene.md §Index array — "u16 indices, triangle list": CONFIRMED.
//   spec: BudSceneGltfConverter — same CCW swap applied in the GLB export path.

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
/// is applied instead.
///
/// spec: Docs/RE/formats/terrain_scene.md §Vertex record (32 bytes): CONFIRMED.
/// spec: Docs/RE/formats/terrain_scene.md §Index array — u16 indices, triangle list: CONFIRMED.
/// </summary>
public static class BudMeshBuilder
{
    /// <summary>
    /// Builds a scene graph from all objects in <paramref name="scene"/>.
    ///
    /// <paramref name="textureResolver"/> is called with a 1-based tex_id; it may return null,
    /// in which case the mesh renders with a neutral untextured material.
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

            // Handedness flip: negate X (D3D9 left-handed → Godot right-handed).
            // spec: WorldCoordinates.ToGodot — negate X for BUD geometry.
            // spec: Docs/RE/formats/terrain_scene.md §Vertex record — pos_x @ +0x00: CONFIRMED.
            positions[v] = new Vector3(-bv.PosX, bv.PosY, bv.PosZ);

            // Normals: negate X for the same handedness flip.
            // spec: Docs/RE/formats/terrain_scene.md §Vertex record — normal_x @ +0x0C: CONFIRMED.
            normals[v] = new Vector3(-bv.NormalX, bv.NormalY, bv.NormalZ).Normalized();

            // UV coordinates are passed through directly.
            // spec: Docs/RE/formats/terrain_scene.md §Vertex record — uv_u @ +0x18, uv_v @ +0x1C: CONFIRMED.
            uvs[v] = new Vector2(bv.UvU, bv.UvV);
        }

        // Build index array with CW→CCW winding swap.
        // On-disk order: D3D9 CW. Godot expects CCW. Swap index[i+1] and index[i+2] per triangle.
        // spec: Docs/RE/formats/terrain_scene.md §Index array — "u16 indices, triangle list": CONFIRMED.
        int triCount = obj.Indices.Length / 3;
        var indices = new int[triCount * 3];
        for (int t = 0; t < triCount; t++)
        {
            int src = t * 3;
            int dst = t * 3;
            // CCW swap: keep i0, swap i1 and i2.
            indices[dst + 0] = obj.Indices[src + 0];
            indices[dst + 1] = obj.Indices[src + 2]; // swapped
            indices[dst + 2] = obj.Indices[src + 1]; // swapped
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

        // Apply material: textured if resolver provides one, otherwise neutral grey.
        var mat = new StandardMaterial3D();
        mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps;

        if (textureResolver is not null)
        {
            try
            {
                ImageTexture? tex = textureResolver(obj.TexId);
                if (tex is not null)
                {
                    mat.AlbedoTexture = tex;
                }
                else
                {
                    // No texture for this tex_id — render with a neutral flat colour.
                    mat.AlbedoColor = new Color(0.6f, 0.6f, 0.6f, 1f);
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[BudMeshBuilder] textureResolver threw for tex_id={obj.TexId}: {ex.Message}");
                mat.AlbedoColor = new Color(0.6f, 0.6f, 0.6f, 1f);
            }
        }
        else
        {
            mat.AlbedoColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        }

        mesh.SurfaceSetMaterial(0, mat);

        return new MeshInstance3D
        {
            Mesh = mesh,
            Name = $"BudObject_{objIndex}_tex{obj.TexId}",
        };
    }
}