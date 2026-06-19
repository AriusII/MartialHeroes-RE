using System.Threading.Channels;
using MartialHeroes.Network.Protocol.Routing;

namespace MartialHeroes.Client.Application.Ingestion;

/// <summary>
/// The single inbound ingestion point. Accepts already-decoded/decrypted/decompressed frames
/// (8-byte header + payload), queues them on a <see cref="Channel{T}"/>, and drains them on one
/// logical reader that routes each through the <see cref="PacketRouter"/> into the application's
/// <see cref="IPacketHandler"/>. spec: Docs/RE/opcodes.md (frame header + dispatch).
/// </summary>
/// <remarks>
/// <para>
/// <b>Topology &amp; backpressure.</b> Frames may arrive from the transport on its own thread
/// (single writer); a single application reader drains and routes them, so Domain mutation happens
/// on one logical owner and stays deterministic. The channel is <c>SingleReader = true</c>,
/// <c>SingleWriter = true</c>. It is <b>unbounded</b>: inbound game frames are authoritative state
/// transitions that must not be silently dropped (unlike UI events, which are coalescible). The
/// transport already framed on the 32-bit size field (the frame header `size` is a true u32 — the
/// long-standing u16-vs-u32 question is resolved in favour of u32; spec: Docs/RE/opcodes.md "Wire
/// frame header"), so the volume is bounded by the socket; if
/// future profiling shows unbounded growth, switch to a bounded channel with
/// <see cref="BoundedChannelFullMode.Wait"/> to apply real backpressure rather than dropping state.
/// </para>
/// <para>
/// <b>Buffer ownership.</b> Each enqueued frame is copied into its own byte array on
/// <see cref="Enqueue"/> so the transport may immediately reuse its receive buffer. The router reads
/// the copy as a span — zero further allocation per frame beyond that single owning array.
/// </para>
/// </remarks>
public sealed class InboundFrameDispatcher
{
    private readonly Channel<byte[]> _frames;
    private readonly IPacketHandler _handler;

    public InboundFrameDispatcher(IPacketHandler handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _frames = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });
    }

    /// <summary>
    /// Enqueues one full frame (header + payload). Copies the bytes so the caller's buffer is free to
    /// reuse. Returns <see langword="false"/> only after <see cref="Complete"/>.
    /// </summary>
    public bool Enqueue(ReadOnlySpan<byte> frame) => _frames.Writer.TryWrite(frame.ToArray());

    /// <summary>Signals that no more frames will be enqueued, completing the reader loop.</summary>
    public void Complete() => _frames.Writer.TryComplete();

    /// <summary>
    /// Routes a single frame synchronously through the <see cref="PacketRouter"/> into the handler,
    /// without going through the channel. Useful for tests and for callers that already own the
    /// reader thread. spec: Docs/RE/opcodes.md.
    /// </summary>
    public bool RouteNow(ReadOnlySpan<byte> frame) => PacketRouter.Route(frame, _handler);

    /// <summary>
    /// Drains the inbound channel until completion (or cancellation), routing every queued frame to
    /// the handler. Runs on a single logical reader; the caller awaits this for the session lifetime.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (byte[] frame in _frames.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            PacketRouter.Route(frame, _handler);
        }
    }
}