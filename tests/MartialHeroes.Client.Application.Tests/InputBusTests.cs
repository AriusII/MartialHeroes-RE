using MartialHeroes.Client.Application.Input;
using Xunit;

namespace MartialHeroes.Client.Application.Tests;

public sealed class InputBusTests
{
    /// <summary>A handler that records what it sees and consumes (or not) on demand.</summary>
    private sealed class TrackingHandler(bool consume) : IInputHandler
    {
        public List<InputEvent> Seen { get; } = new();

        public bool TryHandle(in InputEvent e)
        {
            Seen.Add(e);
            return consume;
        }
    }

    private static InputEvent LeftClick =>
        new(InputType.MouseButtonDown, 100, 200, MouseButton.Left, 0);

    [Fact]
    public void Ui_handler_that_consumes_blocks_the_world_handler()
    {
        var ui = new TrackingHandler(consume: true);
        var world = new TrackingHandler(consume: false);
        var bus = new InputBus(ui, world); // UI first, world second. spec: input_ui.md §3

        bool consumed = bus.Dispatch(LeftClick);

        Assert.True(consumed);
        Assert.Single(ui.Seen);
        Assert.Empty(world.Seen); // world never saw the event — UI is the gate.
    }

    [Fact]
    public void World_handler_receives_event_when_ui_does_not_consume()
    {
        var ui = new TrackingHandler(consume: false);
        var world = new TrackingHandler(consume: false);
        var bus = new InputBus(ui, world);

        bool consumed = bus.Dispatch(LeftClick);

        Assert.False(consumed);
        Assert.Single(ui.Seen);
        Assert.Single(world.Seen); // fell through UI to the world.
    }

    [Fact]
    public void First_consuming_handler_in_priority_order_wins()
    {
        var ui = new TrackingHandler(consume: false);
        var world = new TrackingHandler(consume: true);
        var bus = new InputBus(ui, world);

        Assert.True(bus.Dispatch(LeftClick));
        Assert.Single(ui.Seen);
        Assert.Single(world.Seen);
    }

    [Fact]
    public void Enqueue_then_drain_dispatches_in_fifo_order()
    {
        var ui = new TrackingHandler(consume: false);
        var world = new TrackingHandler(consume: false);
        var bus = new InputBus(ui, world);

        var a = new InputEvent(InputType.MouseMove, 1, 1, 0, 0);
        var b = new InputEvent(InputType.MouseWheel, 1, 1, 120, 0);
        bus.Enqueue(a);
        bus.Enqueue(b);
        Assert.Equal(2, bus.PendingCount);

        int drained = bus.DrainAndDispatch();

        Assert.Equal(2, drained);
        Assert.Equal(0, bus.PendingCount);
        Assert.Equal(new[] { a, b }, world.Seen);
    }

    [Fact]
    public void Left_button_predicates_match_button_encoding()
    {
        Assert.True(LeftClick.IsLeftButtonDown);
        Assert.False(LeftClick.IsLeftButtonUp);

        var up = new InputEvent(InputType.MouseButtonUp, 0, 0, MouseButton.Left, 0);
        Assert.True(up.IsLeftButtonUp);
        Assert.False(up.IsLeftButtonDown);
    }

    [Fact]
    public void Input_taxonomy_press_release_click_are_distinct_codes()
    {
        // spec: input_ui.md §2a — 3=move, 4=press, 5=release, 6=synthesised click, 7=double-click, 8=wheel.
        Assert.Equal(3, (byte)InputType.MouseMove);
        Assert.Equal(4, (byte)InputType.MouseButtonDown); // press
        Assert.Equal(5, (byte)InputType.MouseButtonUp); // release
        Assert.Equal(6, (byte)InputType.MouseButtonClick); // synthesised click
        Assert.Equal(7, (byte)InputType.MouseDoubleClick);
        Assert.Equal(8, (byte)InputType.MouseWheel);

        // press / release / click are three DISTINCT values (the prior enum collapsed click into release).
        Assert.NotEqual(InputType.MouseButtonDown, InputType.MouseButtonUp);
        Assert.NotEqual(InputType.MouseButtonUp, InputType.MouseButtonClick);
        Assert.NotEqual(InputType.MouseButtonDown, InputType.MouseButtonClick);
    }

    [Fact]
    public void IsLeftButtonClick_only_for_synthesised_left_click()
    {
        // spec: input_ui.md §2b — HUD buttons fire on the synthesised click (type 6), not press/release.
        var click = new InputEvent(InputType.MouseButtonClick, 0, 0, MouseButton.Left, 0);
        Assert.True(click.IsLeftButtonClick);
        Assert.False(click.IsLeftButtonDown);
        Assert.False(click.IsLeftButtonUp);

        // A press / release with the same button is NOT a click.
        Assert.False(LeftClick.IsLeftButtonClick);
        Assert.False(new InputEvent(InputType.MouseButtonUp, 0, 0, MouseButton.Left, 0).IsLeftButtonClick);

        // A right-button click is not a left click.
        Assert.False(new InputEvent(InputType.MouseButtonClick, 0, 0, MouseButton.Right, 0).IsLeftButtonClick);
    }
}