using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.Scene.Controllers;

/// <summary>State 0 — Initialisation (stub). spec: Docs/RE/specs/client_runtime.md §7.3 (state 0).</summary>
public sealed partial class InitScene : StubSceneController
{
    /// <inheritdoc/>
    public override EngineSceneState State => EngineSceneState.Init;
}