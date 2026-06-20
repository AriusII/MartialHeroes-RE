using System.Net;
using Godot;
using MartialHeroes.Network.Abstractions.Transport;
using MartialHeroes.Network.Crypto;
using MartialHeroes.Network.Transport.Pipelines;

namespace MartialHeroes.Client.Godot.Autoload;

public sealed partial class ClientContext
{
    /// <summary>
    ///     Opens the TCP game connection, installs a <see cref="CryptoOutboundPacketSink" /> over it,
    ///     and activates the relay sink so subsequent <see cref="IOutboundPacketSink.SendAsync" /> calls
    ///     go to the real wire.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Idempotent: a second call while the connection is already open is a no-op.
    ///     </para>
    ///     <para>
    ///         Called by <c>LoginScene.SelectServerAsync</c> once the lobby has resolved the game-server
    ///         host:port via <c>SelectServerAsync</c>.
    ///     </para>
    ///     <para>
    ///         The inbound path: <c>TcpTransport</c> is constructed with a <see cref="DispatcherFrameSink" />
    ///         adapter that enqueues each decoded frame into the existing <see cref="Dispatcher" /> channel,
    ///         which the engine loop already drains.  No second reader is added to the event bus.
    ///     </para>
    ///     <para>
    ///         Decompression: <see cref="PayloadCompression.DecompressPayload" /> is injected as the
    ///         <c>InboundDecompressDelegate</c>.
    ///         spec: Docs/RE/specs/crypto.md §5 — inbound is LZ4-decompress only (no inverse cipher).
    ///     </para>
    /// </remarks>
    /// <param name="host">Dotted IPv4 literal or DNS hostname returned by the lobby.</param>
    /// <param name="port">Game-server port returned by the lobby.</param>
    public async Task OpenGameConnectionAsync(string host, int port)
    {
        // Idempotency guard — only the first caller proceeds.
        if (Interlocked.CompareExchange(ref _gameConnectionOpened, 1, 0) != 0)
        {
            GD.Print($"[ClientContext] OpenGameConnectionAsync({host}:{port}): connection already open — skipped.");
            return;
        }

        try
        {
            // Resolve host (may be a name or dotted quad).
            var addresses = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
            if (addresses.Length == 0)
            {
                GD.PrintErr(
                    $"[ClientContext] OpenGameConnectionAsync: DNS resolution returned no addresses for '{host}'.");
                Interlocked.Exchange(ref _gameConnectionOpened, 0); // allow a retry
                return;
            }

            var endpoint = new EndpointDescriptor(
                new IPEndPoint(addresses[0], port),
                $"game-server({host}:{port})");

            // Build the inbound frame sink that feeds the existing Dispatcher channel.
            // DispatcherFrameSink is defined in ClientContext.ApplicationGraph.cs.
            var frameSink = new DispatcherFrameSink(Dispatcher);

            // TcpTransport → ConnectAsync → SocketConnection (receive + frame loops start immediately).
            // spec: Docs/RE/specs/crypto.md §5 — inbound = LZ4 decompress only; no inverse cipher.
            var transport = new TcpTransport(frameSink, PayloadCompression.DecompressPayload);
            var session = await transport
                .ConnectAsync(endpoint, CancellationToken.None)
                .ConfigureAwait(false);

            _gameConnection = session;

            // Install the crypto outbound sink and activate the relay.
            // spec: Docs/RE/specs/crypto.md §3.1 / §3.2 — outbound: cipher in-place then LZ4 compress.
            var cryptoSink = new CryptoOutboundPacketSink(
                session,
                WireCipher.EncryptInPlace,
                PayloadCompression.CompressPayload);

            _relaySink?.SetTarget(cryptoSink);

            GD.Print($"[ClientContext] Game connection OPEN to {endpoint}. Outbound relay active. " +
                     "spec: crypto.md §3.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ClientContext] OpenGameConnectionAsync({host}:{port}) FAILED: {ex.Message}");
            Interlocked.Exchange(ref _gameConnectionOpened, 0); // allow a retry on the next server select
        }
    }
}