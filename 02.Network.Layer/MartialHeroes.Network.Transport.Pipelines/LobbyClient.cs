using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using MartialHeroes.Network.Abstractions.Lobby;

namespace MartialHeroes.Network.Transport.Pipelines;

public sealed class LobbyClient : ILobbyClient
{
    public const int LobbyBasePort = 10000;

    public const string FallbackHost = "211.196.150.4";

    public const int IpTextMaxLength = 19;

    public const int
        ServerRecordSize = LobbyServerRecordWire.WireSize;

    public const int ChannelEndpointLength = 30;


    private const int WrapperSize = 8;
    private const int WrapperSizeOffset = 0;
    private const int WrapperCountOffset = 4;
    private readonly InboundDecompressDelegate _decompress;


    private readonly string _lobbyHost;

    static LobbyClient()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }


    public LobbyClient(string lobbyHost, InboundDecompressDelegate decompress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lobbyHost);
        ArgumentNullException.ThrowIfNull(decompress);
        _lobbyHost = lobbyHost;
        _decompress = decompress;
    }


    public Task<LobbyServerListResult> FetchServerListAsync(
        CancellationToken cancellationToken = default)
    {
        var result = FetchServerListCore(cancellationToken);
        return Task.FromResult(result);
    }

    public Task<LobbyChannelEndpoint> FetchChannelEndpointAsync(
        ushort serverId,
        CancellationToken cancellationToken = default)
    {
        var result = FetchChannelEndpointCore(serverId, cancellationToken);
        return Task.FromResult(result);
    }


    private LobbyServerListResult FetchServerListCore(CancellationToken cancellationToken)
    {
        using var socket = ConnectBlocking(_lobbyHost, LobbyBasePort, cancellationToken);
        if (!socket.Connected) return LobbyServerListResult.Failed;

        var (decompressed, _) = ReceiveAndDecompress(socket, out var countWord);
        int count = (short)countWord;

        if (count < 0) return LobbyServerListResult.Failed;
        if (count == 0) return LobbyServerListResult.Empty;
        if (decompressed.Length < count * LobbyServerRecordWire.WireSize)
            return LobbyServerListResult.Failed;

        var wire = MemoryMarshal.Cast<byte, LobbyServerRecordWire>(
            decompressed.Span[..(count * LobbyServerRecordWire.WireSize)]);

        var records = new LobbyServerRecord[count];
        for (var i = 0; i < count; i++)
            records[i] = wire[i].ToRecord();

        return LobbyServerListResult.Populated(records);
    }

    private LobbyChannelEndpoint FetchChannelEndpointCore(
        ushort serverId,
        CancellationToken cancellationToken)
    {
        var channelPort = LobbyBasePort + serverId;

        using var socket = ConnectBlocking(_lobbyHost, channelPort, cancellationToken);
        if (!socket.Connected)
            throw new InvalidOperationException(
                $"Lobby channel-endpoint connect to {_lobbyHost}:{channelPort} failed.");

        var (decompressed, _) = ReceiveAndDecompress(socket, out _);

        if (decompressed.Length == 0)
            throw new InvalidOperationException(
                "Lobby channel-endpoint payload was empty. spec: Docs/RE/packets/lobby.yaml RECORD SHAPE B.");

        var window = Math.Min(decompressed.Length, ChannelEndpointLength);
        var endpointBytes = decompressed.Span[..window];

        var contentLength = endpointBytes.IndexOf((byte)0);
        if (contentLength < 0) contentLength = window;

        var endpointText = Encoding.ASCII.GetString(endpointBytes[..contentLength]);

        var parts = endpointText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[1], out var port))
            throw new InvalidOperationException(
                $"Lobby channel-endpoint text '{endpointText}' is not in 'host port' format. " +
                "spec: Docs/RE/packets/lobby.yaml RECORD SHAPE B.");

        return new LobbyChannelEndpoint(parts[0], port);
    }


    private (ReadOnlyMemory<byte> Decompressed, int DecompressedLength) ReceiveAndDecompress(
        Socket socket,
        out ushort countWord)
    {
        Span<byte> wrapper = stackalloc byte[WrapperSize];
        BlockingReceive(socket, wrapper);

        var totalSize = BinaryPrimitives.ReadUInt32LittleEndian(wrapper[..]);
        countWord = BinaryPrimitives.ReadUInt16LittleEndian(wrapper[WrapperCountOffset..]);

        if (totalSize < WrapperSize || totalSize > FramingConstants.MaxFrameSize)
            return (ReadOnlyMemory<byte>.Empty, 0);

        var payloadSize = (int)(totalSize - WrapperSize);
        if (payloadSize == 0) return (ReadOnlyMemory<byte>.Empty, 0);

        var payloadBuf = new byte[payloadSize];
        BlockingReceive(socket, payloadBuf.AsSpan());

        var decompressedOwner = _decompress(payloadBuf.AsSpan(), out var decompressedLength);
        var result = decompressedOwner.Memory.Span[..decompressedLength].ToArray();
        decompressedOwner.Dispose();

        return (result, decompressedLength);
    }

    private static void BlockingReceive(Socket socket, Span<byte> destination)
    {
        var offset = 0;
        while (offset < destination.Length)
        {
            var received = socket.Receive(destination[offset..], SocketFlags.None);
            if (received == 0)
                throw new EndOfStreamException(
                    "Lobby connection closed before the full response was received.");

            offset += received;
        }
    }

    private static Socket ConnectBlocking(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            Blocking = true
        };

        try
        {
            if (!IPAddress.TryParse(host, out var address))
                return socket;

            var endpoint = new IPEndPoint(address, port);
            socket.ConnectAsync(endpoint, cancellationToken).AsTask().GetAwaiter().GetResult();
        }
        catch (SocketException)
        {
        }
        catch (OperationCanceledException)
        {
            socket.Dispose();
            throw;
        }

        return socket;
    }
}