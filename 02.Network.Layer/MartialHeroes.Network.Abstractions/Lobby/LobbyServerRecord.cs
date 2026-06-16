namespace MartialHeroes.Network.Abstractions.Lobby;

/// <summary>
/// A single decoded server entry returned by the lobby server-list query (port 10000).
/// Corresponds to the 8-byte, little-endian wire record whose layout is defined in the
/// lobby mini-protocol.
/// </summary>
/// <remarks>
/// <para>
/// The lobby payload is <c>wrapper.count</c> packed records of 8 bytes each. This DTO is the
/// decoded, type-safe view that <c>ILobbyClient.FetchServerListAsync</c> surfaces to
/// <c>Client.Application</c>. The wire-to-DTO conversion belongs to the implementing transport;
/// this type is the neutral contract both sides agree on.
/// </para>
/// <para>
/// The localized server <b>name</b> is <b>not</b> on the wire — only the numeric
/// <see cref="ServerId"/> travels. A fresh implementation must supply its own
/// <c>server_id → display-name</c> map (a client-local table). See
/// <see cref="ServerId"/> remarks.
/// </para>
/// <para>
/// Load-color thresholds (1200 / 800 / 500) and the per-caption message-id lookup are
/// presentation-layer constants documented in the spec for a faithful select screen; they are
/// not parsed here.
/// </para>
/// </remarks>
/// <param name="ServerId">
/// Server id / select-key (+0, i16). Three roles, all on this one field:
/// (a) fed to the client-local localized server-name lookup to fetch the display name (when
/// <see cref="Status"/> is in 1..39); (b) compared against the remembered "last server"
/// default to draw the current-selection highlight; (c) <b>tested == 100 to unlock the
/// per-row select buttons</b> — this is the "server is AVAILABLE" gate. The 100 availability
/// sentinel lives on <em>this</em> field, NOT on <see cref="Status"/>.
/// <br/>
/// spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE A — SERVER-LIST record, offset +0 (i16)
/// [CODE-CONFIRMED: the 100 sentinel is on id_selectkey (+0), not status_kind (+2)].
/// </param>
/// <param name="Status">
/// Caption / branch selector (+2, i16), referred to in the spec as <c>status_kind</c>. Drives
/// the per-row caption switch:
/// <list type="bullet">
///   <item>0 — derive a population label/color from <see cref="Population"/> / <see cref="Flag"/>
///   (see POPULATION rules in the spec).</item>
///   <item>3 — special: if <see cref="Population"/> == 24 → caption message-id 6004; else a
///   latency caption (message-id 6005) formatted from <see cref="Population"/> and
///   <see cref="Flag"/> (each split as <c>value/10</c> and <c>value%10</c>).</item>
///   <item>1..39 — index a per-value caption-string array; the server display name is looked up
///   from <see cref="ServerId"/>.</item>
///   <item>&lt; 1 or &gt; 39 — fallback caption message-id 5901 ("unknown id"), formatted with
///   <see cref="ServerId"/>.</item>
/// </list>
/// The full enum beyond {0, 3} and 1..39 is capture-unverified.
/// <br/>
/// spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE A, offset +2 (i16) [CODE-CONFIRMED ids].
/// </param>
/// <param name="Population">
/// Population / count value (+4, i16). In <see cref="Status"/> == 0 numeric mode it is
/// thresholded against 500/800/1200. In the <see cref="Status"/> == 3 branch it is compared
/// == 24 (6004 vs 6005 split) and serves as a digit-split numerator in the 6005 latency format
/// (<c>Population/10</c> and <c>Population%10</c>). In discrete mode (when
/// <see cref="Flag"/> == 0) it carries a discrete load level (2/3/4 switch).
/// <br/>
/// spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE A, offset +4 (i16) [CODE-CONFIRMED].
/// </param>
/// <param name="Flag">
/// Mode / latency-format flag (+6, i16). A MODE flag controlling how <see cref="Population"/>
/// is interpreted in the <see cref="Status"/> == 0 branch:
/// <list type="bullet">
///   <item>Nonzero — treat <see cref="Population"/> as a numeric population value; apply the
///   500/800/1200 thresholds.</item>
///   <item>Zero — treat <see cref="Population"/> as a discrete load level (switch on 2/3/4).</item>
/// </list>
/// In the <see cref="Status"/> == 3 branch this field is also used as a digit-split numerator
/// in the 6005 latency format (<c>Flag/10</c> and <c>Flag%10</c>). This is NOT an
/// HH/MM open-clock — that framing is NOT supported by the spec.
/// <br/>
/// spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE A, offset +6 (i16) [CODE-CONFIRMED].
/// </param>
public readonly record struct LobbyServerRecord(
    ushort ServerId,
    short Status,
    short Population,
    short Flag);