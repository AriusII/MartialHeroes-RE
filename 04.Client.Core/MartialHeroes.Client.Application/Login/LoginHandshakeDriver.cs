using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Network.Crypto;

namespace MartialHeroes.Client.Application.Login;

public sealed class LoginHandshakeDriver : ILoginHandshakeDriver
{
    private const ushort AuthReplyMajor = 1;

    private const ushort AuthReplyMinor = 4;

    private readonly LoginCredentialStore _credentials;
    private readonly IOutboundPacketSink _outbound;
    private readonly IPaddingRandom _paddingRandom;
    private readonly SessionId _sessionId;

    public LoginHandshakeDriver(
        IOutboundPacketSink outbound,
        LoginCredentialStore credentials,
        SessionId sessionId,
        IPaddingRandom? paddingRandom = null)
    {
        _outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _sessionId = sessionId;
        _paddingRandom = paddingRandom ?? CryptoPaddingRandom.Shared;
    }

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

        _ = _outbound.SendAsync(_sessionId, AuthReplyMajor, AuthReplyMinor, reply);

        _credentials.Clear();

        return reply.Length;
    }
}