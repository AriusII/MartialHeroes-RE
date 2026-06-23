using Godot;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Shared.Kernel.Enums;
using MartialHeroes.Shared.Kernel.State;

namespace MartialHeroes.Client.Godot.Scene.Controllers;

public sealed partial class ErrorScene : StubSceneController
{
    private const double DismissSeconds = 4.0;

    public override EngineSceneState State => EngineSceneState.Error;

    public override void OnEnter(SceneHost host)
    {
        Name = $"Scene{(int)State}_{State}";

        var ctx = GetNodeOrNull<ClientContext>("/root/ClientContext");
        var gs = ctx?.SceneMachine.Current ?? GameState.Initial;

        var message = $"A fatal error occurred.\nreason {gs.SubState}, detail {gs.ErrorDetail}";

        GD.PushError($"[ErrorScene] State 7 Error — reason={gs.SubState} detail={gs.ErrorDetail}. "
                     + "spec: client_runtime.md §7.3 / §7.5.1 / §7.7.");

        BuildModal(message);

        var timer = GetTree().CreateTimer(DismissSeconds);
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
            VerticalAlignment = VerticalAlignment.Center
        };
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(label);

        AddChild(layer);
    }
}