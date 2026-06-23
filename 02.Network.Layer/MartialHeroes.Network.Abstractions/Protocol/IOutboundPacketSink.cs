using MartialHeroes.Network.Abstractions.Session;

namespace MartialHeroes.Network.Abstractions.Protocol;

public interface IOutboundPacketSink
{
    ValueTask SendAsync(
        SessionId sessionId,
        ushort majorOpcode,
        ushort minorOpcode,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);
}