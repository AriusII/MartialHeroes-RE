// Ui/Scenes/Select/CharSelectEventDrainer.cs
//
// Event drainer for the new CharSelectWindow — mirrors Screens/CharListEventDrainer.cs in
// structure and threading contract, but typed for CharSelectWindow rather than CharacterSelectScreen.
//
// Threading: _Process runs on the Godot main thread.
// All Control mutations inside CharSelectWindow.ApplyCharacterList are therefore safe without
// CallDeferred.
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation —
//       "drain Application channels on _Process; never touch a Control from a background task".
// spec: Docs/RE/specs/frontend_scenes.md §3.1 — CharacterListEvent (opcode 3/1) drives char-select.

using Godot;
using MartialHeroes.Client.Application.Events;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

/// <summary>
/// Drains the Application event bus on the Godot main thread and routes
/// <see cref="CharacterListEvent"/> to the bound <see cref="CharSelectWindow"/>.
///
/// <para>All other events are surfaced via <see cref="EventDrained"/> so the scene owner
/// can react without competing with this reader.</para>
///
/// spec: Docs/RE/specs/frontend_scenes.md §3.1 — CharacterListEvent drives char-select. CODE-CONFIRMED.
/// </summary>
public sealed partial class CharSelectEventDrainer : Node
{
    private CharSelectWindow? _window;
    private IClientEventBus? _bus;

    /// <summary>
    /// Raised for every drained Application event so the scene owner can observe
    /// state/result events without adding a second competing channel reader.
    /// </summary>
    public event Action<IClientEvent>? EventDrained;

    /// <summary>
    /// Binds the drainer to the window and the Application event bus.
    /// Call immediately after construction before adding to the scene tree.
    /// </summary>
    public void Bind(CharSelectWindow window, IClientEventBus bus)
    {
        _window = window;
        _bus = bus;
    }

    public override void _Process(double delta)
    {
        if (_bus is null || _window is null) return;

        while (_bus.Reader.TryRead(out IClientEvent? evt))
        {
            EventDrained?.Invoke(evt);

            if (evt is CharacterListEvent charList)
            {
                GD.Print($"[CharSelectEventDrainer] CharacterListEvent ({charList.Characters.Length} slots) " +
                         "→ CharSelectWindow.ApplyCharacterList. " +
                         "spec: frontend_scenes.md §3.1. CODE-CONFIRMED.");
                _window.ApplyCharacterList(charList.Characters);
            }
            // Other events are NOT consumed here; they are surfaced via EventDrained to SelectScene.
        }
    }
}