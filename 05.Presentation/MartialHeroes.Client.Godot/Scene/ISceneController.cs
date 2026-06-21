using Godot;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.Scene;

/// <summary>
///     One engine scene (state 0..7). The <see cref="SceneHost" /> instantiates exactly one controller
///     at a time and adds its node to the tree, mirroring the legacy "build + run" case body: each
///     <c>switch</c> case constructs its handler, runs while live, then tears it down.
///     spec: Docs/RE/specs/client_runtime.md §7.3.
/// </summary>
/// <remarks>
///     During the CAMPAIGN-15 scene-by-scene rebuild each controller starts as a thin stub and is filled
///     in its own increment (Login first, then Load, Opening, Select, World, Quit/Error). A controller is
///     a Godot <see cref="Node" /> so the host can simply <c>AddChild</c> / <c>QueueFree</c> it.
/// </remarks>
public interface ISceneController
{
    /// <summary>The engine state this controller renders.</summary>
    EngineSceneState State { get; }

    /// <summary>The Godot node the host adds to / removes from the tree for this scene.</summary>
    Node Node { get; }

    /// <summary>
    ///     Called once when this scene becomes live, after its node is in the tree. The host is passed
    ///     so the scene can request the engine-internal advance (the case body's next-state write) when
    ///     it finishes. spec: Docs/RE/specs/client_runtime.md §7.2 (commit → re-dispatch).
    /// </summary>
    void OnEnter(SceneHost host);
}