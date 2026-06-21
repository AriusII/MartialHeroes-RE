using System.Buffers;
using K4os.Compression.LZ4;

namespace MartialHeroes.Network.Crypto;

/// <summary>
///     The LZ4 compression stage of the wire pipeline. Stock <b>raw-block</b> LZ4 — no frame format,
///     no magic, no block-size prefix, no checksum. On the outbound path this runs <i>after</i> the byte
///     cipher; on the inbound path the client runs <i>decompress only</i> — there is
///     <b>
///         no inverse cipher
///         on the client receive path
///     </b>
///     (crypto.md §5, open-question #1 RESOLVED: the byte-cipher routine
///     has a single send-side cross-reference, so it is structurally unreachable inbound). A future server
///     runs the inverse cipher to read client packets, but the client inbound stage is decompress-and-route.
///     <para>
///         LZ4 carries no length of its own — the (compressed) payload length comes from the 8-byte frame
///         header's size field, and the decompressed length is bounded by a fixed inbound cap. Callers own
///         the header and pass only the payload region here.
///     </para>
///     spec: Docs/RE/specs/crypto.md §3.2, §8.1.
/// </summary>
public static class PayloadCompression
{
    /// <summary>
    ///     Maximum decompressed inbound payload size; the decode capacity to supply. A single inbound
    ///     payload never exceeds this.
    ///     spec: Docs/RE/specs/crypto.md §3.2, §8.1 (inbound max decompressed size 0x2DA0 = 11680).
    /// </summary>
    public const int InboundMaxDecompressedSize = 0x2DA0; // 11680

    /// <summary>
    ///     LZ4 acceleration for the compressor: default fast mode.
    ///     spec: Docs/RE/specs/crypto.md §3.2, §8.1 (acceleration = 1).
    /// </summary>
    private const int Acceleration = 1;

    /// <summary>
    ///     Raw-block-compress <paramref name="source" /> into a freshly rented buffer and return the
    ///     exact compressed length. The returned <see cref="IMemoryOwner{T}" /> must be disposed by the
    ///     caller (it wraps an <see cref="ArrayPool{T}" /> rental). The wire bytes are
    ///     <c>owner.Memory.Span[..length]</c>.
    ///     spec: Docs/RE/specs/crypto.md §3.2.
    /// </summary>
    public static IMemoryOwner<byte> CompressPayload(ReadOnlySpan<byte> source, out int length)
    {
        var maxOut = LZ4Codec.MaximumOutputSize(source.Length);
        var buffer = ArrayPool<byte>.Shared.Rent(maxOut == 0 ? 1 : maxOut);
        try
        {
            length = LZ4Codec.Encode(source, buffer.AsSpan());
            if (length < 0)
                throw new InvalidOperationException(
                    "LZ4 raw-block compression failed: output buffer too small (should not happen with MaximumOutputSize).");

            return new PooledBuffer(buffer, length);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    /// <summary>
    ///     Raw-block-decompress <paramref name="source" /> into a freshly rented buffer of the inbound cap
    ///     and return the exact decompressed length. The caller disposes the returned owner. Throws if the
    ///     decompressed output would exceed <see cref="InboundMaxDecompressedSize" /> or the block is
    ///     malformed.
    ///     spec: Docs/RE/specs/crypto.md §3.2 (supply known output capacity; enforce the inbound cap).
    /// </summary>
    public static IMemoryOwner<byte> DecompressPayload(ReadOnlySpan<byte> source, out int length)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(InboundMaxDecompressedSize);
        try
        {
            length = LZ4Codec.Decode(source, buffer.AsSpan(0, InboundMaxDecompressedSize));
            if (length < 0)
                throw new InvalidOperationException(
                    "LZ4 raw-block decompression failed: malformed block or output exceeds the inbound cap (0x2DA0).");

            return new PooledBuffer(buffer, length);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    /// <summary>
    ///     Decompress directly into a caller-owned destination span (zero rental). Returns the
    ///     decompressed length, or throws if the block is malformed or overruns the destination. The
    ///     destination must be sized to at least the expected decompressed length (≤ the inbound cap).
    ///     spec: Docs/RE/specs/crypto.md §3.2.
    /// </summary>
    public static int DecompressPayloadInto(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (destination.Length > InboundMaxDecompressedSize) destination = destination[..InboundMaxDecompressedSize];

        var written = LZ4Codec.Decode(source, destination);
        if (written < 0)
            throw new InvalidOperationException(
                "LZ4 raw-block decompression failed: malformed block or destination too small.");

        return written;
    }

    /// <summary>An <see cref="IMemoryOwner{T}" /> over a pooled array, returned to the pool on dispose.</summary>
    private sealed class PooledBuffer(byte[] array, int length) : IMemoryOwner<byte>
    {
        private byte[]? _array = array;

        public Memory<byte> Memory => _array is null
            ? throw new ObjectDisposedException(nameof(PooledBuffer))
            : _array.AsMemory(0, length);

        public void Dispose()
        {
            var toReturn = _array;
            if (toReturn is not null)
            {
                _array = null;
                ArrayPool<byte>.Shared.Return(toReturn);
            }
        }
    }
}