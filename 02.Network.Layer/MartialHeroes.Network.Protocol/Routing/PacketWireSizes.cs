// spec: Docs/RE/opcodes.md + the per-packet specs — compile-time opcode → struct wire/header size.
//
// Reflection-free. This is an explicit `switch` (no Dictionary, no Activator, no Type.GetType) that
// maps each opcode this project models to the byte size of its Pack=1 struct: the full WireSize for
// fixed-size packets, or the fixed HeaderSize for variable-length packets (whose tail the caller
// hand-codes). It complements the IPacketHandler dispatch in PacketRouter without changing it: a
// consumer can size-check a payload before reinterpreting it via TypedPacketView, additively.

using MartialHeroes.Network.Protocol.Opcodes;
using MartialHeroes.Network.Protocol.Packets;

namespace MartialHeroes.Network.Protocol.Routing;

/// <summary>
/// Compile-time map from a packed opcode to the byte size of its modelled wire struct. Returns the
/// fixed <c>WireSize</c> for fixed-size packets and the fixed <c>HeaderSize</c> for variable-length
/// packets (those carry an additional hand-coded tail). spec: Docs/RE/opcodes.md and the per-packet
/// specs cited on each arm.
/// </summary>
public static class PacketWireSizes
{
    /// <summary>
    /// Returns <see langword="true"/> and the modelled struct size for a known opcode; otherwise
    /// <see langword="false"/>. Reflection-free explicit switch. spec: Docs/RE/opcodes.md.
    /// </summary>
    public static bool TryGet(uint packedOpcode, out int size, out bool isVariableLength)
    {
        switch (packedOpcode)
        {
            // --- fixed-size S2C packets ---
            case Opcodes.Opcodes.SmsgCharDespawn: // spec: packets/5-0_char_despawn.yaml
                size = SmsgCharDespawn.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.Opcodes.SmsgEnterGameAck: // spec: packets/3-5_enter_game_response.yaml
                size = SmsgEnterGameAck.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.Opcodes.SmsgActorMovementUpdate: // spec: packets/5-13_actor_movement_update.yaml
                size = SmsgActorMovementUpdate.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.Opcodes.SmsgCharSpawn: // spec: packets/5-3_char_spawn.yaml
                size = SmsgCharSpawn.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.Opcodes.SmsgActorSpawnExtended: // spec: packets/5-1_actor_spawn_extended.yaml
                size = SmsgActorSpawnExtended.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.Opcodes.SmsgActorVitalsAndPairState: // spec: packets/5-53_actor_vitals_and_pair_state.yaml
                size = SmsgActorVitalsAndPairState.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.Opcodes.SmsgLevelUp: // spec: packets/5-32_level_up.yaml
                size = SmsgLevelUp.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.Opcodes.SmsgStatUpdate: // spec: packets/4-29_stat_update.yaml
                size = SmsgStatUpdate.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.Opcodes.SmsgEquipItemResult: // spec: Docs/RE/structs/item.md (EquipItemResult, 4/12)
                size = SmsgEquipItemResult.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.Opcodes.SmsgItemSlotStateAck: // spec: Docs/RE/structs/item.md (EquipSlotBody, 4/22)
                size = SmsgItemSlotStateAck.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.Opcodes.SmsgNpcBuyOrAcquireAck: // spec: Docs/RE/structs/item.md (NpcBuy ack, 4/19)
                size = SmsgNpcBuyOrAcquireAck.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.Opcodes.SmsgSkillHotbarSlotSet: // spec: Docs/RE/structs/skill.md (SkillHotbarSlotSet, 5/33)
                size = SmsgSkillHotbarSlotSet.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.Opcodes.SmsgSkillHotbarAssignResult
                : // spec: Docs/RE/structs/skill.md (SkillHotbarAssignResult, 4/41)
                size = SmsgSkillHotbarAssignResult.WireSize;
                isVariableLength = false;
                return true;

            // --- variable-length packets: only the fixed header is modelled ---
            case Opcodes.Opcodes.SmsgCharacterList: // spec: packets/3-1_character_list.yaml
                size = SmsgCharacterListHeader.HeaderSize;
                isVariableLength = true;
                return true;
            case Opcodes.Opcodes.SmsgChatBroadcast: // spec: packets/5-7_chat_broadcast.yaml
                size = SmsgChatBroadcastHeader.HeaderSize;
                isVariableLength = true;
                return true;
            case Opcodes.Opcodes.SmsgSkillPointUpdate: // spec: Docs/RE/structs/skill.md (SkillPointUpdate, 4/150)
                size = SmsgSkillPointUpdateHeader.HeaderSize;
                isVariableLength = true;
                return true;

            // --- C2S CharacterMgmt request builders (major 1; modelled for the send path) ---
            case Opcodes.Opcodes.CmsgLogout: // spec: packets/cmsg_logout.yaml (header-only, 0 B payload)
                size = CmsgLogout.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.Opcodes.CmsgCreateCharacter: // spec: packets/cmsg_char_create.yaml (52 B opaque body)
                size = CmsgCreateCharacter.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.Opcodes.CmsgSelectCharacter: // spec: packets/cmsg_char_select.yaml
                size = CmsgSelectCharacter.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.Opcodes.CmsgEnterGameRequest: // spec: packets/cmsg_char_enter.yaml
                size = CmsgEnterGameRequest.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.Opcodes.CmsgRenameCharacter: // spec: packets/cmsg_char_rename.yaml
                size = CmsgRenameCharacter.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.Opcodes.CmsgMoveCharacter: // spec: packets/cmsg_char_move.yaml
                size = CmsgMoveCharacter.WireSize;
                isVariableLength = false;
                return true;

            // --- other C2S packets the client emits (modelled for the send path) ---
            case Opcodes.Opcodes.CmsgMoveRequest: // spec: packets/2-13_move_request.yaml
                size = CmsgMoveRequest.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.Opcodes.CmsgWhisper: // spec: packets/2-7_whisper.yaml
                size = CmsgWhisperHeader.HeaderSize;
                isVariableLength = true;
                return true;
            case Opcodes.Opcodes.CmsgUseSkill: // spec: packets/2-52_use_skill.yaml
                size = CmsgUseSkillHeader.HeaderSize;
                isVariableLength = true;
                return true;
            case Opcodes.Opcodes.CmsgChatContextual: // spec: packets/2-83_chat_contextual.yaml
                size = CmsgChatContextualHeader.HeaderSize;
                isVariableLength = true;
                return true;
            case Opcodes.Opcodes.CmsgChatChannel: // spec: packets/3-21_chat_channel.yaml
                size = CmsgChatChannelHeader.HeaderSize;
                isVariableLength = true;
                return true;

            default:
                size = 0;
                isVariableLength = false;
                return false;
        }
    }
}