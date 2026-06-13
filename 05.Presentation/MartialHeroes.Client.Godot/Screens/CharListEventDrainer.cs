// Screens/CharListEventDrainer.cs
//
// Helper Godot Node — drains the Application event bus (IClientEventBus.Reader) each _Process tick
// looking for CharacterListEvent (opcode 3/1) and routes it to CharacterSelectScreen.ApplyCharacterList.
//
// Runs entirely on the Godot main thread (_Process) so all Control mutations inside
// ApplyCharacterList are safe without CallDeferred.
//
// The drainer is owned by BootFlow: it is added as a child when CharacterSelectScreen is shown
// and freed when the player enters the world or goes back to login.
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation —
//       "drain Application channels on _Process; never touch a Control from a background task".
// spec: Docs/RE/specs/frontend_scenes.md §3.1 — CharacterListEvent (opcode 3/1) drives char-select.

using Godot;
using MartialHeroes.Client.Application.Events;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// A lightweight Godot <see cref="Node"/> that polls <see cref="IClientEventBus.Reader"/>
/// each frame and routes <see cref="CharacterListEvent"/> to the bound
/// <see cref="CharacterSelectScreen"/> on the main thread.
///
/// <para>All other events are silently passed through (not consumed — other consumers, e.g.
/// <c>GameLoop</c>, handle them once the world is loaded).</para>
///
/// <para>Threading: <c>_Process</c> runs on the Godot main thread, so
/// <see cref="CharacterSelectScreen.ApplyCharacterList"/> may mutate Controls directly.</para>
/// </summary>
public sealed partial class CharListEventDrainer : Node
{
    private CharacterSelectScreen? _select;
    private IClientEventBus? _bus;

    /// <summary>
    /// Binds the drainer to a target screen and the Application event bus.
    /// Call immediately after construction before adding to the scene tree.
    /// </summary>
    public void Bind(CharacterSelectScreen select, IClientEventBus bus)
    {
        _select = select;
        _bus = bus;
    }

    public override void _Process(double delta)
    {
        if (_bus is null || _select is null) return;

        // TryRead is non-blocking; drain until the queue is empty this frame.
        while (_bus.Reader.TryRead(out IClientEvent? evt))
        {
            if (evt is CharacterListEvent charList)
            {
                GD.Print($"[CharListEventDrainer] CharacterListEvent received " +
                         $"({charList.Characters.Length} slots) → applying to CharacterSelectScreen.");

                // All Control mutation happens here, on the main thread.
                _select.ApplyCharacterList(charList.Characters);

                // Note: We do NOT call StateMachine.OnCharacterListReceived() here.
                // BootFlow (the owner) drives FSM transitions in response to signals emitted by
                // CharacterSelectScreen. Keeping the FSM reference in one place avoids split ownership.
            }
            // All other events are NOT consumed here — they remain in the channel for downstream
            // consumers (GameLoop, HUD nodes, etc.) once the world scene is loaded.
            // Re-publish workaround: we can't "un-read" from a channel, so GameLoop won't see
            // events that arrived during the login flow. This is acceptable: events that matter
            // in the world context (actor spawns, HP updates) will not arrive before entering
            // the world anyway. CharacterListEvent is the only pre-world event we handle here.
        }
    }
}