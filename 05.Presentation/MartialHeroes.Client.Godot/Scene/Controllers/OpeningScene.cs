using Godot;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Screens;
using MartialHeroes.Shared.Kernel.Enums;
using NewOpeningWindow = MartialHeroes.Client.Godot.Ui.Scenes.Opening.OpeningWindow;

namespace MartialHeroes.Client.Godot.Scene.Controllers;

/// <summary>
/// State 3 — Opening. Builds the post-login intro window (Ui/Scenes substrate). There is NO
/// auto-finish: the slideshow holds panel 4 indefinitely and the ONLY exit is an explicit skip
/// (Enter/ESC/Space or the skip button), which persists OPENNING/SKIP=1 and advances to Select.
///
/// <para>Uses <see cref="NewOpeningWindow"/> on the Ui/Scenes substrate.</para>
///
/// spec: Docs/RE/specs/intro_sequence.md §3.1 (no auto-finish; skip is the sole exit — CAMPAIGN 16);
/// Docs/RE/specs/client_runtime.md §7.3.
/// </summary>
public sealed partial class OpeningScene : StubSceneController
{
    private SceneHost? _host;
    private ScreenHost? _screenHost;
    private ClientContext? _ctx;
    private FrontEndAudio? _audio;
    private NewOpeningWindow? _opening;
    private bool _advanceRequested;

    /// <inheritdoc/>
    public override EngineSceneState State => EngineSceneState.Opening;

    /// <inheritdoc/>
    public override void OnEnter(SceneHost host)
    {
        Name = $"Scene{(int)State}_{State}";
        _host = host;

        // Resolve the HudAtlasLibrary from the composition root.
        // spec: Docs/RE/specs/intro_sequence.md §1 — textures loaded via VFS atlas. SAMPLE-VERIFIED.
        _ctx = GetNodeOrNull<ClientContext>("/root/ClientContext");

        _screenHost = new ScreenHost { Name = "OpeningScreenHost" };
        AddChild(_screenHost);

        _audio = new FrontEndAudio { Name = "OpeningFrontEndAudio" };
        AddChild(_audio);

        // Build the new Ui/Scenes substrate OpeningWindow.
        // Atlas comes from ClientContext.HudAtlas (Phase-A substrate, shared handle).
        _opening = new NewOpeningWindow
        {
            Name = "OpeningWindow",
            Atlas = _ctx?.HudAtlas,
            Audio = _audio,
        };
        _opening.IntroFinished += OnIntroFinished;
        _screenHost.SetScreen(_opening);

        GD.Print("[OpeningScene] State 3 built.");
        // Opening has NO self-advance: the only exit is an explicit skip. spec: Docs/RE/specs/intro_sequence.md §2.4.
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