using MartialHeroes.Network.Abstractions.Session;

namespace MartialHeroes.Network.Abstractions.Protocol;

public interface IFrameSink
{
    void OnFrame(SessionId sessionId, uint packedOpcode, ReadOnlySpan<byte> payload);
}