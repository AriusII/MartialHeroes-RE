using MartialHeroes.Network.Protocol.Packets.Login.Packets;
using MartialHeroes.Network.Protocol.Packets.World.Packets;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    public void Handle(in SmsgSrvBillingDeactivated packet)
    {
    }

    public void Handle(in SmsgSrvBillingActivated packet)
    {
    }

    public void Handle(in SmsgGameTickConfig packet)
    {
    }

    public void Handle(in SmsgAreaEntitySnapshot packet)
    {
        HandleAreaEntitySnapshot(ActivePayload);
    }

    public void Handle(in SmsgNpcShopSlotClearAck packet)
    {
    }

    public void Handle(in SmsgQuickEquipSlotAck packet)
    {
    }

    public void Handle(in SmsgUserTradeRequestResult packet)
    {
    }

    public void Handle(in SmsgUserTradeSlotUpdate packet)
    {
    }

    public void Handle(in SmsgUserTradeFullResponse packet)
    {
    }

    public void Handle(in SmsgMotionWeaponFxSwap packet)
    {
    }

    public void Handle(in SmsgActorTickTableOpA packet)
    {
    }

    public void Handle(in SmsgActorTickTableOpB packet)
    {
    }

    public void Handle(in SmsgActorTickTableOpC packet)
    {
    }

    public void Handle(in SmsgRankProgressEvent packet)
    {
    }

    public void Handle(in SmsgGuildStateChangeResult packet)
    {
    }

    public void Handle(in SmsgResponseSlot71 packet)
    {
    }

    public void Handle(in SmsgStallListRefill packet)
    {
    }

    public void Handle(in SmsgProductPurchaseResultPanel packet)
    {
    }

    public void Handle(in SmsgPvpDeathResult packet)
    {
    }

    public void Handle(in SmsgActionErrorResult packet)
    {
    }

    public void Handle(in SmsgRentalItemExpirySweep packet)
    {
    }

    public void Handle(in SmsgGuildPanelTextUpdate packet)
    {
    }

    public void Handle(in SmsgLocalActorSkillStateFlag packet)
    {
    }

    public void Handle(in SmsgItemPanelSlotChunk packet)
    {
        HandleItemPanelSlotChunk(ActivePayload);
    }

    public void Handle(in SmsgSkillPointUpdateHeader packet)
    {
        HandleSkillPointUpdate(in packet);
    }

    public void Handle(in SmsgItemPanelSlotRefresh packet)
    {
        HandleItemPanelSlotRefresh(ActivePayload);
    }

    public void Handle(in SmsgSceneEnterSpawnSnapshot packet)
    {
    }

    public void Handle(in SmsgChatBroadcastHeader packet)
    {
        HandleChatBroadcast(ActivePayload);
    }

    public void Handle(in SmsgExpGain packet)
    {
        HandleExpGain(ActivePayload);
    }

    public void Handle(in SmsgRankXpGain packet)
    {
        HandleRankXpGain(ActivePayload);
    }

    public void Handle(in SmsgBuffSlotUpdate packet)
    {
        HandleBuffSlotUpdate(ActivePayload);
    }

    public void Handle(in SmsgActorSkillAction packet)
    {
        HandleActorSkillAction(ActivePayload);
    }

    public void Handle(in SmsgStatsUpdate packet)
    {
        HandleStatsUpdate(ActivePayload);
    }

    public void Handle(in SmsgQuestList packet)
    {
        HandleQuestList(ActivePayload);
    }

    public void Handle(in SmsgQuestComplete packet)
    {
        HandleQuestComplete(ActivePayload);
    }

    public void Handle(in SmsgRankProgressPanelBulk packet)
    {
    }

    public void Handle(in SmsgDungeonEventStateSyncA packet)
    {
    }

    public void Handle(in SmsgDungeonEventStateSyncB packet)
    {
    }

    public void Handle(in SmsgPvpRevengeRoster packet)
    {
    }

    public void Handle(in SmsgPvpCounters packet)
    {
    }

    public void Handle(in SmsgPvpScoreUpdate packet)
    {
    }

    public void Handle(in SmsgPvpRequestOrNotice packet)
    {
    }

    public void Handle(in SmsgActorMobIdChange packet)
    {
    }

    public void Handle(in SmsgVoteResult packet)
    {
    }

    public void Handle(in SmsgCharPropertySet packet)
    {
    }

    public void Handle(in SmsgActorStanceSet packet)
    {
    }

    public void Handle(in SmsgStealthToggle packet)
    {
    }

    public void Handle(in SmsgPvpRankScoreUpdate packet)
    {
    }

    public void Handle(in SmsgAckRequest packet)
    {
        if (linkAckEmitter is null) return;

        _ = linkAckEmitter(packet.Token, CancellationToken.None);
    }

    public void Handle(in SmsgTrackedPanelSlotUpdate packet)
    {
    }

    public void Handle(in SmsgCubeGambleReelUpdate packet)
    {
        HandleCombatAttackUpdate(ActivePayload);
    }

    public void Handle(in SmsgCharacterListHeader packet)
    {
        HandleCharacterList(ActivePayload);
    }

    public void Handle(in SmsgSceneEntityUpdate packet)
    {
        HandleSceneEntityUpdate(ActivePayload);
    }
}