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
}