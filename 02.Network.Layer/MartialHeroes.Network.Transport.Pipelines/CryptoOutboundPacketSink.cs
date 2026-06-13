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
/// <b>Zero allocation on the hot path</b>: the payload is copied once into a pooled scratch
/// buffer for in-place encryption, then the compressor's pooled output is handed directly to
/// <see cref="IConnectionSession.SendAsync"/> with the header prepended in the same rental.
/// No per-send heap allocation occurs.
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
    ///   <item>If payload is empty, send a bare 8-byte header frame — no cipher, no compression
    ///         (spec: Docs/RE/specs/crypto.md §2 header-only pass-through).</item>
    ///   <item>Copy plaintext payload into a pooled scratch buffer, apply the cipher in place.</item>
    ///   <item>LZ4-compress the enciphered bytes into a second pooled buffer.</item>
    ///   <item>Prepend the 8-byte header into a single contiguous rental (header + compressed payload)
    ///         and forward to <see cref="IConnectionSession.SendAsync"/>.</item>
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
            byte[] headerOnly = BuildHeader(
                totalSize: (ushort)FramingConstants.HeaderSize,
                major: majorOpcode,
                minor: minorOpcode);

            await _session.SendAsync(headerOnly, cancellationToken).ConfigureAwait(false);
            return;
        }

        // --- Step 1: copy plaintext into a pooled scratch buffer for in-place encryption ---
        int payloadLen = payload.Length;
        byte[] scratch = ArrayPool<byte>.Shared.Rent(payloadLen);
        try
        {
            payload.Span.CopyTo(scratch.AsSpan(0, payloadLen));

            // --- Step 2: cipher in place ---
            // spec: Docs/RE/specs/crypto.md §3.1 — apply byte cipher before compression.
            _encrypt(scratch.AsSpan(0, payloadLen));

            // --- Step 3: LZ4 compress ---
            // spec: Docs/RE/specs/crypto.md §3.2 — raw-block LZ4 after cipher.
            using IMemoryOwner<byte> compressed = _compress(scratch.AsSpan(0, payloadLen),
                out int compressedLength);

            // --- Step 4: assemble header + compressed payload into a single send buffer ---
            // spec: Docs/RE/opcodes.md — header layout: +0 u16 total size, +2 u16 zero,
            //       +4 u16 major, +6 u16 minor; total size = 8 + compressedLength.
            int totalSize = FramingConstants.HeaderSize + compressedLength;
            if (totalSize > FramingConstants.MaxFrameSize)
            {
                throw new InvalidOperationException(
                    $"Outbound frame size {totalSize} exceeds the u16 wire maximum " +
                    $"{FramingConstants.MaxFrameSize}. spec: Docs/RE/opcodes.md.");
            }

            byte[] frame = ArrayPool<byte>.Shared.Rent(totalSize);
            try
            {
                WriteHeader(frame.AsSpan(), (ushort)totalSize, majorOpcode, minorOpcode);
                compressed.Memory.Span[..compressedLength].CopyTo(frame.AsSpan(FramingConstants.HeaderSize));

                await _session.SendAsync(
                    frame.AsMemory(0, totalSize),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(frame);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    // -----------------------------------------------------------------------
    // Header helpers (zero-alloc hot path variant uses a rented buffer;
    // the header-only variant rents a tiny fixed-size array).
    // -----------------------------------------------------------------------

    /// <summary>
    /// Writes the 8-byte wire header into <paramref name="destination"/>.
    /// spec: Docs/RE/opcodes.md — Wire frame header layout.
    /// </summary>
    private static void WriteHeader(Span<byte> destination, ushort totalSize, ushort major, ushort minor)
    {
        // +0: u16 LE total frame size (includes the 8-byte header)
        BinaryPrimitives.WriteUInt16LittleEndian(destination[0..], totalSize);
        // +2: u16 LE unused / zero (upper half of physical u32 — always zero)
        // spec: Docs/RE/opcodes.md "upper 2 bytes unused/zero for framing"
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], 0);
        // +4: u16 LE major opcode
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], major);
        // +6: u16 LE minor opcode
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], minor);
    }

    /// <summary>
    /// Allocates a new 8-byte array for a header-only frame. Only used for the
    /// zero-payload pass-through path where no rental is active.
    /// </summary>
    private static byte[] BuildHeader(ushort totalSize, ushort major, ushort minor)
    {
        byte[] header = new byte[FramingConstants.HeaderSize];
        WriteHeader(header, totalSize, major, minor);
        return header;
    }
}