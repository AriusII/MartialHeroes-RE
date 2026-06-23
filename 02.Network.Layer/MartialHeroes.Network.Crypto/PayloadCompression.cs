using System.Buffers;
using K4os.Compression.LZ4;

namespace MartialHeroes.Network.Crypto;

public static class PayloadCompression
{
    public const int InboundMaxDecompressedSize = 0x2DA0;

    private const int Acceleration = 1;

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

    public static int DecompressPayloadInto(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (destination.Length > InboundMaxDecompressedSize) destination = destination[..InboundMaxDecompressedSize];

        var written = LZ4Codec.Decode(source, destination);
        if (written < 0)
            throw new InvalidOperationException(
                "LZ4 raw-block decompression failed: malformed block or destination too small.");

        return written;
    }

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