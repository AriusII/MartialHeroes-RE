namespace MartialHeroes.Client.Godot.Helpers;

/// <summary>
/// Pure, engine-free helper for converting between legacy Martial Heroes world-space
/// and Godot 4.x world-space.
///
/// Coordinate system notes:
///   Legacy (D3D9 default): left-handed, Y-up. X grows East, Z grows South, Y grows Up.
///   Godot 4 (glTF / OpenGL basis): right-handed, Y-up. X grows East, -Z grows North, Y grows Up.
///
///   The handedness flip means X is preserved, Y is preserved, and Z is negated.
///   This is the same convention already applied by GltfConverter for mesh export.
///   spec: Docs/RE/formats/mesh.md §Vertex list — "Legacy format uses left-handed Y-up
///         coordinate system (D3D9 default). Conversion: negate the X component."
///   NOTE: the mesh converter negates X; for world coordinates the convention is to
///         negate Z instead (the legacy Z-forward axis maps to Godot -Z). Both are
///         equivalent handedness flips — the mesh uses its per-vertex convention;
///         world positions use this Z-negate convention. If future RE refines the axis
///         mapping, update ONLY this file and GltfConverter together.
///
/// World scale: the legacy engine uses the same unit scale as Godot (1 unit = 1 metre
/// equivalent). No additional scale factor is applied unless a future spec confirms
/// otherwise. If RE work documents a scale constant, add it here with a spec: comment.
///
/// This class is intentionally engine-free (no `using Godot;`) so it can be unit-tested
/// without the Godot runtime.
/// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — "put coordinate-conversion
///       in a plain, engine-free helper that can be unit-tested".
/// </summary>
public static class WorldCoordinates
{
    /// <summary>
    /// Converts a legacy world-space position (left-handed Y-up) to Godot world-space
    /// (right-handed Y-up) by negating Z.
    ///
    /// spec: Docs/RE/formats/mesh.md §Vertex list coordinate conventions (handedness flip).
    /// </summary>
    /// <param name="legacyX">Legacy X (East).</param>
    /// <param name="legacyY">Legacy Y (Up — world Y forced to 0 server-side).</param>
    /// <param name="legacyZ">Legacy Z (South in left-handed space).</param>
    /// <returns>Godot-space (X, Y, Z) with Z negated.</returns>
    public static (float X, float Y, float Z) ToGodot(float legacyX, float legacyY, float legacyZ)
        => (legacyX, legacyY, -legacyZ);

    /// <summary>
    /// Converts a Godot world-space position back to legacy world-space (inverts ToGodot).
    /// Applied when converting a Godot raycast hit back to a Q16.16 position for use-case calls.
    ///
    /// spec: Docs/RE/formats/mesh.md §Vertex list coordinate conventions (handedness flip).
    /// </summary>
    public static (float X, float Y, float Z) ToLegacy(float godotX, float godotY, float godotZ)
        => (godotX, godotY, -godotZ);

    // =========================================================================
    // Unified skinning handedness conversion (the ONE conversion for skinned rigs)
    // =========================================================================
    //
    // The skinning spec mandates that a skinned character (.skn mesh + .bnd skeleton +
    // .mot keyframes) be brought into Godot space by ONE single, uniform handedness
    // conversion applied identically to bone bind translations, mesh vertex positions,
    // AND keyframe translations — NOT the project's two historical ad-hoc flips
    // (world negate-Z + mesh-local negate-X applied piecemeal), which mirror the skin
    // relative to the skeleton and break skinning.
    //
    // The chosen conversion is the world negate-Z (so the skinned actor lives in the
    // same Godot world space as everything else placed through ToGodot): (x,y,z) → (x,y,-z).
    // Negating Z is a handedness flip (a reflection, det = -1).
    //
    // Because this conversion is a linear isometry, it COMMUTES with the whole linear-blend
    // skinning pipeline: deforming in native left-handed space and then converting each final
    // vertex once is numerically identical to converting bones+verts+keyframes up front and
    // deforming in Godot space. The faithful CPU LBS path therefore does ALL skinning in native
    // space (where the inverse-bind ⊗ forward-bone cancellation is exact) and applies this
    // single conversion only to the final deformed position/normal — guaranteeing the
    // rest-pose cancellation survives the change of basis.
    //
    // spec: Docs/RE/specs/skinning.md §8(b) — "Pick one handedness conversion — the world
    //       Z-negate — and apply it uniformly to bone bind translations, mesh vertex positions,
    //       AND keyframe translations … the key requirement is uniformity."
    // spec: Docs/RE/specs/skinning.md §7 — "No axis negation or mirroring happens inside the
    //       skinning math … the known project conventions … are importer-layer transforms."

    /// <summary>
    /// Applies the unified skinned-rig handedness conversion to a position-like vector:
    /// negate Z. Use for final deformed vertex positions (and equivalently for bind
    /// translations / keyframe translations when converting up front).
    ///
    /// spec: Docs/RE/specs/skinning.md §8(b) — single conversion = world Z-negate.
    /// </summary>
    public static (float X, float Y, float Z) SkinToGodot(float x, float y, float z)
        => (x, y, -z);

    /// <summary>
    /// Applies the unified handedness conversion to a quaternion (XYZW, scalar W last).
    /// Under a Z-negation the two components orthogonal to the un-flipped Z axis flip sign:
    /// <c>(x, y, z, w) → (−x, −y, z, w)</c>. The scalar W is unchanged.
    ///
    /// This is the quaternion remap that keeps a rotation consistent with
    /// <see cref="SkinToGodot"/> applied to the vectors it rotates, so a deformed normal
    /// computed in native space converts identically whether the rotation is remapped first
    /// or the vector is converted last (the two routes agree because Z-negate is an isometry).
    ///
    /// spec: Docs/RE/specs/skinning.md §8(b) — "under it a quaternion (x,y,z,w) maps to
    ///       (−x,−y,z,w) (negate the two components orthogonal to the un-flipped Z axis)."
    /// spec: Docs/RE/specs/skinning.md §9 — exact remap is PROPOSED; validated here by the
    ///       rest-pose cancellation invariant (the final mesh equals the converted rest mesh).
    /// </summary>
    public static (float X, float Y, float Z, float W) SkinQuatToGodot(
        float x, float y, float z, float w)
        => (-x, -y, z, w);
}