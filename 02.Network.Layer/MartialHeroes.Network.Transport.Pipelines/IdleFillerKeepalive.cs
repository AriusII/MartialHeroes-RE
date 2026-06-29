using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;

namespace MartialHeroes.Network.Transport.Pipelines;

public sealed class IdleFillerKeepalive
{
    private const ushort GameKeepaliveMajor = 1;
    private const ushort IdleFillerMinor = 2;

    private readonly Func<bool>? _isSendInFlight;
    private readonly IOutboundPacketSink _outbound;
    private readonly SessionId _sessionId;

    private long _lastFillMs = long.MinValue;
    private volatile bool _enabled;

    public IdleFillerKeepalive(
        IOutboundPacketSink outbound,
        SessionId sessionId,
        Func<bool>? isSendInFlight = null)
    {
        ArgumentNullException.ThrowIfNull(outbound);

        _outbound = outbound;
        _sessionId = sessionId;
        _isSendInFlight = isSendInFlight;
    }

    public bool IsEnabled => _enabled;

    public void Enable() => _enabled = true;

    public void Disable() => _enabled = false;

    public void NotifyActivity(long nowMs) => Volatile.Write(ref _lastFillMs, nowMs);

    public ValueTask Tick(long nowMs, CancellationToken cancellationToken = default)
    {
        if (!_enabled) return ValueTask.CompletedTask;

        if (_isSendInFlight is { } busy && busy()) return ValueTask.CompletedTask;

        if (Volatile.Read(ref _lastFillMs) == nowMs) return ValueTask.CompletedTask;

        Volatile.Write(ref _lastFillMs, nowMs);

        return _outbound.SendAsync(
            _sessionId,
            GameKeepaliveMajor,
            IdleFillerMinor,
            ReadOnlyMemory<byte>.Empty,
            cancellationToken);
    }
}
