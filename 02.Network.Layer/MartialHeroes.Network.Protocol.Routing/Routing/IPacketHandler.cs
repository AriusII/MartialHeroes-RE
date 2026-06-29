using MartialHeroes.Network.Protocol.Packets.Login.Packets;
using MartialHeroes.Network.Protocol.Packets.World.Packets;

namespace MartialHeroes.Network.Protocol.Routing.Routing;

public interface IPacketHandler
{
    void Handle(in SmsgKeyExchange packet);

    void Handle(in SmsgSrvBillingDeactivated packet);

    void Handle(in SmsgSrvBillingActivated packet);

    void Handle(in SmsgGameStateTick packet);

    void Handle(in SmsgGameTickConfig packet);

    void Handle(in SmsgLocalPlayerStateSync packet);

    void Handle(in SmsgAreaEntitySnapshot packet);

    void Handle(in SmsgItemUseResult packet);

    void Handle(in SmsgEquipItemResult packet);

    void Handle(in SmsgItemSlotStateAck packet);

    void Handle(in SmsgNpcBuyOrAcquireAck packet);

    void Handle(in SmsgNpcSellItemAck packet);

    void Handle(in SmsgNpcShopSlotClearAck packet);

    void Handle(in SmsgGroundItemSlotAck packet);

    void Handle(in SmsgItemWorldPickupAck packet);

    void Handle(in SmsgEquipChangeResult packet);

    void Handle(in SmsgQuickEquipSlotAck packet);

    void Handle(in SmsgUserTradeRequestResult packet);

    void Handle(in SmsgUserTradeSlotUpdate packet);

    void Handle(in SmsgUserTradeFullResponse packet);

    void Handle(in SmsgStatUpdate packet);

    void Handle(in SmsgPartyMemberRemoveResult packet);

    void Handle(in SmsgSkillHotbarAssignResult packet);

    void Handle(in SmsgMotionWeaponFxSwap packet);

    void Handle(in SmsgActorTickTableOpA packet);

    void Handle(in SmsgActorTickTableOpB packet);

    void Handle(in SmsgActorTickTableOpC packet);

    void Handle(in SmsgRankProgressEvent packet);

    void Handle(in SmsgUpgradeItemResult packet);

    void Handle(in SmsgGuildStateChangeResult packet);

    void Handle(in SmsgGuildInfoFullSync packet);

    void Handle(in SmsgResponseSlot71 packet);

    void Handle(in SmsgStallListRefill packet);

    void Handle(in SmsgProductPurchaseResultPanel packet);

    void Handle(in SmsgPvpDeathResult packet);

    void Handle(in SmsgActionErrorResult packet);

    void Handle(in SmsgRentalItemExpirySweep packet);

    void Handle(in SmsgSkillWindowStateUpdate packet);

    void Handle(in SmsgGuildPanelTextUpdate packet);

    void Handle(in SmsgPlayerGoldBalanceUpdate packet);

    void Handle(in SmsgLocalActorSkillStateFlag packet);

    void Handle(in SmsgItemShopPurchaseResult packet);

    void Handle(in SmsgCashShopActionResult packet);

    void Handle(in SmsgItemShopBalanceUpdate packet);

    void Handle(in SmsgItemUseEffect packet);

    void Handle(in SmsgItemPanelSlotChunk packet);

    void Handle(in SmsgSkillPointUpdateHeader packet);

    void Handle(in SmsgItemPanelSlotRefresh packet);

    void Handle(in SmsgShowPopupByCode packet);

    void Handle(in SmsgSceneEnterSpawnSnapshot packet);

    void Handle(in SmsgCharDespawn packet);

    void Handle(in SmsgActorSpawnExtended packet);

    void Handle(in SmsgCharSpawn packet);

    void Handle(in SmsgActorStateEvent packet);

    void Handle(in SmsgChatBroadcastHeader packet);

    void Handle(in SmsgExpGain packet);

    void Handle(in SmsgCharDeath packet);

    void Handle(in SmsgRankXpGain packet);

    void Handle(in SmsgActorVisualSlotSet packet);

    void Handle(in SmsgActorMovementUpdate packet);

    void Handle(in SmsgGroundItemSpawn packet);

    void Handle(in SmsgActorVisualSlotClear packet);

    void Handle(in SmsgPartyRosterEvent packet);

    void Handle(in SmsgBuffSlotUpdate packet);

    void Handle(in SmsgLevelUp packet);

    void Handle(in SmsgSkillHotbarSlotSet packet);

    void Handle(in SmsgPartyMemberStats packet);

    void Handle(in SmsgActorSkillAction packet);

    void Handle(in SmsgActorVitalsAndPairState packet);

    void Handle(in SmsgGuildMemberRosterUpdate packet);

    void Handle(in SmsgStatsUpdate packet);

    void Handle(in SmsgQuestList packet);

    void Handle(in SmsgQuestComplete packet);

    void Handle(in SmsgRankProgressPanelBulk packet);

    void Handle(in SmsgDungeonEventStateSyncA packet);

    void Handle(in SmsgDungeonEventStateSyncB packet);

    void Handle(in SmsgPvpRevengeRoster packet);

    void Handle(in SmsgPvpCounters packet);

    void Handle(in SmsgPvpScoreUpdate packet);

    void Handle(in SmsgPvpRequestOrNotice packet);

    void Handle(in SmsgActorMobIdChange packet);

    void Handle(in SmsgVoteResult packet);

    void Handle(in SmsgCharPropertySet packet);

    void Handle(in SmsgActorVisualFlagsSet packet);

    void Handle(in SmsgActorStanceSet packet);

    void Handle(in SmsgStealthToggle packet);

    void Handle(in SmsgPvpRankScoreUpdate packet);

    void Handle(in SmsgActorTimedStateUpdate packet);

    void Handle(in SmsgAckRequest packet);

    void Handle(in SmsgActorCombatFlagUpdate packet);

    void Handle(in SmsgTrackedPanelSlotUpdate packet);

    void Handle(in SmsgGroundItemRemove packet);

    void Handle(in SmsgCubeGambleReelUpdate packet);

    void Handle(in SmsgEnterGameAck packet);

    void Handle(in SmsgCharacterListHeader packet);

    void Handle(in SmsgSceneEntityUpdate packet);

    void Handle(in SmsgCharSpawnResult packet);

    void Handle(in SmsgCharManageResult packet);

    void Handle(in SmsgRenameCharResult packet);

    void Handle(in SmsgCharStatusBytesByName packet);

    void Handle(in SmsgCharActionResult packet);

    void Handle(in SmsgLocalPlayerRelationSlot packet)
    {
        OnUnhandled(SmsgLocalPlayerRelationSlot.OpcodeId, ReadOnlySpan<byte>.Empty);
    }

    void Handle(in SmsgRemoteActorRelationPair packet)
    {
        OnUnhandled(SmsgRemoteActorRelationPair.OpcodeId, ReadOnlySpan<byte>.Empty);
    }

    void OnUnhandled(uint packedOpcode, ReadOnlySpan<byte> payload);
}