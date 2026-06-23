using Godot;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.Scene.Controllers;

public sealed partial class InitScene : StubSceneController
{
    public override EngineSceneState State => EngineSceneState.Init;

    public override void OnEnter(SceneHost host)
    {
        base.OnEnter(host);

        GD.Print("[InitScene] State 0 init complete (VFS + services via ClientContext autoload) — "
                 + "writing GameState = 1 and advancing to Login. spec: client_runtime.md §7.1/§7.3.");
        host.CallDeferred(SceneHost.MethodName.Advance);
    }
}