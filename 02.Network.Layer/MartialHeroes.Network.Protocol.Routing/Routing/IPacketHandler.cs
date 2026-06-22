// spec: Docs/RE/opcodes.md — the inbound dispatch seam (major:minor -> handler).

using MartialHeroes.Network.Protocol.Packets.Login.Packets;
using MartialHeroes.Network.Protocol.Packets.World.Packets;

// spec: Docs/RE/specs/net_contracts.md §2.2 (binary wins — 3/23 is a 28-byte roster patch, not create-result).
// spec: Docs/RE/opcodes.md (0/0 hardwired branch; 3/1 and 3/4 share the 981-byte per-slot record format).
//
// DESIGN NOTE — variable-length opcodes 3/1 and 3/4:
//   SmsgCharacterListHeader and SmsgSceneEntityUpdate are variable-length packets (3-byte fixed header +
//   N×981-byte per-slot records). The source-generated typed-dispatch seam can only reinterpret the fixed
//   header (3 bytes), but the handlers for 3/1 and 3/4 MUST read the full payload to walk the per-slot
//   records via DecodeAndRetainRoster. Placing these in IPacketHandler typed dispatch would strip the
//   variable tail from the handler. They remain correctly in OnUnhandled where the full payload span is
//   available. spec: Docs/RE/packets/3-1_character_list.yaml; Docs/RE/packets/3-4_scene_entity_update.yaml.

namespace MartialHeroes.Network.Protocol.Routing.Routing;

/// <summary>
///     Sink for typed, zero-copy packet views produced by <see cref="PacketRouter" />. A higher layer
///     (Client.Application) implements this to react to decoded packets.
/// </summary>
/// <remarks>
///     Handlers receive frames as <c>ref readonly</c> struct views reinterpreted in place over the
///     frame buffer — no copies, no boxing, no allocation. Each method corresponds to one fully-specced
///     fixed-size packet. Unspecced opcodes are surfaced raw via <see cref="OnUnhandled" />.
///     <para>
///         Decision (Phase 4-E): keep both seams. <c>IPacketHandler</c> (Protocol-local, typed zero-copy
///         dispatch) and <c>IFrameSink</c> (<c>Network.Abstractions</c>, the transport→protocol byte boundary
///         reached via <c>OnFrame</c> for opcodes without a typed <c>Handle</c> overload) coexist by design.
///         Unifying would require a Protocol→Abstractions downward reference and moving the typed packet
///         structs; deferred indefinitely as unnecessary — the bridge is: transport calls
///         <c>IFrameSink.OnFrame</c> → the frame-sink implementation calls
///         <c>PacketRouter.Route(packedOpcode, payload, IPacketHandler)</c> → typed <c>Handle</c> or
///         <c>OnUnhandled</c>. No Protocol→Abstractions reference is being added.
///     </para>
/// </remarks>
public interface IPacketHandler
{
    // --- major 0: KeyExchange (hardwired S2C handshake branch) ---

    /// <summary>
    ///     0/0 — RSA key-exchange handshake (62 bytes). The first frame of the secure session. On receipt
    ///     the application must immediately reply with the C2S 1/4 credential send (via Network.Crypto).
    ///     spec: Docs/RE/packets/0-0_key_exchange.yaml; Docs/RE/specs/crypto.md §6.
    ///     NOTE: the original major-0 handler is a hardwired branch (NOT a switch). Routing here preserves
    ///     that semantic via the packed-opcode 0x0.
    /// </summary>
    void Handle(in SmsgKeyExchange packet); // spec: Docs/RE/opcodes.md row 0/0; packets/0-0_key_exchange.yaml

    // --- major 4: Response table-driven ---

    /// <summary>
    ///     4/1 — game-state tick and world-entry snapshot (9100-byte fixed body). Form 0 = periodic tick;
    ///     form 1 = world-entry seed (AreaId + SpawnX/Z; 3088+4044+1920-byte interior blocks).
    ///     Use <see cref="SmsgGameStateTick.TryReadWorldEntrySeed" /> to extract the world-entry seed.
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml; Docs/RE/specs/handlers.md §4/1.
    /// </summary>
    void Handle(in SmsgGameStateTick packet); // spec: Docs/RE/opcodes.md row 4/1; packets/4-1_game_state_tick.yaml

    /// <summary>
    ///     4/13 — local player state synchronization (56-byte fixed block). Gated to the local player
    ///     ((TargetSort, TargetId) must match the local actor). Reads X/Z (Y forced 0), the heading,
    ///     and the sync-mode byte at wire offset 33 (NOT 32; wire bytes 24..32 are an unconsumed
    ///     reserved gap — CONFLICT RESOLVED, binary wins). Mode 1 = BattleController sync; mode 5 =
    ///     no-state-write. Publishes <c>LocalPlayerStateSyncedEvent</c>.
    ///     spec: Docs/RE/opcodes.md row 4/13; Docs/RE/packets/4-13_local_player_state_sync.yaml.
    /// </summary>
    void
        Handle(in SmsgLocalPlayerStateSync packet); // spec: Docs/RE/opcodes.md row 4/13 (0x4000d); packets/4-13_local_player_state_sync.yaml

    // --- major 5: Push table-driven world events ---

    /// <summary>5/0 — actor despawn. spec: packets/5-0_char_despawn.yaml.</summary>
    void Handle(in SmsgCharDespawn packet);

    /// <summary>
    ///     5/10 — actor death push. The server announces that an actor has died and who killed it.
    ///     Resolves victim and killer by their composite (id, sort) keys, applies common death state
    ///     (clear locked battle target, zero combat resources, play death motion, clear buff array),
    ///     then switches on <see cref="SmsgCharDeath.DeathCause" /> (0 normal / 1 PK-A / 2 PK-B /
    ///     3 special-no-modal). Publishes <c>ActorDiedEvent</c>; layer 05 plays the death motion /
    ///     effect and opens the respawn modal off it.
    ///     spec: Docs/RE/opcodes.md row 5/10; Docs/RE/packets/5-10_combat_death.yaml.
    /// </summary>
    void Handle(in SmsgCharDeath packet); // spec: Docs/RE/opcodes.md row 5/10 (0x5000a); packets/5-10_combat_death.yaml

    /// <summary>3/5 — enter-world ack. spec: packets/3-5_enter_game_response.yaml.</summary>
    void Handle(in SmsgEnterGameAck packet);

    /// <summary>5/13 — actor movement update. spec: packets/5-13_actor_movement_update.yaml.</summary>
    void Handle(in SmsgActorMovementUpdate packet);

    /// <summary>5/3 — actor spawn. spec: packets/5-3_char_spawn.yaml.</summary>
    void Handle(in SmsgCharSpawn packet);

    /// <summary>5/53 — actor vitals push (HP/MP/stamina). spec: packets/5-53_actor_vitals_and_pair_state.yaml.</summary>
    void Handle(in SmsgActorVitalsAndPairState packet);

    /// <summary>5/1 — extended actor spawn (880-byte descriptor field). spec: packets/5-1_actor_spawn_extended.yaml.</summary>
    void Handle(in SmsgActorSpawnExtended packet);

    /// <summary>4/29 — stat-allocation ack. spec: packets/4-29_stat_update.yaml.</summary>
    void Handle(in SmsgStatUpdate packet);

    /// <summary>5/32 — level up. spec: packets/5-32_level_up.yaml.</summary>
    void Handle(in SmsgLevelUp packet);

    /// <summary>4/12 — equip/unequip result. spec: Docs/RE/specs/handlers.md §3 (4/12).</summary>
    void Handle(in SmsgEquipItemResult packet);

    /// <summary>4/22 — item-slot state ack. spec: Docs/RE/specs/handlers.md §13 Group B (4/22).</summary>
    void Handle(in SmsgItemSlotStateAck packet);

    /// <summary>4/19 — NPC buy / acquire ack. spec: Docs/RE/specs/handlers.md §13 Group B (4/19).</summary>
    void Handle(in SmsgNpcBuyOrAcquireAck packet);

    /// <summary>5/33 — skill-hotbar slot overwrite. spec: Docs/RE/specs/handlers.md §4 (5/33).</summary>
    void Handle(in SmsgSkillHotbarSlotSet packet);

    /// <summary>4/41 — skill-hotbar assign result. spec: Docs/RE/specs/handlers.md §13 Group C (4/41).</summary>
    void Handle(in SmsgSkillHotbarAssignResult packet);

    /// <summary>4/102 — full skill/state-window snapshot (30 buff records). spec: packets/4-102_buff_state.yaml.</summary>
    void Handle(in SmsgSkillWindowStateUpdate packet);

    /// <summary>3/14 — enter-game spawn result (local player). spec: Docs/RE/specs/login_flow.md §5.3.</summary>
    void Handle(in SmsgCharSpawnResult packet);

    /// <summary>3/7 — char manage / delete result. spec: Docs/RE/specs/login_flow.md §5.5.</summary>
    void Handle(in SmsgCharManageResult packet);

    /// <summary>3/6 — rename-character result. spec: packets/3-6_rename_char_result.yaml.</summary>
    void Handle(in SmsgRenameCharResult packet);

    /// <summary>
    ///     3/23 — select-screen character level and status update (28 bytes).
    ///     Binary-confirmed (Phase 2b, build 263bd994): 3/23 is NOT a 12-byte create-result — it is a
    ///     28-byte by-name roster patch. Create is acked via 3/7 + a refreshed 3/4 list.
    ///     spec: Docs/RE/packets/3-23_char_select_status_update.yaml;
    ///     Docs/RE/specs/net_contracts.md §2.2.
    /// </summary>
    void Handle(in SmsgCharStatusBytesByName packet);

    /// <summary>3/100 — generic char-management action/result code. spec: Docs/RE/opcodes.md (3/100).</summary>
    void Handle(in SmsgCharActionResult packet);

    /// <summary>
    ///     Any opcode without a specced fixed-size struct. The router passes the raw payload span
    ///     (zero-copy) and the packed opcode so the application can route it itself or drop it.
    /// </summary>
    void OnUnhandled(uint packedOpcode, ReadOnlySpan<byte> payload);
}