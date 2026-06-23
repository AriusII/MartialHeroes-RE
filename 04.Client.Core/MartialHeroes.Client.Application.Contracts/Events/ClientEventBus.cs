using System.Threading.Channels;

namespace MartialHeroes.Client.Application.Contracts.Events;

public sealed class ClientEventBus : IClientEventBus
{
    public const int Unbounded = 0;

    public const int DefaultCapacity = 1024;

    private readonly Channel<IClientEvent> _channel;

    public ClientEventBus(int capacity = DefaultCapacity)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be >= 0.");

        _channel = capacity == Unbounded
            ? Channel.CreateUnbounded<IClientEvent>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            })
            : Channel.CreateBounded<IClientEvent>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.DropOldest
            });
    }

    public ChannelReader<IClientEvent> Reader => _channel.Reader;

    public bool Publish(IClientEvent clientEvent)
    {
        ArgumentNullException.ThrowIfNull(clientEvent);

        return _channel.Writer.TryWrite(clientEvent);
    }

    public void Complete()
    {
        _channel.Writer.TryComplete();
    }
}