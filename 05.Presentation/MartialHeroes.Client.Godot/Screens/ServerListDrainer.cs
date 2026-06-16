// Screens/ServerListDrainer.cs
//
// Helper Godot Node — drains the Application event bus (IClientEventBus.Reader) each _Process tick
// looking for ServerListReceivedEvent (lobby server-list response, port 10000) and routes it to
// ServerSelectScreen.SetServers via a view-model mapping.
//
// Mirrors CharListEventDrainer.cs in structure and threading contract.
//
// Threading: _Process runs on the Godot main thread so all Control mutations inside
// ServerSelectScreen.SetServers are safe without CallDeferred.
//
// The drainer is owned by BootFlow: added as a child when ServerSelectScreen is shown and freed
// when the player selects a server or goes back to login.
//
// VIEW-MODEL MAPPING:
//   ServerListEntryView (Application) → ServerEntry (view-local record in ServerSelectScreen.cs).
//   DisplayName is left empty so ServerSelectScreen.PaintOnePlate resolves it from msg bank
//   5001..5040 (the live-wire path). Dev-seeded ServerEntry records fill DisplayName directly.
//   spec: Docs/RE/specs/login_flow.md §1 step 2 / §2.1 (lobby server-list wire path).
//   spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE A (wire fields).
//
// PASSIVE: zero game logic. View state only.

using Godot;
using MartialHeroes.Client.Application.Events;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// A lightweight Godot <see cref="Node"/> that polls <see cref="IClientEventBus.Reader"/>
/// each frame and routes <see cref="ServerListReceivedEvent"/> to the bound
/// <see cref="ServerSelectScreen"/> on the main thread.
///
/// <para>All other events are silently passed through (not consumed).</para>
///
/// <para>Threading: <c>_Process</c> runs on the Godot main thread, so
/// <see cref="ServerSelectScreen.SetServers"/> may mutate Controls directly.</para>
/// </summary>
public sealed partial class ServerListDrainer : Node
{
    private ServerSelectScreen? _screen;
    private IClientEventBus? _bus;

    /// <summary>
    /// Binds the drainer to a target screen and the Application event bus.
    /// Call immediately after construction, before adding to the scene tree.
    /// </summary>
    public void Bind(ServerSelectScreen screen, IClientEventBus bus)
    {
        _screen = screen;
        _bus = bus;
    }

    public override void _Process(double delta)
    {
        if (_bus is null || _screen is null) return;

        // TryRead is non-blocking; drain until the queue is empty this frame.
        while (_bus.Reader.TryRead(out IClientEvent? evt))
        {
            if (evt is ServerListReceivedEvent serverList)
            {
                GD.Print($"[ServerListDrainer] ServerListReceivedEvent received " +
                         $"({serverList.Servers.Length} entries) → applying to ServerSelectScreen. " +
                         "spec: login_flow.md §1 step 2 / §2.1.");

                // Map Application view-model to the view-local ServerEntry records.
                // spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE A (ServerId, Status, Population, Flag).
                var entries = new List<ServerEntry>(serverList.Servers.Length);
                foreach (ServerListEntryView e in serverList.Servers)
                {
                    // DisplayName is left empty; ServerSelectScreen.PaintOnePlate resolves it
                    // from msg bank 5001..5040 when DisplayName is empty.
                    // spec: Docs/RE/specs/frontend_scenes.md §2.8 / §11.4. CODE-CONFIRMED.
                    entries.Add(new ServerEntry(
                        ServerId: e.ServerId,
                        DisplayName: string.Empty,
                        StatusCode: e.Status,
                        Population: e.Population,
                        Flag: e.Flag));
                }

                // All Control mutation happens here, on the main thread.
                _screen.SetServers(entries);

                // Note: we do NOT consume non-ServerListReceivedEvent events — they remain in the
                // channel for downstream consumers. CharListEventDrainer will handle CharacterListEvent
                // once char-select is shown.
            }
            // All other events are NOT consumed here.
        }
    }
}