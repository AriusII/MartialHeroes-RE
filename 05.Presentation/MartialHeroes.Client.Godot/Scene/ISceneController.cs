using Godot;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.Scene;

public interface ISceneController
{
    EngineSceneState State { get; }

    Node Node { get; }

    void OnEnter(SceneHost host);
}