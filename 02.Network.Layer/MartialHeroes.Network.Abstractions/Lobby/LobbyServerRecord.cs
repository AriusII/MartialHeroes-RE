namespace MartialHeroes.Network.Abstractions.Lobby;

/// <summary>
///     A single decoded server entry returned by the lobby server-list query (port 10000).
///     Corresponds to the 8-byte, little-endian wire record whose layout is defined in the
///     lobby mini-protocol.
/// </summary>
/// <remarks>
///     <para>
///         The lobby payload is <c>wrapper.count</c> packed records of 8 bytes each. This DTO is the
///         decoded, type-safe view that <c>ILobbyClient.FetchServerListAsync</c> surfaces to
///         <c>Client.Application</c>. The wire-to-DTO conversion belongs to the implementing transport;
///         this type is the neutral contract both sides agree on.
///     </para>
///     <para>
///         The localized server <b>name</b> is <b>not</b> on the wire — only the numeric
///         <see cref="ServerId" /> travels. A fresh implementation must supply its own
///         <c>server_id → display-name</c> map (a client-local table). See
///         <see cref="ServerId" /> remarks.
///     </para>
///     <para>
///         Commit / selectability gate (the only hard wire-relevant predicate): a row is selectable iff
///         <c>StatusCode == 0 AND Load &lt; 2400</c> (0x960, signed strict less-than). There is NO
///         <c>ServerId == 100</c> selectability gate — the only <c>== 100</c> literal is a display-only
///         "new server" label reposition that commits nothing.
///         spec: Docs/RE/packets/lobby.yaml Record Shape A.
///     </para>
/// </remarks>
/// <param name="ServerId">
///     Server id / select-key (+0, i16, range 1..40). Two roles on this field: (a) fed to the
///     client-local localized server-name lookup to fetch the display name (when <see cref="StatusCode" />
///     is in 1..39); (b) compared against the remembered "last server" default to draw the
///     current-selection highlight. It is NOT a selectability gate (the <c>== 100</c> literal is
///     display-only — a cosmetic "new server" label reposition).
///     <br />
///     spec: Docs/RE/specs/login_flow.md §2.1 — field +0, i16 (signed; CYCLE 9 signedness correction,
///     binary-won, 263bd994 — previously erroneously typed ushort; all four record fields are signed i16).
/// </param>
/// <param name="StatusCode">
///     Availability / caption selector (+2, i16). <c>== 0</c> is the active/selectable state — the commit
///     gate equality-tests this field for 0 (a non-zero value skips the commit). <c>== 3</c> is the
///     scheduled-open branch, which combines <see cref="Load" /> (hour) and <see cref="OpenTime" /> (minute)
///     into an HH:MM caption. The full status enum beyond 0 and 3 is unverified.
///     <br />
///     spec: Docs/RE/specs/login_flow.md §2.1 — field +2, i16.
/// </param>
/// <param name="Load">
///     Population / load gauge (+4, i16). The commit gate requires <c>Load &lt; 2400</c> (0x960, signed
///     strict less-than). When <see cref="StatusCode" /> == 3 the painter reuses this field as the
///     scheduled-open HOUR (combined with <see cref="OpenTime" /> for the HH:MM caption). Render-side
///     thresholds bucket it for the plate color (presentation-only).
///     <br />
///     spec: Docs/RE/specs/login_flow.md §2.1 — field +4, i16.
/// </param>
/// <param name="OpenTime">
///     Scheduled-open MINUTE value (+6, i16) — a time component, NOT a flag/bitfield. Read ONLY in the
///     <see cref="StatusCode" /> == 3 branch, where it supplies the minute digits of an HH:MM caption (hour
///     from <see cref="Load" />). Not read when <see cref="StatusCode" /> == 0 (active).
///     <br />
///     spec: Docs/RE/specs/login_flow.md §2.1 — field +6, i16.
/// </param>
public readonly record struct LobbyServerRecord(
    short ServerId,
    short StatusCode,
    short Load,
    short OpenTime)
{
    /// <summary>
    ///     Returns <see langword="true" /> when this server record is selectable by the player.
    ///     The wire-side commit gate: <c>StatusCode == 0</c> (active) AND <c>Load &lt; 2400</c>
    ///     (0x960, signed strict less-than). Both comparisons are on signed <c>i16</c> fields.
    ///     Downstream consumers (e.g. <c>EnvLogin</c>) MUST route through this property rather
    ///     than duplicating the predicate.
    ///     <br />
    ///     spec: Docs/RE/specs/login_flow.md §2.1 (signed status_code == 0 &amp;&amp; load &lt; 2400
    ///     — signedness CONFIRMED CYCLE 9, binary-won, 263bd994).
    /// </summary>
    public bool IsSelectable =>
        StatusCode == 0 && Load < 2400; // spec: Docs/RE/specs/login_flow.md §2.1 (signed status==0 && load<2400)
}