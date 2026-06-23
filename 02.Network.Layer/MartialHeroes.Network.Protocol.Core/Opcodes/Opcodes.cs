
namespace MartialHeroes.Network.Protocol.Core.Opcodes;

public static class Opcodes
{
    public const uint SmsgKeyExchange = 0x0;

    public const uint CmsgLogout = 0x10000;

    public const uint CmsgCreateCharacter = 0x10006;

    public const uint CmsgSelectCharacterSlot = 0x10007;

    public const uint CmsgEnterGameRequest = 0x10009;

    public const uint CmsgRenameCharacter = 0x1000d;

    public const uint CmsgMoveCharacter = 0x1000e;

    public const uint SmsgSrvBillingDeactivated = 0x10010;

    public const uint SmsgSrvBillingActivated = 0x10011;

    public const uint SmsgSrvBillingExpiryNotice = 0x10013;

    public const uint SmsgSrvLetterReceived = 0x10014;

    public const uint CmsgLeaveWorld = 0x20000;

    public const uint CmsgWhisper = 0x20007;

    public const uint CmsgMoveRequest = 0x2000d;

    public const uint CmsgUseSkill = 0x20034;

    public const uint CmsgChatContextual = 0x20053;

    public const uint CmsgKeepaliveToggle = 0x20070;

    public const uint CmsgKeepalive = 0x22710;

    public const uint SmsgCharacterList = 0x30001;

    public const uint CmsgChatChannel = 0x30015;

    public const uint SmsgSceneEntityUpdate = 0x30004;

    public const uint SmsgEnterGameAck = 0x30005;

    public const uint SmsgRenameCharResult = 0x30006;

    public const uint SmsgCharManageResult = 0x30007;

    public const uint SmsgShopPageUpdate = 0x30008;

    public const uint SmsgCharStatusUpdate = 0x3000d;

    public const uint SmsgCharSpawnResult = 0x3000e;

    public const uint SmsgCharStatusBytesByName = 0x30017;

    public const uint SmsgCharActionResult = 0x30064;

    public const uint SmsgGmChatMessage = 0x3c350;

    public const uint SmsgGameStateTick = 0x40001;

    public const uint SmsgGameTickConfig = 0x40002;

    public const uint SmsgBillingInfo = 0x40003;

    public const uint SmsgAreaEntitySnapshot = 0x40004;

    public const uint SmsgItemUseResult = 0x40005;

    public const uint SmsgEquipItemResult = 0x4000c;

    public const uint SmsgLocalPlayerStateSync = 0x4000d;

    public const uint SmsgGroundItemSlotAck = 0x4000e;

    public const uint SmsgItemWorldPickupAck = 0x4000f;

    public const uint SmsgEquipChangeResult = 0x40010;

    public const uint SmsgQuickEquipSlotAck = 0x40011;

    public const uint SmsgNpcBuyOrAcquireAck = 0x40013;

    public const uint SmsgNpcSellItemAck = 0x40014;

    public const uint SmsgNpcShopSlotClearAck = 0x40015;

    public const uint SmsgItemSlotStateAck = 0x40016;

    public const uint SmsgUserTradeRequestResult = 0x40017;

    public const uint SmsgUserTradeSlotUpdate = 0x40018;

    public const uint SmsgUserTradeFullResponse = 0x40019;

    public const uint SmsgRespawnConfirm = 0x4001c;

    public const uint SmsgStatUpdate = 0x4001d;

    public const uint SmsgSocialPanelTarget = 0x4001e;

    public const uint SmsgPartyInviteState = 0x40023;

    public const uint SmsgPartyMemberRemoveResult = 0x40024;

    public const uint SmsgPartyLeaderActionResult = 0x40025;

    public const uint SmsgResponseDiscard39 = 0x40027;

    public const uint SmsgResponseSlot40 = 0x40028;

    public const uint SmsgSkillHotbarAssignResult = 0x40029;

    public const uint SmsgPlayerInteractionState = 0x4002a;

    public const uint SmsgPlayerInteractionResult = 0x4002b;

    public const uint SmsgActorTickTableOpA = 0x4002c;

    public const uint SmsgActorTickTableOpB = 0x4002d;

    public const uint SmsgActorTickTableOpC = 0x4002e;

    public const uint SmsgSocialAckDrain = 0x4002f;

    public const uint SmsgRankProgressEvent = 0x40030;

    public const uint SmsgRankProgressPanelUpdate = 0x40031;

    public const uint SmsgUpgradeItemResult = 0x40032;

    public const uint SmsgGuildRankSlotUpdate = 0x40036;

    public const uint SmsgGuildInfoUpdateResult = 0x40037;

    public const uint SmsgResponseSlot56 = 0x40038;

    public const uint SmsgResponseSlot57 = 0x40039;

    public const uint SmsgResponseSlot58 = 0x4003a;

    public const uint SmsgResponseSlot60 = 0x4003c;

    public const uint SmsgGuildStateChangeResult = 0x4003d;

    public const uint SmsgGuildInviteJoinState = 0x4003e;

    public const uint SmsgGuildMemberRemoveResult = 0x4003f;

    public const uint SmsgGuildPositionChangeResult = 0x40040;

    public const uint SmsgGuildInfoFullSync = 0x40041;

    public const uint SmsgResponseSlot66 = 0x40042;

    public const uint SmsgResponseSlot70 = 0x40046;

    public const uint SmsgResponseSlot71 = 0x40047;

    public const uint SmsgResponseSlot72 = 0x40048;

    public const uint SmsgResponseSlot74 = 0x4004a;

    public const uint SmsgResponseSlot75 = 0x4004b;

    public const uint SmsgPartyAcceptResult = 0x4004c;

    public const uint SmsgServerTimeNotification = 0x4004e;

    public const uint SmsgCraftingResult = 0x4004f;

    public const uint SmsgPvpDeathResult = 0x40050;

    public const uint SmsgActionErrorResult = 0x40051;

    public const uint SmsgBillingBalanceUpdate = 0x40052;

    public const uint SmsgBillingItemUseResult = 0x40053;

    public const uint SmsgRecordSlotConsumeResult = 0x40054;

    public const uint SmsgUserVoteTallyUpdate = 0x4005d;

    public const uint SmsgLocalPlayerBattleMode = 0x4005f;

    public const uint SmsgActorGuildRosterEntry = 0x40060;

    public const uint SmsgAreaSkillEffectPanel = 0x40061;

    public const uint SmsgCombatResultMessage = 0x40063;

    public const uint SmsgCombatAttackUpdate = 0x40064;

    public const uint SmsgGlobalScalarPairUpdate = 0x40065;

    public const uint SmsgSkillWindowStateUpdate = 0x40066;

    public const uint SmsgGuildPanelTextUpdate = 0x40067;

    public const uint SmsgGagePanelProgressUpdate = 0x40069;

    public const uint SmsgEventLotteryPickResult = 0x4006b;

    public const uint SmsgPlayerGoldBalanceUpdate = 0x4006c;

    public const uint SmsgLocalActorSkillStateFlag = 0x4006d;

    public const uint SmsgItemShopPurchaseResult = 0x40071;

    public const uint SmsgCashShopActionResult = 0x40072;

    public const uint SmsgItemShopBalanceUpdate = 0x40073;

    public const uint SmsgActorTableBatchResult = 0x40078;

    public const uint SmsgResponseSlot122 = 0x4007a;

    public const uint SmsgResponseSlot123 = 0x4007b;

    public const uint SmsgResponseSlot125 = 0x4007d;

    public const uint SmsgResponseSlot126 = 0x4007e;

    public const uint SmsgGmNoticeError = 0x40084;

    public const uint SmsgRankProgressUpdate = 0x40085;

    public const uint SmsgStatChangeNotify = 0x40086;

    public const uint SmsgResponseSlot135 = 0x40087;

    public const uint SmsgGatheringResult = 0x40089;

    public const uint SmsgNoticeError = 0x4008a;

    public const uint SmsgItemUseEffect = 0x4008b;

    public const uint SmsgColoredSystemText = 0x4008c;

    public const uint SmsgResponseSlot142 = 0x4008e;

    public const uint SmsgShowMessage51027 = 0x40092;

    public const uint SmsgPlaytimeRewardResult = 0x40094;

    public const uint SmsgItemPanelSlotChunk = 0x40095;

    public const uint SmsgSkillPointUpdate = 0x40096;

    public const uint SmsgResponseSlot151 = 0x40097;

    public const uint SmsgResponseSlot152 = 0x40098;

    public const uint SmsgItemPanelSlotRefresh = 0x40099;

    public const uint SmsgShowPopupByCode = 0x401f4;

    public const uint SmsgResponseDiscardText = 0x4c350;

    public const uint SmsgCharDespawn = 0x50000;

    public const uint SmsgActorSpawnExtended = 0x50001;

    public const uint SmsgCharSpawn = 0x50003;

    public const uint SmsgActorStateEvent = 0x50005;

    public const uint SmsgActorAutotargetOrMotion = 0x50006;

    public const uint SmsgChatBroadcast = 0x50007;

    public const uint SmsgExpGain = 0x50009;

    public const uint SmsgCharDeath = 0x5000a;

    public const uint SmsgRankXpGain = 0x5000b;

    public const uint SmsgActorVisualSlotSet = 0x5000c;

    public const uint SmsgActorMovementUpdate = 0x5000d;

    public const uint SmsgCombatEffectInstanceSpawn = 0x5000e;

    public const uint SmsgTrackedWorldObjectRemove = 0x5000f;

    public const uint SmsgActorVisualSlotClear = 0x50010;

    public const uint SmsgGameClockUpdate = 0x50012;

    public const uint SmsgPartyRosterEvent = 0x50015;

    public const uint SmsgLocalPlayerRelationSlot = 0x5001a;

    public const uint SmsgRespawnAtPoint = 0x5001c;

    public const uint SmsgBuffSlotUpdate = 0x5001f;

    public const uint SmsgLevelUp = 0x50020;

    public const uint SmsgSkillHotbarSlotSet = 0x50021;

    public const uint SmsgBillingBannerToggle = 0x50022;

    public const uint SmsgPartyMemberStats = 0x50026;

    public const uint SmsgPushNop39 = 0x50027;

    public const uint SmsgPlayerPairSystemNotice = 0x5002a;

    public const uint SmsgSkillGuideState = 0x50033;

    public const uint SmsgActorSkillAction = 0x50034;

    public const uint SmsgActorVitalsAndPairState = 0x50035;

    public const uint SmsgGuildNameDisplayUpdate = 0x50037;

    public const uint SmsgTrackedPanelSlotUpdate = 0x50039;

    public const uint SmsgRankProgressSfxEvent = 0x5003b;

    public const uint SmsgActorNameOverlaySet = 0x5003d;

    public const uint SmsgRemoteActorRelationPair = 0x50040;

    public const uint SmsgGuildMemberRosterUpdate = 0x50041;

    public const uint SmsgStatsUpdate = 0x50043;

    public const uint SmsgQuestList = 0x50044;

    public const uint SmsgQuestComplete = 0x50049;

    public const uint SmsgPartyMemberJoined = 0x5004c;

    public const uint SmsgRankProgressPanelBulk = 0x5004d;

    public const uint SmsgActorDeathState = 0x5004f;

    public const uint SmsgPvpDeathFx = 0x50050;

    public const uint SmsgDungeonEventStateSyncA = 0x50055;

    public const uint SmsgDungeonEventStateSyncB = 0x50056;

    public const uint SmsgDungeonEventActorClear = 0x50057;

    public const uint SmsgPvpStateBytes = 0x50058;

    public const uint SmsgPvpRevengeRoster = 0x50059;

    public const uint SmsgPvpCounters = 0x5005a;

    public const uint SmsgPvpScoreUpdate = 0x5005b;

    public const uint SmsgPvpRequestOrNotice = 0x5005c;

    public const uint SmsgActorMobIdChange = 0x5005d;

    public const uint SmsgVoteResult = 0x5005e;

    public const uint SmsgActorClassFormRefresh = 0x50062;

    public const uint SmsgTradeStateToggle = 0x5006a;

    public const uint SmsgCharPropertySet = 0x50079;

    public const uint SmsgGiftCharReceiveConfirm = 0x5007b;

    public const uint SmsgActorVisualFlagsSet = 0x5007c;

    public const uint SmsgActorStanceSet = 0x5007e;

    public const uint SmsgStealthToggle = 0x5007f;

    public const uint SmsgMonsterNoticeByMobId = 0x50081;

    public const uint SmsgMonsterNoticeWithText = 0x50082;

    public const uint SmsgPvpRankScoreUpdate = 0x50083;

    public const uint SmsgActorTimedStateUpdate = 0x50088;

    public const uint SmsgAttackEffect = 0x5008b;

    public const uint SmsgPacketResponseAckRequest = 0x50092;

    public const uint SmsgActorCombatFlagUpdate = 0x50093;
}