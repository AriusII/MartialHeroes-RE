using MartialHeroes.Network.Abstractions.Session;

namespace MartialHeroes.Network.Abstractions.Protocol;

/// <summary>
/// Low-level inbound dispatch seam: receives a single, fully decrypted and deframed byte
/// payload identified by its packed opcode.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam between <c>Network.Protocol</c>'s router and the rest of the stack.
/// <c>Network.Protocol</c>'s <c>PacketRouter</c> implements the call-site; <c>Client.Application</c>
/// implements <see cref="IPacketHandler"/> (the typed seam above this one) and is reached
/// via <see cref="OnUnhandled"/> for opcodes that have no specced struct yet.
/// </para>
/// <para>
/// All parameters are stack-only. <paramref name="payload"/> is a zero-copy slice over the
/// pipeline buffer; it MUST NOT be stored or read after the method returns. Implementors that
/// need to defer processing must copy to a pooled buffer first.
/// </para>
/// <para>
/// Design: <c>Network.Protocol</c>'s <c>PacketRouter</c> will reference this interface (adding a
/// downward dependency Protocol -&gt; Abstractions) once this project is stable, replacing its local
/// <c>IPacketHandler</c> seam.
/// </para>
/// </remarks>
public interface IFrameSink
{
    /// <summary>
    /// Dispatches a decoded frame to this sink.
    /// </summary>
    /// <param name="sessionId">The session on which the frame arrived.</param>
    /// <param name="packedOpcode">
    /// The <c>(major &lt;&lt; 16) | minor</c> packed opcode identifying the message family and
    /// message type. Matches the constants in <c>Network.Protocol.Opcodes.Opcodes</c>.
    /// </param>
    /// <param name="payload">
    /// The decrypted, payload-only slice of the frame buffer (header stripped). Zero-copy;
    /// the memory backing this span is owned by the pipeline and must not outlive this call.
    /// </param>
    void OnFrame(SessionId sessionId, uint packedOpcode, ReadOnlySpan<byte> payload);
}