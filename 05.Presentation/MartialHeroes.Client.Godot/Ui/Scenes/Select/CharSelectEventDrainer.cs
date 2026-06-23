using Godot;
using MartialHeroes.Client.Application.Contracts.Events;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

public sealed partial class CharSelectEventDrainer : Node
{
    private IClientEventBus? _bus;
    private CharSelectWindow? _window;

    public event Action<IClientEvent>? EventDrained;

    public void Bind(CharSelectWindow window, IClientEventBus bus)
    {
        _window = window;
        _bus = bus;
    }

    public override void _Process(double delta)
    {
        if (_bus is null || _window is null) return;

        while (_bus.Reader.TryRead(out var evt))
        {
            EventDrained?.Invoke(evt);

            if (evt is CharacterListEvent charList)
            {
                GD.Print($"[CharSelectEventDrainer] CharacterListEvent ({charList.Characters.Length} slots) " +
                         "→ CharSelectWindow.ApplyCharacterList. " +
                         "spec: frontend_scenes.md §3.1. CODE-CONFIRMED.");
                _window.ApplyCharacterList(charList.Characters);
            }
        }
    }
}