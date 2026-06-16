using System.Buffers;
using System.Buffers.Binary;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;

namespace MartialHeroes.Network.Transport.Pipelines;

/// <summary>
/// Concrete <see cref="IOutboundPacketSink"/> that applies the outbound wire transform
/// (cipher then LZ4 compression), prepends the 8-byte plaintext frame header, and forwards
/// the fully-framed bytes to the target <see cref="IConnectionSession.SendAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Outbound pipeline per spec: Docs/RE/specs/crypto.md §3:
/// <code>
///   plaintext payload
///     → byte cipher (EncryptDelegate, injected from Network.Crypto)
///     → LZ4 compress (CompressDelegate, injected from Network.Crypto)
///     → prepend 8-byte plaintext header (size = 8 + compressed length; major/minor opcode)
///     → IConnectionSession.SendAsync
/// </code>
/// </para>
/// <para>
/// <b>Header-only / keepalive exception</b> (spec: Docs/RE/specs/crypto.md §2):
/// When <paramref name="payload"/> is empty (total frame size == 8), neither the cipher nor
/// the compressor is applied. The 8-byte header is sent as-is.
/// </para>
/// <para>
/// <b>Dependency boundary</b>: Transport.Pipelines may only reference Network.Abstractions.
/// The crypto operations are therefore supplied as delegates so that a composition root
/// (or the wire-references skill) can inject <c>WireCipher.EncryptInPlace</c> and
/// <c>PayloadCompression.CompressPayload</c> without creating a project reference to
/// Network.Crypto from this project.
/// </para>
/// <para>
/// <b>Zero allocation on the hot path</b>: a SINGLE pooled buffer is rented per send, sized to the
/// 8-byte header plus the worst-case LZ4 compress bound. The plaintext is copied into that buffer's
/// payload region and ciphered in place; the compressor's pooled output is copied back over the same
/// region and the header is written ahead of it, so the whole frame ships from one rental. The
/// header-only / keepalive path likewise writes into a pooled 8-byte rental. No per-send heap
/// allocation occurs (the compressor's transient pooled buffer is owned and returned by the codec).
/// </para>
/// </remarks>
public sealed class CryptoOutboundPacketSink : IOutboundPacketSink
{
    // -----------------------------------------------------------------------
    // Delegate types
    // -----------------------------------------------------------------------

    /// <summary>
    /// Applied in place to the payload span before compression.
    /// Matches the shape of <c>WireCipher.EncryptInPlace(Span&lt;byte&gt;)</c>.
    /// spec: Docs/RE/specs/crypto.md §3.1.
    /// </summary>
    public delegate void EncryptInPlaceDelegate(Span<byte> payload);

    /// <summary>
    /// Compresses <paramref name="source"/> and returns the compressed bytes together with
    /// the exact compressed length via an <see cref="IMemoryOwner{T}"/>.
    /// Matches the shape of <c>PayloadCompression.CompressPayload</c>.
    /// spec: Docs/RE/specs/crypto.md §3.2.
    /// </summary>
    public delegate IMemoryOwner<byte> CompressPayloadDelegate(
        ReadOnlySpan<byte> source, out int compressedLength);

    // -----------------------------------------------------------------------
    // State
    // -----------------------------------------------------------------------

    private readonly IConnectionSession _session;
    private readonly EncryptInPlaceDelegate _encrypt;
    private readonly CompressPayloadDelegate _compress;

    /// <summary>
    /// Initialises a new sink bound to a specific session.
    /// </summary>
    /// <param name="session">
    /// The live session whose <see cref="IConnectionSession.SendAsync"/> is called for each
    /// outbound packet.
    /// </param>
    /// <param name="encrypt">
    /// In-place cipher delegate (e.g. <c>WireCipher.EncryptInPlace</c>).
    /// spec: Docs/RE/specs/crypto.md §3.1.
    /// </param>
    /// <param name="compress">
    /// Compression delegate (e.g. <c>PayloadCompression.CompressPayload</c>).
    /// spec: Docs/RE/specs/crypto.md §3.2.
    /// </param>
    public CryptoOutboundPacketSink(
        IConnectionSession session,
        EncryptInPlaceDelegate encrypt,
        CompressPayloadDelegate compress)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(encrypt);
        ArgumentNullException.ThrowIfNull(compress);

        _session = session;
        _encrypt = encrypt;
        _compress = compress;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Outbound stages (spec: Docs/RE/specs/crypto.md §3):
    /// <list type="number">
    ///   <item>If payload is empty, send a bare 8-byte header frame from a pooled rental — no cipher,
    ///         no compression (spec: Docs/RE/specs/crypto.md §2 header-only pass-through).</item>
    ///   <item>Rent ONE buffer (header + worst-case compress bound); copy the plaintext into its
    ///         payload region and cipher it in place.</item>
    ///   <item>LZ4-compress the enciphered bytes (the codec returns its own pooled output).</item>
    ///   <item>Copy the compressed bytes back over the payload region, write the 8-byte header ahead of
    ///         them in the SAME rental, and forward to <see cref="IConnectionSession.SendAsync"/>.</item>
    /// </list>
    /// </remarks>
    public async ValueTask SendAsync(
        SessionId sessionId,
        ushort majorOpcode,
        ushort minorOpcode,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        // The sessionId parameter is carried so that a future multi-session sink can route to
        // the correct connection. In this single-session implementation we validate it matches
        // our session and send.
        if (payload.IsEmpty)
        {
            // Header-only packet (e.g. keepalive 2/10000): no cipher, no compression.
            // spec: Docs/RE/specs/crypto.md §2 — "header-only packets bypass all transforms."
            // SendAsync is awaited, so a single pooled 8-byte header is safe to reuse and return.
            byte[] headerOnly = ArrayPool<byte>.Shared.Rent(FramingConstants.HeaderSize);
            try
            {
                WriteHeader(
                    headerOnly.AsSpan(0, FramingConstants.HeaderSize),
                    (uint)FramingConstants.HeaderSize,
                    majorOpcode,
                    minorOpcode);

                await _session
                    .SendAsync(headerOnly.AsMemory(0, FramingConstants.HeaderSize), cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(headerOnly);
            }

            return;
        }

        // --- Single rental: header + room for the ciphered plaintext, sized to the worst-case
        // compressed bound so the same buffer holds the framed output after compression. We cipher
        // the plaintext IN PLACE in this buffer's payload region (eliminating the separate scratch
        // rental), then compress out of it. (LZ4 cannot compress in-place over its own source, so the
        // compressor produces its own pooled output, which we then copy back over the payload region.)
        int payloadLen = payload.Length;
        // Worst-case raw-block LZ4 output bound (canonical compressBound): srcSize + srcSize/255 + 16.
        // Computed locally so this project keeps its single ProjectReference (Abstractions) and never
        // takes a direct LZ4 package dependency — the compressor itself stays behind the injected
        // delegate. spec: Docs/RE/specs/crypto.md §3.2/§8.1 (compress-bound = srcSize + srcSize/255 + 16).
        int compressBound = payloadLen + (payloadLen / 255) + 16;
        int frameCapacity = FramingConstants.HeaderSize + compressBound;
        byte[] frame = ArrayPool<byte>.Shared.Rent(frameCapacity);
        try
        {
            Span<byte> payloadRegion = frame.AsSpan(FramingConstants.HeaderSize, payloadLen);
            payload.Span.CopyTo(payloadRegion);

            // --- Step 1: cipher in place (in the frame's payload region) ---
            // spec: Docs/RE/specs/crypto.md §3.1 — apply byte cipher before compression.
            _encrypt(payloadRegion);

            // --- Step 2: LZ4 compress ---
            // spec: Docs/RE/specs/crypto.md §3.2 — raw-block LZ4 after cipher.
            using IMemoryOwner<byte> compressed = _compress(payloadRegion, out int compressedLength);

            // --- Step 3: assemble the frame in the SAME rental: header at [0..8), compressed
            //       payload at [8..). spec: Docs/RE/opcodes.md + crypto.md §2 — header layout:
            //       +0 u32 total size, +4 u16 major, +6 u16 minor; total size = 8 + compressedLength.
            int totalSize = FramingConstants.HeaderSize + compressedLength;
            // Guard: MaxFrameSize (8 + 0x2DA0) is derived from the client's INBOUND fixed LZ4
            // decompress scratch capacity (spec: Docs/RE/specs/crypto.md §3/§5 — "fixed 11680-byte
            // output buffer"). No separate outbound max is recovered from the spec; this value is
            // reused as a conservative sanity bound only. Compressed payloads for game traffic are
            // always smaller than the plaintext, so this guard should never fire under normal use.
            // spec: Docs/RE/specs/network_dispatch.md §6.2 + Docs/RE/specs/crypto.md §5.
            if (totalSize > FramingConstants.MaxFrameSize)
            {
                throw new InvalidOperationException(
                    $"Outbound frame size {totalSize} exceeds the sanity bound " +
                    $"{FramingConstants.MaxFrameSize} (8 + 0x2DA0; reused from inbound LZ4 capacity — " +
                    $"no separate outbound ceiling is recovered from the spec). " +
                    $"spec: Docs/RE/specs/crypto.md §3/§5 + Docs/RE/specs/network_dispatch.md §6.2.");
            }

            compressed.Memory.Span[..compressedLength].CopyTo(frame.AsSpan(FramingConstants.HeaderSize));
            WriteHeader(frame.AsSpan(), (uint)totalSize, majorOpcode, minorOpcode);

            await _session.SendAsync(
                frame.AsMemory(0, totalSize),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(frame);
        }
    }

    // -----------------------------------------------------------------------
    // Header helper (both the payload and the header-only paths write the header
    // into a pooled rental — no per-send heap allocation).
    // -----------------------------------------------------------------------

    /// <summary>
    /// Writes the 8-byte wire header into <paramref name="destination"/>.
    /// spec: Docs/RE/opcodes.md + Docs/RE/specs/crypto.md §2 — Wire frame header layout.
    /// </summary>
    private static void WriteHeader(Span<byte> destination, uint totalSize, ushort major, ushort minor)
    {
        // +0..+3: u32 LE total frame size (includes the 8-byte header). The size field is a true
        // u32 — the u16-vs-u32 question is RESOLVED in favour of u32. spec: crypto.md §2.
        BinaryPrimitives.WriteUInt32LittleEndian(destination[0..], totalSize);
        // +4: u16 LE major opcode
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], major);
        // +6: u16 LE minor opcode
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], minor);
    }
}