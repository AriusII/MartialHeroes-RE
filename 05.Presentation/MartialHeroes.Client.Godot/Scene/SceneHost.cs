using Godot;
using MartialHeroes.Client.Application.Contracts.Scene;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Scene.Controllers;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.Scene;

public sealed partial class SceneHost : Node
{
    private readonly Dictionary<EngineSceneState, Func<ISceneController>> _factories = new()
    {
        [EngineSceneState.Init] = static () => new InitScene(),
        [EngineSceneState.Login] = static () => new LoginScene(),
        [EngineSceneState.Load] = static () => new LoadScene(),
        [EngineSceneState.Opening] = static () => new OpeningScene(),
        [EngineSceneState.Select] = static () => new SelectScene(),
        [EngineSceneState.InGame] = static () => new InGameScene(),
        [EngineSceneState.Quit] = static () => new QuitScene(),
        [EngineSceneState.Error] = static () => new ErrorScene()
    };

    private ISceneController? _current;
    private EngineSceneState? _currentState;

    private SceneStateMachine _machine = null!;

    public EngineSceneState CurrentState => _machine.Current.State;

    public override void _Ready()
    {
        _machine = ResolveSceneMachine();
        GD.Print($"[SceneHost] ready — boot state {(int)_machine.Current.State} {_machine.Current.State}.");
        ShowSceneFor(_machine.Current.State);
    }

    public void Advance()
    {
        var before = _machine.Current.State;

        if (!_machine.AdvanceScene())
        {
            GD.Print($"[SceneHost] state {(int)before} {before} — no further advance (spine settled).");
            return;
        }

        ShowSceneFor(_machine.Current.State);
    }

    public void SyncToCurrentState()
    {
        ShowSceneFor(_machine.Current.State);
    }

    private void ShowSceneFor(EngineSceneState state)
    {
        if (_current is not null && _currentState == state) return;

        if (_current is not null)
        {
            _current.Node.QueueFree();
            _current = null;
            _currentState = null;
        }

        if (!_factories.TryGetValue(state, out var factory))
        {
            GD.PushError($"[SceneHost] no controller registered for state {(int)state} {state}.");
            return;
        }

        var scene = factory();
        AddChild(scene.Node);
        _current = scene;
        _currentState = state;
        scene.OnEnter(this);
    }

    private SceneStateMachine ResolveSceneMachine()
    {
        var ctx = GetNodeOrNull<ClientContext>("/root/ClientContext");
        if (ctx?.SceneMachine is { } machine) return machine;

        throw new InvalidOperationException(
            "[SceneHost] ClientContext.SceneMachine is unavailable. " +
            "The ClientContext autoload must be present and fully initialised before SceneHost._Ready runs.");
    }
}