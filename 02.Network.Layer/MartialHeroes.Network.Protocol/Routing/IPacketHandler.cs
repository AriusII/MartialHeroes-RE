// spec: Docs/RE/opcodes.md — the inbound dispatch seam (major:minor -> handler).

namespace MartialHeroes.Network.Protocol.Routing;

/// <summary>
/// Sink for typed, zero-copy packet views produced by <see cref="PacketRouter"/>. A higher layer
/// (Client.Application) implements this to react to decoded packets.
/// </summary>
/// <remarks>
/// Handlers receive frames as <c>ref readonly</c> struct views reinterpreted in place over the
/// frame buffer — no copies, no boxing, no allocation. Each method corresponds to one fully-specced
/// fixed-size packet. Unspecced opcodes are surfaced raw via <see cref="OnUnhandled"/>.
/// <para>
/// Decision (Phase 4-E): keep both seams. <c>IPacketHandler</c> (Protocol-local, typed zero-copy
/// dispatch) and <c>IFrameSink</c> (<c>Network.Abstractions</c>, the transport→protocol byte boundary
/// reached via <c>OnFrame</c> for opcodes without a typed <c>Handle</c> overload) coexist by design.
/// Unifying would require a Protocol→Abstractions downward reference and moving the typed packet
/// structs; deferred indefinitely as unnecessary — the bridge is: transport calls
/// <c>IFrameSink.OnFrame</c> → the frame-sink implementation calls
/// <c>PacketRouter.Route(packedOpcode, payload, IPacketHandler)</c> → typed <c>Handle</c> or
/// <c>OnUnhandled</c>. No Protocol→Abstractions reference is being added.
/// </para>
/// </remarks>
public interface IPacketHandler
{
    /// <summary>5/0 — actor despawn. spec: packets/5-0_char_despawn.yaml.</summary>
    void Handle(in Packets.SmsgCharDespawn packet);

    /// <summary>3/5 — enter-world ack. spec: packets/3-5_enter_game_response.yaml.</summary>
    void Handle(in Packets.SmsgEnterGameAck packet);

    /// <summary>5/13 — actor movement update. spec: packets/5-13_actor_movement_update.yaml.</summary>
    void Handle(in Packets.SmsgActorMovementUpdate packet);

    /// <summary>5/3 — actor spawn. spec: packets/5-3_char_spawn.yaml.</summary>
    void Handle(in Packets.SmsgCharSpawn packet);

    /// <summary>5/53 — actor vitals push (HP/MP/stamina). spec: packets/5-53_actor_vitals_and_pair_state.yaml.</summary>
    void Handle(in Packets.SmsgActorVitalsAndPairState packet);

    /// <summary>5/1 — extended actor spawn (880-byte descriptor field). spec: packets/5-1_actor_spawn_extended.yaml.</summary>
    void Handle(in Packets.SmsgActorSpawnExtended packet);

    /// <summary>4/29 — stat-allocation ack. spec: packets/4-29_stat_update.yaml.</summary>
    void Handle(in Packets.SmsgStatUpdate packet);

    /// <summary>5/32 — level up. spec: packets/5-32_level_up.yaml.</summary>
    void Handle(in Packets.SmsgLevelUp packet);

    /// <summary>4/12 — equip/unequip result. spec: Docs/RE/specs/handlers.md §3 (4/12).</summary>
    void Handle(in Packets.SmsgEquipItemResult packet);

    /// <summary>4/22 — item-slot state ack. spec: Docs/RE/specs/handlers.md §13 Group B (4/22).</summary>
    void Handle(in Packets.SmsgItemSlotStateAck packet);

    /// <summary>4/19 — NPC buy / acquire ack. spec: Docs/RE/specs/handlers.md §13 Group B (4/19).</summary>
    void Handle(in Packets.SmsgNpcBuyOrAcquireAck packet);

    /// <summary>5/33 — skill-hotbar slot overwrite. spec: Docs/RE/specs/handlers.md §4 (5/33).</summary>
    void Handle(in Packets.SmsgSkillHotbarSlotSet packet);

    /// <summary>4/41 — skill-hotbar assign result. spec: Docs/RE/specs/handlers.md §13 Group C (4/41).</summary>
    void Handle(in Packets.SmsgSkillHotbarAssignResult packet);

    /// <summary>4/102 — full skill/state-window snapshot (30 buff records). spec: packets/4-102_buff_state.yaml.</summary>
    void Handle(in Packets.SmsgSkillWindowStateUpdate packet);

    /// <summary>3/14 — enter-game spawn result (local player). spec: Docs/RE/specs/login_flow.md §5.3.</summary>
    void Handle(in Packets.SmsgCharSpawnResult packet);

    /// <summary>3/7 — char manage / delete result. spec: Docs/RE/specs/login_flow.md §5.5.</summary>
    void Handle(in Packets.SmsgCharManageResult packet);

    /// <summary>3/6 — rename-character result. spec: packets/3-6_rename_char_result.yaml.</summary>
    void Handle(in Packets.SmsgRenameCharResult packet);

    /// <summary>3/23 — character-create result. spec: Docs/RE/specs/login_flow.md §5.4.</summary>
    void Handle(in Packets.SmsgCharCreateResult packet);

    /// <summary>3/100 — generic char-management action/result code. spec: Docs/RE/opcodes.md (3/100).</summary>
    void Handle(in Packets.SmsgCharActionResult packet);

    /// <summary>
    /// Any opcode without a specced fixed-size struct. The router passes the raw payload span
    /// (zero-copy) and the packed opcode so the application can route it itself or drop it.
    /// </summary>
    void OnUnhandled(uint packedOpcode, ReadOnlySpan<byte> payload);
}
