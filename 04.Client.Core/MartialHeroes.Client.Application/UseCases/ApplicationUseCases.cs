using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.StateMachine;
using MartialHeroes.Client.Application.World;
using System.Collections.Immutable;
using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Client.Domain.Inventory;
using MartialHeroes.Client.Domain.Skills;
using MartialHeroes.Network.Abstractions.Lobby;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Network.Protocol.Packets;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.UseCases;

/// <summary>
/// Default <see cref="IApplicationUseCases"/>. Translates presentation intents into FSM transitions
/// and outbound requests via the injected <see cref="IOutboundPacketSink"/>. Each use-case serialises
/// the matching Network.Protocol packet struct to a PLAINTEXT payload (the wire fields, no header) and
/// calls <see cref="IOutboundPacketSink.SendAsync"/> with the opcode; the sink prepends the 8-byte
/// frame header and applies the byte cipher + LZ4 downstream. No transport/framing/crypto here.
/// spec: Docs/RE/opcodes.md (outbound frame header); Docs/RE/specs/crypto.md §6.1 (login staging).
/// </summary>
/// <remarks>
/// <para>
/// <b>Field writes vs. struct construction.</b> The request structs are immutable (no public ctor),
/// so each field is written to the payload at the struct's documented Pack=1 offset using
/// <see cref="BinaryPrimitives"/> (little-endian, matching the struct layout). The byte widths equal
/// the structs' <c>WireSize</c> / <c>HeaderSize</c> constants. spec: each packet yaml.
/// </para>
/// <para>
/// <b>Version token (sample-verified).</b> The 1/9 version token is derived via
/// <see cref="ClientVersionToken.Derive"/> (<c>10 × versionField + 9</c>) from the injected
/// <see cref="IClientVersionSource"/>. The default source yields the sampled field 2114 → token 21149
/// (<c>sample_verified</c> from the real <c>game.ver</c>). spec: login_flow.md §3.3 / §7. The pre-0/0
/// username send (1/6) is not specced; <see cref="LoginAsync"/> stages the credential but emits no 1/6
/// frame. The chat/credential text charset is UNKNOWN per the chat specs; UTF-8 is the documented
/// provisional choice.
/// </para>
/// </remarks>
public sealed class ApplicationUseCases : IApplicationUseCases
{
    private readonly IOutboundPacketSink _outbound;
    private readonly ClientStateMachine _stateMachine;
    private readonly ClientWorld _world;
    private readonly LoginCredentialStore _credentials;
    private readonly SessionId _sessionId;
    private readonly byte[] _versionToken;
    private readonly LocalPlayerState? _localPlayer;
    private readonly CharacterSelectionStore? _characterSelection;
    private readonly IClientEventBus? _eventBus;
    private readonly ILobbyClient? _lobbyClient;
    private readonly bool _emitLoginBlob16;

    /// <summary>The 1/9 version-token length: a fixed 33-byte buffer. spec: 1-9_enter_game_request.yaml.</summary>
    public const int VersionTokenLength = 33;

    /// <summary>
    /// Creates the use-case facade.
    /// </summary>
    /// <param name="versionToken">
    /// Optional explicit override of the 33-byte 1/9 version-token buffer. When <b>empty</b> (the
    /// default), the token is DERIVED as <c>10 × versionField + 9</c> from
    /// <paramref name="versionSource"/> and stamped as a NUL-terminated decimal ASCII string. A non-empty
    /// span is used verbatim (zero-padded / truncated to 33 bytes) — an escape hatch for a future binary
    /// placement. spec: login_flow.md §3.3 / §7; packets/1-9_enter_game_request.yaml.
    /// </param>
    /// <param name="versionSource">
    /// Supplies the <c>game.ver</c> version field used to derive the 1/9 token when no explicit
    /// <paramref name="versionToken"/> is given. Defaults to <see cref="DefaultClientVersionSource.Instance"/>
    /// (field 2114 → token 21149, <c>sample_verified</c>). spec: login_flow.md §3.3 / §7.
    /// </param>
    /// <param name="characterSelection">
    /// The session-scoped store the 3/1 handler fills and <see cref="SelectCharacterAsync"/> reads to
    /// detect "@BLANK@" and cache the chosen descriptor for the 3/7 spawn. spec: login_flow.md §3.5.
    /// </param>
    /// <param name="eventBus">
    /// Used to publish <see cref="CreateCharacterRequestedEvent"/> when an empty slot is confirmed, and
    /// the lobby <see cref="ServerListReceivedEvent"/> / <see cref="ChannelEndpointResolvedEvent"/>.
    /// Optional; when absent the create-character routing is silent (still suppresses the 1/9 send) and
    /// the lobby use-cases still return their results without publishing.
    /// </param>
    /// <param name="lobbyClient">
    /// The lobby discovery surface (server-list + channel-endpoint). Injected as a contract; the app
    /// never references the concrete transport. Required only for the lobby use-cases. spec:
    /// Docs/RE/specs/login_flow.md §2.
    /// </param>
    /// <param name="emitLoginBlob16">
    /// FEATURE FLAG (default <see langword="false"/>) for emitting the optional 1/6 login blob in
    /// <see cref="LoginAsync"/>. Kept OFF because the 1/6 opcode collision (login blob vs character
    /// create) is unresolved pending a capture. spec: Docs/RE/packets/1-6_login_or_create.yaml;
    /// Docs/RE/specs/login_flow.md §4.2.
    /// </param>
    public ApplicationUseCases(
        IOutboundPacketSink outbound,
        ClientStateMachine stateMachine,
        ClientWorld world,
        LoginCredentialStore credentials,
        SessionId sessionId,
        ReadOnlySpan<byte> versionToken = default,
        LocalPlayerState? localPlayer = null,
        IClientVersionSource? versionSource = null,
        CharacterSelectionStore? characterSelection = null,
        IClientEventBus? eventBus = null,
        ILobbyClient? lobbyClient = null,
        bool emitLoginBlob16 = false)
    {
        _outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _sessionId = sessionId;
        _localPlayer = localPlayer; // optional: drives the cast state machine when wired
        _characterSelection = characterSelection; // optional: needed for @BLANK@ + descriptor cache
        _eventBus = eventBus; // optional: publishes CreateCharacterRequested + lobby events
        _lobbyClient = lobbyClient; // optional: only the lobby use-cases need it
        _emitLoginBlob16 = emitLoginBlob16; // FEATURE FLAG: off by default (1/6 collision unresolved)

        _versionToken = new byte[VersionTokenLength];
        if (!versionToken.IsEmpty)
        {
            // Explicit override: use the supplied bytes verbatim (zero-padded / truncated to 33 bytes).
            versionToken[..Math.Min(versionToken.Length, VersionTokenLength)].CopyTo(_versionToken);
        }
        else
        {
            // Derive the version token: token = 10 × versionField + 9. spec: login_flow.md §3.3 / §7.
            // The default source yields the sampled field 2114 -> token 21149 (sample_verified). Stamp it
            // as a NUL-terminated decimal ASCII string into the fixed 33-byte buffer; the exact in-body
            // placement is UNVERIFIED (spec §9 item 5) but the token VALUE is firm.
            // spec: packets/1-9_enter_game_request.yaml (VersionToken = 33-byte asciiz).
            uint token = ClientVersionToken.Derive(
                (versionSource ?? DefaultClientVersionSource.Instance).VersionField);
            string decimalToken = token.ToString(CultureInfo.InvariantCulture);
            Encoding.ASCII.GetBytes(
                decimalToken.AsSpan(0, Math.Min(decimalToken.Length, VersionTokenLength - 1)), _versionToken);
            // Remaining bytes stay 0 (NUL terminator + zero tail).
        }
    }

    /// <inheritdoc />
    public ValueTask LoginAsync(
        string username, string password, string? pin = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);
        cancellationToken.ThrowIfCancellationRequested();

        // Stage the credential before any 0/0 arrives; the 1/4 reply is built later by the login
        // handler when the server's KeyExchange lands. spec: Docs/RE/specs/crypto.md §6.1.
        _credentials.Stage(username, password);

        // Drive the FSM from Login toward handshaking. CharacterSelection is the application-state
        // analogue of "secure session established / awaiting auth result".
        _stateMachine.OnAuthenticated();

        // OPT-IN 1/6 login-blob emit, OFF by default. The 1/6 opcode is a known UNRESOLVED collision
        // (login credential blob vs character-create body) — a capture is needed to disambiguate, so
        // this path stays gated behind the feature flag. The password is NEVER in this blob; it travels
        // only via the RSA 1/4 reply. spec: Docs/RE/packets/1-6_login_or_create.yaml;
        // Docs/RE/specs/login_flow.md §4.2 / §7.
        if (_emitLoginBlob16)
        {
            byte[] blob = BuildLoginBlob16(username, pin);
            return SendAsync(major: 1, minor: 6, blob, cancellationToken);
        }

        // Default (flag OFF): we do NOT fabricate a 1/6 frame; the handshake proceeds from the inbound
        // 0/0. Documented gap pending capture resolution of the 1/6 collision.
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<LobbyServerRecord>> FetchServerListAsync(
        CancellationToken cancellationToken = default)
    {
        ILobbyClient lobby = _lobbyClient
            ?? throw new InvalidOperationException(
                "No ILobbyClient was wired; FetchServerListAsync requires the lobby surface. spec: login_flow.md §2.");

        IReadOnlyList<LobbyServerRecord> records =
            await lobby.FetchServerListAsync(cancellationToken).ConfigureAwait(false);

        // Map each decoded record to a presentation view, attaching the load-color band + status-sentinel
        // hints so the ServerSelect screen does not re-derive the spec constants. spec: login_flow.md §2.1.
        var builder = ImmutableArray.CreateBuilder<ServerListEntryView>(records.Count);
        foreach (LobbyServerRecord r in records)
        {
            builder.Add(new ServerListEntryView(
                r.ServerId, r.Status, r.Load, r.OpenTime,
                ClassifyLoad(r.Load), ClassifyStatus(r.Status)));
        }

        _eventBus?.Publish(new ServerListReceivedEvent(builder.ToImmutable()));
        return records;
    }

    /// <inheritdoc />
    public async ValueTask<LobbyChannelEndpoint> SelectServerAsync(
        ushort serverId, CancellationToken cancellationToken = default)
    {
        ILobbyClient lobby = _lobbyClient
            ?? throw new InvalidOperationException(
                "No ILobbyClient was wired; SelectServerAsync requires the lobby surface. spec: login_flow.md §2.");

        // Resolve the game-server endpoint for the chosen server (channel port = 10000 + serverId).
        // spec: login_flow.md §2.2 / §7 (lobby base port constant).
        LobbyChannelEndpoint endpoint =
            await lobby.FetchChannelEndpointAsync(serverId, cancellationToken).ConfigureAwait(false);

        _eventBus?.Publish(new ChannelEndpointResolvedEvent(serverId, endpoint.Host, endpoint.Port));
        return endpoint;
    }

    /// <inheritdoc />
    public ValueTask RequestMoveAsync(
        Vector3Fixed target, bool running, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Heading = atan2 of (target - current) in the XZ plane, from the local player's current
        // position. spec: Docs/RE/packets/2-13_move_request.yaml (Heading 0x00). When the local actor
        // is unknown we fall back to a zero origin (heading still well-defined toward the target).
        Vector3Fixed current = _world.LocalActor?.Position ?? Vector3Fixed.Zero;
        var (curX, _, curZ) = current.ToVector3Float();
        var (tgtX, _, tgtZ) = target.ToVector3Float();
        float heading = MathF.Atan2(tgtZ - curZ, tgtX - curX); // angular unit unconfirmed; radians provisional

        // Build the fixed 16-byte 2/13 payload at the struct's Pack=1 offsets. spec: 2-13 fields.
        Span<byte> payload = stackalloc byte[CmsgMoveRequest.WireSize];
        payload.Clear();
        BinaryPrimitives.WriteSingleLittleEndian(payload.Slice(0x00, 4), heading); // 0x00 Heading
        BinaryPrimitives.WriteSingleLittleEndian(payload.Slice(0x04, 4), tgtX); // 0x04 TargetX
        BinaryPrimitives.WriteSingleLittleEndian(payload.Slice(0x08, 4), tgtZ); // 0x08 TargetZ
        // 0x0c ModeFlags: low byte is the click-to-move mode selector (1); a run bit packs into the
        // same region. The sub-byte split is UNKNOWN, so we set mode=1 plus a run bit at 0x100.
        // spec: 2-13_move_request.yaml (ModeFlags, byte split unconfirmed -> PROVISIONAL packing).
        const uint moveModeClickToMove = 1u;
        const uint runBit = 0x0000_0100u;
        uint modeFlags = moveModeClickToMove | (running ? runBit : 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0x0c, 4), modeFlags); // 0x0c ModeFlags

        return SendAsync(major: 2, minor: 13, payload, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask SelectCharacterAsync(int slotIndex, CancellationToken cancellationToken = default)
    {
        // Slot-range guard: the char list supports a maximum of 5 slots (indices 0..4). spec:
        // login_flow.md §3.5 ("slot index ≤ 4") / §7 (Char-list maximum slots = 5).
        if (slotIndex is < 0 or > CharacterSelectionStore.MaxSlotIndex)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slotIndex), slotIndex, "Slot index must be 0..4 (spec: login_flow.md §3.5).");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Confirm the slot against the cached 3/1 roster: detect the "@BLANK@" empty-slot sentinel,
        // re-check the range, and on a real character cache the chosen descriptor for the 3/7 spawn.
        // spec: login_flow.md §3.3 / §3.5.
        if (_characterSelection is not null)
        {
            CharacterSelectionStore.SelectOutcome outcome = _characterSelection.Confirm(slotIndex);
            switch (outcome)
            {
                case CharacterSelectionStore.SelectOutcome.Blank:
                    // Empty slot -> route to character creation; do NOT send 1/9. spec: §3.3 / §3.5.
                    _eventBus?.Publish(new CreateCharacterRequestedEvent(slotIndex));
                    return ValueTask.CompletedTask;

                case CharacterSelectionStore.SelectOutcome.Invalid:
                    // Unoccupied / unknown slot (no retained record): nothing to enter. Suppress the send.
                    return ValueTask.CompletedTask;

                case CharacterSelectionStore.SelectOutcome.Confirmed:
                default:
                    break; // real character cached -> proceed to send 1/9.
            }
        }

        // Build the fixed 40-byte 1/9 payload. spec: 1-9_enter_game_request.yaml.
        Span<byte> payload = stackalloc byte[CmsgEnterGameRequest.WireSize];
        payload.Clear();
        payload[0x00] = (byte)slotIndex; // 0x00 SlotIndex (HIGH CONFIDENCE)
        // 0x01 VersionToken: token = 10 × versionField + 9 (= 21149 for the sampled game.ver), stamped
        // as a NUL-terminated decimal ASCII string. spec: login_flow.md §3.3 / §7.
        _versionToken.CopyTo(payload.Slice(0x01, VersionTokenLength));
        // 0x22 Tail (6 bytes) stays zero. spec: 1-9 (observed zero).

        // Drive the deterministic lifecycle toward Loading; the 3/5 ack later transitions to World
        // (handled by GamePacketHandler). spec: Docs/RE/opcodes.md (3/5 SmsgEnterGameAck).
        _stateMachine.OnCharacterSelected();

        return SendAsync(major: 1, minor: 9, payload, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask UseSkillAsync(
        byte slot,
        ReadOnlyMemory<uint> targetsA = default,
        ReadOnlyMemory<uint> targetsB = default,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ReadOnlySpan<uint> a = targetsA.Span;
        ReadOnlySpan<uint> b = targetsB.Span;
        if (a.Length > ushort.MaxValue || b.Length > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(targetsA), "Too many skill targets for a u16 count.");
        }

        // Total payload = 24-byte header + (CountA + CountB) * 4. spec: 2-52_use_skill.yaml.
        int tailBytes = (a.Length + b.Length) * sizeof(uint);
        var payload = new byte[CmsgUseSkillHeader.HeaderSize + tailBytes];
        Span<byte> p = payload;
        p.Clear();

        // Fixed 24-byte header at Pack=1 offsets. spec: 2-52 fields.
        p[0x00] = slot; // 0x00 SkillSlot (0xFF = basic attack)
        // 0x01..0x03 Pad0 stays zero.
        // 0x04 AimMode / 0x08 AimScale / 0x0c AimX / 0x10 AimZ default to 0 (caller did not supply aim).
        // spec: 2-52 (aim fields; defaults documented).
        BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(0x14, 2), (ushort)a.Length); // 0x14 CountA
        BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(0x16, 2), (ushort)b.Length); // 0x16 CountB

        // Variable tail: array A (u32 ids) then array B (u32 ids). spec: 2-52 TRAILER.
        int cursor = CmsgUseSkillHeader.HeaderSize;
        foreach (uint id in a)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(cursor, 4), id);
            cursor += sizeof(uint);
        }

        foreach (uint id in b)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(cursor, 4), id);
            cursor += sizeof(uint);
        }

        return SendAsync(major: 2, minor: 52, payload, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask SendChatAsync(
        uint channel,
        string text,
        string? recipientName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();

        bool isWhisper = !string.IsNullOrEmpty(recipientName);
        return isWhisper
            ? SendWhisperAsync(channel, recipientName!, text, cancellationToken)
            : SendChannelChatAsync(channel, text, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<SkillCastResult> CastSkillAsync(
        byte slot,
        SkillDefinition skill,
        CasterState caster,
        ISkillTargetingQuery targeting,
        Vector3Fixed aimPoint,
        long nowMs,
        ReadOnlyMemory<uint> targetsA = default,
        ReadOnlyMemory<uint> targetsB = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targeting);
        cancellationToken.ThrowIfCancellationRequested();

        // Run the full ordered gate chain via the cast state machine (which delegates to
        // SkillCastValidator). On a non-Ok result, NOTHING is sent. spec: skills.md §2 / §2.4.
        SkillCastState current = _localPlayer?.CastState ?? SkillCastState.Idle;
        CooldownTable cooldowns = _localPlayer?.Cooldowns ?? _scratchCooldowns;
        (SkillCastState next, SkillCastResult result) =
            current.TryBeginCast(in skill, in caster, cooldowns, targeting, in aimPoint, nowMs);

        if (result != SkillCastResult.Ok)
        {
            return result; // gate blocked -> no 2/52 send. spec: skills.md §2.1.
        }

        // Advance the cast state machine (Casting phase) when a local-player holder is wired.
        if (_localPlayer is not null)
        {
            _localPlayer.CastState = next;
        }

        // Success -> build and send 2/52 CmsgUseSkill (header + the two target-id arrays). spec: §2.4.
        await UseSkillAsync(slot, targetsA, targetsB, cancellationToken).ConfigureAwait(false);
        return SkillCastResult.Ok;
    }

    /// <inheritdoc />
    public ValueTask<EquipCheckResult> EquipItemAsync(
        byte mode,
        byte slot,
        byte fromSub,
        byte toSlot,
        byte sub,
        int itemIndex,
        EquipStateGates state,
        EquipRelationContext relation = default,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Client-side gate chain (valid index, state gates, slot-8 relation guard). spec: inventory_trade.md §4.2.
        EquipCheckResult check = EquipRules.CheckEquip(itemIndex, toSlot, in state, in relation);
        if (check != EquipCheckResult.Allowed)
        {
            return ValueTask.FromResult(check); // blocked -> no 2/16 send. spec: §4.2.
        }

        // Build the 12-byte 2/16 CmsgEquipChange payload. spec: inventory_trade.md §4.1.
        var payload = new byte[EquipChangeWireSize];
        payload[0x00] = mode; // 0x00 mode (0 unequip / 1 equip / 2 swap)
        payload[0x01] = slot; // 0x01 equipment slot
        payload[0x02] = fromSub; // 0x02 from
        payload[0x03] = toSlot; // 0x03 to
        payload[0x04] = sub; // 0x04 sub (e.g. bag page)
        // 0x05..0x07 alignment pad stays zero. spec: §4.1.
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0x08, 4), itemIndex); // 0x08 item_index (≥ 0)

        return SendResultAsync(major: 2, minor: 16, payload, EquipCheckResult.Allowed, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<bool> MoveItemAsync(
        InventoryGrid grid,
        int fromIndex,
        int toIndex,
        uint quantity = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grid);
        cancellationToken.ThrowIfCancellationRequested();

        // An empty source has nothing to move: do not mutate and do not emit network traffic for a no-op.
        // (Domain's Move treats an empty source as a successful no-op; we suppress the send.) spec: §9.
        if (grid.IsEmpty(fromIndex))
        {
            return ValueTask.FromResult(false);
        }

        // Apply the local grid mutation deterministically (Domain owns the rules). quantity 0 = full move;
        // a partial quantity into an empty slot is a split; same-item into an occupied slot is a merge.
        // spec: inventory_trade.md §9.
        bool moved = quantity > 0
            ? ItemStackOps.Split(grid, fromIndex, toIndex, quantity)
            : grid.Move(fromIndex, toIndex);

        if (!moved)
        {
            return ValueTask.FromResult(false); // local move rejected -> nothing sent. spec: §9.
        }

        // The in-bag move rides the equip slot-move opcode (2/16). spec: inventory_trade.md §9 / §4.1.
        // mode 1, slot 0; from/to carry the slot indices (low byte each); item_index = source slot.
        var payload = new byte[EquipChangeWireSize];
        payload[0x00] = 1; // mode (in-bag move reuses the equip path). spec: §9.
        payload[0x02] = unchecked((byte)fromIndex); // 0x02 from
        payload[0x03] = unchecked((byte)toIndex); // 0x03 to
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0x08, 4), fromIndex); // 0x08 item_index

        return SendResultAsync(major: 2, minor: 16, payload, result: true, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<(TradeSession Next, bool Accepted)> TradeRequestAsync(
        TradeSession session,
        uint partnerActorId,
        byte requestMode = 0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Self-target guard: never trade with yourself. spec: inventory_trade.md §8.1 (self-target guard).
        if (_world.LocalActorKey is { } self && self.RawId == partnerActorId)
        {
            return ValueTask.FromResult((session, false));
        }

        // Drive the Domain trade state machine (idle -> requested). spec: inventory_trade.md §8.1/§8.3.
        (TradeSession next, bool accepted) = session.Request(partnerActorId);
        if (!accepted)
        {
            return ValueTask.FromResult((session, false)); // out of phase / zero partner -> nothing sent.
        }

        // Build the 8-byte 2/23 CmsgTradeRequest. spec: inventory_trade.md §8.1.
        var payload = new byte[TradeRequestWireSize];
        payload[0x00] = requestMode; // 0x00 mode (request/accept/decline/cancel; enum UNVERIFIED)
        // 0x01..0x03 pad stays zero. spec: §8.1.
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x04, 4), partnerActorId); // 0x04 value = target id

        return SendResultAsync(major: 2, minor: 23, payload, (next, true), cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<bool> PartyInviteAsync(
        uint targetActorId,
        byte subOp = 0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Guard: a zero id or a self-invite is refused. spec: social.md §2.4 (self-target guarded cluster).
        if (targetActorId == 0 || (_world.LocalActorKey is { } self && self.RawId == targetActorId))
        {
            return ValueTask.FromResult(false);
        }

        // Build the 8-byte 2/35 CmsgPartyOrRelationInvite: [u8 sub-op][u32 target id]. spec: social.md §2.4.
        var payload = new byte[PartyInviteWireSize];
        payload[0x00] = subOp; // 0x00 sub-op
        // 0x01..0x03 pad stays zero (dword alignment of the target id).
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x04, 4), targetActorId); // 0x04 target id

        return SendResultAsync(major: 2, minor: 35, payload, result: true, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // C2S payload sizes (spec-cited; no struct exists for these client-emitted minors)
    // -------------------------------------------------------------------------

    /// <summary>2/16 CmsgEquipChange payload size. spec: Docs/RE/specs/inventory_trade.md §4.1 (12 bytes).</summary>
    private const int EquipChangeWireSize = 12;

    /// <summary>2/23 CmsgTradeRequest payload size. spec: Docs/RE/specs/inventory_trade.md §8.1 (8 bytes).</summary>
    private const int TradeRequestWireSize = 8;

    /// <summary>2/35 CmsgPartyOrRelationInvite payload size. spec: Docs/RE/specs/social.md §2.4 (8 bytes).</summary>
    private const int PartyInviteWireSize = 8;

    /// <summary>An idle scratch cooldown table for the cast gate when no local-player state is wired (tests).</summary>
    private readonly CooldownTable _scratchCooldowns = new();

    /// <summary>
    /// First payload byte of the 1/6 login blob: sub-opcode 0x2B (43). spec:
    /// Docs/RE/specs/login_flow.md §4.2 / §7 (login sub-opcode byte = 0x2B).
    /// </summary>
    private const byte LoginBlobSubOpcode = 0x2B;

    // -------------------------------------------------------------------------
    // 1/6 login blob (feature-flagged) + lobby presentation classification
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds the optional 1/6 login blob: <c>[0x2B][u32len account\0]([u32len PIN\0])</c>. Each
    /// length-prefixed field is <c>[u32 LE length][bytes][NUL]</c> where the length INCLUDES the
    /// trailing NUL (so prefix = byte-count + 1). The PIN field is omitted entirely when no PIN is
    /// supplied. The password is NEVER serialized here — it rides only the RSA 1/4 reply. spec:
    /// Docs/RE/specs/login_flow.md §4.2 / §7; Docs/RE/packets/1-6_login_or_create.yaml (collision-gated).
    /// </summary>
    private static byte[] BuildLoginBlob16(string account, string? pin)
    {
        // Field bytes are CP949 in the Korean client; ASCII account/PIN round-trip losslessly. We use
        // the same length-prefixed encoder as the chat path (u32 LE length incl. NUL). spec: §4.2.
        byte[] accountField = BuildLengthPrefixedText(account);
        byte[]? pinField = string.IsNullOrEmpty(pin) ? null : BuildLengthPrefixedText(pin);

        int size = sizeof(byte) + accountField.Length + (pinField?.Length ?? 0);
        var blob = new byte[size];
        Span<byte> w = blob;
        w[0] = LoginBlobSubOpcode; // 0x2B sub-opcode. spec: §4.2 order 1.
        int cursor = 1;
        accountField.CopyTo(w[cursor..]); // [u32len account\0]. spec: §4.2 order 2.
        cursor += accountField.Length;
        if (pinField is not null)
        {
            pinField.CopyTo(w[cursor..]); // optional [u32len PIN\0]. spec: §4.2 order 3.
        }

        return blob;
    }

    /// <summary>
    /// Classifies a lobby server's load into a presentation color band from the spec thresholds
    /// 1200 / 800 / 500 (presentation-only; not a wire decode). spec: Docs/RE/specs/login_flow.md
    /// §2.1 / §7 (load-color thresholds).
    /// </summary>
    private static ServerLoadBand ClassifyLoad(short load) => load switch
    {
        >= 1200 => ServerLoadBand.Full, // spec: §2.1 high threshold 1200
        >= 800 => ServerLoadBand.Busy, // spec: §2.1 threshold 800
        >= 500 => ServerLoadBand.Moderate, // spec: §2.1 threshold 500
        _ => ServerLoadBand.Light,
    };

    /// <summary>
    /// Classifies a lobby server's status sentinel for presentation (3 = scheduled open, 24 =
    /// preparing, 100 = current selection; in-range otherwise = normal; ≤ 0 or &gt; 40 = invalid).
    /// Presentation-only; not a wire decode. spec: Docs/RE/specs/login_flow.md §2.1 / §7.
    /// </summary>
    private static ServerStatusHint ClassifyStatus(short status) => status switch
    {
        3 => ServerStatusHint.ScheduledOpen, // spec: §2.1 sentinel 3
        24 => ServerStatusHint.Preparing, // spec: §2.1 sentinel 24
        100 => ServerStatusHint.CurrentSelection, // spec: §2.1 sentinel 100
        <= 0 or > 40 => ServerStatusHint.Invalid, // spec: §2.1 (≤ 0 or > 40 is invalid/unavailable)
        _ => ServerStatusHint.Normal,
    };

    // -------------------------------------------------------------------------
    // Chat helpers
    // -------------------------------------------------------------------------

    private ValueTask SendWhisperAsync(uint channel, string recipientName, string text, CancellationToken ct)
    {
        // 2/7: 19-byte header + [u32 textLength][text bytes]; textLength includes the terminating NUL.
        // spec: Docs/RE/packets/2-7_whisper.yaml.
        byte[] body = BuildLengthPrefixedText(text);
        var payload = new byte[CmsgWhisperHeader.HeaderSize + body.Length];
        Span<byte> p = payload;
        p.Clear();

        p[0x00] = (byte)channel; // 0x00 ChannelType (channel selector, low byte)
        // 0x01 Flag stays zero (meaning UNKNOWN; spec).
        // 0x02 TargetName: NUL-padded into a fixed 16-byte buffer. spec: 2-7 (HIGH CONFIDENCE).
        WriteFixedAscii(recipientName, p.Slice(0x02, 16));
        // 0x12 HeaderTail stays zero.

        body.CopyTo(p.Slice(CmsgWhisperHeader.HeaderSize));
        return SendAsync(major: 2, minor: 7, payload, ct);
    }

    private ValueTask SendChannelChatAsync(uint channel, string text, CancellationToken ct)
    {
        // 3/21: 56-byte context header + [u32 textLength][text bytes]; textLength = strlen + 1.
        // spec: Docs/RE/packets/3-21_chat_channel.yaml.
        byte[] body = BuildLengthPrefixedText(text);
        var payload = new byte[CmsgChatChannelHeader.HeaderSize + body.Length];
        Span<byte> p = payload;
        p.Clear();

        // 0x00..0x03 HeaderPrefix stays zero (not decoded; spec).
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x04, 4), channel); // 0x04 ChannelSelector
        // 0x08..0x37 HeaderRest stays zero (not decoded; spec).

        body.CopyTo(p.Slice(CmsgChatChannelHeader.HeaderSize));
        return SendAsync(major: 3, minor: 21, payload, ct);
    }

    /// <summary>
    /// Builds a chat text body: <c>[u32 LE textLength][UTF-8 text][0x00]</c> where textLength includes
    /// the terminating NUL (spec: 2-7 / 3-21 length-prefix includes NUL). UTF-8 is PROVISIONAL (wire
    /// charset UNKNOWN per the specs).
    /// </summary>
    private static byte[] BuildLengthPrefixedText(string text)
    {
        int textByteCount = Encoding.UTF8.GetByteCount(text);
        uint lengthWithNul = checked((uint)(textByteCount + 1)); // +1 for the terminating NUL
        var body = new byte[sizeof(uint) + textByteCount + 1];
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0, 4), lengthWithNul);
        Encoding.UTF8.GetBytes(text, body.AsSpan(sizeof(uint), textByteCount));
        // trailing NUL already present (zeroed array).
        return body;
    }

    /// <summary>Writes <paramref name="value"/> as ASCII into a NUL-padded fixed buffer (truncating if too long).</summary>
    private static void WriteFixedAscii(string value, Span<byte> destination)
    {
        destination.Clear();
        int written = Encoding.ASCII.GetBytes(
            value.AsSpan(0, Math.Min(value.Length, destination.Length - 1)), destination);
        _ = written;
    }

    // -------------------------------------------------------------------------
    // Send
    // -------------------------------------------------------------------------

    private ValueTask SendAsync(ushort major, ushort minor, ReadOnlySpan<byte> payload, CancellationToken ct)
    {
        // The sink takes a ReadOnlyMemory that must outlive the send; copy the (often stack-allocated)
        // payload into an owning array. Per-send allocation is acceptable for these low-rate intents.
        var owned = payload.ToArray();
        return _outbound.SendAsync(_sessionId, major, minor, owned, ct);
    }

    private ValueTask SendAsync(ushort major, ushort minor, ReadOnlyMemory<byte> payload, CancellationToken ct) =>
        _outbound.SendAsync(_sessionId, major, minor, payload, ct);

    /// <summary>
    /// Sends an already-owning <paramref name="payload"/> array and completes with <paramref name="result"/>.
    /// Lets the validated-then-send use cases return their Domain result while the send goes through the
    /// sink. The array must outlive the send (the sink's <see cref="ReadOnlyMemory{T}"/> aliases it).
    /// </summary>
    private async ValueTask<T> SendResultAsync<T>(
        ushort major, ushort minor, byte[] payload, T result, CancellationToken ct)
    {
        await _outbound.SendAsync(_sessionId, major, minor, payload, ct).ConfigureAwait(false);
        return result;
    }
}