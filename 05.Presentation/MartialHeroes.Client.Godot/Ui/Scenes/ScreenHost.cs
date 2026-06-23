
using Godot;
using MartialHeroes.Client.Presentation.Screens.Layout;

namespace MartialHeroes.Client.Godot.Ui.Scenes;

public sealed partial class ScreenHost : Control
{
    private const float RefWidth = LoginLayout.RefWidth;
    private const float RefHeight = LoginLayout.RefHeight;

    private Control? _canvas;

    public override void _Ready()
    {
        ApplyViewportSize();
        GetViewport().SizeChanged += OnViewportSizeChanged;

        MouseFilter = MouseFilterEnum.Ignore;

        _canvas = new Control { Name = "RefCanvas" };
        _canvas.Size = new Vector2(RefWidth, RefHeight);
        _canvas.CustomMinimumSize = new Vector2(RefWidth, RefHeight);
        AddChild(_canvas);

        Rescale();
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(GetViewport()))
            GetViewport().SizeChanged -= OnViewportSizeChanged;
    }

    private void OnViewportSizeChanged()
    {
        ApplyViewportSize();
        Rescale();
    }

    public void SetScreen(Control screen)
    {
        if (_canvas is null) return;

        foreach (var child in _canvas.GetChildren())
            if (child is Control)
                child.QueueFree();

        screen.Size = new Vector2(RefWidth, RefHeight);
        _canvas.AddChild(screen);
        Rescale();
    }

    private void ApplyViewportSize()
    {
        var vpSize = GetViewportRect().Size;
        if (vpSize.X > 0 && vpSize.Y > 0)
        {
            Position = Vector2.Zero;
            Size = vpSize;
        }
    }

    private void Rescale()
    {
        if (_canvas is null) return;

        var windowSize = Size;
        if (windowSize.X <= 0 || windowSize.Y <= 0)
            windowSize = GetViewportRect().Size;

        if (windowSize.X <= 0 || windowSize.Y <= 0) return;

        var scaleX = windowSize.X / RefWidth;
        var scaleY = windowSize.Y / RefHeight;
        _canvas.Scale = new Vector2(scaleX, scaleY);
        _canvas.Position = Vector2.Zero;
    }
}