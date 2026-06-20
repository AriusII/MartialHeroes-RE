namespace MartialHeroes.Client.Domain.Simulation.Simulation;

/// <summary>
///     The five in-world camera view modes (plus the out-of-world select preview).
///     spec: Docs/RE/specs/camera_movement.md §A.1 (view-mode table).
/// </summary>
/// <remarks>
///     Each in-world mode is one manipulator class sharing a common base; "switch view mode" enables the
///     chosen manipulator and disables the others (§A.2). The <see cref="Select" /> mode is the
///     character-select / create-preview camera, not part of the in-world set. spec: camera_movement.md §A.1/§A.2.
/// </remarks>
public enum CameraMode : byte
{
    /// <summary>
    ///     Over-the-shoulder follow camera (the default). Orbits the player; runs terrain collision. spec:
    ///     camera_movement.md §A.1 (Third).
    /// </summary>
    Third = 0,

    /// <summary>
    ///     First-person (eye at the player head); yaw/pitch look, no follow distance, no terrain clamp. spec:
    ///     camera_movement.md §A.1 (First).
    /// </summary>
    First = 1,

    /// <summary>
    ///     Fixed-angle tracking; follows position but never rotates around the player; no terrain collision. spec:
    ///     camera_movement.md §A.1 (Static).
    /// </summary>
    Static = 2,

    /// <summary>
    ///     Orbit camera for the gamble / betting minigame UI; yaw orbit, no terrain collision. spec: camera_movement.md
    ///     §A.1 (Gamble).
    /// </summary>
    Gamble = 3,

    /// <summary>Scripted / cutscene camera; data-driven path; the player loses control. spec: camera_movement.md §A.1 (Event).</summary>
    Event = 4,

    /// <summary>Character-select / create-preview camera (out-of-world). spec: camera_movement.md §A.1 (Select).</summary>
    Select = 5
}