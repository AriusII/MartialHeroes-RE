namespace MartialHeroes.Client.Application.Contracts.Hud;

/// <summary>
///     Marker for an immutable, HUD-bound event published on the <see cref="IHudEventHub" />.
/// </summary>
/// <remarks>
///     Every HUD event is an immutable snapshot (a <c>record</c> / <c>readonly record struct</c>) carrying
///     only value types and already-decoded managed strings — never a reference to live mutable Domain
///     state and never a wire struct. This guarantees the Godot consumer can read the event on its own
///     thread (the render thread) without observing torn Domain state. Mirrors the contract of
///     <see cref="MartialHeroes.Client.Application.Contracts.Events.IClientEvent" /> but for the separate
///     World-Scene HUD surface.
/// </remarks>
public interface IHudEvent;