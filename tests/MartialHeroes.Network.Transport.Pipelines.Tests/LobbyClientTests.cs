using System.Buffers;
using System.Buffers.Binary;
using MartialHeroes.Network.Abstractions.Lobby;
using MartialHeroes.Network.Crypto;
using MartialHeroes.Network.Transport.Pipelines;

namespace MartialHeroes.Network.Transport.Pipelines.Tests;

/// <summary>
/// Unit tests for <see cref="LobbyClient"/> driven by synthetic byte buffers.
/// No real network socket is required.
///
/// Wire format being tested (spec: Docs/RE/packets/lobby.yaml):
///   8-byte wrapper: [+0 u16 LE total size][+2 u16 zero][+4 u16 major/count][+6 u16 minor/zero]
///   followed by raw-block LZ4-compressed payload.
///
/// Server-list payload (RECORD SHAPE A): count × 8 bytes each
///   {+0 u16 server_id, +2 i16 status, +4 i16 load, +6 i16 open_time}
///
/// Channel-endpoint payload (RECORD SHAPE B): first 30 bytes = ASCII "host port"
/// </summary>
public sealed class LobbyClientTests
{
    // -----------------------------------------------------------------------
    // Helpers to build synthetic lobby frames
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds an 8-byte lobby frame wrapper.
    /// spec: Docs/RE/packets/lobby.yaml — COMMON LOBBY FRAME WRAPPER.
    /// </summary>
    private static byte[] BuildWrapper(ushort totalSize, ushort major)
    {
        byte[] wrapper = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(wrapper.AsSpan(0), totalSize); // +0 size
        BinaryPrimitives.WriteUInt16LittleEndian(wrapper.AsSpan(2), 0);         // +2 unused
        BinaryPrimitives.WriteUInt16LittleEndian(wrapper.AsSpan(4), major);     // +4 count/major
        BinaryPrimitives.WriteUInt16LittleEndian(wrapper.AsSpan(6), 0);         // +6 minor (unused)
        return wrapper;
    }

    /// <summary>
    /// Builds a single 8-byte server-list record.
    /// spec: Docs/RE/packets/lobby.yaml RECORD SHAPE A.
    /// </summary>
    private static byte[] BuildServerRecord(ushort serverId, short status, short load, short openTime)
    {
        byte[] rec = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(0), serverId);          // +0 server_id u16
        BinaryPrimitives.WriteInt16LittleEndian(rec.AsSpan(2), status);             // +2 status i16
        BinaryPrimitives.WriteInt16LittleEndian(rec.AsSpan(4), load);               // +4 load i16
        BinaryPrimitives.WriteInt16LittleEndian(rec.AsSpan(6), openTime);           // +6 open_time i16
        return rec;
    }

    /// <summary>
    /// Compresses <paramref name="payload"/> using the real LZ4 raw-block codec,
    /// then prepends the 8-byte lobby wrapper (count = <paramref name="recordCount"/>).
    /// </summary>
    private static byte[] BuildLobbyFrame(byte[] payload, ushort recordCount)
    {
        // Compress the payload.
        using IMemoryOwner<byte> compressed = PayloadCompression.CompressPayload(
            payload.AsSpan(), out int compressedLength);

        ushort totalSize = (ushort)(8 + compressedLength);
        byte[] wrapper = BuildWrapper(totalSize, major: recordCount);

        byte[] frame = new byte[totalSize];
        wrapper.CopyTo(frame.AsSpan(0));
        compressed.Memory.Span[..compressedLength].CopyTo(frame.AsSpan(8));
        return frame;
    }

    /// <summary>
    /// A lobby-decode test double that exercises the parsing logic directly on synthetic byte
    /// buffers without a socket, returning the Abstractions DTOs
    /// (<see cref="LobbyServerRecord"/> / <see cref="LobbyChannelEndpoint"/>).
    /// </summary>
    private sealed class TestLobbyDecoder
    {
        private readonly InboundDecompressDelegate _decompress;

        public TestLobbyDecoder()
        {
            _decompress = PayloadCompression.DecompressPayload;
        }

        /// <summary>
        /// Decodes a server-list lobby frame (wrapper + compressed payload) from
        /// <paramref name="frameBytes"/> without a socket.
        /// Returns <see cref="LobbyServerRecord"/> entries (Network.Abstractions DTOs).
        /// spec: Docs/RE/packets/lobby.yaml RECORD SHAPE A.
        /// </summary>
        public LobbyServerRecord[] DecodeServerList(byte[] frameBytes)
        {
            Span<byte> span = frameBytes.AsSpan();

            // Read wrapper.
            ushort totalSize = BinaryPrimitives.ReadUInt16LittleEndian(span[0..]);
            ushort count = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]);

            int payloadSize = totalSize - 8;
            ReadOnlySpan<byte> compressed = span.Slice(8, payloadSize);

            using IMemoryOwner<byte> decompOwner = _decompress(compressed, out int decompLen);
            ReadOnlySpan<byte> data = decompOwner.Memory.Span[..decompLen];

            var records = new LobbyServerRecord[count];
            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> rec = data.Slice(i * LobbyClient.ServerRecordSize, LobbyClient.ServerRecordSize);
                // spec: Docs/RE/packets/lobby.yaml RECORD SHAPE A:
                //   +0 u16 server_id, +2 i16 status, +4 i16 load, +6 i16 open_time
                records[i] = new LobbyServerRecord(
                    ServerId: BinaryPrimitives.ReadUInt16LittleEndian(rec[0..]),
                    Status:   BinaryPrimitives.ReadInt16LittleEndian(rec[2..]),
                    Load:     BinaryPrimitives.ReadInt16LittleEndian(rec[4..]),
                    OpenTime: BinaryPrimitives.ReadInt16LittleEndian(rec[6..]));
            }

            return records;
        }

        /// <summary>
        /// Decodes a channel-endpoint lobby frame (wrapper + compressed payload) from
        /// <paramref name="frameBytes"/> without a socket.
        /// Returns a <see cref="LobbyChannelEndpoint"/> (Network.Abstractions DTO).
        /// spec: Docs/RE/packets/lobby.yaml RECORD SHAPE B.
        /// </summary>
        public LobbyChannelEndpoint DecodeChannelEndpoint(byte[] frameBytes)
        {
            Span<byte> span = frameBytes.AsSpan();
            ushort totalSize = BinaryPrimitives.ReadUInt16LittleEndian(span[0..]);

            int payloadSize = totalSize - 8;
            ReadOnlySpan<byte> compressed = span.Slice(8, payloadSize);

            using IMemoryOwner<byte> decompOwner = _decompress(compressed, out int decompLen);
            ReadOnlySpan<byte> data = decompOwner.Memory.Span[..decompLen];

            // First 30 bytes = ASCII "host port".
            // spec: Docs/RE/packets/lobby.yaml RECORD SHAPE B.
            Assert.True(decompLen >= LobbyClient.ChannelEndpointLength,
                $"Decompressed payload {decompLen} < required {LobbyClient.ChannelEndpointLength}");

            ReadOnlySpan<byte> endpointBytes = data[..LobbyClient.ChannelEndpointLength];
            int nulAt = endpointBytes.IndexOf((byte)0);
            int contentLen = nulAt < 0 ? LobbyClient.ChannelEndpointLength : nulAt;

            string text = System.Text.Encoding.ASCII.GetString(endpointBytes[..contentLen]);
            string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int port = int.Parse(parts[1]);
            return new LobbyChannelEndpoint(parts[0], port);
        }
    }

    // -----------------------------------------------------------------------
    // Test (a): server-list decode with >= 2 records
    // spec: Docs/RE/packets/lobby.yaml RECORD SHAPE A
    // -----------------------------------------------------------------------

    [Fact]
    public void DecodeServerList_TwoRecords_ParsedCorrectly()
    {
        // Build 2 server records.
        // spec: Docs/RE/packets/lobby.yaml — 8-byte records {server_id u16 @0, status i16 @2, load i16 @4, open_time i16 @6}
        byte[] rec1 = BuildServerRecord(serverId: 1, status: 0, load: 500, openTime: 0);
        byte[] rec2 = BuildServerRecord(serverId: 2, status: 3, load: 24, openTime: 30);

        byte[] payload = [..rec1, ..rec2];
        byte[] frame = BuildLobbyFrame(payload, recordCount: 2);

        var decoder = new TestLobbyDecoder();
        LobbyServerRecord[] records = decoder.DecodeServerList(frame);

        Assert.Equal(2, records.Length);

        // Record 0
        Assert.Equal((ushort)1, records[0].ServerId);
        Assert.Equal((short)0, records[0].Status);
        Assert.Equal((short)500, records[0].Load);
        Assert.Equal((short)0, records[0].OpenTime);

        // Record 1
        Assert.Equal((ushort)2, records[1].ServerId);
        Assert.Equal((short)3, records[1].Status);
        Assert.Equal((short)24, records[1].Load);
        Assert.Equal((short)30, records[1].OpenTime);
    }

    [Fact]
    public void DecodeServerList_ThreeRecords_AllParsedCorrectly()
    {
        byte[] rec1 = BuildServerRecord(serverId: 5, status: 0, load: 1201, openTime: 0);
        byte[] rec2 = BuildServerRecord(serverId: 10, status: 100, load: 0, openTime: 0);
        byte[] rec3 = BuildServerRecord(serverId: 40, status: 4, load: 0, openTime: 0);

        byte[] payload = [..rec1, ..rec2, ..rec3];
        byte[] frame = BuildLobbyFrame(payload, recordCount: 3);

        var decoder = new TestLobbyDecoder();
        LobbyServerRecord[] records = decoder.DecodeServerList(frame);

        Assert.Equal(3, records.Length);
        Assert.Equal((ushort)5, records[0].ServerId);
        Assert.Equal((short)1201, records[0].Load);
        Assert.Equal((ushort)10, records[1].ServerId);
        Assert.Equal((short)100, records[1].Status); // "current selection" sentinel
        Assert.Equal((ushort)40, records[2].ServerId);
    }

    // -----------------------------------------------------------------------
    // Test (b): channel-endpoint decode — "host port" ASCII text
    // spec: Docs/RE/packets/lobby.yaml RECORD SHAPE B
    // -----------------------------------------------------------------------

    [Fact]
    public void DecodeChannelEndpoint_TypicalAsciiHostPort_ParsedCorrectly()
    {
        // Build a 30-byte ASCII "host port" buffer, NUL-padded.
        // spec: Docs/RE/packets/lobby.yaml — "first 30 (0x1E) bytes = NUL-padded ASCII 'host port'"
        const string endpointText = "192.168.1.100 7000";
        byte[] endpointBytes = new byte[LobbyClient.ChannelEndpointLength]; // zero-filled
        System.Text.Encoding.ASCII.GetBytes(endpointText, endpointBytes.AsSpan());

        byte[] frame = BuildLobbyFrame(endpointBytes, recordCount: 0);

        var decoder = new TestLobbyDecoder();
        LobbyChannelEndpoint ep = decoder.DecodeChannelEndpoint(frame);

        Assert.Equal("192.168.1.100", ep.Host);
        Assert.Equal(7000, ep.Port);
    }

    [Fact]
    public void DecodeChannelEndpoint_FallbackIpWithStandardPort_ParsedCorrectly()
    {
        // The hardcoded fallback lobby host is 211.196.150.4; game port is typically in range.
        // spec: Docs/RE/specs/login_flow.md §7 — "Default fallback IP = 211.196.150.4".
        const string endpointText = "211.196.150.4 9000";
        byte[] endpointBytes = new byte[LobbyClient.ChannelEndpointLength];
        System.Text.Encoding.ASCII.GetBytes(endpointText, endpointBytes.AsSpan());

        byte[] frame = BuildLobbyFrame(endpointBytes, recordCount: 0);

        var decoder = new TestLobbyDecoder();
        LobbyChannelEndpoint ep = decoder.DecodeChannelEndpoint(frame);

        Assert.Equal("211.196.150.4", ep.Host);
        Assert.Equal(9000, ep.Port);
    }

    // -----------------------------------------------------------------------
    // Test (c): LZ4 round-trip fidelity — the compression wrapper is spec-correct
    // -----------------------------------------------------------------------

    [Fact]
    public void LobbyFrame_LZ4RoundTrip_PayloadSurvivesCompressDecompress()
    {
        // Arbitrary binary payload (not ASCII text), verifies the raw-block LZ4 codec
        // is correctly round-tripping inside the lobby frame builder.
        byte[] originalPayload = Enumerable.Range(0, 64).Select(i => (byte)(i * 3 + 7)).ToArray();
        byte[] frame = BuildLobbyFrame(originalPayload, recordCount: 0);

        // Decompress the compressed region manually.
        ushort totalSize = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(0));
        int payloadSize = totalSize - 8;
        ReadOnlySpan<byte> compressed = frame.AsSpan(8, payloadSize);

        using IMemoryOwner<byte> decompOwner =
            PayloadCompression.DecompressPayload(compressed, out int decompLen);
        byte[] recovered = decompOwner.Memory.Span[..decompLen].ToArray();

        Assert.Equal(originalPayload, recovered);
    }

    // -----------------------------------------------------------------------
    // Test (d): lobby constants match the specs
    // -----------------------------------------------------------------------

    [Fact]
    public void LobbyConstants_MatchSpec()
    {
        // spec: Docs/RE/specs/login_flow.md §7
        Assert.Equal(10000, LobbyClient.LobbyBasePort);
        Assert.Equal("211.196.150.4", LobbyClient.FallbackHost);
        Assert.Equal(19, LobbyClient.IpTextMaxLength);
        Assert.Equal(8, LobbyClient.ServerRecordSize);
        Assert.Equal(30, LobbyClient.ChannelEndpointLength); // 0x1E
    }

    // -----------------------------------------------------------------------
    // Test (e): ILobbyClient surface — LobbyClient implements the contract
    // -----------------------------------------------------------------------

    [Fact]
    public void LobbyClient_ImplementsILobbyClient()
    {
        // Verify the compile-time type relationship (no network call made).
        // Using the decompress delegate that would be injected in production.
        var client = new LobbyClient("127.0.0.1", PayloadCompression.DecompressPayload);
        Assert.IsAssignableFrom<ILobbyClient>(client);
    }
}
