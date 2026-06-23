using MartialHeroes.Client.Application.Input;

namespace MartialHeroes.Client.Presentation.Input;

public sealed class HudInputHandler(Func<int, int, bool>? hitTest = null) : IInputHandler
{
    private volatile Func<int, int, bool>? _hitTest = hitTest;

    public bool TryHandle(in InputEvent e)
    {
        if (_hitTest is null) return false;

        return e.Type == InputType.MouseButtonDown && _hitTest(e.X, e.Y);
    }

    public void SetHitTest(Func<int, int, bool> hitTest)
    {
        _hitTest = hitTest;
    }
}