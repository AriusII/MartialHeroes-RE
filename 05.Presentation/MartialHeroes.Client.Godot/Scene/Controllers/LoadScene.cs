using Godot;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Screens;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.Scene.Controllers;

/// <summary>
/// State 2 — Load. Builds the Diamond_LoadingWindow analogue and advances only after the
/// loading screen reports preload-done + the 500 ms grace; the application scene machine then
/// chooses 2→3 or 2→4 from its <c>OPENNING/SKIP</c> policy.
/// spec: Docs/RE/specs/client_runtime.md §7.3; Docs/RE/specs/frontend_scenes.md §2L / §9.1.
/// </summary>
public sealed partial class LoadScene : StubSceneController
{
    private ClientContext? _ctx;
    private SceneHost? _host;
    private ScreenHost? _screenHost;
    private UiAssetLoader? _sharedAssets;
    private LoadingScreen? _loading;
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

        _sharedAssets = UiAssetLoader.Open();

        _screenHost = new ScreenHost { Name = "LoadScreenHost" };
        AddChild(_screenHost);

        _loading = new LoadingScreen
        {
            Name = "LoadingScreen",
            SharedAssets = _sharedAssets,
            ExternalCompletion = _ctx?.LoadOrchestrator is not null,
            PercentProvider = _ctx?.LoadOrchestrator is { } orchestrator
                ? () => Math.Clamp(orchestrator.ProgressQuotient, 0, 100)
                : null,
            PlayOwnBgm = _ctx?.LoadOrchestrator is null,
        };
        _loading.LoadingComplete += OnLoadingComplete;
        _screenHost.SetScreen(_loading);

        StartCoreLoad();

        GD.Print("[LoadScene] State 2 Load built real LoadingScreen; preload started by screen " +
                 "and completion will advance via SceneHost.Advance(). spec: frontend_scenes.md §2L / §9.1.");
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

        _sharedAssets?.Dispose();
        _sharedAssets = null;
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
            _loading?.CallDeferred(LoadingScreen.MethodName.CompleteExternalLoad);
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
                    GD.Print("[LoadScene] LoadOrchestrator completion observed → LoadingScreen 500 ms grace.");
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
                    $"[LoadScene] LoadOrchestrator faulted: {ex.Message}; advancing after loading-screen grace.");
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