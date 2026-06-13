namespace MartialHeroes.Network.Abstractions.Lobby;

/// <summary>
/// A single decoded server entry returned by the lobby server-list query (port 10000).
/// Corresponds to the 8-byte, little-endian wire record whose layout is defined in the
/// lobby mini-protocol.
/// </summary>
/// <remarks>
/// <para>
/// The lobby payload is <c>wrapper.major</c> packed records of 8 bytes each. This DTO is the
/// decoded, type-safe view that <c>ILobbyClient.FetchServerListAsync</c> surfaces to
/// <c>Client.Application</c>. The wire-to-DTO conversion belongs to the implementing transport;
/// this type is the neutral contract both sides agree on.
/// </para>
/// <para>
/// The localized server <b>name</b> is <b>not</b> on the wire — only the numeric
/// <see cref="ServerId"/> travels. A fresh implementation must supply its own
/// <c>server_id → display-name</c> map (a 41-entry client-local table). See
/// <see cref="ServerId"/> remarks.
/// </para>
/// <para>
/// Load-color thresholds (1200 / 800 / 500) and status sentinels (3 / 24 / 100) are
/// presentation-layer constants documented in the spec for a faithful select screen; they are
/// not parsed here.
/// </para>
/// </remarks>
/// <param name="ServerId">
/// Index (1..40) into the client-local localized server-name table. Values outside 1..40 should
/// be treated as invalid by the presentation layer.
/// <br/>
/// spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE A — SERVER-LIST record, offset +0 (u16).
/// </param>
/// <param name="Status">
/// Server availability / status code. Special-cased values: 0 = normal/open; 3 = scheduled
/// open (render the open clock); 24 = preparing / check sentinel; 100 = current-selection
/// sentinel. The full enum is capture-unverified.
/// <br/>
/// spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE A, offset +2 (i16);
/// Docs/RE/specs/login_flow.md §2.1.
/// </param>
/// <param name="Load">
/// Population / load gauge. Drives the load-color thresholds on the select screen. For
/// status == 3 (scheduled open) this field supplies the <b>hour</b> component of the open
/// clock (HH from <c>Load</c>, MM from <c>OpenTime</c>).
/// <br/>
/// spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE A, offset +4 (i16).
/// </param>
/// <param name="OpenTime">
/// Used only when <see cref="Status"/> == 3 (scheduled open). Supplies the <b>minute</b>
/// component of the open clock. The exact digit-packing (×10 split) is inferred, not
/// capture-verified.
/// <br/>
/// spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE A, offset +6 (i16).
/// </param>
public readonly record struct LobbyServerRecord(
    ushort ServerId,
    short Status,
    short Load,
    short OpenTime);