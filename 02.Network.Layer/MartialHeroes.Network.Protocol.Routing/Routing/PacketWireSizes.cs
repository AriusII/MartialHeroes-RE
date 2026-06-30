using MartialHeroes.Network.Protocol.Core.Opcodes;
using MartialHeroes.Network.Protocol.Packets;
using MartialHeroes.Network.Protocol.Packets.Login.Packets;
using MartialHeroes.Network.Protocol.Packets.Social.Packets;
using MartialHeroes.Network.Protocol.Packets.World.Packets;

namespace MartialHeroes.Network.Protocol.Routing.Routing;

public static class PacketWireSizes
{
    public static bool TryGet(uint packedOpcode, out int size, out bool isVariableLength)
    {
        switch (packedOpcode)
        {
            case Opcodes.SmsgKeyExchange:
                size = SmsgKeyExchange.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgCharDespawn:
                size = SmsgCharDespawn.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgEnterGameAck:
                size = SmsgEnterGameAck.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgActorMovementUpdate:
                size = SmsgActorMovementUpdate.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgCharDeath:
                size = SmsgCharDeath.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgLocalPlayerStateSync
                :
                size = SmsgLocalPlayerStateSync.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgCharSpawn:
                size = SmsgCharSpawn.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgActorSpawnExtended:
                size = SmsgActorSpawnExtended.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgActorVitalsAndPairState:
                size = SmsgActorVitalsAndPairState.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgLevelUp:
                size = SmsgLevelUp.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgStatUpdate:
                size = SmsgStatUpdate.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgEquipItemResult:
                size = SmsgEquipItemResult.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgItemSlotStateAck:
                size = SmsgItemSlotStateAck.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgNpcBuyOrAcquireAck:
                size = SmsgNpcBuyOrAcquireAck.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgSkillHotbarSlotSet:
                size = SmsgSkillHotbarSlotSet.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgSkillHotbarAssignResult
                :
                size = SmsgSkillHotbarAssignResult.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgCharActionResult:
                size = SmsgCharActionResult.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgSrvBillingDeactivated:
                size = 0;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgSrvBillingActivated:
                size = 0;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgSrvBillingExpiryNotice:
                size = SmsgSrvBillingExpiryNotice.Size;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgRenameCharResult:
                size = SmsgRenameCharResult.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgCharManageResult:
                size = SmsgCharManageResult.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgShopPageUpdate:
                size = SmsgShopPageUpdate.Size;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgCharStatusUpdate:
                size = SmsgCharStatusUpdate.Size;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgCharSpawnResult
                :
                size = SmsgCharSpawnResult.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgCharStatusBytesByName
                :
                size = SmsgCharStatusBytesByName.WireSize;
                isVariableLength = false;
                return true;

            case Opcodes.SmsgAreaEntitySnapshot
                :
                size = SmsgAreaEntitySnapshot.HeaderSize;
                isVariableLength = true;
                return true;
            case Opcodes.SmsgGameStateTick:
                size = SmsgGameStateTick.WireSize;
                isVariableLength = true;
                return true;
            case Opcodes.SmsgCharacterList:
                size = SmsgCharacterListHeader.HeaderSize;
                isVariableLength = true;
                return true;
            case Opcodes.SmsgChatBroadcast:
                size = SmsgChatBroadcastHeader.HeaderSize;
                isVariableLength = true;
                return true;
            case Opcodes.SmsgSkillPointUpdate:
                size = SmsgSkillPointUpdateHeader.HeaderSize;
                isVariableLength = true;
                return true;
            case Opcodes.SmsgSrvLetterReceived:
                size = 76;
                isVariableLength = true;
                return true;
            case Opcodes.SmsgSceneEntityUpdate:
                size = SmsgSceneEntityUpdate
                    .HeaderSize;
                isVariableLength = true;
                return true;
            case Opcodes.SmsgGmChatMessage:
                size = 5;
                isVariableLength = true;
                return true;

            case Opcodes.CmsgLogout:
                size = CmsgLogout.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.CmsgCreateCharacter:
                size = CmsgCreateCharacter.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.CmsgSelectCharacterSlot
                :
                size = CmsgSelectCharacterSlot.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.CmsgEnterGameRequest:
                size = CmsgEnterGameRequest.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.CmsgRenameCharacter:
                size = CmsgRenameCharacter.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.CmsgMoveCharacter:
                size = CmsgMoveCharacter.WireSize;
                isVariableLength = false;
                return true;

            case Opcodes.CmsgMoveRequest:
                size = CmsgMoveRequest.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.CmsgWhisper:
                size = CmsgWhisperHeader.HeaderSize;
                isVariableLength = true;
                return true;
            case Opcodes.CmsgUseSkill:
                size = CmsgUseSkillHeader.HeaderSize;
                isVariableLength = true;
                return true;
            case Opcodes.CmsgChatContextual:
                size = CmsgChatContextualHeader.HeaderSize;
                isVariableLength = true;
                return true;
            case Opcodes.CmsgChatChannel:
                size = CmsgChatChannelHeader.HeaderSize;
                isVariableLength = true;
                return true;
            case Opcodes.CmsgKeepaliveToggle:
                size = CmsgKeepaliveToggle.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgBillingBalanceUpdate:
                size = SmsgBillingBalanceUpdate.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgCubeGambleResult:
                size = SmsgCubeGambleResult.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgCraftingResult:
                size = SmsgCraftingResult.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgActorDeathState:
                size = SmsgActorDeathState.WireSize;
                isVariableLength = false;
                return true;
            case Opcodes.SmsgPvpDeathFx:
                size = SmsgPvpDeathFx.WireSize;
                isVariableLength = false;
                return true;

            default:
                size = 0;
                isVariableLength = false;
                return false;
        }
    }
}