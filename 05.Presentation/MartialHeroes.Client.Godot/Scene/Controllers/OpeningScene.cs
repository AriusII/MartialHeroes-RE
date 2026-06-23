using Godot;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Scenes;
using MartialHeroes.Shared.Kernel.Enums;
using NewOpeningWindow = MartialHeroes.Client.Godot.Ui.Scenes.Opening.OpeningWindow;

namespace MartialHeroes.Client.Godot.Scene.Controllers;

public sealed partial class OpeningScene : StubSceneController
{
    private bool _advanceRequested;
    private FrontEndAudio? _audio;
    private ClientContext? _ctx;
    private SceneHost? _host;
    private NewOpeningWindow? _opening;
    private ScreenHost? _screenHost;

    public override EngineSceneState State => EngineSceneState.Opening;

    public override void OnEnter(SceneHost host)
    {
        Name = $"Scene{(int)State}_{State}";
        _host = host;

        _ctx = GetNodeOrNull<ClientContext>("/root/ClientContext");

        _screenHost = new ScreenHost { Name = "OpeningScreenHost" };
        AddChild(_screenHost);

        _audio = new FrontEndAudio { Name = "OpeningFrontEndAudio" };
        AddChild(_audio);

        _opening = new NewOpeningWindow
        {
            Name = "OpeningWindow",
            Atlas = _ctx?.HudAtlas,
            Audio = _audio
        };
        _opening.IntroFinished += OnIntroFinished;
        _screenHost.SetScreen(_opening);

        GD.Print("[OpeningScene] State 3 built.");
    }

    public override void _ExitTree()
    {
        if (_opening is not null)
        {
            _opening.IntroFinished -= OnIntroFinished;
            _opening = null;
        }
    }

    private void OnIntroFinished()
    {
        if (_advanceRequested) return;

        _advanceRequested = true;

        if (_host?.CurrentState != EngineSceneState.Opening)
        {
            GD.Print(
                "[OpeningScene] IntroFinished arrived after SceneHost already left Opening; skipping duplicate advance.");
            return;
        }

        GD.Print("[OpeningScene] IntroFinished → requesting state-3 advance to Select. " +
                 "spec: client_runtime.md §7.5.1; intro_sequence.md §3.1.");
        _host.CallDeferred(SceneHost.MethodName.Advance);
    }
}