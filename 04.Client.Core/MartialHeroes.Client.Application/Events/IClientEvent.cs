namespace MartialHeroes.Client.Application.Events;

/// <summary>
/// Marker for an immutable, UI-bound event published on the outbound event bus.
/// </summary>
/// <remarks>
/// Every event published to the presentation layer is an immutable snapshot (a <c>record</c>)
/// carrying only Kernel/Domain value types — never a reference to a live mutable
/// <see cref="MartialHeroes.Client.Domain.Actors.Actor"/> and never a wire struct. This guarantees
/// the consumer can read the event on its own thread without observing torn Domain state.
/// </remarks>
public interface IClientEvent;
