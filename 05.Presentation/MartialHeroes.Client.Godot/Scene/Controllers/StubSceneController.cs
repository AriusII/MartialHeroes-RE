using Godot;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.Scene.Controllers;

public abstract partial class StubSceneController : Node, ISceneController
{
    public abstract EngineSceneState State { get; }

    public Node Node => this;

    public virtual void OnEnter(SceneHost host)
    {
        Name = $"Scene{(int)State}_{State}";
        GD.Print($"[SceneHost] state {(int)State} {State} — stub entered.");
    }
}