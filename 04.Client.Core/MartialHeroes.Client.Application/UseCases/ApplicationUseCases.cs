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
using MartialHeroes.Client.Domain.Social;
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
/// (<c>sample_verified</c> from the real <c>game.ver</c>). spec: login_flow.md §3.3 / §7.
/// <see cref="LoginAsync"/> only STAGES the credential and emits no outbound frame: the credential
/// rides the secure 1/4 reply built later by the login handshake driver (1/6 is character-create only).
/// spec: login_flow.md §4.2. The chat text charset is CP949 per the chat specs; the body bytes here are
/// still UTF-8 (PROVISIONAL — ASCII round-trips identically).
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

    /// <summary>The 1/9 version-token length: a fixed 33-byte buffer. spec: 1-9_enter_game_request.yaml.</summary>
    public const int VersionTokenLength = 33;

    /// <summary>
    /// CP949 (code page 949 / EUC-KR) — the on-wire charset for ALL chat text (both the say-box body and
    /// the whisper target name). The code-pages provider is registered once here (idempotent) so the
    /// chat builders never emit UTF-8/ASCII bytes for Korean text. spec: Docs/RE/specs/chat.md §0 / §8.2
    /// (all chat text is CP949); packets/2-7_whisper.yaml (CP949 text + NUL-padded CP949 target name).
    /// </summary>
    private static readonly Encoding Cp949 = CreateCp949();

    private static Encoding CreateCp949()
    {
        // spec: Docs/RE/specs/chat.md §0 — register the code-pages provider once; CP949 is not built-in.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949); // spec: Docs/RE/specs/chat.md §0 (code page 949 / EUC-KR)
    }

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
    /// detect "@BLANK@" and cache the chosen descriptor for the 3/14 spawn. spec: login_flow.md §3.5.
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
        ILobbyClient? lobbyClient = null)
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

        // Stage the credential (account bytes, 17-byte zero-padded M, optional PIN) before any 0/0
        // arrives. The credential — account + optional PIN (its 0x2B-prefixed plaintext pre-image) plus
        // the RSA-encrypted password — rides the SECURE 1/4 reply, built later by LoginHandshakeDriver
        // when the server's KeyExchange lands. There is NO separate 1/6 login frame: the prior "1/6
        // login blob" attribution was a false premise — 1/6 is character-create only. spec:
        // Docs/RE/specs/login_flow.md §4.2 (RESOLVED); Docs/RE/specs/crypto.md §6.1, §6.6;
        // packets/login.yaml (CmsgLoginCredential).
        _credentials.Stage(username, password, pin);

        // Drive the FSM from Login toward handshaking. CharacterSelection is the application-state
        // analogue of "secure session established / awaiting auth result".
        _stateMachine.OnAuthenticated();

        // No outbound frame here: the handshake proceeds from the inbound 0/0 KeyExchange, which the
        // driver answers with the secure 1/4 reply carrying the staged credential. spec: login_flow.md §4.2.
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

        // Map each decoded record to a presentation view, attaching the population-color band + caption
        // branch hints so the ServerSelect screen does not re-derive the spec constants. The availability
        // gate is ServerId == 100 (wire +0), the caption branch is Status (wire +2), the population band
        // comes from Population (wire +4) gated by the Flag mode (wire +6).
        // spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE A.
        var builder = ImmutableArray.CreateBuilder<ServerListEntryView>(records.Count);
        foreach (LobbyServerRecord r in records)
        {
            builder.Add(new ServerListEntryView(
                r.ServerId, r.Status, r.Population, r.Flag,
                ClassifyPopulation(r.Population, r.Flag), ClassifyStatus(r.ServerId, r.Status)));
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
        // re-check the range, and on a real character cache the chosen descriptor for the 3/14 spawn.
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

        // EVERY everyday say-box channel (say 0 / shout 1 / party 2 / guild 3 / event 6 / special 7 /
        // alliance 15) AND whisper (channel 9) is sent through the SINGLE chat sender as opcode (2:7),
        // with the channel code as the first payload byte and a 17-byte target-name area that is filled
        // only for whisper and zeroed otherwise. There is NO say-box (2:7)-vs-(3:21) split: (3:21) is a
        // separate chat-command dispatcher path, not the say box. spec: Docs/RE/specs/chat.md §4.1;
        // packets/2-7_whisper.yaml (uniform 19-byte prefix).
        bool isWhisper = !string.IsNullOrEmpty(recipientName) || channel == WhisperChannelCode;

        // Domain gate: whisper self-target guard + per-channel text-length caps (whisper 119, everyday
        // chat < 200). The text caps are CHARACTER caps in the editbox model. spec: chat.md §2.3 / §8.2;
        // social.md §3 / §4 / §8. A non-Send result aborts before any frame is built.
        ChatChannel routeChannel = isWhisper ? ChatChannel.Whisper : ChatChannel.Channel;
        ChatRouteResult route = ChatRouting.Validate(
            routeChannel,
            text.Length,
            // Self-target resolution is by character name, which this layer cannot resolve (no
            // name->actor-id map below layer 04); the server still validates. Pass false. spec: social.md §1/§3.
            isSelfTarget: false,
            // The everyday say-box (2:7) is NOT the (3:21) broadcast path, so no selector bypass applies;
            // pass a non-broadcast selector so the < 200 gate is honoured. spec: chat.md §4.1; social.md §4.
            channelSelector: 0);
        if (route != ChatRouteResult.Send)
        {
            // Gate blocked (self-whisper / empty / too long) -> send nothing. spec: social.md §8.
            return ValueTask.CompletedTask;
        }

        return SendChat27Async(channel, isWhisper ? recipientName : null, text, cancellationToken);
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

    // -------------------------------------------------------------------------
    // Lobby presentation classification
    // -------------------------------------------------------------------------
    // NOTE: the former feature-flagged 1/6 "login blob" builder was removed — the 1/6-vs-login-credential
    // collision is RESOLVED: the credential (0x2B pre-image + RSA password) rides the secure 1/4 reply
    // built by LoginHandshakeReply / LoginCredentialReply, and 1/6 is CmsgCreateCharacter only.
    // spec: Docs/RE/specs/login_flow.md §4.2.

    /// <summary>
    /// Classifies a lobby server's population into a presentation color band. In numeric mode
    /// (<paramref name="flag"/> nonzero) the population is thresholded against 500 / 800 / 1200
    /// (strict greater-than); in discrete mode (<paramref name="flag"/> == 0) the population carries a
    /// discrete load level (4 = Full, 3 = Busy, 2 = Moderate, else Light). Presentation-only; not a wire
    /// decode. spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE A (POPULATION caption mapping when
    /// status_kind == 0).
    /// </summary>
    private static ServerLoadBand ClassifyPopulation(short population, short flag) => flag != 0
        ? population switch
        {
            > 1200 => ServerLoadBand.Full, // spec: §RECORD SHAPE A +4 > 1200 -> 6001
            > 800 => ServerLoadBand.Busy, // spec: §RECORD SHAPE A +4 > 800 -> 6002
            > 500 => ServerLoadBand.Moderate, // spec: §RECORD SHAPE A +4 > 500 -> 6003
            _ => ServerLoadBand.Light, // default tier
        }
        : population switch
        {
            4 => ServerLoadBand.Full, // spec: §RECORD SHAPE A discrete +4 == 4 -> 6001
            3 => ServerLoadBand.Busy, // spec: §RECORD SHAPE A discrete +4 == 3 -> 6002
            2 => ServerLoadBand.Moderate, // spec: §RECORD SHAPE A discrete +4 == 2 -> 6003
            _ => ServerLoadBand.Light, // default tier
        };

    /// <summary>
    /// Classifies a lobby server's caption branch + availability for presentation. The AVAILABILITY gate
    /// is <paramref name="serverId"/> == 100 (wire +0), NOT a status value. The caption branch comes from
    /// <paramref name="status"/> (wire +2, <c>status_kind</c>): 0 = numeric/discrete population, 3 =
    /// special (6004/6005), 1..39 = caption array, else = fallback 5901. Presentation-only; not a wire
    /// decode. spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE A.
    /// </summary>
    private static ServerStatusHint ClassifyStatus(ushort serverId, short status)
    {
        // Availability sentinel is on the id/select-key (+0), not status_kind (+2).
        // spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE A, offset +0 [CODE-CONFIRMED].
        if (serverId == 100)
        {
            return ServerStatusHint.Available;
        }

        return status switch
        {
            0 => ServerStatusHint.Normal, // spec: §RECORD SHAPE A status_kind == 0 (population branch)
            3 => ServerStatusHint.Special, // spec: §RECORD SHAPE A status_kind == 3 (6004/6005)
            >= 1 and <= 39 => ServerStatusHint.Caption, // spec: §RECORD SHAPE A 1..39 caption array
            _ => ServerStatusHint.Invalid, // spec: §RECORD SHAPE A < 1 or > 39 -> 5901 fallback
        };
    }

    // -------------------------------------------------------------------------
    // Chat helpers
    // -------------------------------------------------------------------------

    /// <summary>The whisper channel code (named private channel). spec: Docs/RE/specs/chat.md §2.3 / §3 (channel 9).</summary>
    private const byte WhisperChannelCode = 9;

    /// <summary>The whisper text byte cap on the CP949 body (the 0x77 strncpy cap). spec: 2-7_whisper.yaml (119 bytes).</summary>
    private const int WhisperBodyByteCap = 119;

    /// <summary>The fixed 16-byte whisper target-name buffer at payload +0x02. spec: 2-7_whisper.yaml (TargetName).</summary>
    private const int WhisperTargetNameBytes = 16;

    /// <summary>
    /// Builds and sends the single (2:7) <c>CmsgChat</c> message that carries EVERY everyday say-box
    /// channel and whisper. The wire form is a UNIFORM 19-byte prefix — channel code at +0x00, a flag
    /// byte at +0x01, a 16-byte CP949 target-name area at +0x02 (NUL-padded; filled only for whisper),
    /// and a trailing header byte at +0x12 — followed by a length-prefixed CP949 text body whose u32
    /// length EXCLUDES the terminating NUL (built from the byte count, not byte-count + 1; this DIFFERS
    /// from (3:21), whose prefix includes the NUL). spec: Docs/RE/specs/chat.md §4.1 / §4.2;
    /// packets/2-7_whisper.yaml.
    /// </summary>
    private ValueTask SendChat27Async(uint channel, string? recipientName, string text, CancellationToken ct)
    {
        // Text body: CP949 bytes, hard-capped at 119 bytes for whisper (the 0x77 send-site strncpy cap).
        // spec: 2-7_whisper.yaml (TEXT BODY; strncpy cap 0x77 = 119). All chat text is CP949 (Korean
        // codepage), never a managed UTF-16/ASCII string on the wire. spec: chat.md §0 / §8.2.
        bool isWhisper = !string.IsNullOrEmpty(recipientName) || channel == WhisperChannelCode;
        int bodyByteCount = Cp949.GetByteCount(text);
        if (isWhisper && bodyByteCount > WhisperBodyByteCap)
        {
            bodyByteCount = TruncateCp949(text, WhisperBodyByteCap, out _);
        }

        var payload = new byte[CmsgWhisperHeader.HeaderSize + sizeof(uint) + bodyByteCount + 1];
        Span<byte> p = payload;
        p.Clear();

        p[0x00] = (byte)channel; // 0x00 Channel (channel code; low byte). spec: 2-7_whisper.yaml.
        // 0x01 Selector / Flag stays zero (meaning capture-pending). spec: 2-7_whisper.yaml.
        // 0x02 TargetName: 16-byte CP949 name area, NUL-padded; filled ONLY for whisper, zero otherwise.
        // spec: chat.md §4.1 ("fill the 16-byte target name only for whisper"); 2-7_whisper.yaml.
        if (isWhisper && recipientName is { Length: > 0 })
        {
            WriteFixedCp949(recipientName, p.Slice(0x02, WhisperTargetNameBytes));
        }

        // 0x12 trailing header byte stays zero (completes the 19-byte prefix). spec: 2-7_whisper.yaml.

        // Text tail at +0x13: [u32 textLength EXCLUDING the NUL][CP949 text bytes][NUL]. spec: chat.md §4.2.
        Span<byte> tail = p.Slice(CmsgWhisperHeader.HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(tail.Slice(0, sizeof(uint)), (uint)bodyByteCount);
        Cp949.GetBytes(text.AsSpan(0, Cp949CharCountForBytes(text, bodyByteCount)),
            tail.Slice(sizeof(uint), bodyByteCount));
        // The trailing NUL after the body is already present (zeroed array). spec: 2-7_whisper.yaml.

        return SendAsync(major: 2, minor: 7, payload, ct);
    }

    /// <summary>
    /// Writes <paramref name="value"/> as CP949 bytes into a NUL-padded fixed buffer, never overrunning
    /// and always leaving at least one NUL terminator (the legacy fixed-buffer name convention). spec:
    /// 2-7_whisper.yaml (TargetName: CP949, NUL-padded, strncpy-capped at 16 bytes).
    /// </summary>
    private static void WriteFixedCp949(string value, Span<byte> destination)
    {
        destination.Clear();
        // Cap the CP949 byte run at destination.Length - 1 so a NUL terminator always remains, and never
        // split a 2-byte CP949 glyph across the cap. spec: chat.md §0 (CP949 lead-byte aware).
        int charCount = Cp949CharCountForBytes(value, destination.Length - 1);
        if (charCount > 0)
        {
            Cp949.GetBytes(value.AsSpan(0, charCount), destination);
        }
    }

    /// <summary>
    /// Truncates <paramref name="text"/> to at most <paramref name="byteCap"/> CP949 bytes without
    /// splitting a multi-byte glyph, returning the resulting CP949 byte count. spec: 2-7_whisper.yaml
    /// (whisper body capped at 119 bytes); chat.md §0 (CP949 lead-byte aware caret/wrap).
    /// </summary>
    private static int TruncateCp949(string text, int byteCap, out int charCount)
    {
        charCount = Cp949CharCountForBytes(text, byteCap);
        return Cp949.GetByteCount(text.AsSpan(0, charCount));
    }

    /// <summary>
    /// Returns the largest prefix character count of <paramref name="text"/> whose CP949 encoding fits in
    /// <paramref name="byteCap"/> bytes, never splitting a 2-byte CP949 glyph. spec: chat.md §0.
    /// </summary>
    private static int Cp949CharCountForBytes(string text, int byteCap)
    {
        if (byteCap <= 0)
        {
            return 0;
        }

        int total = Cp949.GetByteCount(text);
        if (total <= byteCap)
        {
            return text.Length;
        }

        // Grow the prefix char-by-char until the next char would overflow the byte cap.
        int chars = 0;
        int bytes = 0;
        while (chars < text.Length)
        {
            int next = Cp949.GetByteCount(text.AsSpan(chars, 1));
            if (bytes + next > byteCap)
            {
                break;
            }

            bytes += next;
            chars++;
        }

        return chars;
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