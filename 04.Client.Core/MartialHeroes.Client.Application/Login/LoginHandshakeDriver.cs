using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Network.Crypto;

namespace MartialHeroes.Client.Application.Login;

public sealed class LoginHandshakeDriver(
    IOutboundPacketSink outbound,
    LoginCredentialStore credentials,
    SessionId sessionId,
    IPaddingRandom? paddingRandom = null)
    : ILoginHandshakeDriver
{
    private const ushort AuthReplyMajor = 1;

    private const ushort AuthReplyMinor = 4;

    private readonly LoginCredentialStore _credentials =
        credentials ?? throw new ArgumentNullException(nameof(credentials));

    private readonly IOutboundPacketSink _outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
    private readonly IPaddingRandom _paddingRandom = paddingRandom ?? CryptoPaddingRandom.Shared;

    public int OnKeyExchange(ReadOnlySpan<byte> keyExchangePayload)
    {
        var keyExchange = SessionHandshake.ParseKeyExchange(keyExchangePayload);

        var reply = LoginCredentialReply.Build(
            in keyExchange,
            _credentials.AccountBytes,
            _credentials.PinBytes,
            _credentials.IncludePin,
            _credentials.StagedPasswordM,
            _paddingRandom);

        _ = _outbound.SendAsync(sessionId, AuthReplyMajor, AuthReplyMinor, reply);

        _credentials.Clear();

        return reply.Length;
    }
}