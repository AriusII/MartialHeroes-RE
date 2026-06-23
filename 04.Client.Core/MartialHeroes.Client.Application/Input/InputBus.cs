using System.Collections.Concurrent;

namespace MartialHeroes.Client.Application.Input;

public sealed class InputBus
{
    private readonly IInputHandler[] _handlers;

    private readonly ConcurrentQueue<InputEvent> _pending = new();

    public InputBus(params IInputHandler[] handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _handlers = (IInputHandler[])handlers.Clone();
        foreach (var handler in _handlers) ArgumentNullException.ThrowIfNull(handler);
    }

    public bool Dispatch(in InputEvent e)
    {
        if (e.Type == InputType.MouseMove)
        {
            var consumed = false;
            foreach (var handler in _handlers)
                if (handler.TryHandle(in e))
                    consumed = true;

            return consumed;
        }

        foreach (var handler in _handlers)
            if (handler.TryHandle(in e))
                return true;

        return false;
    }

    public void Enqueue(in InputEvent e)
    {
        _pending.Enqueue(e);
    }

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
}