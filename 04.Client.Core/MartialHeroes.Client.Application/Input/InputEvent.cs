namespace MartialHeroes.Client.Application.Input;

public enum InputType : byte
{
    None = 0,

    MouseMove = 3,

    MouseButtonDown = 4,

    MouseButtonUp = 5,

    MouseButtonClick = 6,

    MouseDoubleClick = 7,

    MouseWheel = 8,

    KeyDown = 16,

    KeyUp = 17,

    KeyChar = 18
}

public static class MouseButton
{
    public const int Left = 1;

    public const int Right = 2;

    public const int Middle = 3;
}

public readonly record struct InputEvent(
    InputType Type,
    int X,
    int Y,
    int ButtonOrDelta,
    int Modifiers)
{
    public bool IsLeftButtonClick =>
        Type == InputType.MouseButtonClick && ButtonOrDelta == MouseButton.Left;
}