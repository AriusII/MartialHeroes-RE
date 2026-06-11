using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.UseCases;

/// <summary>
/// The presentation-facing facade: input intents (UI -&gt; server). The Godot layer calls these to
/// translate player input into outbound requests. Outbound sends go through
/// <see cref="MartialHeroes.Network.Abstractions.Protocol.IOutboundPacketSink"/>; this layer never
/// touches transport, framing, or crypto.
/// </summary>
public interface IApplicationUseCases
{
    /// <summary>
    /// Requests that the local player move toward <paramref name="target"/> (Q16.16 logical
    /// position; world Y ignored, XZ-plane movement). Converts back to wire floats at this boundary.
    /// </summary>
    ValueTask RequestMoveAsync(Vector3Fixed target, bool running, CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects a character slot and requests enter-game. Drives the FSM toward
    /// <see cref="Events.ClientState.Loading"/>; the 3/5 ack later completes the transition to World.
    /// </summary>
    ValueTask SelectCharacterAsync(int slotIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests activation of a skill against an optional target. spec:
    /// Docs/RE/packets/2-52_use_skill.yaml — <b>INCOMPLETE</b> (C2S send-site not analyzed); the wire
    /// layout is a hypothesis only, so this is a stub and must not emit a fabricated frame.
    /// </summary>
    ValueTask UseSkillAsync(uint skillId, uint targetId, CancellationToken cancellationToken = default);
}
