using System.Buffers;
using System.Buffers.Binary;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;

namespace MartialHeroes.Network.Transport.Pipelines;

public sealed class CryptoOutboundPacketSink : IOutboundPacketSink
{
    public delegate IMemoryOwner<byte> CompressPayloadDelegate(
        ReadOnlySpan<byte> source, out int compressedLength);

    public delegate void EncryptInPlaceDelegate(Span<byte> payload);

    private const ushort KeepaliveMajor = 2;
    private const ushort KeepaliveMinor = 10000;
    private const int KeepaliveBodySize = 4;

    private readonly CompressPayloadDelegate _compress;
    private readonly EncryptInPlaceDelegate _encrypt;

    private readonly object _keepaliveGate = new();


    private readonly IConnectionSession _session;

    private byte[]? _keepaliveFrame;

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

    public async ValueTask SendAsync(
        SessionId sessionId,
        ushort majorOpcode,
        ushort minorOpcode,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        if (majorOpcode == KeepaliveMajor && minorOpcode == KeepaliveMinor)
        {
            var keepalive = GetOrBuildKeepaliveFrame();
            await _session.SendAsync(keepalive, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (payload.IsEmpty)
        {
            var headerOnly = ArrayPool<byte>.Shared.Rent(FramingConstants.HeaderSize);
            try
            {
                WriteHeader(
                    headerOnly.AsSpan(0, FramingConstants.HeaderSize),
                    FramingConstants.HeaderSize,
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

        var payloadLen = payload.Length;
        var compressBound = payloadLen + payloadLen / 255 + 16;
        var frameCapacity = FramingConstants.HeaderSize + compressBound;
        var frame = ArrayPool<byte>.Shared.Rent(frameCapacity);
        try
        {
            var payloadRegion = frame.AsSpan(FramingConstants.HeaderSize, payloadLen);
            payload.Span.CopyTo(payloadRegion);

            _encrypt(payloadRegion);

            using var compressed = _compress(payloadRegion, out var compressedLength);

            var totalSize = FramingConstants.HeaderSize + compressedLength;
            if (totalSize > FramingConstants.MaxFrameSize)
                throw new InvalidOperationException(
                    $"Outbound frame size {totalSize} exceeds the sanity bound " +
                    $"{FramingConstants.MaxFrameSize} (8 + 0x2DA0; reused from inbound LZ4 capacity — " +
                    $"no separate outbound ceiling is recovered from the spec). " +
                    $"spec: Docs/RE/specs/crypto.md §3/§5 + Docs/RE/specs/network_dispatch.md §6.2.");

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


    private ReadOnlyMemory<byte> GetOrBuildKeepaliveFrame()
    {
        var existing = Volatile.Read(ref _keepaliveFrame);
        if (existing is not null) return existing;

        lock (_keepaliveGate)
        {
            if (_keepaliveFrame is not null) return _keepaliveFrame;

            Span<byte> body = stackalloc byte[KeepaliveBodySize];
            body.Clear();

            using var compressed = _compress(body, out var compressedLength);

            var totalSize = FramingConstants.HeaderSize + compressedLength;
            var frame = new byte[totalSize];
            WriteHeader(frame, (uint)totalSize, KeepaliveMajor, KeepaliveMinor);
            compressed.Memory.Span[..compressedLength].CopyTo(frame.AsSpan(FramingConstants.HeaderSize));

            Volatile.Write(ref _keepaliveFrame, frame);
            return frame;
        }
    }

    private static void WriteHeader(Span<byte> destination, uint totalSize, ushort major, ushort minor)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination[..], totalSize);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], major);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], minor);
    }
}