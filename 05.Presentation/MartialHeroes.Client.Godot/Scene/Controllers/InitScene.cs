using Godot;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.Scene.Controllers;

/// <summary>
///     State 0 — Initialisation. The legacy entry point performs its one-time init (mount the VFS, create
///     the render device/window, read the boot config) BEFORE the <c>while(1)</c> scene switch, then enters
///     the loop with <c>GameState = 1</c> (Login). The one-time-init analogue is the
///     <see cref="MartialHeroes.Client.Godot.Autoload.ClientContext" /> autoload composition root (VFS mounted,
///     service graph built) which runs before this scene is shown; state 0 therefore completes immediately
///     and writes the next state = Login.
/// </summary>
/// <remarks>
///     This automatic 0→1 advance is what drives the <b>interactive (windowed/player)</b> boot to the Login
///     window — it does NOT depend on the headless developer auto-walk. Without it the host would sit on the
///     empty Init scene forever in windowed mode (a blank/grey window), because the auto-walk only runs
///     headless or under <c>MH_SCENE_AUTOWALK=1</c>. The advance is deferred so it runs after this
///     <see cref="OnEnter" /> returns, never re-entering <c>ShowSceneFor</c> while the host is mid-dispatch.
///     spec: Docs/RE/specs/client_runtime.md §7.1 / §7.3 (init precedes the loop; the loop starts at Login).
/// </remarks>
public sealed partial class InitScene : StubSceneController
{
    /// <inheritdoc />
    public override EngineSceneState State => EngineSceneState.Init;

    /// <inheritdoc />
    public override void OnEnter(SceneHost host)
    {
        base.OnEnter(host); // names the node + logs the state-0 entry

        // State 0 init is complete (VFS + service graph mounted by the ClientContext autoload before
        // this scene). The legacy entry point now writes GameState = 1 and enters the scene loop at
        // Login. Deferred so it runs after OnEnter returns (no re-entrant ShowSceneFor).
        // spec: Docs/RE/specs/client_runtime.md §7.1 (init precedes the loop; loop begins at Login).
        GD.Print("[InitScene] State 0 init complete (VFS + services via ClientContext autoload) — "
                 + "writing GameState = 1 and advancing to Login. spec: client_runtime.md §7.1/§7.3.");
        host.CallDeferred(SceneHost.MethodName.Advance);
    }
}