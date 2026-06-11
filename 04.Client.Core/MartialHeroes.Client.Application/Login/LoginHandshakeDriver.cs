using MartialHeroes.Client.Application.StateMachine;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Network.Crypto;

namespace MartialHeroes.Client.Application.Login;

/// <summary>
/// Default <see cref="ILoginHandshakeDriver"/>. On a 0/0 KeyExchange it parses the payload with
/// <see cref="SessionHandshake.ParseKeyExchange"/>, builds the 1/4 Auth reply from the staged login
/// credential with <see cref="SessionHandshake.BuildAuthReply"/>, sends the reply body through
/// <see cref="IOutboundPacketSink"/> as opcode 1/4 (the builder already applied the per-dword
/// whitening; the sink then applies the standard byte cipher + LZ4 + frame header like any outbound
/// payload), then clears the staged credential. spec: Docs/RE/specs/crypto.md §6.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="SessionHandshake.BuildAuthReply"/> output is the 1/4 reply body (per-dword whitened);
/// it is handed to the normal outbound send path exactly like any other plaintext payload, so the sink
/// applies the standard cipher + LZ4 + frame header downstream. spec: crypto.md §6.3 step 5.
/// </para>
/// <para>
/// This driver lives in the Application layer (orchestration). The cryptographic math is entirely in
/// Network.Crypto; here we only sequence parse -> build -> send -> clear and nudge the FSM.
/// </para>
/// </remarks>
public sealed class LoginHandshakeDriver : ILoginHandshakeDriver
{
    private readonly IOutboundPacketSink _outbound;
    private readonly LoginCredentialStore _credentials;
    private readonly IPaddingRandom _paddingRandom;
    private readonly ClientStateMachine? _stateMachine;
    private readonly SessionId _sessionId;

    /// <summary>Auth reply opcode major. spec: Docs/RE/specs/crypto.md §6 (1/4 out).</summary>
    private const ushort AuthReplyMajor = 1;

    /// <summary>Auth reply opcode minor. spec: Docs/RE/specs/crypto.md §6 (1/4 out).</summary>
    private const ushort AuthReplyMinor = 4;

    public LoginHandshakeDriver(
        IOutboundPacketSink outbound,
        LoginCredentialStore credentials,
        SessionId sessionId,
        IPaddingRandom? paddingRandom = null,
        ClientStateMachine? stateMachine = null)
    {
        _outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _sessionId = sessionId;
        // Production default is the cryptographic RNG; tests inject a deterministic one. spec: crypto.md §6.3.
        _paddingRandom = paddingRandom ?? CryptoPaddingRandom.Shared;
        _stateMachine = stateMachine;
    }

    /// <inheritdoc />
    public int OnKeyExchange(ReadOnlySpan<byte> keyExchangePayload)
    {
        // Parse the server's live n, e, and scalars. spec: crypto.md §6.2.
        SessionHandshake.KeyExchange keyExchange = SessionHandshake.ParseKeyExchange(keyExchangePayload);

        // Build the 1/4 reply body from the staged credential (PKCS#1 type-2 pad -> modexp ->
        // serialize -> whiten). spec: crypto.md §6.3, §6.4.
        byte[] reply = SessionHandshake.BuildAuthReply(in keyExchange, _credentials.Credential, _paddingRandom);

        // Send the plaintext reply body as 1/4; the sink applies cipher + LZ4 + header. spec: §6.3 step 5.
        // Fire-and-forget against the ValueTask: the handler runs on the single logical reader and the
        // sink queues the send; we do not block the receive loop awaiting the network write.
        _ = _outbound.SendAsync(_sessionId, AuthReplyMajor, AuthReplyMinor, reply);

        // Wipe the staged credential. spec: crypto.md §6.1 (zeroed and freed after the reply is built).
        _credentials.Clear();

        // Advance the lifecycle out of Login if a state machine was supplied.
        _stateMachine?.OnAuthenticated();

        return reply.Length;
    }
}