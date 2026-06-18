using Godot;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.Scene.Controllers;

/// <summary>
/// Base for the CAMPAIGN-15 stub scene controllers. Each concrete stub fixes only its
/// <see cref="ISceneController.State"/>; the <see cref="SceneHost"/> owns all walk/advance policy.
/// A stub logs its entry, reproducing the legacy "build + run" case body at skeleton fidelity.
/// spec: Docs/RE/specs/client_runtime.md §7.3.
/// </summary>
/// <remarks>
/// Each stub is filled with its real scene in its own increment (Login first, then Load, Opening,
/// Select, World, Quit/Error). Keeping them as separate thin files makes that fill a localised,
/// one-writer-per-path edit.
/// </remarks>
public abstract partial class StubSceneController : Node, ISceneController
{
    /// <inheritdoc/>
    public abstract EngineSceneState State { get; }

    /// <inheritdoc/>
    public Node Node => this;

    /// <inheritdoc/>
    public virtual void OnEnter(SceneHost host)
    {
        Name = $"Scene{(int)State}_{State}";
        GD.Print($"[SceneHost] state {(int)State} {State} — stub entered.");
    }
}