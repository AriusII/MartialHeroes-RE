using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Shared.Kernel.State;

namespace MartialHeroes.Client.Application.Contracts.Scene;

/// <summary>
///     Immutable snapshot published whenever the engine scene state changes — the application-layer
///     signal the presentation host drains to swap the live scene. spec: client_runtime.md §7.2.
/// </summary>
/// <param name="Previous">The engine-state struct before the transition.</param>
/// <param name="Next">The engine-state struct after the transition (the scene now live).</param>
/// <remarks>
///     Carries only Kernel value types (<see cref="GameState" />), never a wire struct or a live Domain
///     reference, per the <see cref="IClientEvent" /> contract. The presentation host reads
///     <see cref="GameState.State" /> and dispatches the matching scene controller, mirroring the legacy
///     <c>switch</c>. spec: Docs/RE/specs/client_runtime.md §7.2 (commit → re-dispatch), §7.3.
/// </remarks>
public sealed record SceneStateChangedEvent(GameState Previous, GameState Next) : IClientEvent;