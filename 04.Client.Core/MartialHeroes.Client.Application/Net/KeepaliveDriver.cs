using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Network.Protocol.Packets;
using MartialHeroes.Network.Protocol.Packets.Social.Packets;

namespace MartialHeroes.Client.Application.Net;

public sealed class KeepaliveDriver(IOutboundPacketSink outbound, SessionId sessionId, InFlightLatch? latch = null)
{
    public const long IdleHeartbeatIntervalMs = 20_000;

    private const ushort KeepaliveMajor = 2;
    private const ushort IdleHeartbeatMinor = 10000;
    private const ushort ToggleMinor = 112;

    private const byte ToggleEnable = 0x01;
    private const byte ToggleDisable = 0x00;

    private readonly IOutboundPacketSink _outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));

    private long _lastOutboundMs;

    public bool IsInWorld { get; private set; }

    public ValueTask OnWorldEnteredAsync(long nowMs, CancellationToken cancellationToken = default)
    {
        IsInWorld = true;
        _lastOutboundMs = nowMs;
        return SendToggleAsync(ToggleEnable, cancellationToken);
    }

    public ValueTask OnWorldExitedAsync(CancellationToken cancellationToken = default)
    {
        IsInWorld = false;
        return SendToggleAsync(ToggleDisable, cancellationToken);
    }

    public ValueTask Tick(long nowMs, CancellationToken cancellationToken = default)
    {
        if (!IsInWorld) return ValueTask.CompletedTask;

        if (latch is { IsArmed: true }) return ValueTask.CompletedTask;

        if (nowMs - _lastOutboundMs < IdleHeartbeatIntervalMs)
            return ValueTask.CompletedTask;

        _lastOutboundMs = nowMs;

        var body = new byte[CmsgKeepalive.Size];
        return _outbound.SendAsync(sessionId, KeepaliveMajor, IdleHeartbeatMinor, body, cancellationToken);
    }

    private ValueTask SendToggleAsync(byte flag, CancellationToken cancellationToken)
    {
        var body = new byte[CmsgKeepaliveToggle.WireSize];
        body[0] = flag;
        return _outbound.SendAsync(sessionId, KeepaliveMajor, ToggleMinor, body, cancellationToken);
    }
}