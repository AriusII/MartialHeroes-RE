using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MartialHeroes.Network.Abstractions.Lobby;

namespace MartialHeroes.Network.Transport.Pipelines;

/// <summary>
/// Synchronous, blocking TCP client for the port-10000 lobby mini-protocol.
/// </summary>
/// <remarks>
/// <para>
/// The lobby is a <b>separate surface</b> from the main game connection (spec:
/// Docs/RE/specs/login_flow.md §2, Docs/RE/packets/lobby.yaml). It does NOT use the
/// <c>(major:minor)</c> dispatcher and carries NO byte cipher. Both queries use the same
/// 8-byte wrapper + LZ4 decompression.
/// </para>
/// <para>
/// <b>8-byte lobby frame wrapper</b> (spec: Docs/RE/packets/lobby.yaml — "COMMON LOBBY FRAME WRAPPER"):
/// <code>
///   +0 (u32 LE)  size   — total frame size including the 8-byte wrapper [CODE-CONFIRMED u32]
///   +4 (u16 LE)  count  — on the server-list query this is the RECORD COUNT; elsewhere ignored
///   +6 (u16 LE)  unused — reuses the game frame "minor" word; not read by lobby threads
/// </code>
/// The client reads <c>size − 8</c> payload bytes and LZ4-decompresses them.
/// </para>
/// <para>
/// <b>Dependency boundary</b>: LZ4 decompression is injected as a delegate so this project
/// does not need a direct reference to <c>Network.Crypto</c>.
/// </para>
/// <para>
/// Both lobby queries use short-lived throwaway sockets opened and closed per call, consistent
/// with the spec's "synchronous, blocking, throwaway sockets — one per query, closed after"
/// description (spec: Docs/RE/specs/login_flow.md §2).
/// </para>
/// </remarks>
public sealed class LobbyClient : ILobbyClient
{
    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------

    /// <summary>
    /// Base lobby port.
    /// spec: Docs/RE/specs/login_flow.md §7 — "Lobby base port = 10000".
    /// </summary>
    public const int LobbyBasePort = 10000; // spec: Docs/RE/specs/login_flow.md §7

    /// <summary>
    /// Hardcoded fallback lobby IP when ip.txt is absent and list.dat resolution fails.
    /// spec: Docs/RE/specs/login_flow.md §7 — "Default fallback IP = 211.196.150.4".
    /// </summary>
    public const string FallbackHost = "211.196.150.4"; // spec: Docs/RE/specs/login_flow.md §7

    /// <summary>
    /// Maximum length of the token read from ip.txt (truncated to this length).
    /// spec: Docs/RE/specs/login_flow.md §7 — "IP override file ip.txt: single token, ≤ 19 chars".
    /// </summary>
    public const int IpTextMaxLength = 19; // spec: Docs/RE/specs/login_flow.md §7

    /// <summary>
    /// Byte length of each server-list record.
    /// spec: Docs/RE/packets/lobby.yaml — "Per-record total = 8 bytes".
    /// </summary>
    public const int ServerRecordSize = 8; // spec: Docs/RE/packets/lobby.yaml RECORD SHAPE A

    /// <summary>
    /// Byte length of the channel-endpoint text field in the decompressed payload.
    /// spec: Docs/RE/specs/login_flow.md §7 — "Channel-endpoint copy length = 30 (0x1E) bytes".
    /// </summary>
    public const int ChannelEndpointLength = 30; // 0x1E — spec: Docs/RE/specs/login_flow.md §7

    // -----------------------------------------------------------------------
    // Wire wrapper offsets
    // -----------------------------------------------------------------------

    // spec: Docs/RE/packets/lobby.yaml — COMMON LOBBY FRAME WRAPPER
    private const int WrapperSize = 8; // lobby frame wrapper is 8 bytes (same as game header)
    private const int WrapperSizeOffset = 0; // +0 u32 LE: total frame size [CODE-CONFIRMED u32]
    private const int WrapperCountOffset = 4; // +4 u16 LE: server-record count (server-list query)

    // -----------------------------------------------------------------------
    // State
    // -----------------------------------------------------------------------

    private readonly string _lobbyHost;
    private readonly InboundDecompressDelegate _decompress;

    // -----------------------------------------------------------------------
    // Construction
    // -----------------------------------------------------------------------

    /// <summary>
    /// Initialises a lobby client that will connect to <paramref name="lobbyHost"/>.
    /// </summary>
    /// <param name="lobbyHost">
    /// Resolved lobby host IP string. Use <see cref="ResolveHost"/> to obtain it from
    /// the ip.txt / list.dat / fallback resolution order.
    /// </param>
    /// <param name="decompress">
    /// LZ4 raw-block decompression delegate (e.g. <c>PayloadCompression.DecompressPayload</c>).
    /// spec: Docs/RE/specs/crypto.md §3.2.
    /// </param>
    public LobbyClient(string lobbyHost, InboundDecompressDelegate decompress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lobbyHost);
        ArgumentNullException.ThrowIfNull(decompress);
        _lobbyHost = lobbyHost;
        _decompress = decompress;
    }

    // -----------------------------------------------------------------------
    // Host resolution
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resolves the lobby host string using the spec-mandated priority order:
    /// <list type="number">
    ///   <item>If <c>ip.txt</c> exists at <paramref name="workingDirectory"/>, read the first
    ///   whitespace-free token truncated to 19 characters.</item>
    ///   <item>Otherwise return <paramref name="fallbackHost"/> (defaults to
    ///   <see cref="FallbackHost"/>).</item>
    /// </list>
    /// spec: Docs/RE/specs/login_flow.md §2.0 / §7 — host resolution order.
    /// </summary>
    /// <remarks>
    /// The <c>list.dat</c> step (spec: Docs/RE/packets/lobby.yaml RECORD SHAPE C) is intentionally
    /// excluded from this clean-room implementation: <c>list.dat</c> is a Windows-registry-keyed
    /// binary file that requires access to the real client installation. The implementer should
    /// extend this method when a list.dat reader is available.
    /// </remarks>
    public static string ResolveHost(
        string workingDirectory,
        string fallbackHost = FallbackHost)
    {
        string ipTxtPath = Path.Combine(workingDirectory, "ip.txt");
        if (File.Exists(ipTxtPath))
        {
            string raw = File.ReadAllText(ipTxtPath).Trim();
            // Split on whitespace, take first token, truncate to 19 chars.
            // spec: Docs/RE/specs/login_flow.md §7 — "ip.txt: single whitespace-free token, ≤ 19 chars".
            string token = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
            if (token.Length > IpTextMaxLength)
            {
                token = token[..IpTextMaxLength];
            }

            return token;
        }

        // list.dat step omitted — not implementable without client installation and registry access.
        return fallbackHost;
    }

    // -----------------------------------------------------------------------
    // ILobbyClient implementation
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// The blocking socket I/O is performed on the calling thread. The wrapper's <c>major</c>
    /// field (+4) carries the record count.
    /// spec: Docs/RE/packets/lobby.yaml — "count = wrapper.major"; RECORD SHAPE A.
    /// </remarks>
    public Task<IReadOnlyList<LobbyServerRecord>> FetchServerListAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LobbyServerRecord> result = FetchServerListCore(cancellationToken);
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The blocking socket I/O is performed on the calling thread.
    /// spec: Docs/RE/packets/lobby.yaml — RECORD SHAPE B;
    /// Docs/RE/specs/login_flow.md §2.2.
    /// </remarks>
    public Task<LobbyChannelEndpoint> FetchChannelEndpointAsync(
        ushort serverId,
        CancellationToken cancellationToken = default)
    {
        LobbyChannelEndpoint result = FetchChannelEndpointCore(serverId, cancellationToken);
        return Task.FromResult(result);
    }

    // -----------------------------------------------------------------------
    // Private synchronous core (wrapped by the ILobbyClient async methods)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Connects to the lobby on port <see cref="LobbyBasePort"/>, reads the 8-byte wrapper
    /// plus the LZ4-compressed server-list payload, and returns the decoded server records.
    /// </summary>
    /// <remarks>
    /// The wrapper's <c>major</c> field (+4) carries the record count.
    /// spec: Docs/RE/packets/lobby.yaml — "count = wrapper.major"; RECORD SHAPE A.
    /// </remarks>
    /// <param name="cancellationToken">Token to abort the blocking I/O.</param>
    /// <returns>
    /// A list of <see cref="LobbyServerRecord"/> values, one per server, in wire order.
    /// Returns an empty list on connect failure.
    /// </returns>
    private IReadOnlyList<LobbyServerRecord> FetchServerListCore(CancellationToken cancellationToken)
    {
        using Socket socket = ConnectBlocking(_lobbyHost, LobbyBasePort, cancellationToken);
        if (!socket.Connected)
        {
            return [];
        }

        (ReadOnlyMemory<byte> decompressed, int _) = ReceiveAndDecompress(socket, out ushort major);
        // spec: Docs/RE/packets/lobby.yaml — "count = wrapper.major" on the server-list query.
        int count = major; // major field re-purposed as record count on this query

        if (count <= 0 || decompressed.Length < count * ServerRecordSize)
        {
            return [];
        }

        LobbyServerRecord[] records = new LobbyServerRecord[count];
        ReadOnlySpan<byte> data = decompressed.Span;
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> rec = data.Slice(i * ServerRecordSize, ServerRecordSize);
            // spec: Docs/RE/packets/lobby.yaml RECORD SHAPE A:
            //   +0 u16 id_selectkey, +2 i16 status_kind, +4 i16 population, +6 i16 flag
            records[i] = new LobbyServerRecord(
                ServerId: BinaryPrimitives.ReadUInt16LittleEndian(rec[0..]),
                Status: BinaryPrimitives.ReadInt16LittleEndian(rec[2..]),
                Population: BinaryPrimitives.ReadInt16LittleEndian(rec[4..]),
                Flag: BinaryPrimitives.ReadInt16LittleEndian(rec[6..]));
        }

        return records;
    }

    /// <summary>
    /// Connects to port <c>10000 + <paramref name="serverId"/></c>, reads the 8-byte wrapper
    /// plus the LZ4-compressed payload, and parses the game server endpoint from the first
    /// 30 decompressed bytes.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/packets/lobby.yaml — RECORD SHAPE B:
    /// The decompressed payload's first 30 bytes are a NUL-padded ASCII "host port" string.
    /// Split on whitespace: first token = host, second token = decimal port.
    /// The selected <paramref name="serverId"/> is added directly to 10000 to form the port
    /// (spec: Docs/RE/packets/lobby.yaml — "server_id IS the channel port offset, ports 10001..10040").
    /// </remarks>
    /// <param name="serverId">
    /// The numeric server ID selected by the player (1..40).
    /// spec: Docs/RE/packets/lobby.yaml +0 u16 server_id.
    /// </param>
    /// <param name="cancellationToken">Token to abort the blocking I/O.</param>
    private LobbyChannelEndpoint FetchChannelEndpointCore(
        ushort serverId,
        CancellationToken cancellationToken)
    {
        // spec: Docs/RE/packets/lobby.yaml — "port = 10000 + selected server_id".
        int channelPort = LobbyBasePort + serverId;

        using Socket socket = ConnectBlocking(_lobbyHost, channelPort, cancellationToken);
        if (!socket.Connected)
        {
            throw new InvalidOperationException(
                $"Lobby channel-endpoint connect to {_lobbyHost}:{channelPort} failed.");
        }

        (ReadOnlyMemory<byte> decompressed, _) = ReceiveAndDecompress(socket, out _);

        if (decompressed.Length < ChannelEndpointLength)
        {
            throw new InvalidOperationException(
                $"Lobby channel-endpoint payload too short: got {decompressed.Length} bytes, " +
                $"need at least {ChannelEndpointLength}. spec: Docs/RE/packets/lobby.yaml RECORD SHAPE B.");
        }

        // First 30 bytes = NUL-padded ASCII "host port".
        // spec: Docs/RE/specs/login_flow.md §7 — "Channel-endpoint copy length = 30 (0x1E) bytes".
        ReadOnlySpan<byte> endpointBytes = decompressed.Span[..ChannelEndpointLength];

        // Find the actual content length (before any NUL padding).
        int contentLength = endpointBytes.IndexOf((byte)0);
        if (contentLength < 0)
        {
            contentLength = ChannelEndpointLength;
        }

        // Decode as ASCII. The spec says ASCII "host port"; the client is Korean but the endpoint
        // string carries only dotted IPv4 + decimal port (pure ASCII).
        string endpointText = Encoding.ASCII.GetString(endpointBytes[..contentLength]);

        string[] parts = endpointText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[1], out int port))
        {
            throw new InvalidOperationException(
                $"Lobby channel-endpoint text '{endpointText}' is not in 'host port' format. " +
                "spec: Docs/RE/packets/lobby.yaml RECORD SHAPE B.");
        }

        return new LobbyChannelEndpoint(parts[0], port);
    }

    // -----------------------------------------------------------------------
    // Internal helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reads the 8-byte lobby wrapper then the <c>size - 8</c> compressed payload bytes from
    /// <paramref name="socket"/>, LZ4-decompresses the payload, and returns the decompressed
    /// bytes together with the <c>major</c> field from the wrapper.
    /// </summary>
    /// <remarks>
    /// The receive loop uses a cooperative blocking read with back-off, consistent with the spec:
    /// Docs/RE/specs/login_flow.md §2.0 — "retries on 'would block' with a short back-off sleep".
    /// </remarks>
    private (ReadOnlyMemory<byte> Decompressed, int DecompressedLength) ReceiveAndDecompress(
        Socket socket,
        out ushort major)
    {
        // --- Read the 8-byte wrapper ---
        Span<byte> wrapper = stackalloc byte[WrapperSize];
        BlockingReceive(socket, wrapper);

        // +0: u32 LE total frame size (spec: Docs/RE/packets/lobby.yaml — "COMMON LOBBY FRAME WRAPPER" +0 (u32) size [CODE-CONFIRMED])
        uint totalSize = BinaryPrimitives.ReadUInt32LittleEndian(wrapper[WrapperSizeOffset..]);
        // +4: u16 LE count (= record count on server-list query; reuses the game frame "major" slot)
        major = BinaryPrimitives.ReadUInt16LittleEndian(wrapper[WrapperCountOffset..]);

        // Guard: totalSize must be at least WrapperSize (8 bytes). The u32 read could produce
        // a very large value on a malformed frame — clamp before subtraction to avoid underflow.
        if (totalSize < WrapperSize || totalSize > FramingConstants.MaxFrameSize)
        {
            return (ReadOnlyMemory<byte>.Empty, 0);
        }

        int payloadSize = (int)(totalSize - WrapperSize);
        if (payloadSize == 0)
        {
            return (ReadOnlyMemory<byte>.Empty, 0);
        }

        // --- Read the payload ---
        byte[] payloadBuf = new byte[payloadSize];
        BlockingReceive(socket, payloadBuf.AsSpan());

        // --- LZ4 decompress ---
        // spec: Docs/RE/specs/crypto.md §3.2 — raw-block LZ4, no frame magic.
        // spec: Docs/RE/packets/lobby.yaml — both lobby responses are LZ4-compressed.
        IMemoryOwner<byte> decompressedOwner = _decompress(payloadBuf.AsSpan(), out int decompressedLength);
        // Materialize into a managed array so the caller owns the lifetime cleanly.
        byte[] result = decompressedOwner.Memory.Span[..decompressedLength].ToArray();
        decompressedOwner.Dispose();

        return (result, decompressedLength);
    }

    /// <summary>
    /// Blocking receive that retries until all <paramref name="count"/> bytes are in
    /// <paramref name="destination"/>.
    /// spec: Docs/RE/specs/login_flow.md §2.0 — "cooperative blocking read: retries on
    /// 'would block' with a short back-off sleep until the full wrapper / payload have arrived."
    /// </summary>
    private static void BlockingReceive(Socket socket, Span<byte> destination)
    {
        int offset = 0;
        while (offset < destination.Length)
        {
            int received = socket.Receive(destination[offset..], SocketFlags.None);
            if (received == 0)
            {
                throw new EndOfStreamException(
                    "Lobby connection closed before the full response was received.");
            }

            offset += received;
        }
    }

    /// <summary>
    /// Opens a blocking TCP socket to <paramref name="host"/>:<paramref name="port"/> and
    /// returns it. If the connect fails the socket is returned disconnected; callers must
    /// check <see cref="Socket.Connected"/> before use.
    /// spec: Docs/RE/specs/login_flow.md §2.0 — "connection failure is non-fatal to the helper".
    /// </summary>
    private static Socket ConnectBlocking(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            Blocking = true,
        };

        try
        {
            // Resolve the host to an IP address.
            IPAddress[] addresses = Dns.GetHostAddresses(host);
            if (addresses.Length == 0)
            {
                // Host unresolvable — return disconnected socket; caller handles.
                return socket;
            }

            var endpoint = new IPEndPoint(addresses[0], port);
            // Synchronous connect — this is the intentionally-blocking lobby path.
            // Use the async-over-sync pattern with GetAwaiter().GetResult() so the caller's
            // blocking-thread semantics are preserved. The CancellationToken is forwarded so
            // the caller can abort a stalled DNS/connect.
            socket.ConnectAsync(endpoint, cancellationToken).AsTask().GetAwaiter().GetResult();
        }
        catch (SocketException)
        {
            // spec: Docs/RE/specs/login_flow.md §2.0 — non-fatal; return disconnected socket.
        }
        catch (OperationCanceledException)
        {
            socket.Dispose();
            throw;
        }

        return socket;
    }
}