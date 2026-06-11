using System.Threading.Channels;

namespace MartialHeroes.Client.Application.Events;

/// <summary>
/// Default <see cref="IClientEventBus"/> backed by a <see cref="Channel{T}"/> of immutable
/// <see cref="IClientEvent"/> snapshots.
/// </summary>
/// <remarks>
/// <para>
/// <b>Topology.</b> One logical producer (the network reader that owns Domain mutation) and one
/// logical consumer (the presentation layer). The channel is therefore configured
/// <c>SingleReader = true</c>, <c>SingleWriter = true</c> — the cheapest, lock-light synchronisation
/// the BCL offers for this fan-in/fan-out shape. If a future topology adds producers, funnel them
/// through the inbound ingestion channel rather than relaxing this flag.
/// </para>
/// <para>
/// <b>Bounding / backpressure.</b> Bounded by default (capacity in the constructor) with
/// <see cref="BoundedChannelFullMode.DropOldest"/>: the UI only cares about the latest world state,
/// so under a burst we discard the oldest pending event rather than stalling the network reader or
/// growing unbounded. <see cref="Publish"/> stays non-blocking and returns the
/// <see cref="ChannelWriter{T}.TryWrite"/> result so callers can count drops. A caller that needs
/// loss-free delivery (e.g. a test) can pass <see cref="Unbounded"/>.
/// </para>
/// </remarks>
public sealed class ClientEventBus : IClientEventBus
{
    /// <summary>Sentinel capacity meaning "unbounded channel" for the constructor.</summary>
    public const int Unbounded = 0;

    /// <summary>Default bounded capacity; a generous burst window for one frame of world events.</summary>
    public const int DefaultCapacity = 1024;

    private readonly Channel<IClientEvent> _channel;

    /// <summary>
    /// Creates a bus. <paramref name="capacity"/> &gt; 0 produces a bounded channel with
    /// drop-oldest backpressure; <see cref="Unbounded"/> produces an unbounded channel.
    /// </summary>
    public ClientEventBus(int capacity = DefaultCapacity)
    {
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be >= 0.");
        }

        _channel = capacity == Unbounded
            ? Channel.CreateUnbounded<IClientEvent>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
            })
            : Channel.CreateBounded<IClientEvent>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.DropOldest,
            });
    }

    /// <inheritdoc />
    public ChannelReader<IClientEvent> Reader => _channel.Reader;

    /// <inheritdoc />
    public bool Publish(IClientEvent clientEvent)
    {
        ArgumentNullException.ThrowIfNull(clientEvent);

        // TryWrite never blocks; with DropOldest on a bounded channel it always succeeds while the
        // writer is open, returning false only after Complete(). spec: backpressure policy above.
        return _channel.Writer.TryWrite(clientEvent);
    }

    /// <inheritdoc />
    public void Complete() => _channel.Writer.TryComplete();
}