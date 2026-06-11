namespace MartialHeroes.Client.Application.Events;

/// <summary>
/// The outbound, UI-bound event stream. The application layer publishes immutable
/// <see cref="IClientEvent"/> snapshots; the presentation layer consumes them.
/// </summary>
/// <remarks>
/// <para>
/// Backed by a <see cref="System.Threading.Channels.Channel{T}"/> (see
/// <see cref="ClientEventBus"/>). The producer side is the single network-reader logical owner that
/// drives Domain mutation; the consumer side is the presentation layer draining the stream once per
/// frame. Publishing never blocks the reader for an unbounded time (see the bus's documented
/// backpressure policy).
/// </para>
/// </remarks>
public interface IClientEventBus
{
    /// <summary>
    /// Publishes an event to the UI stream. Returns <see langword="false"/> only if the event was
    /// dropped under the bus's backpressure policy (see <see cref="ClientEventBus"/>); never blocks
    /// the calling reader thread.
    /// </summary>
    bool Publish(IClientEvent clientEvent);

    /// <summary>
    /// The consumer side. The presentation layer drains this with
    /// <see cref="System.Threading.Channels.ChannelReader{T}.ReadAllAsync"/>.
    /// </summary>
    System.Threading.Channels.ChannelReader<IClientEvent> Reader { get; }

    /// <summary>Signals that no further events will be published, completing the reader.</summary>
    void Complete();
}