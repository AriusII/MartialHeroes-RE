// World/SknMeshBuilder.cs
//
// Converts a SkinnedMesh (Assets.Parsers) into a static-pose Godot ArrayMesh WITHOUT using GltfDocument.
//
// STATUS (CAMPAIGN 9 L6): this static-pose builder is SUPERSEDED by SkinnedCharacterBuilder /
// SkinnedCharacterNode, which now render the full animated CPU-LBS rig (skin deforms + idle .mot
// plays without exploding — verified on the §8(d) g1/g2048 specimen trios). No live caller invokes
// SknMeshBuilder.Build any more (RealWorldRenderer routes through SkinnedCharacterBuilder). It is
// kept only as a minimal crash-free static fallback reference.
//
// Coordinate conventions (D3D9 left-handed → Godot right-handed):
//   ONE unified handedness conversion — the world Z-negate — applied via WorldCoordinates.SkinToGodot,
//   identical to the live skinned path (SkinnedCharacterNode). The previous ad-hoc per-asset X-negate
//   here was inconsistent with the skinned path and is removed: the spec mandates ONE conversion
//   applied uniformly so the static and skinned renders of the same mesh are oriented identically.
//   spec: Docs/RE/specs/skinning.md §8(b) — "Pick one handedness conversion — the world Z-negate —
//         and apply it uniformly … drop the ad-hoc per-asset X/Z flips for skinned characters."
//   spec: Docs/RE/formats/mesh.md §Vertex record — "pos_x stored second on disk at sub-offset 12": CONFIRMED.
//   spec: Docs/RE/formats/mesh.md §Vertex record — "normal_x stored first on disk at sub-offset 0": CONFIRMED.
//
// UV coordinates:
//   The parser already applies the v-flip: stored as 1.0f - uv_v_on_disk.
//   spec: Docs/RE/formats/mesh.md §Face record — uv_v: "engine applies 1.0 - uv_v". CONFIRMED.
//
// Winding order:
//   SknCorner[] from the parser is in D3D9 CW order (per GltfConverter). We apply CCW swap.
//   spec: Docs/RE/formats/mesh.md §Face table — corner order CW: CONFIRMED (via GltfConverter winding swap).

using Godot;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Helpers;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// Builds a static-pose Godot <see cref="ArrayMesh"/> from a <see cref="SkinnedMesh"/>,
/// bypassing <c>GltfDocument.AppendFromBuffer</c>.
///
/// Returns a <see cref="MeshInstance3D"/> ready to be added to the scene tree.
/// Animation (skinning at runtime) is a TODO; only the bind-pose geometry is rendered.
///
/// spec: Docs/RE/formats/mesh.md §Format: .skn — binary skinned mesh.
/// spec: Docs/RE/formats/mesh.md §Face record — 36 bytes (3 corners × 12 bytes each).
/// </summary>
public static class SknMeshBuilder
{
    /// <summary>
    /// Converts the skinned mesh into a static-pose <see cref="MeshInstance3D"/>.
    ///
    /// An optional <paramref name="albedoTexture"/> is applied if provided.
    ///
    /// TODO: wire runtime skinning once the Skeleton/AnimationPlayer pipeline is designed.
    /// </summary>
    public static MeshInstance3D? Build(SkinnedMesh mesh, ImageTexture? albedoTexture = null)
    {
        try
        {
            return BuildInternal(mesh, albedoTexture);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SknMeshBuilder] Failed to build mesh '{mesh.Name}': {ex.Message}");
            return null;
        }
    }

    // -------------------------------------------------------------------------
    // Mesh construction
    // -------------------------------------------------------------------------

    private static MeshInstance3D? BuildInternal(SkinnedMesh skn, ImageTexture? albedoTexture)
    {
        // The face list drives the index array. Each face has 3 SknCorners; each corner
        // carries a VertexIndex into Positions/Normals, plus UVs.
        // spec: Docs/RE/formats/mesh.md §Face table — FaceCount × 3 corners: CONFIRMED.
        int faceCount = (int)skn.FaceCount;
        if (faceCount == 0 || skn.Positions.Length == 0)
        {
            GD.Print($"[SknMeshBuilder] Mesh '{skn.Name}' has no geometry.");
            return null;
        }

        // We build an unindexed (flat) vertex array to avoid rebuilding a shared-vertex index
        // while still supporting per-corner UVs (corners are unique in .skn).
        // Each triangle contributes 3 unique vertices.
        int totalVerts = faceCount * 3;
        var positions = new Vector3[totalVerts];
        var normals = new Vector3[totalVerts];
        var uvs = new Vector2[totalVerts];

        SknCorner[] corners = skn.Corners;
        Vec3[] srcPos = skn.Positions;
        Vec3[] srcNrm = skn.Normals;

        for (int f = 0; f < faceCount; f++)
        {
            // CW→CCW winding swap: emit corners in order 0, 2, 1 per triangle.
            // spec: Docs/RE/formats/mesh.md §Face table — D3D9 CW winding, swap for Godot CCW.
            int cBase = f * 3;
            int vBase = f * 3;

            // Corner indices into the output: 0→vBase+0, 2→vBase+1, 1→vBase+2.
            int[] swap = [cBase + 0, cBase + 2, cBase + 1];

            for (int j = 0; j < 3; j++)
            {
                SknCorner corner = corners[swap[j]];
                uint vi = corner.VertexIndex;

                if (vi >= (uint)srcPos.Length)
                {
                    // Corrupt index — emit a zero vertex and continue.
                    GD.PrintErr($"[SknMeshBuilder] Face {f} corner {j}: VertexIndex {vi} out of range " +
                                $"(posCount={srcPos.Length}) — using origin.");
                    vi = 0;
                }

                Vec3 p = srcPos[vi];
                Vec3 n = srcNrm.Length > vi ? srcNrm[vi] : new Vec3(0f, 1f, 0f);

                // ONE unified handedness conversion (world Z-negate), identical to the live skinned
                // path (SkinnedCharacterNode.DeformAndUpload). spec: Docs/RE/specs/skinning.md §8(b).
                // spec: Docs/RE/formats/mesh.md §Vertex record — pos_x @ sub-offset 12: CONFIRMED.
                var (gx, gy, gz) = WorldCoordinates.SkinToGodot(p.X, p.Y, p.Z);
                positions[vBase + j] = new Vector3(gx, gy, gz);

                // Normal: same single conversion.
                // spec: Docs/RE/formats/mesh.md §Vertex record — normal_x @ sub-offset 0: CONFIRMED.
                var (nx, ny, nz) = WorldCoordinates.SkinToGodot(n.X, n.Y, n.Z);
                normals[vBase + j] = new Vector3(nx, ny, nz).Normalized();

                // UV: already v-flipped by the parser (1.0 - uv_v_on_disk).
                // spec: Docs/RE/formats/mesh.md §Face record — uv_v: "engine applies 1.0 - uv_v". CONFIRMED.
                uvs[vBase + j] = new Vector2(corner.UvU, corner.UvV);
            }
        }

        // Assemble the ArrayMesh (no explicit index array — vertices are already in triangle order).
        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        // No index array — flat unindexed layout.

        var arrayMesh = new ArrayMesh();
        arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        // Apply material.
        var mat = new StandardMaterial3D();
        mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps;
        if (albedoTexture is not null)
        {
            mat.AlbedoTexture = albedoTexture;
        }
        else
        {
            // Neutral grey — visible without texture so the shape is recognisable.
            mat.AlbedoColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        }

        arrayMesh.SurfaceSetMaterial(0, mat);

        return new MeshInstance3D
        {
            Mesh = arrayMesh,
            Name = $"SknMesh_{skn.Name}",
        };
    }
}