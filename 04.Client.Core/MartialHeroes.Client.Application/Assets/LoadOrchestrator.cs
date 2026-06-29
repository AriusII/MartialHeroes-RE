using MartialHeroes.Client.Application.Contracts.Scene;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Application.Assets;

public enum LoadOrchestratorState
{
    NotStarted,
    Running,
    Completed,
    Cancelled,
    Faulted
}

public sealed class LoadOrchestrator(
    SceneStateMachine scene,
    ILoadResourceSource resourceSource,
    IOpeningSkipReader openingSkipReader,
    ILoadingSoundSink? soundSink = null,
    ICatalogueAssembler? catalogueAssembler = null)
{
    private const int
        LoadingSoundCueId = 920100100;

    private const long LegacyProgressDenominatorBytes = 9_395_240;
    private readonly object _gate = new();

    private readonly IOpeningSkipReader _openingSkipReader =
        openingSkipReader ?? throw new ArgumentNullException(nameof(openingSkipReader));

    private readonly ILoadResourceSource _resourceSource =
        resourceSource ?? throw new ArgumentNullException(nameof(resourceSource));

    private readonly SceneStateMachine _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    private Task _completion = Task.CompletedTask;
    private long _cumulativeBytes;
    private bool _startedAsReload;

    public LoadOrchestratorState State { get; private set; } = LoadOrchestratorState.NotStarted;

    public Task Completion
    {
        get
        {
            lock (_gate)
            {
                return _completion;
            }
        }
    }

    public long CumulativeBytes => Volatile.Read(ref _cumulativeBytes);

    public int ProgressQuotient => (int)(CumulativeBytes / LegacyProgressDenominatorBytes);

    public EngineSceneState DestinationAfterLoad =>
        _scene.SkipOpening ? EngineSceneState.Select : EngineSceneState.Opening;

    public bool ShouldSkipOpening => DestinationAfterLoad == EngineSceneState.Select;

    public void Start(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (State == LoadOrchestratorState.Running)
                return;

            if (_scene.Current.State != EngineSceneState.Load)
                throw new InvalidOperationException("State-2 load can only start while SceneStateMachine is in Load.");

            _cumulativeBytes = 0;
            _startedAsReload = _scene.LoadIsReload;
            _scene.SkipOpening = _openingSkipReader.ReadSkipOpening();

            soundSink?.PlayLooping(LoadingSoundCueId);
            State = LoadOrchestratorState.Running;
            _completion = Task.Run(() => RunWorkerAsync(cancellationToken), cancellationToken);
        }
    }

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_startedAsReload)
                await LoadAndTrackAsync(LoadResourcePlan.MessageCataloguePath, cancellationToken).ConfigureAwait(false);

            foreach (var path in LoadResourcePlan.BootWorkerPaths)
                await LoadAndTrackAsync(path, cancellationToken).ConfigureAwait(false);

            State = LoadOrchestratorState.Completed;
        }
        catch (OperationCanceledException)
        {
            State = LoadOrchestratorState.Cancelled;
            throw;
        }
    }

    private async ValueTask LoadAndTrackAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = await _resourceSource.LoadAsync(path, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes > 0)
            Interlocked.Add(ref _cumulativeBytes, bytes);
        catalogueAssembler?.TryAssemble(path);
    }
}