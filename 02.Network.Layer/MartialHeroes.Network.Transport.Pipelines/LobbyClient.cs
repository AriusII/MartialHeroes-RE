using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MartialHeroes.Network.Abstractions.Lobby;

namespace MartialHeroes.Network.Transport.Pipelines;

/// <summary>
///     Synchronous, blocking TCP client for the port-10000 lobby mini-protocol.
/// </summary>
/// <remarks>
///     <para>
///         The lobby is a <b>separate surface</b> from the main game connection (spec:
///         Docs/RE/specs/login_flow.md §2, Docs/RE/packets/lobby.yaml). It does NOT use the
///         <c>(major:minor)</c> dispatcher and carries NO byte cipher. Both queries use the same
///         8-byte wrapper + LZ4 decompression.
///     </para>
///     <para>
///         <b>8-byte lobby frame wrapper</b> (spec: Docs/RE/packets/lobby.yaml — "COMMON LOBBY FRAME WRAPPER"):
///         <code>
///   +0 (u32 LE)  size   — total frame size including the 8-byte wrapper [CODE-CONFIRMED u32]
///   +4 (u16 LE)  count  — on the server-list query this is the RECORD COUNT; elsewhere ignored
///   +6 (u16 LE)  unused — reuses the game frame "minor" word; not read by lobby threads
/// </code>
///         The client reads <c>size − 8</c> payload bytes and LZ4-decompresses them.
///     </para>
///     <para>
///         <b>Dependency boundary</b>: LZ4 decompression is injected as a delegate so this project
///         does not need a direct reference to <c>Network.Crypto</c>.
///     </para>
///     <para>
///         Both lobby queries use short-lived throwaway sockets opened and closed per call, consistent
///         with the spec's "synchronous, blocking, throwaway sockets — one per query, closed after"
///         description (spec: Docs/RE/specs/login_flow.md §2).
///     </para>
/// </remarks>
public sealed class LobbyClient : ILobbyClient
{
    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------

    /// <summary>
    ///     Base lobby port.
    ///     spec: Docs/RE/specs/login_flow.md §7 — "Lobby base port = 10000".
    /// </summary>
    public const int LobbyBasePort = 10000; // spec: Docs/RE/specs/login_flow.md §7

    /// <summary>
    ///     Hardcoded fallback lobby IP when ip.txt is absent and list.dat resolution fails.
    ///     spec: Docs/RE/specs/login_flow.md §7 — "Default fallback IP = 211.196.150.4".
    /// </summary>
    public const string FallbackHost = "211.196.150.4"; // spec: Docs/RE/specs/login_flow.md §7

    /// <summary>
    ///     Maximum length of the token read from ip.txt (truncated to this length).
    ///     spec: Docs/RE/specs/login_flow.md §7 — "IP override file ip.txt: single token, ≤ 19 chars".
    /// </summary>
    public const int IpTextMaxLength = 19; // spec: Docs/RE/specs/login_flow.md §7

    /// <summary>
    ///     Byte length of each server-list record.
    ///     spec: Docs/RE/packets/lobby.yaml — "Per-record total = 8 bytes".
    /// </summary>
    public const int ServerRecordSize = 8; // spec: Docs/RE/packets/lobby.yaml RECORD SHAPE A

    /// <summary>
    ///     Byte length of the channel-endpoint text field in the decompressed payload.
    ///     spec: Docs/RE/specs/login_flow.md §7 — "Channel-endpoint copy length = 30 (0x1E) bytes".
    /// </summary>
    public const int ChannelEndpointLength = 30; // 0x1E — spec: Docs/RE/specs/login_flow.md §7

    // -----------------------------------------------------------------------
    // Wire wrapper offsets
    // -----------------------------------------------------------------------

    // spec: Docs/RE/packets/lobby.yaml — COMMON LOBBY FRAME WRAPPER
    private const int WrapperSize = 8; // lobby frame wrapper is 8 bytes (same as game header)
    private const int WrapperSizeOffset = 0; // +0 u32 LE: total frame size [CODE-CONFIRMED u32]
    private const int WrapperCountOffset = 4; // +4 u16 LE: server-record count (server-list query)
    private readonly InboundDecompressDelegate _decompress;

    // -----------------------------------------------------------------------
    // State
    // -----------------------------------------------------------------------

    private readonly string _lobbyHost;

    // -----------------------------------------------------------------------
    // Construction
    // -----------------------------------------------------------------------

    /// <summary>
    ///     Initialises a lobby client that will connect to <paramref name="lobbyHost" />.
    /// </summary>
    /// <param name="lobbyHost">
    ///     Resolved lobby host IP string. The composition root resolves it via the full 3-tier
    ///     <c>LobbyHostResolver</c> (ip.txt → list.dat/registry → fallback) and passes it in here.
    ///     spec: Docs/RE/specs/login_flow.md §2.0.
    /// </param>
    /// <param name="decompress">
    ///     LZ4 raw-block decompression delegate (e.g. <c>PayloadCompression.DecompressPayload</c>).
    ///     spec: Docs/RE/specs/crypto.md §3.2.
    /// </param>
    public LobbyClient(string lobbyHost, InboundDecompressDelegate decompress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lobbyHost);
        ArgumentNullException.ThrowIfNull(decompress);
        _lobbyHost = lobbyHost;
        _decompress = decompress;
    }

    // Host resolution is owned by the composition root's 3-tier LobbyHostResolver
    // (04.Client.Core/.../Infrastructure/Lobby), which performs ip.txt → list.dat/registry → fallback
    // and passes the resolved host into this client's constructor. spec: Docs/RE/specs/login_flow.md §2.0.

    // -----------------------------------------------------------------------
    // ILobbyClient implementation
    // -----------------------------------------------------------------------

    /// <inheritdoc />
    /// <remarks>
    ///     The blocking socket I/O is performed on the calling thread. The wrapper's <c>major</c>
    ///     field (+4) carries the record count.
    ///     spec: Docs/RE/packets/lobby.yaml — "count = wrapper.major"; RECORD SHAPE A.
    /// </remarks>
    public Task<IReadOnlyList<LobbyServerRecord>> FetchServerListAsync(
        CancellationToken cancellationToken = default)
    {
        var result = FetchServerListCore(cancellationToken);
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The blocking socket I/O is performed on the calling thread.
    ///     spec: Docs/RE/packets/lobby.yaml — RECORD SHAPE B;
    ///     Docs/RE/specs/login_flow.md §2.2.
    /// </remarks>
    public Task<LobbyChannelEndpoint> FetchChannelEndpointAsync(
        ushort serverId,
        CancellationToken cancellationToken = default)
    {
        var result = FetchChannelEndpointCore(serverId, cancellationToken);
        return Task.FromResult(result);
    }

    // -----------------------------------------------------------------------
    // Private synchronous core (wrapped by the ILobbyClient async methods)
    // -----------------------------------------------------------------------

    /// <summary>
    ///     Connects to the lobby on port <see cref="LobbyBasePort" />, reads the 8-byte wrapper
    ///     plus the LZ4-compressed server-list payload, and returns the decoded server records.
    /// </summary>
    /// <remarks>
    ///     The wrapper's <c>major</c> field (+4) carries the record count.
    ///     spec: Docs/RE/packets/lobby.yaml — "count = wrapper.major"; RECORD SHAPE A.
    /// </remarks>
    /// <param name="cancellationToken">Token to abort the blocking I/O.</param>
    /// <returns>
    ///     A list of <see cref="LobbyServerRecord" /> values, one per server, in wire order.
    ///     Returns an empty list on connect failure.
    /// </returns>
    private IReadOnlyList<LobbyServerRecord> FetchServerListCore(CancellationToken cancellationToken)
    {
        using var socket = ConnectBlocking(_lobbyHost, LobbyBasePort, cancellationToken);
        if (!socket.Connected) return [];

        var (decompressed, _) = ReceiveAndDecompress(socket, out var major);
        // spec: Docs/RE/packets/lobby.yaml — "count = wrapper.major" on the server-list query.
        int count = major; // major field re-purposed as record count on this query

        if (count <= 0 || decompressed.Length < count * ServerRecordSize) return [];

        var records = new LobbyServerRecord[count];
        var data = decompressed.Span;
        for (var i = 0; i < count; i++)
        {
            var rec = data.Slice(i * ServerRecordSize, ServerRecordSize);
            // spec: Docs/RE/packets/lobby.yaml Record Shape A:
            //   +0 u16 server_id, +2 i16 status_code, +4 i16 load, +6 i16 open_time
            records[i] = new LobbyServerRecord(
                BinaryPrimitives.ReadUInt16LittleEndian(rec[..]),
                BinaryPrimitives.ReadInt16LittleEndian(rec[2..]),
                BinaryPrimitives.ReadInt16LittleEndian(rec[4..]),
                BinaryPrimitives.ReadInt16LittleEndian(rec[6..]));
        }

        return records;
    }

    /// <summary>
    ///     Connects to port <c>10000 + <paramref name="serverId" /></c>, reads the 8-byte wrapper
    ///     plus the LZ4-compressed payload, and parses the game server endpoint from the first
    ///     30 decompressed bytes.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/packets/lobby.yaml — RECORD SHAPE B:
    ///     The decompressed payload's first 30 bytes are a NUL-padded ASCII "host port" string.
    ///     Split on whitespace: first token = host, second token = decimal port.
    ///     The selected <paramref name="serverId" /> is added directly to 10000 to form the port
    ///     (spec: Docs/RE/packets/lobby.yaml — "server_id IS the channel port offset, ports 10001..10040").
    /// </remarks>
    /// <param name="serverId">
    ///     The numeric server ID selected by the player (1..40).
    ///     spec: Docs/RE/packets/lobby.yaml +0 u16 server_id.
    /// </param>
    /// <param name="cancellationToken">Token to abort the blocking I/O.</param>
    private LobbyChannelEndpoint FetchChannelEndpointCore(
        ushort serverId,
        CancellationToken cancellationToken)
    {
        // spec: Docs/RE/packets/lobby.yaml — "port = 10000 + selected server_id".
        var channelPort = LobbyBasePort + serverId;

        using var socket = ConnectBlocking(_lobbyHost, channelPort, cancellationToken);
        if (!socket.Connected)
            throw new InvalidOperationException(
                $"Lobby channel-endpoint connect to {_lobbyHost}:{channelPort} failed.");

        var (decompressed, _) = ReceiveAndDecompress(socket, out _);

        if (decompressed.Length == 0)
            throw new InvalidOperationException(
                "Lobby channel-endpoint payload was empty. spec: Docs/RE/packets/lobby.yaml RECORD SHAPE B.");

        // The channel-endpoint string is a NUL-terminated ASCII "host port" inside a fixed copy
        // window of AT MOST 30 (0x1E) bytes. The 30 is the client's COPY-BUFFER CAP, NOT a minimum
        // payload length: the live replica returns a SHORTER decompressed payload (observed: 23 bytes)
        // when "host port" + NUL fits in fewer bytes. Read up to 30 but tolerate fewer; stop at the NUL.
        // spec correction (server/binary wins — CYCLE 4 live oracle): login_flow.md §7's "copy length
        // = 30" is a CAP, not a guaranteed length. To be promoted in lobby.yaml / login_flow.md §7.
        var window = Math.Min(decompressed.Length, ChannelEndpointLength);
        var endpointBytes = decompressed.Span[..window];

        // Find the actual content length (before any NUL padding/terminator).
        var contentLength = endpointBytes.IndexOf((byte)0);
        if (contentLength < 0) contentLength = window;

        // Decode as ASCII. The spec says ASCII "host port"; the client is Korean but the endpoint
        // string carries only dotted IPv4 + decimal port (pure ASCII).
        var endpointText = Encoding.ASCII.GetString(endpointBytes[..contentLength]);

        var parts = endpointText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[1], out var port))
            throw new InvalidOperationException(
                $"Lobby channel-endpoint text '{endpointText}' is not in 'host port' format. " +
                "spec: Docs/RE/packets/lobby.yaml RECORD SHAPE B.");

        return new LobbyChannelEndpoint(parts[0], port);
    }

    // -----------------------------------------------------------------------
    // Internal helpers
    // -----------------------------------------------------------------------

    /// <summary>
    ///     Reads the 8-byte lobby wrapper then the <c>size - 8</c> compressed payload bytes from
    ///     <paramref name="socket" />, LZ4-decompresses the payload, and returns the decompressed
    ///     bytes together with the <c>major</c> field from the wrapper.
    /// </summary>
    /// <remarks>
    ///     The receive loop uses a cooperative blocking read with back-off, consistent with the spec:
    ///     Docs/RE/specs/login_flow.md §2.0 — "retries on 'would block' with a short back-off sleep".
    /// </remarks>
    private (ReadOnlyMemory<byte> Decompressed, int DecompressedLength) ReceiveAndDecompress(
        Socket socket,
        out ushort major)
    {
        // --- Read the 8-byte wrapper ---
        Span<byte> wrapper = stackalloc byte[WrapperSize];
        BlockingReceive(socket, wrapper);

        // +0: u32 LE total frame size (spec: Docs/RE/packets/lobby.yaml — "COMMON LOBBY FRAME WRAPPER" +0 (u32) size [CODE-CONFIRMED])
        var totalSize = BinaryPrimitives.ReadUInt32LittleEndian(wrapper[..]);
        // +4: u16 LE count (= record count on server-list query; reuses the game frame "major" slot)
        major = BinaryPrimitives.ReadUInt16LittleEndian(wrapper[WrapperCountOffset..]);

        // Guard: totalSize must be at least WrapperSize (8 bytes). The u32 read could produce
        // a very large value on a malformed frame — clamp before subtraction to avoid underflow.
        if (totalSize < WrapperSize || totalSize > FramingConstants.MaxFrameSize)
            return (ReadOnlyMemory<byte>.Empty, 0);

        var payloadSize = (int)(totalSize - WrapperSize);
        if (payloadSize == 0) return (ReadOnlyMemory<byte>.Empty, 0);

        // --- Read the payload ---
        var payloadBuf = new byte[payloadSize];
        BlockingReceive(socket, payloadBuf.AsSpan());

        // --- LZ4 decompress ---
        // spec: Docs/RE/specs/crypto.md §3.2 — raw-block LZ4, no frame magic.
        // spec: Docs/RE/packets/lobby.yaml — both lobby responses are LZ4-compressed.
        var decompressedOwner = _decompress(payloadBuf.AsSpan(), out var decompressedLength);
        // Materialize into a managed array so the caller owns the lifetime cleanly.
        var result = decompressedOwner.Memory.Span[..decompressedLength].ToArray();
        decompressedOwner.Dispose();

        return (result, decompressedLength);
    }

    /// <summary>
    ///     Blocking receive that retries until all <paramref name="count" /> bytes are in
    ///     <paramref name="destination" />.
    ///     spec: Docs/RE/specs/login_flow.md §2.0 — "cooperative blocking read: retries on
    ///     'would block' with a short back-off sleep until the full wrapper / payload have arrived."
    /// </summary>
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

    /// <summary>
    ///     Opens a blocking TCP socket to <paramref name="host" />:<paramref name="port" /> and
    ///     returns it. If the connect fails the socket is returned disconnected; callers must
    ///     check <see cref="Socket.Connected" /> before use.
    ///     <para>
    ///         <b>inet_addr semantics (no DNS):</b> the lobby host must be a dotted-decimal IPv4 literal.
    ///         The binary uses <c>inet_addr</c> (not <c>gethostbyname</c>) on the lobby connect path —
    ///         DNS is absent from the lobby socket entirely (re-confirmed Phase 2b, build 263bd994).
    ///         The game-server connect (via <c>gethostbyname</c> / DNS) is a separate, unrelated path.
    ///         spec: Docs/RE/specs/login_flow.md §2.0 (inet_addr, no DNS on lobby); §3.0 (game server uses DNS).
    ///     </para>
    ///     spec: Docs/RE/specs/login_flow.md §2.0 — "connection failure is non-fatal to the helper".
    /// </summary>
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
            // LOBBY: parse the dotted-quad literal directly via IPAddress.Parse — NO DNS lookup.
            // The binary uses inet_addr (not gethostbyname) on the lobby socket path: the host
            // must be a dotted-decimal IPv4 string (e.g. "211.196.150.4"), never a DNS hostname.
            // The game-server connect (separate path) uses gethostbyname (DNS) and is not here.
            // spec: Docs/RE/specs/login_flow.md §2.0 (inet_addr, no DNS); §3.0 (game server = DNS).
            if (!IPAddress.TryParse(host, out var address))
                // Not a valid dotted-quad — return disconnected socket; non-fatal per spec.
                // spec: Docs/RE/specs/login_flow.md §2.0 — "connection failure is non-fatal".
                return socket;

            var endpoint = new IPEndPoint(address, port);
            // Synchronous connect — this is the intentionally-blocking lobby path.
            // Use the async-over-sync pattern with GetAwaiter().GetResult() so the caller's
            // blocking-thread semantics are preserved. The CancellationToken is forwarded so
            // the caller can abort a stalled connect.
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