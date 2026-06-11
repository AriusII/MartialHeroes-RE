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
    /// <param name="username">Account name. PROVISIONAL: the pre-0/0 username send (1/6) is not fully
    /// specced; staged here for the flow but no 1/6 frame is emitted until that layout is recovered.</param>
    /// <param name="password">The login credential plaintext (RSA-encrypted later as the 1/4 reply M).</param>
    ValueTask LoginAsync(string username, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests that the local player move toward <paramref name="target"/> (Q16.16 logical position;
    /// world Y ignored, XZ-plane movement). Builds and sends 2/13 CmsgMoveRequest: Heading is the
    /// atan2 of (target - the local player's current position), TargetX/Z are the wire floats, and the
    /// run flag sets the move-mode region. spec: Docs/RE/packets/2-13_move_request.yaml.
    /// </summary>
    ValueTask RequestMoveAsync(Vector3Fixed target, bool running, CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects a character slot and requests enter-game. Builds and sends 1/9 CmsgEnterGameRequest
    /// (slot index + version token), and drives the FSM toward <see cref="Events.ClientState.Loading"/>;
    /// the 3/5 ack later completes the transition to World. spec:
    /// Docs/RE/packets/1-9_enter_game_request.yaml.
    /// </summary>
    ValueTask SelectCharacterAsync(int slotIndex, CancellationToken cancellationToken = default);

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
    /// Sends a chat message on <paramref name="channel"/>. A whisper (a non-empty recipient name)
    /// builds 2/7 CmsgWhisper; otherwise a general/channel send builds 3/21 CmsgChatChannel. Both are
    /// a fixed header followed by a length-prefixed UTF-8 text body. spec:
    /// Docs/RE/packets/2-7_whisper.yaml; Docs/RE/packets/3-21_chat_channel.yaml.
    /// </summary>
    /// <param name="channel">Channel / scope selector (whisper ChannelType byte, or chat ChannelSelector).</param>
    /// <param name="text">Message text (UTF-8; length-prefix includes the terminating NUL).</param>
    /// <param name="recipientName">Whisper target name; empty/null routes a general channel send.</param>
    ValueTask SendChatAsync(
        uint channel,
        string text,
        string? recipientName = null,
        CancellationToken cancellationToken = default);
}