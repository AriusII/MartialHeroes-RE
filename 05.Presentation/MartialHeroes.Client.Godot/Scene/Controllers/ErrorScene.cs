using Godot;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Shared.Kernel.Enums;
using MartialHeroes.Shared.Kernel.State;

namespace MartialHeroes.Client.Godot.Scene.Controllers;

/// <summary>
/// State 7 — Error. The faithful counterpart of the legacy case-7 body: it builds an error message
/// from the engine-state error fields (sub-state = reason, error-detail = result code), surfaces it
/// in a modal, writes the error to the log, then converges on the shared exit tail (field-0 → 8) that
/// returns from <c>WinMain</c>. The <see cref="SceneStateMachine"/> routes here via ToError /
/// OnDisconnected / OnLoginWindowConfigFailed (sub 1) / OnLoginDeviceInitFailed (sub 3) / the 3/100
/// error codes.
/// spec: Docs/RE/specs/client_runtime.md §7.3 (state 7 = error string + error.log + modal → exit),
/// §7.5.1 (7 → 8 exit tail), §7.7 (state-7 writes error.log before the dialog).
/// </summary>
public sealed partial class ErrorScene : StubSceneController
{
    // The legacy modal blocks until the user acknowledges, then the exit tail returns. The port shows
    // the panel for a readable beat, then performs the graceful application exit. spec: §7.3 / §7.5.1.
    private const double DismissSeconds = 4.0;

    /// <inheritdoc/>
    public override EngineSceneState State => EngineSceneState.Error;

    /// <inheritdoc/>
    public override void OnEnter(SceneHost host)
    {
        Name = $"Scene{(int)State}_{State}";

        ClientContext? ctx = GetNodeOrNull<ClientContext>("/root/ClientContext");
        GameState gs = ctx?.SceneMachine.Current ?? GameState.Initial;

        // reason = sub-state (1 = login window-config fail, 3 = device-init fail, 2 = load-time
        // disconnect, 8 = generic), detail = the offending result code. spec: §7.5.1 / §7.5.2.
        string message = $"A fatal error occurred.\nreason {gs.SubState}, detail {gs.ErrorDetail}";

        // State 7 writes error.log before the dialog (§7.7); the port relays it through the engine log.
        GD.PushError($"[ErrorScene] State 7 Error — reason={gs.SubState} detail={gs.ErrorDetail}. "
                     + "spec: client_runtime.md §7.3 / §7.5.1 / §7.7.");

        BuildModal(message);

        // Converge on the exit tail after the modal has been shown. spec: §7.5.1 (7 → 8 → return).
        SceneTreeTimer timer = GetTree().CreateTimer(DismissSeconds);
        timer.Timeout += () => GetTree()?.Quit();
    }

    private void BuildModal(string message)
    {
        var layer = new CanvasLayer { Name = "ErrorLayer" };

        var dim = new ColorRect { Name = "Dim", Color = new Color(0f, 0f, 0f, 0.65f) };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(dim);

        var label = new Label
        {
            Name = "ErrorText",
            Text = message,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(label);

        AddChild(layer);
    }
}