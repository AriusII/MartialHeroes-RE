using System.Threading.Channels;
using MartialHeroes.Client.Application.Handlers;
using MartialHeroes.Network.Protocol.Routing.Routing;
using PacketRouter = MartialHeroes.Network.Protocol.Routing.Routing.PacketRouter;

namespace MartialHeroes.Client.Application.Ingestion;

public sealed class InboundFrameDispatcher(IPacketHandler handler)
{
    private readonly Channel<byte[]> _frames = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = true
    });

    private readonly IPacketHandler _handler = handler ?? throw new ArgumentNullException(nameof(handler));

    public bool Enqueue(ReadOnlySpan<byte> frame)
    {
        return _frames.Writer.TryWrite(frame.ToArray());
    }

    public void Complete()
    {
        _frames.Writer.TryComplete();
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var frame in _frames.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_handler is GamePacketHandler gameHandler) gameHandler.SetActiveFrame(frame);

            PacketRouter.Route(frame, _handler);

            if (_handler is GamePacketHandler doneHandler) doneHandler.SetActiveFrame(null);
        }
    }
}