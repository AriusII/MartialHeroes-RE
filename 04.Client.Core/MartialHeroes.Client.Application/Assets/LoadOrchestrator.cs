using MartialHeroes.Client.Application.Scene;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Application.Assets;

public enum LoadOrchestratorState
{
    NotStarted,
    Running,
    Completed,
    Cancelled,
    Faulted,
}

/// <summary>
/// Engine-free analogue of state 2's LoadHandler: pre-decides Opening vs Select, starts the looping
/// loading SFX, and runs the fixed boot-resource worker behind the loading screen.
/// spec: Docs/RE/specs/resource_pipeline.md §2; Docs/RE/specs/client_runtime.md §7.3.
/// </summary>
public sealed class LoadOrchestrator
{
    private const int
        LoadingSoundCueId = 920100100; // spec: Docs/RE/specs/resource_pipeline.md §2.3; client_runtime.md §7.4.

    private const long LegacyProgressDenominatorBytes = 9_395_240; // spec: Docs/RE/specs/resource_pipeline.md §2.4.

    private readonly SceneStateMachine _scene;
    private readonly ILoadResourceSource _resourceSource;
    private readonly IOpeningSkipReader _openingSkipReader;
    private readonly ILoadingSoundSink? _soundSink;
    private readonly object _gate = new();
    private Task _completion = Task.CompletedTask;
    private long _cumulativeBytes;
    private bool _startedAsReload;

    public LoadOrchestrator(
        SceneStateMachine scene,
        ILoadResourceSource resourceSource,
        IOpeningSkipReader openingSkipReader,
        ILoadingSoundSink? soundSink = null)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _resourceSource = resourceSource ?? throw new ArgumentNullException(nameof(resourceSource));
        _openingSkipReader = openingSkipReader ?? throw new ArgumentNullException(nameof(openingSkipReader));
        _soundSink = soundSink;
    }

    public LoadOrchestratorState State { get; private set; } = LoadOrchestratorState.NotStarted;

    public Exception? Fault { get; private set; }

    public Task Completion
    {
        get
        {
            lock (_gate)
                return _completion;
        }
    }

    public long CumulativeBytes => Volatile.Read(ref _cumulativeBytes);

    public int ProgressQuotient => (int)(CumulativeBytes / LegacyProgressDenominatorBytes);

    /// <summary>
    /// Post-load destination: follows the <c>OPENNING/SKIP</c> gate unconditionally, even on a
    /// reload. A reload reaches Select only because <c>option.ini</c> already has <c>SKIP=1</c>
    /// after the first opening — NOT because the reload forces Select.
    /// spec: Docs/RE/specs/resource_pipeline.md §2.5; client_runtime.md §7.5.1.
    /// </summary>
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

            Fault = null;
            _cumulativeBytes = 0;
            _startedAsReload = _scene.LoadIsReload;
            // Re-read OPENNING/SKIP unconditionally on every state-2 entry, including reloads.
            // The binary case-2 body re-reads the INI key every time it runs; there is no
            // reload-specific "skip the INI read" path. spec: resource_pipeline.md §2.5 (CAMPAIGN 16).
            _scene.SkipOpening = _openingSkipReader.ReadSkipOpening();

            _soundSink?.PlayLooping(LoadingSoundCueId);
            State = LoadOrchestratorState.Running;
            _completion = Task.Run(() => RunWorkerAsync(cancellationToken));
        }
    }

    public bool AdvanceSceneWhenComplete()
    {
        if (State != LoadOrchestratorState.Completed)
            return false;

        return _scene.AdvanceScene();
    }

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // msg.xdb is a case-1-only synchronous pre-load; NOT re-loaded on a reload (3/100 codes
            // 202/203/232). spec: Docs/RE/specs/resource_pipeline.md §2.2 / §2.5.
            if (!_startedAsReload)
                await LoadAndTrackAsync(LoadResourcePlan.MessageCataloguePath, cancellationToken).ConfigureAwait(false);

            foreach (string path in LoadResourcePlan.BootWorkerPaths)
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
        long bytes = await _resourceSource.LoadAsync(path, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes > 0)
            Interlocked.Add(ref _cumulativeBytes, bytes);
    }
}