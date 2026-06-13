using System.Buffers.Binary;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Network.Crypto;
using MartialHeroes.Network.Transport.Pipelines;

namespace MartialHeroes.Network.Transport.Pipelines.Tests;

/// <summary>
/// End-to-end round-trip tests that drive the REAL outbound sink
/// (<see cref="CryptoOutboundPacketSink"/>) with the REAL cipher and compression delegates
/// (<see cref="WireCipher.EncryptInPlace"/> + <see cref="PayloadCompression.CompressPayload"/>)
/// and verify that the resulting wire frame can be recovered by
/// <see cref="FrameSplitter.RunAsync"/> wired with the REAL decompression delegate
/// (<see cref="PayloadCompression.DecompressPayload"/>).
///
/// This exercises the complete outbound pipeline per spec: Docs/RE/specs/crypto.md §3:
///   plaintext → cipher → LZ4 → 8-byte header → wire
/// and the inbound decompress per spec: Docs/RE/specs/crypto.md §5:
///   wire → 8-byte header → LZ4 decompress → dispatcher
///
/// Note: inbound does NOT apply inverse cipher (spec: Docs/RE/specs/crypto.md §5 — inbound is
/// compressed-only in this client). The round-trip therefore demonstrates that cipher+LZ4 on the
/// outbound path is the INVERSE of LZ4-only on the inbound path only when the server's send
/// path is cipher-free. For a client↔server loopback test we therefore use the
/// crypto.md-documented asymmetry: the outbound sink ciphers+compresses; the inbound path
/// only decompresses. To recover the original plaintext from the inbound stream we additionally
/// apply the inverse cipher ourselves after the dispatcher receives the decompressed bytes.
/// This matches the spec: a server that sends ciphered+compressed payloads would need
/// WireCipher.DecryptInPlace BEFORE it calls the dispatcher — exactly as documented.
///
/// For the simpler round-trip (zero-alloc path proof), the test feeds the OUTPUT of the outbound
/// sink directly through the FrameSplitter with decompress-only, then manually decrypts the
/// decompressed bytes. This exactly mirrors what a real server-side implementation would do.
/// </summary>
public sealed class OutboundSinkRoundTripTests
{
    // -----------------------------------------------------------------------
    // Helper: fake IConnectionSession that captures sent frames
    // -----------------------------------------------------------------------

    private sealed class CapturingSession : IConnectionSession
    {
        private readonly List<byte[]> _frames = [];
        public IReadOnlyList<byte[]> Frames => _frames;

        public SessionId Id => new(42);
        public ConnectionState State => ConnectionState.Handshaking;
#pragma warning disable CS0067 // Event never used — test-double stub requirement
        public event Action<SessionDisconnectedEventArgs>? Disconnected;
#pragma warning restore CS0067

        public ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
        {
            _frames.Add(frame.ToArray());
            return ValueTask.CompletedTask;
        }

        public ValueTask DisconnectAsync(DisconnectReason reason = DisconnectReason.LocalClose,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public PipeReader Input => throw new NotSupportedException("Not used in sink tests.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // -----------------------------------------------------------------------
    // Helper: capturing IFrameSink
    // -----------------------------------------------------------------------

    private sealed class CapturingFrameSink : IFrameSink
    {
        private readonly List<(uint PackedOpcode, byte[] Payload)> _frames = [];
        public IReadOnlyList<(uint PackedOpcode, byte[] Payload)> Frames => _frames;

        public void OnFrame(SessionId sessionId, uint packedOpcode, ReadOnlySpan<byte> payload)
        {
            _frames.Add((packedOpcode, payload.ToArray()));
        }
    }

    // -----------------------------------------------------------------------
    // Helper: build a sink backed by a capturing session
    // -----------------------------------------------------------------------

    private static (CryptoOutboundPacketSink Sink, CapturingSession Session) BuildSink()
    {
        var session = new CapturingSession();
        var sink = new CryptoOutboundPacketSink(
            session,
            encrypt: WireCipher.EncryptInPlace,
            compress: PayloadCompression.CompressPayload);
        return (sink, session);
    }

    // -----------------------------------------------------------------------
    // Test (a): end-to-end round-trip — outbound cipher+LZ4 then inbound LZ4+decipher
    //
    // Pipeline:
    //   plaintext payload → CryptoOutboundPacketSink (cipher+LZ4+header) → wire frame bytes
    //   → FrameSplitter (read header, strip header, LZ4 decompress)
    //   → manually WireCipher.DecryptInPlace (server-side step; not done by client inbound)
    //   → recovered plaintext matches original
    //
    // spec: Docs/RE/specs/crypto.md §3 (outbound), §5 (inbound asymmetry documented)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EndToEnd_OutboundCipherAndCompress_ThenInboundDecompressAndDecipher_RecoverOriginalPayload()
    {
        byte[] plaintext =
        [
            0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF,
            0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80
        ];
        const ushort major = 4;
        const ushort minor = 102;
        uint expectedPackedOpcode = ((uint)major << 16) | minor; // 0x40066

        (CryptoOutboundPacketSink sink, CapturingSession session) = BuildSink();

        // --- Outbound: plaintext → cipher + LZ4 + header → wire frame ---
        await sink.SendAsync(
            new SessionId(42),
            major,
            minor,
            plaintext.AsMemory());

        Assert.Single(session.Frames);
        byte[] wireFrame = session.Frames[0];

        // The wire frame must be at least HeaderSize bytes.
        Assert.True(wireFrame.Length >= FramingConstants.HeaderSize);

        // The header must encode the correct total size and opcodes.
        ushort wireSize = BinaryPrimitives.ReadUInt16LittleEndian(wireFrame.AsSpan(0));
        Assert.Equal(wireFrame.Length, wireSize);

        ushort wireMajor = BinaryPrimitives.ReadUInt16LittleEndian(wireFrame.AsSpan(4));
        ushort wireMinor = BinaryPrimitives.ReadUInt16LittleEndian(wireFrame.AsSpan(6));
        Assert.Equal(major, wireMajor);
        Assert.Equal(minor, wireMinor);

        // --- Inbound: feed wire frame through FrameSplitter with LZ4 decompress ---
        // The FrameSplitter decompresses but does NOT decrypt — that is by design
        // (spec: Docs/RE/specs/crypto.md §5 — client inbound is compressed-only).
        var pipe = new Pipe();
        var frameSink = new CapturingFrameSink();

        Task feedTask = Task.Run(async () =>
        {
            await pipe.Writer.WriteAsync(wireFrame.AsMemory());
            await pipe.Writer.CompleteAsync();
        });

        Task<FrameSplitter.FrameLoopResult> splitTask =
            FrameSplitter.RunAsync(
                pipe.Reader,
                new SessionId(42),
                frameSink,
                CancellationToken.None,
                decompress: PayloadCompression.DecompressPayload).AsTask();

        await Task.WhenAll(feedTask, splitTask);

        Assert.Equal(FrameSplitter.FrameLoopResult.Completed, await splitTask);
        Assert.Single(frameSink.Frames);
        Assert.Equal(expectedPackedOpcode, frameSink.Frames[0].PackedOpcode);

        // After FrameSplitter the payload is decompressed but still ciphered.
        // Apply the inverse cipher (server-side step) to recover the original plaintext.
        // spec: Docs/RE/specs/crypto.md §3.3 — DecryptInPlace is the exact inverse of EncryptInPlace.
        byte[] decompressedCiphered = frameSink.Frames[0].Payload;
        WireCipher.DecryptInPlace(decompressedCiphered.AsSpan());

        Assert.Equal(plaintext, decompressedCiphered);
    }

    // -----------------------------------------------------------------------
    // Test (b): header-only frame bypasses cipher and compression
    //
    // spec: Docs/RE/specs/crypto.md §2 — header-only packets (size == 8) bypass transforms.
    // opcodes.md: 2/10000 keepalive is the canonical header-only example.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HeaderOnlyFrame_BypassesCipherAndCompression_SendsExactly8Bytes()
    {
        const ushort major = 2;
        const ushort minor = 10000; // 2/10000 keepalive — spec: Docs/RE/opcodes.md

        (CryptoOutboundPacketSink sink, CapturingSession session) = BuildSink();

        // Empty payload — this is the header-only case.
        await sink.SendAsync(new SessionId(42), major, minor, ReadOnlyMemory<byte>.Empty);

        Assert.Single(session.Frames);
        byte[] frame = session.Frames[0];

        // Must be exactly 8 bytes (header only, no payload).
        Assert.Equal(FramingConstants.HeaderSize, frame.Length);

        // Header fields must be correct.
        ushort size = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(0));
        Assert.Equal(FramingConstants.HeaderSize, size); // size == 8

        ushort wireMajor = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4));
        ushort wireMinor = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(6));
        Assert.Equal(major, wireMajor);
        Assert.Equal(minor, wireMinor);

        // Unused bytes at +2 must be zero.
        ushort unused = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(2));
        Assert.Equal((ushort)0, unused);
    }

    // -----------------------------------------------------------------------
    // Test (c): various payload lengths round-trip through the real cipher + LZ4
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(16)]
    [InlineData(64)]
    [InlineData(256)]
    [InlineData(1024)]
    public async Task VariousPayloadLengths_CipherAndDecipher_RoundTrip(int payloadLength)
    {
        byte[] plaintext = new byte[payloadLength];
        for (int i = 0; i < payloadLength; i++)
        {
            plaintext[i] = (byte)(i & 0xFF);
        }

        (CryptoOutboundPacketSink sink, CapturingSession session) = BuildSink();

        await sink.SendAsync(new SessionId(42), 1, 6, plaintext.AsMemory());

        Assert.Single(session.Frames);
        byte[] wireFrame = session.Frames[0];
        Assert.True(wireFrame.Length >= FramingConstants.HeaderSize);

        // Decompress the payload region (strip the 8-byte header, then LZ4-decompress).
        ReadOnlySpan<byte> compressedPayload = wireFrame.AsSpan(FramingConstants.HeaderSize);
        using System.Buffers.IMemoryOwner<byte> decompOwner =
            PayloadCompression.DecompressPayload(compressedPayload, out int decompLength);
        byte[] decompressed = decompOwner.Memory.Span[..decompLength].ToArray();

        // The decompressed bytes are still ciphered — apply inverse.
        WireCipher.DecryptInPlace(decompressed.AsSpan());

        Assert.Equal(plaintext, decompressed);
    }
}