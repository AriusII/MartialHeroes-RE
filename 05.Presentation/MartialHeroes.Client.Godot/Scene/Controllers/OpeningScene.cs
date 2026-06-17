using Godot;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Screens;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.Scene.Controllers;

/// <summary>
/// State 3 — Opening. Builds the standalone post-login intro window. There is NO auto-finish: the
/// slideshow loops panel 4 indefinitely and the ONLY exit is an explicit skip (Enter/ESC/Space or
/// the skip button), which persists OPENNING/SKIP=1 and advances to Select. In headless the
/// SceneHost developer auto-walk advances it (no user present).
/// spec: Docs/RE/specs/intro_sequence.md §3.1 (no auto-finish; skip is the sole exit — CAMPAIGN 16);
/// Docs/RE/specs/client_runtime.md §7.3.
/// </summary>
public sealed partial class OpeningScene : StubSceneController
{
    private SceneHost? _host;
    private ScreenHost? _screenHost;
    private UiAssetLoader? _sharedAssets;
    private FrontEndAudio? _audio;
    private OpeningWindow? _opening;
    private bool _advanceRequested;

    /// <inheritdoc/>
    public override EngineSceneState State => EngineSceneState.Opening;

    /// <inheritdoc/>
    public override void OnEnter(SceneHost host)
    {
        Name = $"Scene{(int)State}_{State}";
        _host = host;

        _sharedAssets = UiAssetLoader.Open();

        _screenHost = new ScreenHost { Name = "OpeningScreenHost" };
        AddChild(_screenHost);

        _audio = new FrontEndAudio { Name = "OpeningFrontEndAudio" };
        AddChild(_audio);

        _opening = new OpeningWindow
        {
            Name = "OpeningWindow",
            SharedAssets = _sharedAssets,
            Audio = _audio,
        };
        _opening.IntroFinished += OnIntroFinished;
        _screenHost.SetScreen(_opening);

        GD.Print("[OpeningScene] State 3 Opening built OpeningWindow: openning_001..004 slideshow, " +
                 "openning_scenario crawl, skip action 100, BGM 910061000. spec: intro_sequence.md §1–§4.");
        // NO headless auto-finish here. The SceneHost developer auto-walk (MaybeAutoWalk) advances
        // non-interactive states including Opening when running headless. The Opening scene has NO
        // self-advance — the ONLY exit is an explicit skip (keyboard Enter/ESC/Space or click on
        // action-100 skip button). spec: Docs/RE/specs/intro_sequence.md §3.1 (CAMPAIGN 16 correction).
    }

    public override void _ExitTree()
    {
        if (_opening is not null)
        {
            _opening.IntroFinished -= OnIntroFinished;
            _opening = null;
        }

        _sharedAssets?.Dispose();
        _sharedAssets = null;
    }

    private void OnIntroFinished()
    {
        if (_advanceRequested)
        {
            return;
        }

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