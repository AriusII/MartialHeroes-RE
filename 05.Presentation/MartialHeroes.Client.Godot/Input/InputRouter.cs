using Godot;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Helpers;
using MartialHeroes.Shared.Kernel.Numerics;
using GodotInput = Godot.Input;
using GodotDictionary = Godot.Collections.Dictionary;

namespace MartialHeroes.Client.Godot.Input;

/// <summary>
/// Translates raw Godot input events into use-case calls on
/// <see cref="MartialHeroes.Client.Application.UseCases.IApplicationUseCases"/>.
///
/// PASSIVE: this class has ZERO game-rule authority. It captures physical device events
/// and converts them to intent calls. It never moves a node directly, never applies
/// formulas, and never mutates domain state. The result of the intent arrives later as
/// an <see cref="IClientEvent"/> on the event bus, which then causes a visual update.
///
/// Input actions expected in project.godot (Godot input map):
///   "use_skill_1" through "use_skill_6" — hotbar slots
///
/// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — "captures raw input and
///       turns each into a use-case call".
/// </summary>
public sealed partial class InputRouter : Node
{
    private ClientContext _clientContext = null!;

    // Camera reference for screen→world raycasting (click-to-move).
    private Camera3D? _camera;

    /// <summary>Called by GameLoop._Ready before any input is processed.</summary>
    public void Initialise(ClientContext context)
    {
        _clientContext = context;
    }

    public override void _Ready()
    {
        // Defer camera lookup until after the scene is fully loaded.
        CallDeferred(MethodName.LookupCamera);
    }

    private void LookupCamera()
    {
        // Walk the scene tree to find the first Camera3D; adjust this path when the
        // camera rig scene is added.
        _camera = GetViewport().GetCamera3D();
    }

    public override void _UnhandledInput(InputEvent evt)
    {
        // Only route inputs when we are in World state.
        if (_clientContext.StateMachine.Current != ClientState.World)
        {
            return;
        }

        if (evt is InputEventMouseButton mouseBtn
            && mouseBtn.ButtonIndex == MouseButton.Left
            && mouseBtn.Pressed)
        {
            HandleClickToMove(mouseBtn.Position);
            GetViewport().SetInputAsHandled();
            return;
        }

        // Hotbar: skill slots 1–6 mapped to input actions "use_skill_1" … "use_skill_6".
        // GodotInput alias avoids collision with the MartialHeroes.Client.Godot.Input namespace.
        for (uint slot = 0; slot < 6; slot++)
        {
            string actionName = $"use_skill_{slot + 1}";
            if (InputMap.HasAction(actionName) && GodotInput.IsActionJustPressed(actionName))
            {
                // Slot is cast to byte (IApplicationUseCases.UseSkillAsync slot param is byte).
                // No known targets at input time; pass empty arrays for both target groups.
                // spec: IApplicationUseCases.UseSkillAsync; Docs/RE/packets/2-52_use_skill.yaml.
                _ = _clientContext.UseCases.UseSkillAsync(
                    (byte)slot,
                    ReadOnlyMemory<uint>.Empty,
                    ReadOnlyMemory<uint>.Empty);
                GetViewport().SetInputAsHandled();
            }
        }
    }

    // -------------------------------------------------------------------------
    // Click-to-move
    // -------------------------------------------------------------------------

    /// <summary>
    /// Raycasts from the click position into the scene and forwards the world-space
    /// hit position to <see cref="MartialHeroes.Client.Application.UseCases.IApplicationUseCases.RequestMoveAsync"/>.
    ///
    /// The conversion from Godot Vector3 → Q16.16 happens HERE at the presentation boundary
    /// (Vector3Fixed.FromFloat), matching the spec boundary rule.
    /// spec: Vector3Fixed.FromFloat() — presentation boundary only.
    /// </summary>
    private void HandleClickToMove(Vector2 screenPos)
    {
        if (_camera is null)
        {
            return;
        }

        // Obtain the 3D world from the camera's viewport to run the raycast.
        // InputRouter is a plain Node (not Node3D), so we go through the camera's world.
        World3D world3D = _camera.GetWorld3D();
        PhysicsDirectSpaceState3D spaceState = world3D.DirectSpaceState;

        Vector3 from = _camera.ProjectRayOrigin(screenPos);
        Vector3 to = from + _camera.ProjectRayNormal(screenPos) * 1000f;

        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.CollideWithAreas = false;

        // GodotDictionary alias avoids ambiguity between Godot.Collections and the implicit
        // System.Collections namespace when the file's own namespace starts with MartialHeroes.Client.Godot.
        GodotDictionary result = spaceState.IntersectRay(query);

        if (result.Count == 0)
        {
            return;
        }

        // Extract the world-space hit point.
        var hitPoint = result["position"].AsVector3();

        // Convert Godot world space → legacy world space, then → Q16.16.
        // spec: WorldCoordinates helpers — coordinate system bridging.
        (float lx, float ly, float lz) = WorldCoordinates.ToLegacy(hitPoint.X, hitPoint.Y, hitPoint.Z);
        Vector3Fixed fixedTarget = Vector3Fixed.FromFloat(lx, ly, lz);

        // Forward the intent to the Application layer. Running = false (walk on click).
        _ = _clientContext.UseCases.RequestMoveAsync(fixedTarget, running: false);
    }
}