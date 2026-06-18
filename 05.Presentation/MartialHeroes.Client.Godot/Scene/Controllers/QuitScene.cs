using Godot;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.Scene.Controllers;

/// <summary>
/// State 6 — Quit. The faithful counterpart of the legacy case-6 body: it runs the engine shutdown
/// and converges on the shared exit tail (field-0 → 8) that returns from <c>WinMain</c> — i.e. it
/// ends the process. The <see cref="SceneStateMachine"/> has already routed here (RequestQuit / the
/// 3/100 result code 0 / a login fatal); this controller performs the graceful application exit.
/// spec: Docs/RE/specs/client_runtime.md §7.3 (state 6 = engine shutdown), §7.5.1 (6 → 8 exit tail).
/// </summary>
public sealed partial class QuitScene : StubSceneController
{
    /// <inheritdoc/>
    public override EngineSceneState State => EngineSceneState.Quit;

    /// <inheritdoc/>
    public override void OnEnter(SceneHost host)
    {
        Name = $"Scene{(int)State}_{State}";
        GD.Print("[QuitScene] State 6 Quit — engine shutdown; converging on the exit tail "
                 + "(field-0 → 8 → return from WinMain). spec: client_runtime.md §7.3 / §7.5.1.");

        // The legacy state-6 body tears the engine down and the shared exit tail returns from WinMain.
        // The faithful Godot equivalent is a graceful application quit; deferred so the current frame
        // and any teardown listeners complete first.
        GetTree()?.CallDeferred(SceneTree.MethodName.Quit);
    }
}