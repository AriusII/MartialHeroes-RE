using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;

namespace MartialHeroes.Network.Transport.Pipelines;

/// <summary>
///     Late-binding <see cref="IOutboundPacketSink" /> relay whose target can be swapped at runtime.
///     Every <see cref="SendAsync" /> call is forwarded to the current target; it is silently discarded
///     (no-op) when no target is set yet.
/// </summary>
/// <remarks>
///     <para>
///         This type is the composition-root seam that allows the <c>ApplicationUseCases</c> object graph
///         to be constructed before a live <see cref="IConnectionSession" /> exists.  Once the TCP game
///         connection is established (and a <see cref="CryptoOutboundPacketSink" /> is built over it),
///         <see cref="SetTarget" /> atomically installs the real sink so subsequent sends go to the wire.
///     </para>
///     <para>
///         Thread safety: <see cref="SetTarget" /> uses a <c>volatile</c> write; <see cref="SendAsync" />
///         reads the same field with a <c>volatile</c> read (via the <c>volatile</c> modifier).  The
///         window where an in-flight <see cref="SendAsync" /> sees the old target while
///         <see cref="SetTarget" /> runs is benign — at worst a single packet is silently dropped, which
///         is acceptable during the connection handshake.
///     </para>
/// </remarks>
public sealed class RelayOutboundPacketSink : IOutboundPacketSink
{
    private volatile IOutboundPacketSink? _target;

    /// <inheritdoc />
    /// <remarks>
    ///     Forwards to the current target when one is set; silently returns a completed
    ///     <see cref="ValueTask" /> otherwise.
    /// </remarks>
    public ValueTask SendAsync(
        SessionId sessionId,
        ushort majorOpcode,
        ushort minorOpcode,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = _target; // volatile read
        if (target is null)
            // No live connection yet — drop the outbound frame silently.
            // Callers on the login path produce outbound frames before the game connection
            // handshake is complete; those are discarded here until SetTarget is called.
            return ValueTask.CompletedTask;

        return target.SendAsync(sessionId, majorOpcode, minorOpcode, payload, cancellationToken);
    }

    /// <summary>
    ///     Installs (or replaces) the current delegate sink. Thread-safe (volatile write).
    /// </summary>
    public void SetTarget(IOutboundPacketSink target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
    }
}