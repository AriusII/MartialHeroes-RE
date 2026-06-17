using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Scene;
using MartialHeroes.Shared.Kernel.Enums;
using MartialHeroes.Shared.Kernel.State;
using Xunit;

namespace MartialHeroes.Client.Application.Tests;

/// <summary>
/// Faithful tests for the 8-state scene spine. Every expectation is re-derived from the transition
/// tables in Docs/RE/specs/client_runtime.md §7.1 / §7.5, never from the code under test.
/// </summary>
public sealed class SceneStateMachineTests
{
    private static SceneStateMachine New(out ClientEventBus bus, GameState? initial = null)
    {
        bus = new ClientEventBus(ClientEventBus.Unbounded);
        return new SceneStateMachine(bus, initial);
    }

    [Fact]
    public void Boot_default_is_init_with_substate_eight() // §7.1
    {
        var fsm = New(out _);
        Assert.Equal(EngineSceneState.Init, fsm.Current.State);
        Assert.Equal(GameState.SubStateNone, fsm.Current.SubState);
        Assert.Equal(8, GameState.SubStateNone);
        Assert.Equal(0, fsm.Current.ErrorDetail);
        Assert.False(fsm.Current.DebugMode);
    }

    [Fact]
    public void Default_spine_walks_init_login_load_opening_select_ingame() // §7.5.1
    {
        var fsm = New(out _);
        fsm.SkipOpening = false; // run the Opening intro (2 → 3)

        Assert.True(fsm.AdvanceScene());
        Assert.Equal(EngineSceneState.Login, fsm.Current.State); // 0 → 1
        Assert.True(fsm.AdvanceScene());
        Assert.Equal(EngineSceneState.Load, fsm.Current.State); // 1 → 2
        Assert.True(fsm.AdvanceScene());
        Assert.Equal(EngineSceneState.Opening, fsm.Current.State); // 2 → 3
        Assert.True(fsm.AdvanceScene());
        Assert.Equal(EngineSceneState.Select, fsm.Current.State); // 3 → 4
        Assert.True(fsm.AdvanceScene());
        Assert.Equal(EngineSceneState.InGame, fsm.Current.State); // 4 → 5
        Assert.True(fsm.AdvanceScene());
        Assert.Equal(EngineSceneState.Select, fsm.Current.State); // 5 → 4 default return
    }

    [Fact]
    public void Load_skips_opening_to_select_when_skip_flag_set() // §7.5.1 OPENNING/SKIP
    {
        var fsm = New(out _, new GameState().To(EngineSceneState.Load));
        fsm.SkipOpening = true;

        Assert.True(fsm.AdvanceScene());
        Assert.Equal(EngineSceneState.Select, fsm.Current.State); // 2 → 4
    }

    [Fact]
    public void Reload_load_re_reads_skip_gate_skip_false_goes_to_opening() // resource_pipeline.md §2.5 (CAMPAIGN 16)
    {
        // A reload (3/100 codes 202/203/232 → state 2) re-enters the identical case-2 body.
        // There is NO "reload forces Select" rule — the OPENNING/SKIP gate applies unconditionally.
        // With SkipOpening=false the reload must go to Opening (2 → 3), not Select.
        var fsm = New(out _, new GameState().To(EngineSceneState.Select));
        fsm.SkipOpening = false;

        Assert.True(fsm.OnCharActionResult(203, hasLocalPlayer: false)); // create/rename accepted → reload load
        Assert.Equal(EngineSceneState.Load, fsm.Current.State);
        Assert.True(fsm.LoadIsReload); // marker set — its sole job: tell LoadOrchestrator to skip msg.xdb
        Assert.True(fsm.AdvanceScene()); // re-reads SKIP gate: SkipOpening=false → Opening
        Assert.Equal(EngineSceneState.Opening, fsm.Current.State); // spec: resource_pipeline.md §2.5
        Assert.False(fsm.LoadIsReload); // marker consumed/cleared during AdvanceScene
    }

    [Fact]
    public void Reload_load_with_skip_true_goes_to_select() // resource_pipeline.md §2.5 (CAMPAIGN 16)
    {
        // With SkipOpening=true (the common in-session case after the opening has played once),
        // the reload correctly reaches Select — but via the SKIP gate, not a special reload rule.
        var fsm = New(out _, new GameState().To(EngineSceneState.Select));
        fsm.SkipOpening = true;

        Assert.True(fsm.OnCharActionResult(232, hasLocalPlayer: false));
        Assert.Equal(EngineSceneState.Load, fsm.Current.State);
        Assert.True(fsm.AdvanceScene()); // SKIP gate: SkipOpening=true → Select
        Assert.Equal(EngineSceneState.Select, fsm.Current.State);
    }

    [Fact]
    public void Login_preloop_failures_route_to_error_substates() // §7.5.1
    {
        var config = New(out _, new GameState().To(EngineSceneState.Login));
        Assert.True(config.OnLoginWindowConfigFailed());
        Assert.Equal(EngineSceneState.Error, config.Current.State);
        Assert.Equal(1, config.Current.SubState);

        var init = New(out _, new GameState().To(EngineSceneState.Login));
        Assert.True(init.OnLoginDeviceInitFailed());
        Assert.Equal(EngineSceneState.Error, init.Current.State);
        Assert.Equal(3, init.Current.SubState);
    }

    [Fact]
    public void EnterGameAck_forces_load_state_agnostically() // §7.5.2 (3/5 is state-agnostic)
    {
        var fsm = New(out _, new GameState().To(EngineSceneState.Login));
        Assert.True(fsm.OnEnterGameAck());
        Assert.Equal(EngineSceneState.Load, fsm.Current.State);

        // State-agnostic: the 3/5 handler forces state 2 (Load) regardless of the live scene.
        var other = New(out _, new GameState().To(EngineSceneState.InGame));
        Assert.True(other.OnEnterGameAck());
        Assert.Equal(EngineSceneState.Load, other.Current.State);

        // No-op only when already on Load (Commit equality guard).
        var already = New(out _, new GameState().To(EngineSceneState.Load));
        Assert.False(already.OnEnterGameAck());
    }

    [Theory]
    [InlineData(EngineSceneState.Load)]
    [InlineData(EngineSceneState.Select)]
    public void CharacterList_reenters_select_sub8(EngineSceneState from) // §7.5.2 (3/1)
    {
        var fsm = New(out _, new GameState().To(from));
        Assert.True(fsm.OnCharacterListReceived());
        Assert.Equal(EngineSceneState.Select, fsm.Current.State);
        Assert.Equal(GameState.SubStateNone, fsm.Current.SubState);
    }

    [Fact]
    public void CharacterList_is_rejected_from_login() // §7.5.2 row is Select/Load only
    {
        var fsm = New(out _, new GameState().To(EngineSceneState.Login));
        Assert.False(fsm.OnCharacterListReceived());
        Assert.Equal(EngineSceneState.Login, fsm.Current.State);
    }

    [Fact]
    public void GameStateTick_no_player_falls_back_to_select() // §7.5.2 (4/1)
    {
        var fsm = New(out _, new GameState().To(EngineSceneState.InGame));
        Assert.True(fsm.OnGameStateTickNoLocalPlayer());
        Assert.Equal(EngineSceneState.Select, fsm.Current.State); // 5 → 4
    }

    [Theory]
    [InlineData(0, EngineSceneState.Quit, GameState.SubStateNone, 0)] // → 6/8
    [InlineData(1, EngineSceneState.Error, 5, 1)] // → 7/5
    [InlineData(2, EngineSceneState.Error, 5, 2)] // → 7/5
    [InlineData(3, EngineSceneState.Error, 5, 3)] // → 7/5
    [InlineData(4, EngineSceneState.Error, 5, 4)] // → 7/5
    [InlineData(7, EngineSceneState.Error, 5, 7)] // → 7/5
    [InlineData(202, EngineSceneState.Load, GameState.SubStateNone, 0)] // → 2 reload
    [InlineData(203, EngineSceneState.Load, GameState.SubStateNone, 0)] // → 2 reload
    [InlineData(232, EngineSceneState.Load, GameState.SubStateNone, 0)] // → 2 reload
    [InlineData(999, EngineSceneState.Error, GameState.SubStateNone, 999)] // → 7/8 detail
    public void CharMgmt_in_select_no_player(int result, EngineSceneState state, int sub, int detail) // §7.5.2
    {
        var fsm = New(out _, new GameState().To(EngineSceneState.Select));
        Assert.True(fsm.OnCharActionResult(result, hasLocalPlayer: false));
        Assert.Equal(state, fsm.Current.State);
        Assert.Equal(sub, fsm.Current.SubState);
        Assert.Equal(detail, fsm.Current.ErrorDetail);
    }

    [Theory]
    [InlineData(0, EngineSceneState.Quit, GameState.SubStateNone, 0)] // → 6/8
    [InlineData(42, EngineSceneState.Error, GameState.SubStateNone, 42)] // → 7/8 detail
    public void CharMgmt_in_game_with_player(int result, EngineSceneState state, int sub, int detail) // §7.5.2
    {
        var fsm = New(out _, new GameState().To(EngineSceneState.InGame));
        Assert.True(fsm.OnCharActionResult(result, hasLocalPlayer: true));
        Assert.Equal(state, fsm.Current.State);
        Assert.Equal(sub, fsm.Current.SubState);
        Assert.Equal(detail, fsm.Current.ErrorDetail);
    }

    [Fact]
    public void Disconnect_during_load_is_error_sub2_else_sub8() // §7.5.2
    {
        var load = New(out _, new GameState().To(EngineSceneState.Load));
        Assert.True(load.OnDisconnected());
        Assert.Equal(EngineSceneState.Error, load.Current.State);
        Assert.Equal(2, load.Current.SubState);

        var world = New(out _, new GameState().To(EngineSceneState.InGame));
        Assert.True(world.OnDisconnected());
        Assert.Equal(EngineSceneState.Error, world.Current.State);
        Assert.Equal(GameState.SubStateNone, world.Current.SubState);

        var select = New(out _, new GameState().To(EngineSceneState.Select));
        Assert.True(select.OnDisconnected());
        Assert.Equal(EngineSceneState.Error, select.Current.State);
        Assert.Equal(GameState.SubStateNone, select.Current.SubState);
    }

    [Fact]
    public void Terminal_states_do_not_advance_or_disconnect() // §7.3 shared exit tail
    {
        var quit = New(out _, new GameState().To(EngineSceneState.Quit, GameState.SubStateNone));
        Assert.True(quit.IsTerminal);
        Assert.False(quit.HasExited);
        Assert.True(quit.AdvanceScene());
        Assert.True(quit.HasExited);
        Assert.False(quit.AdvanceScene());
        Assert.False(quit.OnDisconnected());
        Assert.False(quit.RequestQuit());
    }

    [Theory]
    [InlineData(EngineSceneState.Login, 2)]
    [InlineData(EngineSceneState.Load, 2)]
    [InlineData(EngineSceneState.Select, GameState.SubStateNone)]
    [InlineData(EngineSceneState.InGame, GameState.SubStateNone)]
    public void Request_quit_routes_by_live_scene(EngineSceneState from, int expectedSubState) // §7.5.3
    {
        var fsm = New(out _, new GameState().To(from));
        Assert.True(fsm.RequestQuit());
        Assert.Equal(EngineSceneState.Quit, fsm.Current.State);
        Assert.Equal(expectedSubState, fsm.Current.SubState);
    }

    [Fact]
    public void Select_confirm_character_routes_to_ingame_sub8() // §7.5.3
    {
        var fsm = New(out _, new GameState().To(EngineSceneState.Select));
        Assert.True(fsm.OnSelectConfirmCharacter());
        Assert.Equal(EngineSceneState.InGame, fsm.Current.State);
        Assert.Equal(GameState.SubStateNone, fsm.Current.SubState);
    }

    [Fact]
    public void Login_network_fatal_supports_quit_or_error_paths() // §7.5.3
    {
        var quit = New(out _, new GameState().To(EngineSceneState.Login));
        Assert.True(quit.OnLoginNetworkFatal(quitPath: true));
        Assert.Equal(EngineSceneState.Quit, quit.Current.State);
        Assert.Equal(GameState.SubStateNone, quit.Current.SubState);

        var error = New(out _, new GameState().To(EngineSceneState.Login));
        Assert.True(error.OnLoginNetworkFatal(quitPath: false, errorDetail: 77));
        Assert.Equal(EngineSceneState.Error, error.Current.State);
        Assert.Equal(GameState.SubStateNone, error.Current.SubState);
        Assert.Equal(77, error.Current.ErrorDetail);
    }

    [Fact]
    public void Each_accepted_transition_publishes_exactly_one_event() // §7.2 commit → re-dispatch
    {
        var fsm = New(out ClientEventBus bus);
        Assert.True(fsm.AdvanceScene()); // 0 → 1

        Assert.True(bus.Reader.TryRead(out IClientEvent? evt));
        var changed = Assert.IsType<SceneStateChangedEvent>(evt);
        Assert.Equal(EngineSceneState.Init, changed.Previous.State);
        Assert.Equal(EngineSceneState.Login, changed.Next.State);
        Assert.False(bus.Reader.TryRead(out _)); // exactly one
    }

    [Fact]
    public void Rejected_transition_is_total_noop() // no state change, no event
    {
        var fsm = New(out ClientEventBus bus, new GameState().To(EngineSceneState.InGame));
        Assert.False(fsm.OnSelectConfirmCharacter()); // confirm is only valid from Select
        Assert.Equal(EngineSceneState.InGame, fsm.Current.State);
        Assert.False(bus.Reader.TryRead(out _));
    }
}