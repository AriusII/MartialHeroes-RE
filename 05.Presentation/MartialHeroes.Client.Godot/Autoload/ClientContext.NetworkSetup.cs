using System.Net;
using Godot;
using MartialHeroes.Network.Abstractions.Transport;
using MartialHeroes.Network.Crypto;
using MartialHeroes.Network.Transport.Pipelines;

namespace MartialHeroes.Client.Godot.Autoload;

public sealed partial class ClientContext
{
    public async Task OpenGameConnectionAsync(string host, int port)
    {
        if (Interlocked.CompareExchange(ref _gameConnectionOpened, 1, 0) != 0)
        {
            GD.Print($"[ClientContext] OpenGameConnectionAsync({host}:{port}): connection already open — skipped.");
            return;
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
            if (addresses.Length == 0)
            {
                GD.PrintErr(
                    $"[ClientContext] OpenGameConnectionAsync: DNS resolution returned no addresses for '{host}'.");
                Interlocked.Exchange(ref _gameConnectionOpened, 0);
                return;
            }

            var endpoint = new EndpointDescriptor(
                new IPEndPoint(addresses[0], port),
                $"game-server({host}:{port})");

            var frameSink = new DispatcherFrameSink(Dispatcher);

            var transport = new TcpTransport(frameSink, PayloadCompression.DecompressPayload);
            var session = await transport
                .ConnectAsync(endpoint, CancellationToken.None)
                .ConfigureAwait(false);

            _gameConnection = session;

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
            Interlocked.Exchange(ref _gameConnectionOpened, 0);
        }
    }
}