using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;

namespace MartialHeroes.Network.Transport.Pipelines;

public sealed class RelayOutboundPacketSink : IOutboundPacketSink
{
    private volatile IOutboundPacketSink? _target;

    public ValueTask SendAsync(
        SessionId sessionId,
        ushort majorOpcode,
        ushort minorOpcode,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = _target;
        if (target is null)
            return ValueTask.CompletedTask;

        return target.SendAsync(sessionId, majorOpcode, minorOpcode, payload, cancellationToken);
    }

    public void SetTarget(IOutboundPacketSink target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
    }
}