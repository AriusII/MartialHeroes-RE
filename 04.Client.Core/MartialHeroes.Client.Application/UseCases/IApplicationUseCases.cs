using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Domain.Skills;
using MartialHeroes.Network.Abstractions.Lobby;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.UseCases;

/// <summary>
/// The presentation-facing facade: input intents (UI -&gt; server). The Godot layer calls these to
/// translate player input into outbound requests. Outbound sends go through
/// <see cref="MartialHeroes.Network.Abstractions.Protocol.IOutboundPacketSink"/>; this layer never
/// touches transport, framing, or crypto. Each method serialises a Network.Protocol packet struct to
/// a plaintext payload and hands it (with its opcode) to the sink, which applies the cipher, LZ4, and
/// frame header downstream. spec: Docs/RE/opcodes.md (outbound frame header).
/// </summary>
public interface IApplicationUseCases
{
    /// <summary>
    /// Stages the login credential and drives the FSM toward handshaking. The actual auth reply (1/4)
    /// is built and sent later by the inbound handler when the server's 0/0 KeyExchange arrives.
    /// spec: Docs/RE/specs/crypto.md §6.1 (credential pre-staged at login-form time, before any 0/0).
    /// </summary>
    /// <param name="username">Account name. Staged for the flow; written into the 0x2B-prefixed
    /// plaintext pre-image of the secure 1/4 credential reply.</param>
    /// <param name="password">The login credential plaintext (RSA-encrypted later as the 1/4 reply M).</param>
    /// <param name="pin">
    /// The second-password / PIN collected after the primary submit (≤ 4 chars). It is the optional
    /// length-prefixed field of the secure 1/4 credential pre-image (NOT the password — the password
    /// rides the RSA half of that same 1/4 reply). There is no separate 1/6 login frame: the credential
    /// rides 1/4, and 1/6 is character-create only. spec: Docs/RE/specs/login_flow.md §1 step 1a / §4.2.
    /// </param>
    ValueTask LoginAsync(
        string username, string password, string? pin = null, CancellationToken cancellationToken = default);

    // -------------------------------------------------------------------------
    // Lobby orchestration (server-list fetch + channel-endpoint resolve)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fetches the server list from the lobby (port 10000) via the injected <see cref="ILobbyClient"/>,
    /// maps each decoded <see cref="LobbyServerRecord"/> into a presentation
    /// <see cref="ServerListEntryView"/> (attaching the load-color band + status-sentinel hints), and
    /// publishes a <see cref="ServerListReceivedEvent"/> so the ServerSelect screen can bind to the real
    /// lobby. Returns the decoded raw records for callers that want them directly. Requires a lobby
    /// client to be wired; throws <see cref="System.InvalidOperationException"/> otherwise. spec:
    /// Docs/RE/specs/login_flow.md §1 steps 2-3 / §2.1.
    /// </summary>
    /// <returns>The decoded raw <see cref="LobbyServerRecord"/> list (empty, never null).</returns>
    ValueTask<IReadOnlyList<LobbyServerRecord>> FetchServerListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the game-server endpoint for the selected <paramref name="serverId"/> via the lobby
    /// channel-endpoint query (port 10000 + serverId) on the injected <see cref="ILobbyClient"/>, and
    /// publishes a <see cref="ChannelEndpointResolvedEvent"/> (host + port). The caller hands that
    /// endpoint to the transport to open the persistent game connection. Returns the resolved endpoint.
    /// When an <c>ILastServerStore</c> is wired, persists <paramref name="serverId"/> as
    /// <c>Lastserver</c> (REG_DWORD) after a successful channel-endpoint fetch.
    /// Requires a lobby client to be wired; throws <see cref="System.InvalidOperationException"/>
    /// otherwise. spec: Docs/RE/specs/login_flow.md §1 step 4 / §2.2; §2.0 Registry note
    /// (Lastserver persisted on commit); frontend_layout_tables.md §2.2 sub-state 37.
    /// </summary>
    /// <param name="serverId">The chosen server id (1..40), added to 10000 to form the channel port.</param>
    /// <returns>The resolved game-server <see cref="LobbyChannelEndpoint"/> (host + port).</returns>
    ValueTask<LobbyChannelEndpoint> SelectServerAsync(
        ushort serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests that the local player move toward <paramref name="target"/> (Q16.16 logical position;
    /// world Y ignored, XZ-plane movement). Builds and sends 2/13 CmsgMoveRequest: Heading is the
    /// atan2 of (target - the local player's current position), TargetX/Z are the wire floats, and the
    /// run flag sets the move-mode region. spec: Docs/RE/packets/2-13_move_request.yaml.
    /// </summary>
    ValueTask RequestMoveAsync(Vector3Fixed target, bool running, CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects a character slot and requests enter-game. Enforces the slot-range guard (0..4) and, when a
    /// character-selection store is wired, confirms the slot against the cached 3/1 roster: an
    /// <b>"@BLANK@" empty slot routes to character creation</b> (publishes
    /// <see cref="Events.CreateCharacterRequestedEvent"/>, sends NO 1/9), an unoccupied/unknown slot
    /// sends nothing, and a real character is cached as the chosen descriptor (consumed by the 3/7
    /// spawn). For a real character it builds and sends 1/9 CmsgEnterGameRequest (slot index + the
    /// derived version token, <c>10 × versionField + 9</c> = 21149 for the sampled game.ver); the 3/5
    /// EnterGameAck later drives the scene machine to Load (state 2), and the 3/7 result spawns the
    /// local player. spec:
    /// Docs/RE/specs/login_flow.md §3.3 / §3.5 / §7; Docs/RE/packets/1-9_enter_game_request.yaml.
    /// </summary>
    /// <param name="slotIndex">The chosen character slot (0..4; out-of-range throws).</param>
    ValueTask SelectCharacterAsync(int slotIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the latest 3/1 character-list roster cached by the application, indexed by slot 0..4.
    /// Empty entries are <see langword="null"/>. spec: Docs/RE/packets/3-1_character_list.yaml.
    /// </summary>
    IReadOnlyList<CharacterSlotRecord?> GetCharacterRoster();

    /// <summary>
    /// Sends the select/view pre-enter request <c>1/7 {slot, 0}</c> for an occupied slot.
    /// spec: Docs/RE/packets/cmsg_char_select.yaml.
    /// </summary>
    ValueTask<bool> SelectCharacterSlotAsync(int slotIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates locally and sends <c>1/6 CmsgCreateCharacter</c> when valid.
    /// spec: Docs/RE/specs/frontend_scenes.md §4.4; Docs/RE/packets/cmsg_char_create.yaml.
    /// </summary>
    ValueTask<CharacterNameValidationResult> CreateCharacterAsync(
        CharacterCreateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the delete overload <c>1/7 {slot, 1}</c> for an occupied slot.
    /// spec: Docs/RE/specs/frontend_scenes.md §5; Docs/RE/packets/cmsg_char_select.yaml.
    /// </summary>
    ValueTask<bool> DeleteCharacterAsync(int slotIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates locally and sends <c>1/13 CmsgRenameCharacter</c> when valid.
    /// spec: Docs/RE/specs/frontend_scenes.md §6; Docs/RE/packets/cmsg_char_rename.yaml.
    /// </summary>
    ValueTask<CharacterNameValidationResult> RenameCharacterAsync(
        int slotIndex,
        string newName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests activation of a skill in <paramref name="slot"/> against the supplied target actor ids.
    /// Builds and sends 2/52 CmsgUseSkill: the 24-byte header plus the two count-prefixed u32 target-id
    /// arrays. Aim fields default to zero when the caller does not supply them. spec:
    /// Docs/RE/packets/2-52_use_skill.yaml.
    /// </summary>
    ValueTask UseSkillAsync(
        byte slot,
        ReadOnlyMemory<uint> targetsA = default,
        ReadOnlyMemory<uint> targetsB = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a chat message on <paramref name="channel"/>. All chat text is encoded as <b>CP949</b>
    /// (Korean, the on-wire charset for all game text — not UTF-8). A whisper (a non-empty recipient
    /// name) builds 2/7 CmsgWhisper; otherwise a general/channel send builds 3/21 CmsgChatChannel.
    /// Both are a fixed header followed by a length-prefixed CP949 text body. spec:
    /// Docs/RE/specs/chat.md §0 (all chat text is CP949); Docs/RE/packets/2-7_whisper.yaml;
    /// Docs/RE/packets/3-21_chat_channel.yaml.
    /// </summary>
    /// <param name="channel">Channel / scope selector (whisper ChannelType byte, or chat ChannelSelector).</param>
    /// <param name="text">Message text (CP949; length-prefix includes the terminating NUL).</param>
    /// <param name="recipientName">Whisper target name; empty/null routes a general channel send.</param>
    ValueTask SendChatAsync(
        uint channel,
        string text,
        string? recipientName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a skill cast through the full §2.1 gate chain (party/billing/busy/mounted/zone/stun/
    /// alive/action-lock/target/weapon/self-cast/cooldown/MP/range/LoS/window/targets) via the Domain
    /// <see cref="SkillCastValidator"/> + <see cref="Domain.Skills.CooldownTable"/>, and only on
    /// <see cref="SkillCastResult.Ok"/> sends the 2/52 CmsgUseSkill request. The cast state machine
    /// advances to <see cref="SkillCastPhase.Casting"/> on success. Returns the gate result so the caller
    /// can map a non-Ok code to UI feedback. <b>No packet is sent on a non-Ok result.</b> spec:
    /// Docs/RE/specs/skills.md §2 / §2.4; Docs/RE/packets/2-52_use_skill.yaml.
    /// </summary>
    /// <param name="slot">The hotbar slot to cast (0xFF = basic attack).</param>
    /// <param name="skill">The resolved skill definition (catalogue data) the gate chain reads.</param>
    /// <param name="caster">The caster gate inputs sampled from the local player's state.</param>
    /// <param name="targeting">The range / LoS / target-state port (Application/Assets implementation).</param>
    /// <param name="aimPoint">The aim point (XZ) for the range / LoS test.</param>
    /// <param name="nowMs">The caller-supplied millisecond clock for the cooldown tick.</param>
    /// <param name="targetsA">Ally / PC target id array (2/52 array A).</param>
    /// <param name="targetsB">Enemy / mob target id array (2/52 array B).</param>
    /// <returns>The cast result; <see cref="SkillCastResult.Ok"/> means the 2/52 request was sent.</returns>
    ValueTask<SkillCastResult> CastSkillAsync(
        byte slot,
        SkillDefinition skill,
        CasterState caster,
        ISkillTargetingQuery targeting,
        Vector3Fixed aimPoint,
        long nowMs,
        ReadOnlyMemory<uint> targetsA = default,
        ReadOnlyMemory<uint> targetsB = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests an equip / unequip / swap (or an in-bag slot-move, which rides the same opcode). Runs the
    /// client-side §4.2 gates via Domain <see cref="Domain.Inventory.EquipRules.CheckEquip"/> and only on
    /// <see cref="Domain.Inventory.EquipCheckResult.Allowed"/> sends the 12-byte 2/16 CmsgEquipChange.
    /// <b>No packet is sent when a gate blocks.</b> spec: Docs/RE/specs/inventory_trade.md §4.
    /// </summary>
    /// <param name="mode">0 = unequip, 1 = equip, 2 = equip-swap.</param>
    /// <param name="slot">The equipment slot index involved.</param>
    /// <param name="fromSub">Source slot / sub-index.</param>
    /// <param name="toSlot">Destination slot / sub-index (slot 8 triggers the relation guard).</param>
    /// <param name="sub">Extra sub-index (e.g. bag page).</param>
    /// <param name="itemIndex">The validated item / inventory index; must be ≥ 0.</param>
    /// <param name="state">The shared in-game / not-busy / not-dead predicate.</param>
    /// <param name="relation">The slot-8 relation-guard inputs (only consulted when toSlot == 8).</param>
    /// <returns>The §4.2 gate result; Allowed means the 2/16 request was sent.</returns>
    ValueTask<Domain.Inventory.EquipCheckResult> EquipItemAsync(
        byte mode,
        byte slot,
        byte fromSub,
        byte toSlot,
        byte sub,
        int itemIndex,
        Domain.Inventory.EquipStateGates state,
        Domain.Inventory.EquipRelationContext relation = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves an item between two inventory slots locally (split / merge / full move) via the Domain
    /// <see cref="Domain.Inventory.ItemStackOps"/> / <see cref="Domain.Inventory.InventoryGrid"/>, then —
    /// when the local mutation succeeds — sends the 2/16 slot-move request (the in-bag move rides the
    /// equip opcode; no dedicated split/merge opcode exists). Returns whether the local move applied.
    /// spec: Docs/RE/specs/inventory_trade.md §9 / §4.1.
    /// </summary>
    /// <param name="grid">The inventory grid to mutate.</param>
    /// <param name="fromIndex">Source slot index.</param>
    /// <param name="toIndex">Destination slot index.</param>
    /// <param name="quantity">Quantity to move (0 = full move; &gt; 0 and &lt; stack = split into an empty slot).</param>
    /// <returns>True when the local grid mutation applied and the 2/16 request was sent.</returns>
    ValueTask<bool> MoveItemAsync(
        Domain.Inventory.InventoryGrid grid,
        int fromIndex,
        int toIndex,
        uint quantity = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests a player-to-player trade with <paramref name="partnerActorId"/> via the Domain
    /// <see cref="Domain.Inventory.TradeSession"/> state machine, and on acceptance of the request edge
    /// sends the 8-byte 2/23 CmsgTradeRequest. Returns the next session state and whether the request was
    /// accepted by the state machine (a self-target or out-of-phase request sends nothing). spec:
    /// Docs/RE/specs/inventory_trade.md §8.1 / §8.3.
    /// </summary>
    /// <param name="session">The current trade session state (immutable).</param>
    /// <param name="partnerActorId">The trade-partner actor id (the 2/23 value field).</param>
    /// <param name="requestMode">The 2/23 mode byte (request vs accept vs decline vs cancel; enum UNVERIFIED).</param>
    /// <returns>The next session state and whether the request was accepted / sent.</returns>
    ValueTask<(Domain.Inventory.TradeSession Next, bool Accepted)> TradeRequestAsync(
        Domain.Inventory.TradeSession session,
        uint partnerActorId,
        byte requestMode = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a party / relation invite for <paramref name="targetActorId"/>: the 8-byte 2/35
    /// CmsgPartyOrRelationInvite (<c>[u8 sub-op][u32 target id]</c>). The self-target guard refuses an
    /// invite to the local player (returns false, sends nothing). spec: Docs/RE/specs/social.md §2.4.
    /// </summary>
    /// <param name="targetActorId">The actor to invite.</param>
    /// <param name="subOp">The invite sub-op byte.</param>
    /// <returns>True when the invite was sent (false on a self-target / zero-id guard).</returns>
    ValueTask<bool> PartyInviteAsync(
        uint targetActorId,
        byte subOp = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// In-world quit / logout: sends <c>1/0 CmsgLogout</c> — a header-only (8-byte) frame, NO payload.
    /// This is the LIGHTER of the two world-exit paths: fire-and-forget, it arms NO in-flight latch and
    /// does NOT disarm the keepalive toggle; the server is expected to drop the session. The scene side
    /// (converge on state 6 / sub-state 8) is owned by the scene machine. spec:
    /// Docs/RE/specs/world_exit.md §1 / §1.1; Docs/RE/packets/cmsg_logout.yaml.
    /// </summary>
    ValueTask LogoutAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Guarded leave-world: the HEAVIER world-exit path. It FIRST disarms the <c>2/112</c> keepalive
    /// toggle (sends <c>2/112</c> body 0x00 via the keepalive driver), THEN sends <c>2/0 CmsgLeaveWorld</c>
    /// — a header-only (8-byte) frame, NO payload. The disarm-before-send ordering is the defining
    /// difference from <see cref="LogoutAsync"/>. Both exit paths converge on scene state 6 / sub-state 8.
    /// spec: Docs/RE/specs/world_exit.md §1 / §1.2 / §2.
    /// </summary>
    ValueTask LeaveWorldAsync(CancellationToken cancellationToken = default);
}