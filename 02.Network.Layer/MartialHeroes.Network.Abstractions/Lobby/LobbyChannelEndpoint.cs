namespace MartialHeroes.Network.Abstractions.Lobby;

/// <summary>
///     The game-server endpoint returned by a lobby channel-endpoint query
///     (port <c>10000 + selectedServerId</c>).
/// </summary>
/// <remarks>
///     <para>
///         The lobby channel-endpoint query decompresses the LZ4 payload and copies up to 30 (0x1E)
///         bytes into a zero-filled buffer, then reads it as a NUL-padded ASCII endpoint token.
///         The token format is <c>"&lt;host&gt; &lt;port&gt;"</c> with a <b>single SPACE (0x20) as
///         the delimiter</b> (not a colon, not NUL) — CONFIRMED CYCLE 9 (binary-won, 263bd994).
///         30 is the <b>copy-cap</b>, not a minimum: a shorter decompressed payload is valid and
///         must be tolerated. There is a <b>single endpoint</b> per response (no trailing array).
///         The transport splits on the single space: host = text before, port = decimal-ASCII after.
///         <br />
///         spec: Docs/RE/specs/login_flow.md §2.2 [CODE-CONFIRMED: up-to-30-byte copy cap; single-SPACE delimiter; single endpoint].
///     </para>
///     <para>
///         This endpoint identifies the <b>game server</b>, not the lobby. After obtaining this record
///         the caller opens its persistent overlapped game connection to <c>Host:Port</c> and runs the
///         <c>(major:minor)</c> opcode protocol (<c>0/0</c> key exchange, <c>1/4</c> auth, etc.).
///         The lobby socket is closed immediately after the query.
///     </para>
///     <para>
///         <see cref="Host" /> is a managed string here because it crosses the layer boundary as a
///         decoded value — the raw ASCII bytes never leave the transport layer. This is <b>not</b>
///         a wire struct; no <c>[StructLayout]</c> is needed.
///     </para>
/// </remarks>
/// <param name="Host">
///     The game-server hostname or dotted-decimal IPv4 address, decoded from the leading ASCII
///     field of the 30-byte channel-endpoint payload. Never null; may be empty on a failed parse
///     (the caller should treat an empty host as an error).
///     <br />
///     spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE B — CHANNEL-ENDPOINT text, offset +0 (30 B ASCII).
/// </param>
/// <param name="Port">
///     The game-server TCP port, parsed from the decimal integer token that follows the host in the
///     ASCII payload.
///     <br />
///     spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE B;
///     Docs/RE/specs/login_flow.md §2.2.
/// </param>
public sealed record LobbyChannelEndpoint(string Host, int Port)
{
    /// <inheritdoc />
    public override string ToString()
    {
        return $"{Host}:{Port}";
    }
}