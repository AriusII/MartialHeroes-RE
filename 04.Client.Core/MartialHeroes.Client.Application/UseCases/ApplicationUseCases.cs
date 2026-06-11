using MartialHeroes.Client.Application.StateMachine;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.UseCases;

/// <summary>
/// Default <see cref="IApplicationUseCases"/>. Translates presentation intents into FSM transitions
/// and outbound requests via the injected
/// <see cref="IOutboundPacketSink"/>. No transport/framing/crypto here — the sink prepends the frame
/// header and encrypts. spec: Docs/RE/opcodes.md (outbound frame header).
/// </summary>
/// <remarks>
/// <para>
/// <b>Capture-gap honesty.</b> The catalog confirms major 2 (GameAction) is C2S-only, but no C2S
/// send-site field layout has been recovered for movement or skill use (spec: Docs/RE/opcodes.md
/// "no inbound handler exists"; Docs/RE/packets/2-52_use_skill.yaml status: hypothesis, INCOMPLETE).
/// Rather than fabricate wire layouts, the outbound intents that need an unspecced C2S frame are
/// stubbed with explicit TODOs and do NOT emit a frame. The
/// <see cref="SelectCharacterAsync"/> intent only drives the FSM (deterministic, no wire layout
/// required) until its C2S request is specced.
/// </para>
/// </remarks>
public sealed class ApplicationUseCases : IApplicationUseCases
{
    private readonly IOutboundPacketSink _outbound;
    private readonly ClientStateMachine _stateMachine;
    private readonly SessionId _sessionId;

    public ApplicationUseCases(
        IOutboundPacketSink outbound,
        ClientStateMachine stateMachine,
        SessionId sessionId)
    {
        _outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _sessionId = sessionId;
    }

    /// <inheritdoc />
    public ValueTask RequestMoveAsync(
        Vector3Fixed target, bool running, CancellationToken cancellationToken = default)
    {
        // Boundary conversion back to wire floats would happen here (target.ToVector3Float()).
        // TODO(capture-gap): the C2S movement-request frame layout (major 2 GameAction) was not
        // recovered — no C2S send-site spec exists under Docs/RE/packets/. Do NOT fabricate a frame.
        // Once a Docs/RE/packets/<major>-<minor>_move_request.yaml is published, serialise the XZ
        // floats + run flag into a pooled buffer and call _outbound.SendAsync(...). Until then this
        // is a no-op so we never put a guessed layout on the wire.
        _ = target;
        _ = running;
        _ = _sessionId;
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask SelectCharacterAsync(int slotIndex, CancellationToken cancellationToken = default)
    {
        if (slotIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "Slot index must be >= 0.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Drive the deterministic lifecycle toward Loading; the 3/5 enter-game ack later transitions
        // to World (handled by GamePacketHandler). spec: Docs/RE/opcodes.md (3/5 SmsgEnterGameAck).
        _stateMachine.OnCharacterSelected();

        // TODO(capture-gap): emit the C2S enter-game/select-character request once its frame layout
        // is specced under Docs/RE/packets/. major 2 (GameAction) is C2S-only and no send-site spec
        // exists yet; do NOT fabricate it. The FSM transition above is deterministic and testable
        // without the wire frame.
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask UseSkillAsync(uint skillId, uint targetId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // TODO(capture-gap): 2/52 CmsgUseSkill is the weakest spec in the set — the C2S send-site
        // (UI input -> PACKETBUF build) was never analyzed, so the field layout/size is unknown.
        // spec: Docs/RE/packets/2-52_use_skill.yaml (status: hypothesis, ANALYSIS INCOMPLETE).
        // Deliberately NOT serialising a struct: a fabricated layout would violate the clean-room
        // discipline. When a capture or a promoted send-site note lands, build the payload here and
        // call _outbound.SendAsync(_sessionId, major: 2, minor: 52, payload, cancellationToken).
        _ = skillId;
        _ = targetId;
        return ValueTask.CompletedTask;
    }
}
