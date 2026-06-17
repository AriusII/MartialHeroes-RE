using Godot;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Screens;
using MartialHeroes.Client.Godot.Ui.Scenes.Load;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.Scene.Controllers;

/// <summary>
/// State 2 — Load. Builds the Diamond_LoadingWindow analogue and advances only after the
/// loading window reports preload-done + the 500 ms grace; the application scene machine then
/// chooses 2→3 or 2→4 from its <c>OPENNING/SKIP</c> policy.
/// spec: Docs/RE/specs/client_runtime.md §7.3; Docs/RE/specs/frontend_scenes.md §2L / §9.1.
/// </summary>
public sealed partial class LoadScene : StubSceneController
{
    private ClientContext? _ctx;
    private SceneHost? _host;
    private ScreenHost? _screenHost;
    private LoadingWindow? _loading;
    private CancellationTokenSource? _loadCts;
    private bool _advanceRequested;

    /// <inheritdoc/>
    public override EngineSceneState State => EngineSceneState.Load;

    /// <inheritdoc/>
    public override void OnEnter(SceneHost host)
    {
        Name = $"Scene{(int)State}_{State}";
        _host = host;
        _ctx = GetNodeOrNull<ClientContext>("/root/ClientContext");

        _screenHost = new ScreenHost { Name = "LoadScreenHost" };
        AddChild(_screenHost);

        // Build the new Ui/Scenes substrate LoadingWindow.
        // Atlas comes from ClientContext.HudAtlas (Phase-A substrate, shared handle).
        // PlayOwnCue=false when the LoadOrchestrator is present — it routes BGM 920100100
        // through GodotLoadingSoundSink → AudioService so the cue is not doubled.
        // spec: Docs/RE/specs/frontend_scenes.md §2L / §9.1; sound.md §15.6a. CODE-CONFIRMED.
        _loading = new LoadingWindow
        {
            Name            = "LoadingWindow",
            Atlas           = _ctx?.HudAtlas,
            ProgressProvider = _ctx?.LoadOrchestrator is { } orch
                ? () => Math.Clamp(orch.ProgressQuotient, 0, 100)
                : null,
            PlayOwnCue = _ctx?.LoadOrchestrator is null,
        };
        _loading.LoadingComplete += OnLoadingComplete;
        _screenHost.SetScreen(_loading);

        StartCoreLoad();

        GD.Print("[LoadScene] State 2 Load built LoadingWindow (Ui/Scenes substrate); preload started; " +
                 "completion will advance via SceneHost.Advance(). spec: frontend_scenes.md §2L / §9.1.");
    }

    public override void _ExitTree()
    {
        if (_loading is not null)
        {
            _loading.LoadingComplete -= OnLoadingComplete;
        }

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }

    private void StartCoreLoad()
    {
        if (_ctx?.LoadOrchestrator is not { } load)
        {
            GD.Print(
                "[LoadScene] ClientContext.LoadOrchestrator unavailable — LoadingScreen fallback simulation remains active.");
            return;
        }

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();

        try
        {
            load.Start(_loadCts.Token);
            // Start() synchronously applies the OPENNING/SKIP decision to SceneMachine.SkipOpening.
            // Godot does not choose the route; it only reports the decision for diagnostics.
            // spec: Docs/RE/specs/resource_pipeline.md §2.5; client_runtime.md §7.5.1.
            GD.Print($"[LoadScene] LoadOrchestrator started; destination={load.DestinationAfterLoad}, " +
                     $"skipOpening={load.ShouldSkipOpening}. spec: resource_pipeline.md §2.");
            _ = AwaitCoreLoadAsync(load.Completion);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LoadScene] LoadOrchestrator.Start failed: {ex.Message}; completing Load scene defensively.");
            _loading?.CallDeferred(LoadingWindow.MethodName.CompleteExternalLoad);
        }
    }

    private async Task AwaitCoreLoadAsync(Task completion)
    {
        try
        {
            await completion.ConfigureAwait(false);
            Callable.From(() =>
            {
                if (_loading is not null && IsInstanceValid(_loading))
                {
                    GD.Print("[LoadScene] LoadOrchestrator completion observed → LoadingWindow 500 ms grace.");
                    _loading.CompleteExternalLoad();
                }
            }).CallDeferred();
        }
        catch (OperationCanceledException)
        {
            GD.Print(
                "[LoadScene] LoadOrchestrator completion wait cancelled; orchestrator is settled and can restart on re-enter.");
        }
        catch (Exception ex)
        {
            Callable.From(() =>
            {
                GD.PrintErr(
                    $"[LoadScene] LoadOrchestrator faulted: {ex.Message}; advancing after loading-window grace.");
                _loading?.CompleteExternalLoad();
            }).CallDeferred();
        }
    }

    private void OnLoadingComplete()
    {
        if (_advanceRequested)
        {
            return;
        }

        _advanceRequested = true;

        if (_host?.CurrentState != EngineSceneState.Load)
        {
            GD.Print(
                "[LoadScene] LoadingComplete arrived after SceneHost already left Load; skipping duplicate advance.");
            return;
        }

        GD.Print(
            "[LoadScene] LoadingComplete → requesting state-2 advance; SceneStateMachine chooses Opening vs Select. " +
            "spec: client_runtime.md §7.5.1.");
        _host.CallDeferred(SceneHost.MethodName.Advance);
    }
}