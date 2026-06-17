using MartialHeroes.Client.Application.Assets;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Scene;
using MartialHeroes.Shared.Kernel.Enums;
using MartialHeroes.Shared.Kernel.State;
using Xunit;

namespace MartialHeroes.Client.Application.Tests;

public sealed class LoadOrchestratorTests
{
    [Fact]
    public async Task Post_login_load_reads_ini_once_starts_sfx_and_completes_to_select_when_skip_nonzero()
    {
        var fsm = NewLoadScene();
        var resources = new FakeResources(1);
        var skip = new FakeOpeningSkipReader(true);
        var sounds = new FakeSounds();
        var load = new LoadOrchestrator(fsm, resources, skip, sounds);

        load.Start();
        await load.Completion;

        Assert.Equal(LoadOrchestratorState.Completed, load.State);
        Assert.Equal(1, skip.ReadCount);
        Assert.Equal([920100100], sounds.LoopingCueIds);
        Assert.Equal(LoadResourcePlan.BootWorkerPaths.Length + 1, resources.Paths.Count);
        Assert.Equal(LoadResourcePlan.MessageCataloguePath, resources.Paths[0]);
        Assert.Equal(EngineSceneState.Select, load.DestinationAfterLoad);
        Assert.True(load.AdvanceSceneWhenComplete());
        Assert.Equal(EngineSceneState.Select, fsm.Current.State);
    }

    [Fact]
    public async Task Post_login_load_defaults_to_opening_when_ini_false_or_absent()
    {
        var fsm = NewLoadScene();
        var load = new LoadOrchestrator(fsm, new FakeResources(1), new FakeOpeningSkipReader(false));

        load.Start();
        await load.Completion;

        Assert.Equal(EngineSceneState.Opening, load.DestinationAfterLoad);
        Assert.True(load.AdvanceSceneWhenComplete());
        Assert.Equal(EngineSceneState.Opening, fsm.Current.State);
    }

    [Fact]
    public async Task Reload_load_reads_ini_skips_msg_xdb_and_destination_follows_skip_gate()
    {
        // A reload (3/100 code 203 → state 2) re-enters the identical case-2 body:
        // - OPENNING/SKIP IS re-read unconditionally (skip.ReadCount == 1).
        // - msg.xdb is NOT re-loaded (it is a case-1-only pre-load).
        // - The post-load destination follows the SKIP gate (here SKIP=true → Select).
        // spec: Docs/RE/specs/resource_pipeline.md §2.5 (CAMPAIGN 16); client_runtime.md §7.10 item 2.
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var fsm = new SceneStateMachine(bus, new GameState().To(EngineSceneState.Select))
        {
            SkipOpening = false,
        };
        Assert.True(fsm.OnCharActionResult(203, hasLocalPlayer: false));
        Assert.True(fsm.LoadIsReload);

        var skip = new FakeOpeningSkipReader(true); // SKIP=1 → Select
        var resources = new FakeResources(1);
        var load = new LoadOrchestrator(fsm, resources, skip);

        load.Start();
        await load.Completion;

        // SKIP was re-read on the reload — not skipped.
        Assert.Equal(1, skip.ReadCount);
        // msg.xdb must NOT have been loaded (it is case-1-only, not re-loaded on reload).
        Assert.DoesNotContain(LoadResourcePlan.MessageCataloguePath, resources.Paths);
        // Only the boot-worker corpus was loaded (no msg.xdb).
        Assert.Equal(LoadResourcePlan.BootWorkerPaths.Length, resources.Paths.Count);
        // Destination follows SKIP gate: SKIP=true → Select.
        Assert.Equal(EngineSceneState.Select, load.DestinationAfterLoad);
        Assert.True(load.AdvanceSceneWhenComplete());
        Assert.Equal(EngineSceneState.Select, fsm.Current.State);
    }

    [Fact]
    public async Task Reload_load_with_skip_false_goes_to_opening_and_skips_msg_xdb()
    {
        // When SKIP=0, a reload must go to Opening — not Select — confirming no reload-forces-Select rule.
        // msg.xdb is still not re-loaded on a reload.
        // spec: Docs/RE/specs/resource_pipeline.md §2.5 (CAMPAIGN 16).
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var fsm = new SceneStateMachine(bus, new GameState().To(EngineSceneState.Select))
        {
            SkipOpening = false,
        };
        Assert.True(fsm.OnCharActionResult(232, hasLocalPlayer: false));

        var skip = new FakeOpeningSkipReader(false); // SKIP=0 → Opening
        var resources = new FakeResources(1);
        var load = new LoadOrchestrator(fsm, resources, skip);

        load.Start();
        await load.Completion;

        Assert.Equal(1, skip.ReadCount); // SKIP re-read on reload
        Assert.DoesNotContain(LoadResourcePlan.MessageCataloguePath, resources.Paths);
        Assert.Equal(EngineSceneState.Opening, load.DestinationAfterLoad); // SKIP=false → Opening
        Assert.True(load.AdvanceSceneWhenComplete());
        Assert.Equal(EngineSceneState.Opening, fsm.Current.State);
    }

    [Fact]
    public async Task Progress_uses_legacy_integer_quotient_not_percent()
    {
        var fsm = NewLoadScene();
        var load = new LoadOrchestrator(fsm, new FakeResources(9_395_240), new FakeOpeningSkipReader(false));

        load.Start();
        await load.Completion;

        Assert.Equal(LoadResourcePlan.BootWorkerPaths.Length + 1, load.ProgressQuotient);
    }

    [Fact]
    public async Task Cancelled_load_settles_and_can_start_again_while_still_in_load_state()
    {
        var fsm = NewLoadScene();
        var load = new LoadOrchestrator(fsm, new FakeResources(1), new FakeOpeningSkipReader(false));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        load.Start(cancelled.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load.Completion);

        Assert.Equal(LoadOrchestratorState.Cancelled, load.State);

        load.Start();
        await load.Completion;

        Assert.Equal(LoadOrchestratorState.Completed, load.State);
    }

    [Fact]
    public void OpeningSkipIniReader_reads_openning_skip_nonzero_and_defaults_false()
    {
        string path = Path.Combine(Environment.CurrentDirectory, "mh_openning_skip_test.ini");
        try
        {
            File.WriteAllText(path, "[OPENNING]\nSKIP=1\n");
            Assert.True(new OpeningSkipIniReader(path).ReadSkipOpening());

            File.WriteAllText(path, "[OPENNING]\nSKIP=0\n");
            Assert.False(new OpeningSkipIniReader(path).ReadSkipOpening());

            File.Delete(path);
            Assert.False(new OpeningSkipIniReader(path).ReadSkipOpening());
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static SceneStateMachine NewLoadScene()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        return new SceneStateMachine(bus, new GameState().To(EngineSceneState.Load));
    }

    private sealed class FakeResources(long bytesPerLoad) : ILoadResourceSource
    {
        public List<string> Paths { get; } = [];

        public ValueTask<long> LoadAsync(string logicalPath, CancellationToken cancellationToken = default)
        {
            Paths.Add(logicalPath);
            return ValueTask.FromResult(bytesPerLoad);
        }
    }

    private sealed class FakeOpeningSkipReader(bool value) : IOpeningSkipReader
    {
        public int ReadCount { get; private set; }

        public bool ReadSkipOpening()
        {
            ReadCount++;
            return value;
        }
    }

    private sealed class FakeSounds : ILoadingSoundSink
    {
        public List<int> LoopingCueIds { get; } = [];

        public void PlayLooping(int soundCueId) => LoopingCueIds.Add(soundCueId);
    }
}