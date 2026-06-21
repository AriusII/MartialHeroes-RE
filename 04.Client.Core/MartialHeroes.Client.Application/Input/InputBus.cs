using System.Collections.Concurrent;

namespace MartialHeroes.Client.Application.Input;

/// <summary>
///     The neutral input bus: Godot pushes raw pointer/key events in via <see cref="Dispatch" />; the bus
///     walks the registered handlers as a chain of responsibility (UI first, world second) and stops at
///     the first that consumes the event. spec: Docs/RE/specs/input_ui.md §3 / §6 ("UI is the gate").
/// </summary>
/// <remarks>
///     <para>
///         <b>Two consumption modes.</b>
///         <list type="bullet">
///             <item>
///                 <see cref="Dispatch" /> runs the chain <em>immediately</em> on the caller's thread, returning
///                 whether the event was consumed — the direct analogue of the legacy WndProc dispatch.
///             </item>
///             <item>
///                 <see cref="Enqueue" /> queues the event so the deterministic <c>GameEngineLoop</c> can drain
///                 and dispatch it at the start of a fixed tick (<see cref="DrainInto" />). This funnels input
///                 onto the single logical owner that mutates Domain, keeping the simulation deterministic
///                 (spec: Docs/RE/specs/game_loop.md §6, fixed-tick determinism).
///             </item>
///         </list>
///     </para>
///     <para>
///         <b>Threading.</b> The queue is a lock-free MPSC <see cref="ConcurrentQueue{T}" />: many producers
///         (Godot input thread(s)) enqueue; the single loop thread drains. The handler list is fixed at
///         construction so dispatch never allocates and never races a mutation. spec: input_ui.md §6.
///     </para>
/// </remarks>
public sealed class InputBus
{
    // Priority-ordered handlers. UI handlers come first; the world handler is last. The bus walks them
    // in order and stops at the first that consumes. spec: input_ui.md §3 ("UI before world").
    private readonly IInputHandler[] _handlers;

    // MPSC: Godot input threads enqueue, the single loop thread drains. spec: game_loop.md §6.
    private readonly ConcurrentQueue<InputEvent> _pending = new();

    /// <summary>
    ///     Creates a bus over the supplied handlers, in priority order (UI first, world last). spec:
    ///     Docs/RE/specs/input_ui.md §3.
    /// </summary>
    public InputBus(params IInputHandler[] handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _handlers = (IInputHandler[])handlers.Clone();
        foreach (var handler in _handlers) ArgumentNullException.ThrowIfNull(handler);
    }

    /// <summary>Number of events currently queued for the next tick drain.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>
    ///     Runs the chain of responsibility for <paramref name="e" /> immediately and returns whether it was
    ///     consumed. Walks handlers in priority order; for every event EXCEPT a move it stops at the first
    ///     that returns <see langword="true" /> (first-consumer-wins). spec: Docs/RE/specs/input_ui.md §3
    ///     (UI consumes before world).
    ///     <para>
    ///         A <see cref="InputType.MouseMove" /> (legacy pointer type 3) is the ONE pointer event that does
    ///         NOT stop at the first consumer: it is BROADCAST so every widget refreshes its hover state. The
    ///         walk continues through all handlers; the return value reports whether ANY handler consumed it.
    ///         spec: Docs/RE/specs/ui_event_dispatch.md §4 (move = hover broadcast, keep scanning) / §1
    ///         (pointer/click group {3,4,5,6,7}; move is the lone broadcast).
    ///     </para>
    /// </summary>
    public bool Dispatch(in InputEvent e)
    {
        // Move (type 3) is the lone broadcast event: keep walking ALL handlers so every widget refreshes
        // hover; report whether any consumed. All other events stop at the first consumer.
        // spec: Docs/RE/specs/ui_event_dispatch.md §4 / §1.
        if (e.Type == InputType.MouseMove)
        {
            var consumed = false;
            foreach (var handler in _handlers)
                if (handler.TryHandle(in e))
                    consumed = true; // do NOT stop — broadcast to refresh every widget's hover.

            return consumed;
        }

        foreach (var handler in _handlers)
            if (handler.TryHandle(in e))
                return true; // UI (or an earlier link) consumed it; the world never sees it.

        return false;
    }

    /// <summary>
    ///     Queues <paramref name="e" /> for deterministic dispatch at the next fixed tick. Non-blocking;
    ///     safe to call from any producer thread. spec: Docs/RE/specs/game_loop.md §6 (drain per tick).
    /// </summary>
    public void Enqueue(in InputEvent e)
    {
        _pending.Enqueue(e);
    }

    /// <summary>
    ///     Drains every queued event and dispatches each through the chain of responsibility, returning the
    ///     number drained. Called by the loop at the start of a tick so all input mutation lands on the
    ///     single loop thread. spec: Docs/RE/specs/game_loop.md §6; input_ui.md §3.
    /// </summary>
    public int DrainAndDispatch()
    {
        var count = 0;
        while (_pending.TryDequeue(out var e))
        {
            Dispatch(in e);
            count++;
        }

        return count;
    }

    /// <summary>
    ///     Drains every queued event into <paramref name="destination" /> without dispatching (diagnostics
    ///     / tests). Returns the number drained.
    /// </summary>
    public int DrainInto(ICollection<InputEvent> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var count = 0;
        while (_pending.TryDequeue(out var e))
        {
            destination.Add(e);
            count++;
        }

        return count;
    }
}