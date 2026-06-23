using MartialHeroes.Client.Application.Input;

namespace MartialHeroes.Client.Presentation.Input;

public sealed class HudInputHandler : IInputHandler
{
    private volatile Func<int, int, bool>? _hitTest;

    public HudInputHandler(Func<int, int, bool>? hitTest = null)
    {
        _hitTest = hitTest;
    }

    public bool TryHandle(in InputEvent e)
    {
        if (_hitTest is null) return false;

        if (e.Type != InputType.MouseButtonDown) return false;

        return _hitTest(e.X, e.Y);
    }

    public void SetHitTest(Func<int, int, bool> hitTest)
    {
        _hitTest = hitTest;
    }
}