using Godot;
using MartialHeroes.Client.Application.Contracts.Scene;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Scenes;
using MartialHeroes.Client.Godot.Ui.Scenes.Load;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.Scene.Controllers;

public sealed partial class LoadScene : StubSceneController
{
    private bool _advanceRequested;
    private ClientContext? _ctx;
    private SceneHost? _host;
    private CancellationTokenSource? _loadCts;
    private LoadingWindow? _loading;
    private ScreenHost? _screenHost;
    private bool _syncingScene;

    public override EngineSceneState State => EngineSceneState.Load;

    public override void OnEnter(SceneHost host)
    {
        Name = $"Scene{(int)State}_{State}";
        _host = host;
        _ctx = GetNodeOrNull<ClientContext>("/root/ClientContext");

        _screenHost = new ScreenHost { Name = "LoadScreenHost" };
        AddChild(_screenHost);

        _loading = new LoadingWindow
        {
            Name = "LoadingWindow",
            Atlas = _ctx?.HudAtlas,
            ProgressProvider = _ctx?.LoadOrchestrator is { } orch
                ? () => Math.Clamp(orch.ProgressQuotient, 0, 100)
                : null,
            PlayOwnCue = _ctx?.LoadOrchestrator is null
        };
        _loading.LoadingComplete += OnLoadingComplete;
        _screenHost.SetScreen(_loading);

        StartCoreLoad();

        GD.Print("[LoadScene] State 2 Load built LoadingWindow; preload started. " +
                 "spec: frontend_scenes.md §2L / §9.1.");
    }

    public override void _ExitTree()
    {
        if (_loading is not null) _loading.LoadingComplete -= OnLoadingComplete;

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }

    public override void _Process(double delta)
    {
        if (_ctx is null || _syncingScene) return;

        while (_ctx.EventBus.Reader.TryRead(out var evt))
            switch (evt)
            {
                case SceneStateChangedEvent stateChange when stateChange.Next.State != State:
                    GD.Print(
                        $"[LoadScene] SceneStateChangedEvent {stateChange.Previous.State}→{stateChange.Next.State}; " +
                        "out-of-band committed transition (e.g. 3/100 enter rejection → char-select) — " +
                        "calling SyncToCurrentState. spec: client_runtime.md §7.5.2; login_flow.md §1 step 9.");
                    _syncingScene = true;
                    _host?.CallDeferred(SceneHost.MethodName.SyncToCurrentState);
                    return;
            }
    }

    private void StartCoreLoad()
    {
        if (_ctx?.LoadOrchestrator is not { } load)
        {
            GD.Print("[LoadScene] ClientContext.LoadOrchestrator unavailable.");
            return;
        }

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();

        load.Start(_loadCts.Token);
        GD.Print($"[LoadScene] LoadOrchestrator started; destination={load.DestinationAfterLoad}, " +
                 $"skipOpening={load.ShouldSkipOpening}. spec: resource_pipeline.md §2.");
        _ = AwaitCoreLoadAsync(load.Completion);
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
            GD.Print("[LoadScene] LoadOrchestrator completion wait cancelled.");
        }
        catch (Exception ex)
        {
            Callable.From(() =>
            {
                GD.PrintErr($"[LoadScene] LoadOrchestrator faulted: {ex.Message}; routing to error.");
            }).CallDeferred();
        }
    }

    private void OnLoadingComplete()
    {
        if (_advanceRequested) return;

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