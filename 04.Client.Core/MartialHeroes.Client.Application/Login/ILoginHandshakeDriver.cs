namespace MartialHeroes.Client.Application.Login;

/// <summary>
/// Drives the login handshake reply: given the inbound 0/0 KeyExchange payload, builds the 1/4 Auth
/// reply from the staged credential and sends it. Injected into the inbound handler so the handler
/// stays free of the crypto build details (which live in Network.Crypto). spec:
/// Docs/RE/specs/crypto.md §6.
/// </summary>
public interface ILoginHandshakeDriver
{
    /// <summary>
    /// Handles a 0/0 KeyExchange payload: parse it, build the 1/4 reply from the staged credential,
    /// and send it via the outbound sink. Returns the built reply body length (for diagnostics).
    /// spec: Docs/RE/specs/crypto.md §6.1 (0/0 in -> 1/4 out).
    /// </summary>
    /// <param name="keyExchangePayload">The 62-byte 0/0 payload (already decompressed, no inverse cipher).</param>
    int OnKeyExchange(ReadOnlySpan<byte> keyExchangePayload);
}