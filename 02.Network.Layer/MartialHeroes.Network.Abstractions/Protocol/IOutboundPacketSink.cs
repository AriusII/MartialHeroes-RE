using MartialHeroes.Network.Abstractions.Session;

namespace MartialHeroes.Network.Abstractions.Protocol;

/// <summary>
///     Outbound serialisation seam: accepts a pre-serialised packet struct and queues it for
///     encryption and transmission on the associated session.
/// </summary>
/// <remarks>
///     <para>
///         <b>Consumed by:</b> <c>Client.Application</c> (use-case layer), which serialises outbound
///         request structs into a stack-allocated or pooled buffer, then calls
///         <see cref="SendAsync(SessionId, ushort, ushort, ReadOnlyMemory{byte}, CancellationToken)" />.
///     </para>
///     <para>
///         <b>Implemented by:</b> <c>Network.Crypto</c> or a thin adapter in
///         <c>Transport.Pipelines</c> that prepends the 8-byte frame header (spec: Docs/RE/opcodes.md),
///         runs in-place encryption, and forwards the result to <see cref="IConnectionSession.SendAsync" />.
///     </para>
///     <para>
///         Keeping this interface separate from <see cref="IConnectionSession" /> ensures that
///         <c>Client.Application</c> does not depend on the transport contract and that the crypto layer
///         can sit cleanly between the two without any layer skipping.
///     </para>
/// </remarks>
public interface IOutboundPacketSink
{
    /// <summary>
    ///     Asynchronously sends a single packet on the identified session.
    /// </summary>
    /// <param name="sessionId">
    ///     The session on which to transmit. Must be in a state that allows sends
    ///     (i.e. not <see cref="ConnectionState.Disconnected" /> or <see cref="ConnectionState.Faulted" />).
    /// </param>
    /// <param name="majorOpcode">
    ///     The opcode high part (message family). Written at frame header +4 (little-endian u16).
    ///     spec: Docs/RE/opcodes.md.
    /// </param>
    /// <param name="minorOpcode">
    ///     The opcode low part (message id). Written at frame header +6 (little-endian u16).
    ///     spec: Docs/RE/opcodes.md.
    /// </param>
    /// <param name="payload">
    ///     The serialised packet payload (header NOT included). The implementation prepends the
    ///     8-byte frame header and applies encryption in-place before writing to the socket.
    ///     The memory must remain valid until the returned <see cref="ValueTask" /> completes.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the pending send.</param>
    ValueTask SendAsync(
        SessionId sessionId,
        ushort majorOpcode,
        ushort minorOpcode,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);
}