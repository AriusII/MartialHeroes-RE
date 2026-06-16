using MartialHeroes.Client.Application.StateMachine;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Network.Crypto;

namespace MartialHeroes.Client.Application.Login;

/// <summary>
/// Default <see cref="ILoginHandshakeDriver"/>. On a 0/0 KeyExchange it parses the payload with
/// <see cref="SessionHandshake.ParseKeyExchange"/>, builds the COMPLETE debugger-verified secure 1/4
/// Auth reply via <see cref="LoginCredentialReply.Build"/> (0x2B plaintext pre-image with account +
/// optional PIN, followed by the RSA ciphertext of the 17-byte staged-password M, whitened over the
/// WHOLE payload), sends the reply body through <see cref="IOutboundPacketSink"/> as opcode 1/4
/// (the builder already applied the per-dword whitening; the sink applies the standard byte cipher +
/// LZ4 + frame header like any outbound payload), then clears the staged credential.
/// <para>
/// The staged state (account bytes, 17-byte M, optional PIN bytes + include flag) is held in
/// <see cref="LoginCredentialStore"/> which is populated by the login use-case before any 0/0
/// arrives. spec: Docs/RE/specs/crypto.md §6.1, §6.6, §6b; packets/login.yaml (CmsgLoginCredential).
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="LoginCredentialReply.Build"/> output is the whitened 1/4 reply body (pre-image +
/// RSA ciphertext, per-dword XOR 0x29 whitened); it is handed to the normal outbound send path exactly
/// like any other plaintext payload, so the sink applies the standard cipher + LZ4 + frame header
/// downstream. spec: crypto.md §6.3 step 5, §6.4, §6.6.
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

        // Build the COMPLETE debugger-verified 1/4 payload:
        //   [u8 0x2B] [u32 LE account_len][account][NUL]  ([u32 LE pin_len][PIN][NUL])   <- pre-image
        //   [u32 LE ciphertext_len][big-endian RSA digits]                                 <- RSA half
        // The whole payload is per-dword XOR 0x29 whitened by the builder. spec: crypto.md §6.6,
        // §6.3, §6.4, §6b; packets/login.yaml (CmsgLoginCredential).
        byte[] reply = LoginCredentialReply.Build(
            in keyExchange,
            account: _credentials.AccountBytes,
            pin: _credentials.PinBytes,
            includePin: _credentials.IncludePin,
            stagedPassword: _credentials.StagedPasswordM,
            paddingRng: _paddingRandom);

        // Send the whitened reply body as 1/4; the sink applies byte cipher + LZ4 + header.
        // Fire-and-forget against the ValueTask: the handler runs on the single logical reader and
        // the sink queues the send; we do not block the receive loop awaiting the network write.
        // spec: crypto.md §6.3 step 5.
        _ = _outbound.SendAsync(_sessionId, AuthReplyMajor, AuthReplyMinor, reply);

        // Wipe all staged credential buffers (account, M, PIN). spec: crypto.md §6.1 (the staged M
        // is zeroed and freed after the reply is built); §6a (secure-context teardown zeros M).
        _credentials.Clear();

        // Advance the lifecycle out of Login if a state machine was supplied.
        _stateMachine?.OnAuthenticated();

        // NOTE: this driver does NOT publish LoginHandshakeCompletedEvent. The event is published by the
        // SINGLE owner — GamePacketHandler.HandleKeyExchange, which always has a non-null event bus and
        // invokes this driver. Publishing here too would double-fire the event (and the driver's bus was
        // optional/never-wired). spec: crypto.md §6.1 order 4 (handshake-completed signalled once).
        return reply.Length;
    }
}