namespace MartialHeroes.Network.Abstractions.Lobby;

/// <summary>
/// The game-server endpoint returned by a lobby channel-endpoint query
/// (port <c>10000 + selectedServerId</c>).
/// </summary>
/// <remarks>
/// <para>
/// The lobby channel-endpoint query decompresses the LZ4 payload and reads the first
/// 30 (0x1E) bytes as a fixed ASCII endpoint token (not guaranteed NUL-terminated). The
/// exact internal delimiter (e.g. <c>"host:port"</c>, <c>"host port"</c>, or fixed
/// sub-fields) is <b>NEEDS-CAPTURE</b> — the legacy client consumed the token opaquely.
/// The transport implementation parses it to produce the <see cref="Host"/> and
/// <see cref="Port"/> fields exposed here; a whitespace-split is a reasonable choice pending
/// a capture that confirms the delimiter.
/// <br/>
/// spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE B [CODE-CONFIRMED: 30 bytes; delimiter = NEEDS-CAPTURE].
/// </para>
/// <para>
/// This endpoint identifies the <b>game server</b>, not the lobby. After obtaining this record
/// the caller opens its persistent overlapped game connection to <c>Host:Port</c> and runs the
/// <c>(major:minor)</c> opcode protocol (<c>0/0</c> key exchange, <c>1/4</c> auth, etc.).
/// The lobby socket is closed immediately after the query.
/// </para>
/// <para>
/// <see cref="Host"/> is a managed string here because it crosses the layer boundary as a
/// decoded value — the raw ASCII bytes never leave the transport layer. This is <b>not</b>
/// a wire struct; no <c>[StructLayout]</c> is needed.
/// </para>
/// </remarks>
/// <param name="Host">
/// The game-server hostname or dotted-decimal IPv4 address, decoded from the leading ASCII
/// field of the 30-byte channel-endpoint payload. Never null; may be empty on a failed parse
/// (the caller should treat an empty host as an error).
/// <br/>
/// spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE B — CHANNEL-ENDPOINT text, offset +0 (30 B ASCII).
/// </param>
/// <param name="Port">
/// The game-server TCP port, parsed from the decimal integer token that follows the host in the
/// ASCII payload.
/// <br/>
/// spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE B;
/// Docs/RE/specs/login_flow.md §2.2.
/// </param>
public sealed record LobbyChannelEndpoint(string Host, int Port)
{
    /// <inheritdoc />
    public override string ToString() => $"{Host}:{Port}";
}