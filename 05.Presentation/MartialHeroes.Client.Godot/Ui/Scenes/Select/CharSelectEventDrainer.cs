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

            switch (evt)
            {
                case CharacterListEvent charList:
                    GD.Print($"[CharSelectEventDrainer] CharacterListEvent ({charList.Characters.Length} slots) → ApplyCharacterList.");
                    _window.ApplyCharacterList(charList.Characters);
                    break;

                case CharManageResultEvent manage:
                    GD.Print($"[CharSelectEventDrainer] CharManageResultEvent success={manage.Success} subtype={manage.Subtype}.");
                    _window.OnCharManageResult(manage.Success, string.Empty);
                    break;

                case CharRenameResultEvent rename:
                    GD.Print($"[CharSelectEventDrainer] CharRenameResultEvent success={rename.Success} newName='{rename.NewName}'.");
                    _window.OnCharRenameResult(rename.Success, rename.NewName);
                    break;
            }
        }
    }
}