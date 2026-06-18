using System.Collections.Immutable;

namespace MartialHeroes.Client.Application.Events;

// =====================================================================================================
// Lobby orchestration (server-list fetch + channel-endpoint resolve)
// =====================================================================================================

/// <summary>
/// Presentation-facing snapshot of one lobby server-list entry, mapped from the network
/// <see cref="MartialHeroes.Network.Abstractions.Lobby.LobbyServerRecord"/>. The decoded wire fields
/// are forwarded verbatim and supplemented with the spec's presentation HINTS (load color band +
/// caption-branch classification) so a faithful select screen can render without re-deriving the
/// constants. spec: Docs/RE/packets/lobby.yaml Record Shape A (load thresholds 500/800/1200;
/// selectability gate StatusCode == 0 AND Load &lt; 2400; caption branch from StatusCode;
/// presentation-only).
/// </summary>
/// <param name="ServerId">
/// Server id / select-key (wire +0, range 1..40). An index into the client-local localized
/// server-name table when <see cref="StatusCode"/> is in 1..39. It is NOT a selectability gate (the
/// <c>== 100</c> literal is display-only). spec: Docs/RE/packets/lobby.yaml Record Shape A, offset +0.
/// </param>
/// <param name="StatusCode">
/// Availability / caption selector (wire +2). <c>== 0</c> is the active/selectable state; <c>== 3</c> is
/// the scheduled-open branch. See <see cref="StatusHint"/>.
/// </param>
/// <param name="Load">Population / load gauge (wire +4). Drives <see cref="LoadHint"/>; commit gate requires Load &lt; 2400.</param>
/// <param name="OpenTime">
/// Scheduled-open MINUTE value (wire +6) — a time component, NOT a flag/bitfield. Combined with
/// <see cref="Load"/> (hour) into an HH:MM caption in the <see cref="StatusCode"/> == 3 branch.
/// spec: Docs/RE/packets/lobby.yaml Record Shape A, offset +6.
/// </param>
/// <param name="LoadHint">Presentation color band classified from the load thresholds (presentation hint).</param>
/// <param name="StatusHint">Presentation classification of the caption branch (presentation hint).</param>
public readonly record struct ServerListEntryView(
    ushort ServerId,
    short StatusCode,
    short Load,
    short OpenTime,
    ServerLoadBand LoadHint,
    ServerStatusHint StatusHint);

/// <summary>
/// Presentation color band for a lobby server's load gauge, classified from the spec thresholds
/// 1200 / 800 / 500 (strict greater-than). Pure presentation hint — NOT a wire field. spec:
/// Docs/RE/packets/lobby.yaml Record Shape A (load caption mapping; thresholds 500/800/1200).
/// </summary>
public enum ServerLoadBand
{
    /// <summary>Load ≤ 500 (lightest band).</summary>
    Light,

    /// <summary>500 &lt; load ≤ 800.</summary>
    Moderate,

    /// <summary>800 &lt; load ≤ 1200.</summary>
    Busy,

    /// <summary>Load &gt; 1200 (the high / red band).</summary>
    Full,
}

/// <summary>
/// Presentation classification of a lobby server's caption branch (the status-code selector at
/// wire +2). Pure presentation hint — NOT a wire field. The selectability gate is
/// <c>StatusCode == 0 AND Load &lt; 2400</c>, not a <see cref="ServerListEntryView.ServerId"/> value.
/// spec: Docs/RE/packets/lobby.yaml Record Shape A.
/// </summary>
public enum ServerStatusHint
{
    /// <summary>
    /// StatusCode &lt; 1 or &gt; 39 (and not the special 3): fallback caption 5901 ("unknown id").
    /// spec: Docs/RE/packets/lobby.yaml Record Shape A (else → 5901).
    /// </summary>
    Invalid,

    /// <summary>
    /// StatusCode == 0: the active/selectable state — derive a load label/color from
    /// <see cref="ServerListEntryView.Load"/>.
    /// spec: Docs/RE/packets/lobby.yaml Record Shape A (status_code == 0).
    /// </summary>
    Normal,

    /// <summary>
    /// StatusCode == 3: the scheduled-open branch — an HH:MM caption built from
    /// <see cref="ServerListEntryView.Load"/> (hour) and <see cref="ServerListEntryView.OpenTime"/>
    /// (minute). spec: Docs/RE/packets/lobby.yaml Record Shape A (status_code == 3).
    /// </summary>
    Special,

    /// <summary>
    /// StatusCode in 1..39 (other than the special 3): index a per-value caption-string array; the
    /// display name is looked up from <see cref="ServerListEntryView.ServerId"/>.
    /// spec: Docs/RE/packets/lobby.yaml Record Shape A (status_code in 1..39).
    /// </summary>
    Caption,
}

/// <summary>
/// Published when the lobby server-list fetch completes (port 10000). Immutable snapshot of the
/// decoded server entries with presentation hints attached. The front-end ServerSelect screen binds
/// to this to render the real lobby. spec: Docs/RE/specs/login_flow.md §1 step 2 / §2.1.
/// </summary>
/// <param name="Servers">The decoded server entries (with load/status presentation hints).</param>
public sealed record ServerListReceivedEvent(
    ImmutableArray<ServerListEntryView> Servers) : IClientEvent;

/// <summary>
/// Published when a selected server's game-server endpoint is resolved via the lobby channel-endpoint
/// query (port 10000 + serverId). Immutable snapshot of the host + port the caller hands to the
/// transport to open the persistent game connection. spec: Docs/RE/specs/login_flow.md §1 step 4 /
/// §2.2.
/// </summary>
/// <param name="ServerId">The selected server id whose channel endpoint was resolved.</param>
/// <param name="Host">The game-server hostname or dotted-decimal IPv4 address (decoded; never null).</param>
/// <param name="Port">The game-server TCP port.</param>
public sealed record ChannelEndpointResolvedEvent(
    ushort ServerId,
    string Host,
    int Port) : IClientEvent;

// =====================================================================================================
// Character-management results (3/4, 3/6, 3/23)
// =====================================================================================================

/// <summary>
/// The manage action a 3/4 result is for, mapped from the wire <c>Subtype</c> byte. spec:
/// Docs/RE/specs/login_flow.md §5.5 (subtype 0 / 1 / 2; only subtype 2 = delete-confirm is inferred).
/// </summary>
public enum CharManageSubtype
{
    /// <summary>Subtype 0: generic refresh (semantics UNVERIFIED). spec: §5.5.</summary>
    GenericRefresh,

    /// <summary>Subtype 1: rename-applied slot refresh (semantics UNVERIFIED). spec: §5.5.</summary>
    RenameApplied,

    /// <summary>Subtype 2: delete-confirm — decrements the account char count. spec: §5.5.</summary>
    DeleteConfirm,

    /// <summary>Any other subtype value, forwarded verbatim in <see cref="CharManageResultEvent.RawSubtype"/>.</summary>
    Other,
}

/// <summary>
/// Published when a 3/7 character manage / delete result lands (SmsgCharManageResult). Immutable
/// snapshot. Subtype 2 is delete-confirm; on the blocked path (Success == false) the ReadyTime drives
/// a "wait HH:MM" delete-cooldown message. spec: Docs/RE/specs/login_flow.md §5.5;
/// Docs/RE/packets/3-4_char_manage_result.yaml.
/// </summary>
/// <param name="Success">True when the wire result byte was 1 (success path). spec: §5.5.</param>
/// <param name="Subtype">The classified manage action selector. spec: §5.5.</param>
/// <param name="RawSubtype">The raw wire subtype byte (forwarded verbatim for the unmapped values).</param>
/// <param name="ReadyTime">
/// Delete-cooldown ready timestamp (raw wire u32). On the blocked path the presentation formats a
/// "wait" message from (ReadyTime - now). Epoch / units UNVERIFIED. spec: §5.5.
/// </param>
/// <param name="AccountCharacterCount">
/// The account character count AFTER applying this result (a delete-confirm decremented it). The
/// presentation refreshes the slot count from this. spec: §5.5 (delete-confirm decrements the count).
/// </param>
public sealed record CharManageResultEvent(
    bool Success,
    CharManageSubtype Subtype,
    byte RawSubtype,
    uint ReadyTime,
    int AccountCharacterCount) : IClientEvent;

/// <summary>
/// Published when a 3/6 rename-character result lands (SmsgRenameCharResult). Immutable snapshot. On
/// success <see cref="NewName"/> carries the new CP949-decoded character name; on failure
/// <see cref="ErrorCode"/> carries the wire error code (range 0xC8..0xD4). spec:
/// Docs/RE/specs/login_flow.md §5.6.
/// </summary>
/// <param name="Success">True when the wire result byte was nonzero. spec: §5.6.</param>
/// <param name="NewName">
/// On success: the new character name (CP949 → managed string, decoded at this boundary). Empty on
/// failure. spec: §5.6.
/// </param>
/// <param name="ErrorCode">
/// On failure: the error code (0xC8..0xD4, mapped to a UI string by the presentation). 0 on success.
/// spec: §5.6.
/// </param>
public sealed record CharRenameResultEvent(
    bool Success,
    string NewName,
    byte ErrorCode) : IClientEvent;

/// <summary>
/// Published when a 3/23 character-create result lands (SmsgCharCreateResult). Immutable snapshot.
/// Pairs with the <see cref="CreateCharacterRequestedEvent"/> the select use-case emits for a blank
/// slot. On success the account char count is incremented and <see cref="AssignedSlotId"/> carries
/// the new slot; on failure <see cref="ErrorCode"/> carries the wire error code (0xC8..0xD4). spec:
/// Docs/RE/specs/login_flow.md §5.4.
/// </summary>
/// <param name="Success">True when the wire result byte was 1. spec: §5.4.</param>
/// <param name="AssignedSlotId">On success: the assigned slot id (wire Code byte). 0 on failure. spec: §5.4.</param>
/// <param name="ErrorCode">On failure: the error code (0xC8..0xD4). 0 on success. spec: §5.4.</param>
/// <param name="Value1">Slot-refresh value 1 (wire u32; MEANING UNVERIFIED). spec: §5.4.</param>
/// <param name="Value2">Slot-refresh value 2 (wire u32; MEANING UNVERIFIED). spec: §5.4.</param>
/// <param name="AccountCharacterCount">
/// The account character count AFTER applying this result (a success incremented it). spec: §5.4.
/// </param>
public sealed record CharCreateResultEvent(
    bool Success,
    byte AssignedSlotId,
    byte ErrorCode,
    uint Value1,
    uint Value2,
    int AccountCharacterCount) : IClientEvent;