// NetSmoke — standalone headless diagnostic harness for the MH network stack.
//
// PURPOSE: connect to the live Martial Heroes replica server and observe the real wire
// conversation — the L1 instrument for the live-integration loop.  Buildable and parameterizable;
// the maintainer + Tier-1 run it against the live server.  Do NOT run it automatically.
//
// spec: Docs/RE/specs/crypto.md         (cipher, LZ4, 0/0→1/4 handshake)
// spec: Docs/RE/specs/login_flow.md     (lobby mini-protocol, host resolution)
// spec: Docs/RE/specs/network_dispatch.md (frame header layout, 0/0 dispatch)
// spec: Docs/RE/opcodes.md              (wire frame header: [u32 size][u16 major][u16 minor])
// spec: Docs/RE/packets/cmsg_char_enter.yaml (1/9 CmsgEnterGameRequest, 40-byte body)
// spec: Docs/RE/packets/cmsg_char_select.yaml (1/7 CmsgSelectCharacter, 2-byte body)

using System.Buffers.Binary;
using System.Net;
using System.Text;
using MartialHeroes.Network.Abstractions.Lobby;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Network.Abstractions.Transport;
using MartialHeroes.Network.Crypto;
using MartialHeroes.Network.Protocol.Packets;
using MartialHeroes.Network.Transport.Pipelines;

namespace MartialHeroes.Tools.NetSmoke;

internal static class Program
{
    // -----------------------------------------------------------------------
    // Defaults
    // -----------------------------------------------------------------------

    // Default lobby host.  Resolution order: --host CLI → MH_LOBBY_HOST env →
    // ip.txt → this fallback.
    // spec: Docs/RE/specs/login_flow.md §7 — "Default fallback IP = 211.196.150.4".
    private const string DefaultLobbyHost = "211.196.150.4"; // spec: login_flow.md §7

    // Default observation window in seconds.
    private const int DefaultObserveSecs = 10;

    // Path to ip.txt relative to the Godot project root.
    private const string IpTextRelPath =
        @"05.Presentation\MartialHeroes.Client.Godot\clientdata\ip.txt";

    // Maximum len of the host token read from ip.txt.
    // spec: Docs/RE/specs/login_flow.md §7 — "IP override file ip.txt: single token, ≤ 19 chars".
    private const int IpTextMaxLength = 19; // spec: login_flow.md §7

    // -----------------------------------------------------------------------
    // Entry point
    // -----------------------------------------------------------------------

    internal static async Task<int> Main(string[] args)
    {
        // --help / -h
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintHelp();
            return 0;
        }

        // Parse CLI
        string? cliHost        = GetArg(args, "--host");
        string? cliServerId    = GetArg(args, "--server");
        string? cliObserveSecs = GetArg(args, "--observe-secs");
        string? cliEnterSlot   = GetArg(args, "--enter-slot");

        int observeSecs = int.TryParse(cliObserveSecs, out int os) ? os : DefaultObserveSecs;
        ushort? selectedServerId = ushort.TryParse(cliServerId, out ushort sid) ? sid : null;

        // --enter-slot N: -1 means "observe only, do not enter".
        // spec: Docs/RE/specs/login_flow.md §3.3 — slot range 0..4.
        int enterSlot = int.TryParse(cliEnterSlot, out int es) ? es : -1;
        if (enterSlot > 4)
        {
            LogErr($"--enter-slot {enterSlot} is out of range (0..4). Aborting.");
            return 1;
        }

        // Credentials — env only; never in source.
        string? envUser = Environment.GetEnvironmentVariable("MH_LOGIN_USER");
        string? envPass = Environment.GetEnvironmentVariable("MH_LOGIN_PASS");
        string? envPin  = Environment.GetEnvironmentVariable("MH_LOGIN_PIN");
        bool hasCreds   = !string.IsNullOrEmpty(envUser) && !string.IsNullOrEmpty(envPass);

        Log($"NetSmoke starting — observe window = {observeSecs}s");
        if (!hasCreds)
        {
            Log("No creds (MH_LOGIN_USER / MH_LOGIN_PASS not set) → connect-and-observe only.");
        }

        if (enterSlot >= 0)
        {
            if (!hasCreds)
            {
                LogErr("--enter-slot requires MH_LOGIN_USER + MH_LOGIN_PASS. Aborting.");
                return 1;
            }

            Log($"--enter-slot {enterSlot} requested: will send 1/7 + 1/9 after roster (3/4 or 3/1).");
        }

        // ---- Step 1: resolve lobby host ----
        string lobbyHost = ResolveHost(cliHost);
        Log($"Lobby host resolved to: {lobbyHost}");

        // ---- Step 2: fetch server list ----
        using var lobbyCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var lobbyClient = new LobbyClient(lobbyHost, PayloadCompression.DecompressPayload);

        IReadOnlyList<LobbyServerRecord> records;
        try
        {
            Log($"Fetching server list from {lobbyHost}:{LobbyClient.LobbyBasePort} (timeout 8s)...");
            records = await lobbyClient.FetchServerListAsync(lobbyCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LogErr("Server-list fetch timed out (8s). Is the lobby reachable?");
            return 1;
        }
        catch (Exception ex)
        {
            LogErr($"Server-list fetch failed: {ex.Message}");
            return 1;
        }

        if (records.Count == 0)
        {
            LogErr("Server-list returned 0 records. Cannot continue.");
            return 1;
        }

        Log($"Server list: {records.Count} record(s).");
        foreach (LobbyServerRecord r in records)
        {
            // Commit gate per spec:  StatusCode == 0  &&  Load < 2400
            // spec: Docs/RE/packets/lobby.yaml Record Shape A.
            bool commitGate = r.StatusCode == 0 && r.Load < 2400; // spec: lobby.yaml Record Shape A
            Log($"  server {r.ServerId,3}  status={r.StatusCode,4}  load={r.Load,5}  openTime={r.OpenTime,5}" +
                $"  commitGate={commitGate}");
        }

        // ---- Step 3: pick target server ----
        LobbyServerRecord target;
        if (selectedServerId.HasValue)
        {
            LobbyServerRecord? found = null;
            foreach (LobbyServerRecord r in records)
            {
                if (r.ServerId == selectedServerId.Value)
                {
                    found = r;
                    break;
                }
            }

            if (!found.HasValue)
            {
                LogErr($"--server {selectedServerId} not found in the list. Available: " +
                       string.Join(", ", CollectIds(records)));
                return 1;
            }

            target = found.Value;
            Log($"Using --server {target.ServerId} (explicit CLI choice).");
        }
        else
        {
            // Pick first record (wire order).
            target = records[0];
            Log($"No --server specified → using first record: server {target.ServerId}.");
        }

        // Log commit-gate status of the chosen server.
        bool targetGate = target.StatusCode == 0 && target.Load < 2400; // spec: lobby.yaml Record Shape A
        Log($"Target server {target.ServerId}: commitGate (status==0 && load<2400) = {targetGate}.");

        // ---- Step 4: fetch channel endpoint ----
        LobbyChannelEndpoint channelEndpoint;
        using var chCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            Log($"Fetching channel endpoint for server {target.ServerId}...");
            channelEndpoint = await lobbyClient
                .FetchChannelEndpointAsync(target.ServerId, chCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LogErr("Channel-endpoint fetch timed out (8s).");
            return 1;
        }
        catch (Exception ex)
        {
            LogErr($"Channel-endpoint fetch failed: {ex.Message}");
            return 1;
        }

        Log($"Channel endpoint: {channelEndpoint.Host}:{channelEndpoint.Port}");

        // ---- Step 5: connect to the game endpoint ----
        // Build the IFrameSink that logs every inbound frame to the console.
        var frameLogger = new LoggingFrameSink();

        // TcpTransport wires the IFrameSink + the inbound decompressor (LZ4 decompress only —
        // no inverse cipher on the client receive path).
        // spec: Docs/RE/specs/crypto.md §5 — inbound is LZ4-decompress-only, no inverse cipher.
        var transport = new TcpTransport(frameLogger, PayloadCompression.DecompressPayload);

        IPAddress[] addrs;
        try
        {
            addrs = await Dns.GetHostAddressesAsync(channelEndpoint.Host).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogErr($"DNS resolution of '{channelEndpoint.Host}' failed: {ex.Message}");
            await transport.DisposeAsync().ConfigureAwait(false);
            return 1;
        }

        if (addrs.Length == 0)
        {
            LogErr($"DNS returned 0 addresses for '{channelEndpoint.Host}'.");
            await transport.DisposeAsync().ConfigureAwait(false);
            return 1;
        }

        var gameEndpoint = new EndpointDescriptor(
            new IPEndPoint(addrs[0], channelEndpoint.Port),
            $"game-server-{target.ServerId}");

        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        IConnectionSession session;
        try
        {
            Log($"Connecting to game endpoint {gameEndpoint} (timeout 8s)...");
            session = await transport.ConnectAsync(gameEndpoint, connectCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LogErr("Game-endpoint connect timed out (8s).");
            await transport.DisposeAsync().ConfigureAwait(false);
            return 1;
        }
        catch (Exception ex)
        {
            LogErr($"Game-endpoint connect failed: {ex.Message}");
            await transport.DisposeAsync().ConfigureAwait(false);
            return 1;
        }

        Log($"Connected (session {session.Id}).  Observing for {observeSecs}s...");

        // ---- Step 6: observe inbound frames for observeSecs ----
        // The LoggingFrameSink will log each frame as it arrives.  The 0/0 KeyExchange frame
        // triggers the handshake path below (if creds are present).
        // We pass the session + sink to the handshake coordinator.
        var coordinator = new HandshakeCoordinator(
            session,
            frameLogger,
            hasCreds ? envUser! : null,
            hasCreds ? envPass! : null,
            envPin,
            enterSlot,
            observeSecs);

        await coordinator.RunAsync().ConfigureAwait(false);

        // ---- Step 7 / Step 8: graceful disconnect + summary ----
        Log("Disconnecting...");
        await session.DisconnectAsync(DisconnectReason.LocalClose).ConfigureAwait(false);
        await session.DisposeAsync().ConfigureAwait(false);
        await transport.DisposeAsync().ConfigureAwait(false);

        // Final histogram
        Log("=== Final Summary ===");
        frameLogger.PrintSummary();
        Log("=== NetSmoke done ===");
        return 0;
    }

    // -----------------------------------------------------------------------
    // Host resolution (3-tier, no allocation)
    // -----------------------------------------------------------------------

    private static string ResolveHost(string? cliHost)
    {
        // Tier 1: CLI --host
        if (!string.IsNullOrWhiteSpace(cliHost))
        {
            Log($"[host] Using CLI --host: {cliHost}");
            return cliHost.Trim();
        }

        // Tier 2: env MH_LOBBY_HOST
        string? envHost = Environment.GetEnvironmentVariable("MH_LOBBY_HOST");
        if (!string.IsNullOrWhiteSpace(envHost))
        {
            Log($"[host] Using env MH_LOBBY_HOST: {envHost}");
            return envHost.Trim();
        }

        // Tier 3: ip.txt relative to the repo root (locate the root by walking up from the exe)
        string repoRoot = FindRepoRoot();
        if (!string.IsNullOrEmpty(repoRoot))
        {
            string ipTxtPath = Path.Combine(repoRoot, IpTextRelPath);
            if (File.Exists(ipTxtPath))
            {
                try
                {
                    string raw = File.ReadAllText(ipTxtPath, Encoding.ASCII).Trim();
                    // spec: login_flow.md §7 — single token, ≤ 19 chars.
                    if (raw.Length > IpTextMaxLength)
                    {
                        raw = raw[..IpTextMaxLength];
                    }

                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        Log($"[host] Using ip.txt: {raw}  (path: {ipTxtPath})");
                        return raw;
                    }
                }
                catch (Exception ex)
                {
                    Log($"[host] ip.txt read failed ({ex.Message}) — falling through to default.");
                }
            }
        }

        // Tier 4: hardcoded fallback
        Log($"[host] Using hardcoded fallback: {DefaultLobbyHost}");
        return DefaultLobbyHost;
    }

    private static string FindRepoRoot()
    {
        // Walk up from the current directory until we find MartialHeroes.slnx or hit the FS root.
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "MartialHeroes.slnx")))
            {
                return dir;
            }

            string? parent = Path.GetDirectoryName(dir);
            if (parent == dir)
            {
                break;
            }

            dir = parent;
        }

        return string.Empty;
    }

    // -----------------------------------------------------------------------
    // CLI helpers
    // -----------------------------------------------------------------------

    private static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static IEnumerable<string> CollectIds(IReadOnlyList<LobbyServerRecord> records)
    {
        foreach (LobbyServerRecord r in records)
        {
            yield return r.ServerId.ToString();
        }
    }

    // -----------------------------------------------------------------------
    // Logging helpers (prefixed, timestamped)
    // -----------------------------------------------------------------------

    internal static void Log(string msg)  => Console.WriteLine($"[NetSmoke] {DateTime.Now:HH:mm:ss.fff}  {msg}");
    internal static void LogErr(string msg) => Console.Error.WriteLine($"[NetSmoke][ERR] {DateTime.Now:HH:mm:ss.fff}  {msg}");

    // -----------------------------------------------------------------------
    // --help
    // -----------------------------------------------------------------------

    private static void PrintHelp()
    {
        Console.WriteLine("""
            NetSmoke — MartialHeroes live-integration wire diagnostic
            Usage: NetSmoke [OPTIONS]

            Options:
              --host <ip>         Lobby IP (overrides env + ip.txt + default)
              --server <id>       Select server by id (default: first record)
              --observe-secs <n>  Observation window in seconds (default: 10)
              --enter-slot <n>    Enter the world with character slot N (0..4) after
                                  roster arrives on 3/4 or 3/1.  Sends 1/7 (mode=0)
                                  + 1/9 CmsgEnterGameRequest.  Default: -1 (observe only).
              --help / -h         Show this message

            Credentials (env only — never in source):
              MH_LOBBY_HOST       Override lobby host (CLI --host takes priority)
              MH_LOGIN_USER       Account name
              MH_LOGIN_PASS       Password (triggers 0/0→1/4 handshake if creds present)
              MH_LOGIN_PIN        Optional PIN (second-password, omitted if absent)

            Example:
              set MH_LOGIN_USER=myaccount
              set MH_LOGIN_PASS=mypassword
              dotnet run --project tools/NetSmoke/NetSmoke.csproj -- --server 4 --enter-slot 0 --observe-secs 30
            """);
    }
}

// ---------------------------------------------------------------------------
// IFrameSink implementation — logs every inbound frame, collects histogram
// ---------------------------------------------------------------------------

/// <summary>
/// Receives every fully-framed, LZ4-decompressed inbound packet from <see cref="TcpTransport"/>
/// (which also fires the decompression stage before calling here).  This sink:
/// <list type="bullet">
///   <item>Logs major, minor, payload length, and a short hex preview of the first 32 payload bytes.</item>
///   <item>Accumulates a histogram of (major, minor) → count and total byte count.</item>
///   <item>Signals a <see cref="System.Threading.Tasks.TaskCompletionSource{T}"/> when a
///     0/0 KeyExchange frame arrives so the <see cref="HandshakeCoordinator"/> can react.</item>
///   <item>Signals a registered roster handler when a 3/4 or 3/1 frame arrives (roster delivery).</item>
/// </list>
/// spec: Docs/RE/specs/network_dispatch.md §1 — frame header: [u32 size @+0][u16 major @+4][u16 minor @+6].
/// spec: Docs/RE/specs/crypto.md §5 — inbound = LZ4-decompress only; no inverse cipher.
/// </summary>
internal sealed class LoggingFrameSink : IFrameSink
{
    // 0/0 KeyExchange trigger: the coordinator subscribes via OnKeyExchange.
    // We store the raw payload bytes (already decompressed; no inverse cipher needed on receive).
    // spec: Docs/RE/specs/crypto.md §5 (single-caller positive proof).
    private Action<byte[]>? _keyExchangeHandler;

    // 3/4 or 3/1 roster trigger: the coordinator subscribes to fire the enter-world path.
    // spec: Docs/RE/specs/login_flow.md §7 — roster arrives on 3/4 or 3/1.
    private Action<ushort, ushort, byte[]>? _rosterHandler;

    // Histogram: packed opcode (major<<16|minor) → (count, total bytes)
    private readonly Dictionary<uint, (long count, long bytes)> _histogram = new();
    private long _totalBytes;
    private readonly object _lock = new();

    /// <summary>Register a callback invoked once when opcode 0/0 arrives.</summary>
    public void RegisterKeyExchangeHandler(Action<byte[]> handler)
    {
        _keyExchangeHandler = handler;
    }

    /// <summary>
    /// Register a callback invoked when a 3/4 or 3/1 roster frame arrives.
    /// The callback receives (major, minor, payloadCopy).
    /// Only the first qualifying roster frame fires the callback (one-shot via the coordinator's own guard).
    /// spec: Docs/RE/specs/login_flow.md §7 — both 3/1 and 3/4 deliver the character roster.
    /// </summary>
    public void RegisterRosterHandler(Action<ushort, ushort, byte[]> handler)
    {
        _rosterHandler = handler;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Called by <see cref="FrameSplitter"/> / <see cref="TcpTransport"/> for every decoded frame.
    /// The <paramref name="payload"/> span is zero-copy over the pipeline buffer — we copy the
    /// bytes before any async handoff.  The packed opcode is (major&lt;&lt;16)|minor, matching
    /// <see cref="MartialHeroes.Network.Protocol.Opcodes.PacketOpcode"/> conventions.
    /// </remarks>
    public void OnFrame(SessionId sessionId, uint packedOpcode, ReadOnlySpan<byte> payload)
    {
        ushort major = (ushort)(packedOpcode >> 16);
        ushort minor = (ushort)(packedOpcode & 0xFFFF);
        int len = payload.Length;

        // Short hex preview (first ≤ 32 bytes of the payload — diagnostic output only).
        string hex = HexPreview(payload, 32);

        // Header-only frames (keepalives): total frame = 8 bytes, payload = 0 bytes.
        // spec: Docs/RE/specs/crypto.md §2 — "header-only packets (size==8) are a pass-through".
        string note = len == 0 ? " [header-only/keepalive]" : "";
        Program.Log($"FRAME  major={major}  minor={minor}  payload={len}B{note}  hex=[{hex}]");

        // Update histogram
        lock (_lock)
        {
            ref (long count, long bytes) slot =
                ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(
                    _histogram, packedOpcode, out _);
            slot.count++;
            slot.bytes += len + 8; // include the 8-byte header in the byte tally
            _totalBytes += len + 8;
        }

        // 0/0 KeyExchange dispatch — single unique handler per spec.
        // spec: Docs/RE/specs/network_dispatch.md §1.4 — the (0,0) else-branch parses the
        // inbound key-exchange blob (payload already decompressed, no inverse cipher needed).
        if (major == 0 && minor == 0 && _keyExchangeHandler is { } h)
        {
            // Copy payload before the span is invalidated (pipeline reclaim).
            byte[] copy = payload.ToArray();
            h(copy);
        }

        // 3/4 or 3/1 roster dispatch.
        // spec: Docs/RE/specs/login_flow.md §7 — both 3/1 and 3/4 deliver the character roster
        //       (3/1 = SmsgCharacterList, 3/4 = SmsgCharacterListUpdate/SceneEntityUpdate).
        // The live replica delivers the post-login roster on 3/4 (LIVE+IDA CONFIRMED, CYCLE 4).
        if (major == 3 && (minor == 4 || minor == 1) && _rosterHandler is { } rh)
        {
            byte[] copy = payload.ToArray();
            rh(major, minor, copy);
        }
    }

    /// <summary>Print the (major,minor)→count histogram to stdout.</summary>
    public void PrintSummary()
    {
        Program.Log($"Total bytes observed (header+payload): {_totalBytes}");
        Program.Log($"Inbound opcode histogram ({_histogram.Count} distinct opcodes):");
        foreach (KeyValuePair<uint, (long count, long bytes)> kv in _histogram)
        {
            ushort major = (ushort)(kv.Key >> 16);
            ushort minor = (ushort)(kv.Key & 0xFFFF);
            Program.Log($"  ({major,3},{minor,5})  count={kv.Value.count,6}  bytes={kv.Value.bytes,8}");
        }
    }

    private static string HexPreview(ReadOnlySpan<byte> data, int maxBytes)
    {
        int take = Math.Min(data.Length, maxBytes);
        return Convert.ToHexString(data[..take]);
    }
}

// ---------------------------------------------------------------------------
// HandshakeCoordinator — drives the observe loop, the 0/0→1/4 path, and the
// optional 1/7→1/9 enter-world path
// ---------------------------------------------------------------------------

/// <summary>
/// Coordinates the observation window: waits for inbound frames (via the <see cref="LoggingFrameSink"/>)
/// and, when a <c>0/0</c> KeyExchange frame arrives AND credentials are present, builds and sends
/// the secure <c>1/4</c> Auth reply using the real layer-02 classes.
///
/// If <c>enterSlot</c> is ≥ 0, after the character roster arrives (3/4 or 3/1) the coordinator
/// also sends <c>1/7 CmsgSelectCharacter</c> (pre-stage, mode=0) followed immediately by
/// <c>1/9 CmsgEnterGameRequest</c>, then continues observing for the full window to capture the
/// world-bootstrap response sequence (3/5 SmsgEnterGameAck, 4/1 SmsgGameStateTick, etc.).
///
/// The 1/7 build uses:
/// <list type="number">
///   <item><see cref="CmsgSelectCharacter"/> struct: <c>{SlotIndex, Mode=0}</c> (select/view).
///     spec: Docs/RE/packets/cmsg_char_select.yaml — mode 0 = plain select.</item>
/// </list>
///
/// The 1/9 build uses:
/// <list type="number">
///   <item><see cref="CmsgEnterGameRequest"/> struct: SlotIndex, SessionToken (zeroes — no launcher
///     token in diagnostic context), Pad (zeroes), VersionToken = 21149.</item>
///   <item>VersionToken value 21149 = 10 × 2114 + 9, sample_verified from data/cursor/game.ver
///     field index 5. spec: Docs/RE/packets/cmsg_char_enter.yaml / login_flow.md §3.3 / §7.</item>
///   <item>SessionToken: the spec says 33 bytes copied from the launcher argv0 global; in NetSmoke
///     (no launcher), the buffer is left zero-filled (matching the memset the client does before
///     any partial fill). spec: Docs/RE/packets/cmsg_char_enter.yaml field SessionToken.</item>
/// </list>
///
/// The 1/7 + 1/9 build uses:
/// <list type="number">
///   <item><see cref="CryptoOutboundPacketSink.SendAsync"/> (injected via the session) to encrypt +
///     LZ4-compress and write each frame on the wire.</item>
/// </list>
/// spec: Docs/RE/specs/login_flow.md §3.3, §3.6 (enter-game flow).
/// spec: Docs/RE/packets/cmsg_char_enter.yaml (1/9 field layout).
/// spec: Docs/RE/packets/cmsg_char_select.yaml (1/7 field layout).
/// </summary>
internal sealed class HandshakeCoordinator
{
    private readonly IConnectionSession _session;
    private readonly LoggingFrameSink _sink;
    private readonly string? _user;
    private readonly string? _pass;
    private readonly string? _pin;
    private readonly int _enterSlot;
    private readonly int _observeSecs;

    // Signals that a 0/0 frame arrived and was handled (or that we decided to skip).
    private readonly TaskCompletionSource<bool> _keyExchangeDone = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Signals that 1/9 was sent (true) or that the enter path was skipped/failed.
    private readonly TaskCompletionSource<bool> _enterGameDone = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Guard: only ever send 1/7+1/9 once — the roster may arrive multiple times.
    private int _enterSent; // 0 = not sent, 1 = sent (Interlocked)

    public HandshakeCoordinator(
        IConnectionSession session,
        LoggingFrameSink sink,
        string? user,
        string? pass,
        string? pin,
        int enterSlot,
        int observeSecs)
    {
        _session     = session;
        _sink        = sink;
        _user        = user;
        _pass        = pass;
        _pin         = pin;
        _enterSlot   = enterSlot;
        _observeSecs = observeSecs;
    }

    public async Task RunAsync()
    {
        // Wire up the 0/0 handler — this must be done before the observe window starts so we
        // don't miss a fast-arriving 0/0.
        if (!string.IsNullOrEmpty(_user) && !string.IsNullOrEmpty(_pass))
        {
            _sink.RegisterKeyExchangeHandler(OnKeyExchange);
        }

        // Wire up the roster handler only if --enter-slot was requested.
        if (_enterSlot >= 0 && !string.IsNullOrEmpty(_user))
        {
            _sink.RegisterRosterHandler(OnRoster);
        }
        else
        {
            // No enter-world path: mark it done immediately so we don't wait.
            _enterGameDone.TrySetResult(false);
        }

        // Observe for the primary window.
        Program.Log($"Entering primary observe window ({_observeSecs}s)...");
        using var observeCts = new CancellationTokenSource(TimeSpan.FromSeconds(_observeSecs));
        try
        {
            // Wait until either the observe window expires or the session disconnects.
            await WaitUntilCancelledOrDisconnected(observeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Program.Log("Primary observe window expired.");
        }

        // If we sent a 1/4, wait for the secondary observe window.
        if (_keyExchangeDone.Task.IsCompletedSuccessfully && _keyExchangeDone.Task.Result)
        {
            Program.Log($"Handshake sent. Entering secondary observe window ({_observeSecs}s)...");
            using var obs2Cts = new CancellationTokenSource(TimeSpan.FromSeconds(_observeSecs));
            try
            {
                await WaitUntilCancelledOrDisconnected(obs2Cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Program.Log("Secondary observe window expired.");
            }
        }

        // If we sent 1/7+1/9 (enter-world), wait for the world-bootstrap observe window.
        if (_enterGameDone.Task.IsCompletedSuccessfully && _enterGameDone.Task.Result)
        {
            Program.Log($"1/9 Enter-game sent. Entering world-bootstrap observe window ({_observeSecs}s)...");
            using var obs3Cts = new CancellationTokenSource(TimeSpan.FromSeconds(_observeSecs));
            try
            {
                await WaitUntilCancelledOrDisconnected(obs3Cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Program.Log("World-bootstrap observe window expired.");
            }
        }
    }

    // Called by LoggingFrameSink.OnFrame on the 0/0 frame.  Runs on the pipeline's callback
    // thread; any heavy work (the modexp in LoginCredentialReply.Build) is offloaded to a Task.
    private void OnKeyExchange(byte[] payload)
    {
        // Fire-and-forget the async build+send so the callback thread is not blocked.
        Task.Run(() => HandleKeyExchangeAsync(payload));
    }

    private async Task HandleKeyExchangeAsync(byte[] payload)
    {
        Program.Log($"0/0 KeyExchange arrived ({payload.Length}B).  Parsing...");

        // -- Parse the 62-byte key blob --
        // spec: Docs/RE/specs/crypto.md §6.2 — 54-byte blob + two 4-byte scalars = 62 bytes total.
        // spec: Docs/RE/specs/network_dispatch.md §1.4 — the payload is already decompressed;
        //        no inverse cipher is applied (single-caller positive proof, crypto.md §5).
        SessionHandshake.KeyExchange kex;
        try
        {
            kex = SessionHandshake.ParseKeyExchange(payload);
        }
        catch (Exception ex)
        {
            Program.LogErr($"0/0 ParseKeyExchange failed: {ex.Message}.  No 1/4 will be sent.");
            _keyExchangeDone.TrySetResult(false);
            return;
        }

        Program.Log($"0/0 parsed:  modulus={kex.ModulusByteLength}B  " +
                    $"scalar1=0x{kex.Scalar1:X8}  scalar2=0x{kex.Scalar2:X8}");

        if (string.IsNullOrEmpty(_user) || string.IsNullOrEmpty(_pass))
        {
            Program.Log("No creds → 1/4 NOT sent.");
            _keyExchangeDone.TrySetResult(false);
            return;
        }

        // Build the full 1/4 whitened payload:
        //   [u8 0x2B][u32 account_len][account]([u32 pin_len][pin])[u32 len(c)][BE RSA digits]
        //   then per-dword XOR 0x29 whitening over the whole payload.
        // spec: Docs/RE/specs/crypto.md §6.3, §6.4, §6.6; packets/login.yaml.
        //
        // Credential encoding: the game uses CP949 for its wire strings (CLAUDE.md, constraints).
        // For pure ASCII credentials (expected here) this is identical to ASCII.  Register the
        // provider defensively so it is available if the account name contains non-ASCII chars.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding cp949 = Encoding.GetEncoding(949); // spec: CLAUDE.md — "All game text is CP949"

        byte[] accountBytes = cp949.GetBytes(_user);
        byte[]? pinBytes = null;
        bool includePin = false;
        if (!string.IsNullOrEmpty(_pin))
        {
            pinBytes = cp949.GetBytes(_pin);
            includePin = true;
        }

        // Stage the RSA plaintext M: password in a zero-padded buffer of the password-field cap.
        // spec: Docs/RE/specs/crypto.md §6.1, §6.6 ("width = password-field cap, caller-supplied").
        //       §8.1 ("runtime cap 17 bytes in the debugger-observed session").
        byte[] passwordBytes = cp949.GetBytes(_pass);
        byte[] stagedM;
        try
        {
            stagedM = CredentialPlaintext.StagePassword(passwordBytes);
        }
        catch (Exception ex)
        {
            Program.LogErr($"CredentialPlaintext.StagePassword failed: {ex.Message}.");
            _keyExchangeDone.TrySetResult(false);
            return;
        }

        // Build the whitened 1/4 payload using the real LoginCredentialReply composer.
        // spec: Docs/RE/specs/crypto.md §6.6 — plaintext pre-image + RSA ciphertext, then
        //       per-dword XOR 0x29 whitening over the whole payload.
        byte[] authPayload;
        try
        {
            authPayload = LoginCredentialReply.Build(
                in kex,
                accountBytes,
                pinBytes ?? [],
                includePin,
                stagedM,
                CryptoPaddingRandom.Shared); // spec: crypto.md §6.3 (only randomness = padding)
        }
        catch (Exception ex)
        {
            Program.LogErr($"LoginCredentialReply.Build failed: {ex.Message}.");
            _keyExchangeDone.TrySetResult(false);
            return;
        }

        Program.Log($"1/4 payload built ({authPayload.Length}B whitened).  Sending via CryptoOutboundPacketSink...");

        // Wire the outbound send:
        //   CryptoOutboundPacketSink applies the byte cipher (spec crypto.md §3.1) then LZ4
        //   compression (spec crypto.md §3.2), prepends the 8-byte plaintext header with opcode
        //   major 1 / minor 4, and hands the frame to IConnectionSession.SendAsync.
        // spec: Docs/RE/specs/crypto.md §3 outbound pipeline; Docs/RE/specs/network_dispatch.md §6.1.
        var sink = new CryptoOutboundPacketSink(
            _session,
            WireCipher.EncryptInPlace,       // spec: crypto.md §3.1 — keyless 3-round byte cipher
            PayloadCompression.CompressPayload); // spec: crypto.md §3.2 — raw-block LZ4

        try
        {
            await sink.SendAsync(
                _session.Id,
                majorOpcode: 1,  // spec: crypto.md §6 — Auth reply opcode major 1 / minor 4
                minorOpcode: 4,  // spec: Docs/RE/opcodes.md — CmsgLoginCredential opcode 1/4
                new ReadOnlyMemory<byte>(authPayload)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Program.LogErr($"SendAsync 1/4 failed: {ex.Message}.");
            _keyExchangeDone.TrySetResult(false);
            return;
        }

        Program.Log("1/4 Auth reply sent successfully.  Watching for server responses...");
        _keyExchangeDone.TrySetResult(true);
    }

    // Called by LoggingFrameSink.OnFrame when a 3/4 or 3/1 frame arrives (roster delivery).
    // spec: Docs/RE/specs/login_flow.md §7 — both 3/1 and 3/4 deliver the character roster.
    private void OnRoster(ushort major, ushort minor, byte[] payload)
    {
        // Only fire once — the roster may arrive multiple times (e.g. a 3/4 update followed by 3/1).
        if (Interlocked.CompareExchange(ref _enterSent, 1, 0) != 0)
        {
            return;
        }

        // spec: Docs/RE/specs/login_flow.md §7 — 3-byte header: [srv][chan][slot_mask].
        // We only check the length defensively; we don't parse the roster here (diagnostic only).
        Program.Log($"Roster arrived on {major}/{minor} ({payload.Length}B).  " +
                    $"Sending 1/7 (pre-stage slot {_enterSlot}) + 1/9 (enter-game)...");

        // Fire-and-forget the async build+send so the callback thread is not blocked.
        Task.Run(() => HandleEnterGameAsync(major, minor));
    }

    private async Task HandleEnterGameAsync(ushort rosterMajor, ushort rosterMinor)
    {
        // Build the outbound sink (same cipher+LZ4 as the 1/4 send above).
        // spec: Docs/RE/specs/crypto.md §3 outbound pipeline.
        var sink = new CryptoOutboundPacketSink(
            _session,
            WireCipher.EncryptInPlace,
            PayloadCompression.CompressPayload);

        // ---- Step A: 1/7 CmsgSelectCharacter — DEFAULT OFF (LIVE DEBUGGER finding, CYCLE 4) ----
        // The real doida.exe, debugged live: clicking a character at char-select sends NO 1/7, and the
        // Enter handler (SelectWindow_EnterGame) sends ONLY the 1/9 (Cmsg_EnterGame_Send) — never a 1/7.
        // So the real enter flow is JUST the 1/9 (it carries the slot at payload +0). NetSmoke previously
        // sent a SPURIOUS 1/7 (mode=0) ahead of the 1/9 — the likely cause of the enter-game 3/100 code-8
        // rejection (the 1/9 itself is byte-identical to the real client's). Kept opt-in for experiments.
        // spec (dirty): Docs/RE/_dirty/dbg/enter_game_19.md.
        if (Environment.GetEnvironmentVariable("MH_SEND_SELECT17") == "1")
        {
            byte[] selectBytes = new byte[CmsgSelectCharacter.WireSize]; // spec: cmsg_char_select.yaml size:2
            selectBytes[0] = (byte)_enterSlot; // 0x00 SlotIndex
            selectBytes[1] = 0;                // 0x01 Mode=0 (select/view)
            Program.Log($"[opt-in MH_SEND_SELECT17] Sending 1/7 slot={_enterSlot} mode=0 ({selectBytes.Length}B)...");
            try
            {
                await sink.SendAsync(_session.Id, majorOpcode: 1, minorOpcode: 7,
                    new ReadOnlyMemory<byte>(selectBytes)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Program.LogErr($"SendAsync 1/7 failed: {ex.Message}.  Aborting enter-game.");
                _enterGameDone.TrySetResult(false);
                return;
            }
            Program.Log("1/7 sent.");
        }
        else
        {
            Program.Log("1/7 SKIPPED (real enter = 1/9 only; live-dbg finding). Set MH_SEND_SELECT17=1 to force it.");
        }

        // ---- Step B: 1/9 CmsgEnterGameRequest ----
        // spec: Docs/RE/packets/cmsg_char_enter.yaml — 40-byte body.
        // Fields:
        //   SlotIndex    @ 0x00  (u8)   — the chosen character slot (0..4)
        //   SessionToken @ 0x01  (33B)  — launcher argv0 token; left zero (no launcher in NetSmoke)
        //   Pad          @ 0x22  (2B)   — alignment; zero
        //   VersionToken @ 0x24  (u32)  — 10 × (game.ver field index 5) + 9 = 21149
        //
        // VersionToken derivation: spec: Docs/RE/packets/cmsg_char_enter.yaml §VersionToken,
        //   login_flow.md §3.3 / §7. sample_verified value = 21149 for this build
        //   (game.ver field 5 = 2114; formula 10 × 2114 + 9 = 21149).
        //
        // SessionToken: spec: cmsg_char_enter.yaml field SessionToken — "33B launcher session/
        //   identity token string copied from the process command-line / argv0 global; NUL-bounded
        //   within the 33-byte field". In NetSmoke (no launcher), the buffer is zero-filled.
        //   The client zero-fills the full 40-byte buffer before any partial fill, so zero is the
        //   correct wire default when no launcher token is available.

        // 1/9 CmsgEnterGameRequest — 40-byte body, zero-initialised, then partially filled.
        // Layout (Pack=1, CODE-CONFIRMED offsets; spec: Docs/RE/packets/cmsg_char_enter.yaml):
        //   +0x00 (1B)  SlotIndex    = enterSlot
        //   +0x01 (33B) SessionToken = zero (no launcher in NetSmoke; spec: cmsg_char_enter.yaml)
        //   +0x22 (2B)  Pad          = zero
        //   +0x24 (4B)  VersionToken = 21149 (u32 LE)
        //
        // VersionToken = 10 × (game.ver field index 5) + 9 = 10 × 2114 + 9 = 21149.
        // spec: login_flow.md §3.3 / §7; cmsg_char_enter.yaml §VersionToken. sample_verified.

        const uint VersionToken = 21149; // spec: login_flow.md §3.3 / §7; cmsg_char_enter.yaml — sample_verified

        byte[] enterBytes = new byte[CmsgEnterGameRequest.WireSize]; // spec: cmsg_char_enter.yaml size:40; zero-init by default
        enterBytes[0x00] = (byte)_enterSlot; // 0x00 SlotIndex; spec: cmsg_char_enter.yaml

        // 0x01..0x21 SessionToken (33B) = the MD5 HEX digest (32 lowercase chars + NUL) of the legit
        // doida.exe — a CLIENT-SIDE anti-tamper fingerprint the server whitelists, NOT a launcher token
        // (CYCLE 4 IDA: the original sends MD5(argv0 file) formatted to 32 hex chars). Read from env
        // MH_SESSION_TOKEN; zeros if unset. spec: Docs/RE/packets/cmsg_char_enter.yaml SessionToken.
        string? md5Hex = Environment.GetEnvironmentVariable("MH_SESSION_TOKEN");
        if (!string.IsNullOrWhiteSpace(md5Hex))
        {
            md5Hex = md5Hex.Trim().ToLowerInvariant();
            int n = Math.Min(md5Hex.Length, 32); // 32 hex chars; the 33rd byte (NUL @ 0x21) stays zero
            for (int i = 0; i < n; i++)
            {
                enterBytes[0x01 + i] = (byte)md5Hex[i]; // ASCII hex char
            }
            Program.Log($"1/9 SessionToken set from MH_SESSION_TOKEN ({n} hex chars).");
        }
        else
        {
            Program.Log("1/9 SessionToken: MH_SESSION_TOKEN unset → zeros (server will likely reject). spec: cmsg_char_enter.yaml.");
        }

        // 0x22..0x23 Pad stays zero (2 bytes);            spec: cmsg_char_enter.yaml
        BinaryPrimitives.WriteUInt32LittleEndian(enterBytes.AsSpan(0x24), VersionToken); // 0x24 VersionToken (u32 LE); spec: cmsg_char_enter.yaml

        if (enterBytes.Length != CmsgEnterGameRequest.WireSize)
        {
            // Defensive: the array was sized from WireSize so this cannot fire — kept for documentation.
            Program.LogErr($"1/9 buffer size mismatch: got {enterBytes.Length}B, expected {CmsgEnterGameRequest.WireSize}B.  Aborting.");
            _enterGameDone.TrySetResult(false);
            return;
        }

        Program.Log($"Sending 1/9 CmsgEnterGameRequest  slot={_enterSlot}  versionToken={VersionToken}  ({enterBytes.Length}B)...");
        try
        {
            await sink.SendAsync(
                _session.Id,
                majorOpcode: 1,  // spec: opcodes.md — CmsgEnterGameRequest major 1 / minor 9
                minorOpcode: 9,  // spec: Docs/RE/packets/cmsg_char_enter.yaml
                new ReadOnlyMemory<byte>(enterBytes)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Program.LogErr($"SendAsync 1/9 failed: {ex.Message}.");
            _enterGameDone.TrySetResult(false);
            return;
        }

        Program.Log("1/9 CmsgEnterGameRequest sent.  " +
                    "Watching for world bootstrap: 3/5 SmsgEnterGameAck, 4/1 SmsgGameStateTick...");
        _enterGameDone.TrySetResult(true);
    }

    // Waits until the cancellation token fires or the session transitions to Disconnected/Faulted.
    private async Task WaitUntilCancelledOrDisconnected(CancellationToken ct)
    {
        var disconnectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnDisconnected(SessionDisconnectedEventArgs _) => disconnectedTcs.TrySetResult(true);

        _session.Disconnected += OnDisconnected;
        try
        {
            await Task.WhenAny(
                Task.Delay(Timeout.Infinite, ct),
                disconnectedTcs.Task).ConfigureAwait(false);

            if (disconnectedTcs.Task.IsCompletedSuccessfully)
            {
                Program.Log("Session disconnected by server.");
            }
        }
        finally
        {
            _session.Disconnected -= OnDisconnected;
        }

        ct.ThrowIfCancellationRequested();
    }
}
