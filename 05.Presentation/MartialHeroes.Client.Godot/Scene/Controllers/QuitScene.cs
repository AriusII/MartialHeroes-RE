using Godot;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.Scene.Controllers;

public sealed partial class QuitScene : StubSceneController
{
    public override EngineSceneState State => EngineSceneState.Quit;

    public override void OnEnter(SceneHost host)
    {
        Name = $"Scene{(int)State}_{State}";
        GD.Print("[QuitScene] State 6 Quit — engine shutdown; converging on the exit tail "
                 + "(field-0 → 8 → return from WinMain). spec: client_runtime.md §7.3 / §7.5.1.");

        GetTree()?.CallDeferred(SceneTree.MethodName.Quit);
    }
}