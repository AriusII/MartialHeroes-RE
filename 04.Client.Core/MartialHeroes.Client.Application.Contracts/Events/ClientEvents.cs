using System.Collections.Immutable;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Client.Domain.Stats.Stats;
using MartialHeroes.Shared.Kernel.Ids;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.Contracts.Events;

public sealed record ActorSpawnedEvent(
    ActorKey Key,
    string Name,
    ushort Level,
    Vector3Fixed Position,
    uint CurrentHp,
    uint MaxHp,
    ushort ServerClass,
    ushort InternalClass = 0,
    byte AppearanceVariant = 0,
    ImmutableArray<uint> EquipGids = default) : IClientEvent;

public sealed record ActorMovedEvent(
    ActorKey Key,
    Vector3Fixed Position,
    Vector3Fixed MoveTarget,
    int Yaw,
    bool IsRunning) : IClientEvent;

public sealed record ActorDespawnedEvent(ActorKey Key, bool PlayLeaveEffect) : IClientEvent;

public sealed record ActorVitalsChangedEvent(
    ActorKey Key,
    uint CurrentHp,
    uint CurrentMp,
    uint CurrentStamina) : IClientEvent;

public sealed record ActorStatsChangedEvent(
    ActorKey Key,
    uint Stat0,
    uint Stat1,
    uint Stat2,
    uint Stat3,
    uint Stat4,
    uint RemainingStatPoints) : IClientEvent;

public sealed record ActorLeveledUpEvent(
    ActorKey Key,
    ushort NewLevel,
    uint CurrentHp,
    uint CurrentMp,
    uint CurrentStamina,
    int RemainingStatPoints) : IClientEvent;

public sealed record LoginHandshakeCompletedEvent(int ReplyByteCount) : IClientEvent;

public sealed record EquipResultEvent(
    bool Success,
    byte FromSlot,
    byte ToSlot,
    bool TitleVisualRebuild) : IClientEvent;

public sealed record ItemSlotStateEvent(
    bool Success,
    byte FromSlot,
    byte ToSlot,
    int BonusField1,
    int BonusField2,
    int BonusField3) : IClientEvent;

public sealed record NpcAcquireResultEvent(
    bool Success,
    byte ReasonCode,
    byte BagSlotIndex,
    int ItemActorId,
    int GoldLow) : IClientEvent;

public sealed record SkillHotbarSlotSetEvent(
    byte HotbarSlot,
    SkillId Skill,
    short SkillPoints) : IClientEvent;

public sealed record SkillHotbarAssignResultEvent(
    bool Success,
    byte ResultCode,
    int HotbarSlot,
    SkillId Skill,
    uint SkillPointPool) : IClientEvent;

public sealed record SkillPointUpdateEvent(uint Mode, uint Value) : IClientEvent;

public sealed record BuffSlotChangedEvent(
    ActorKey Key,
    int SlotIndex,
    int EffectCode,
    int DurationTicks,
    int Param) : IClientEvent;

public sealed record ActorStatSyncEvent(
    ActorKey Key,
    uint Stat0,
    uint Stat2,
    uint Stat4,
    uint Stat5,
    uint Stat6,
    long CurrentXp) : IClientEvent;

public sealed record CombatStatsRecomputedEvent(ActorKey Key, CombatStats Stats) : IClientEvent;

public sealed record CombatAttackUpdateEvent(
    byte Phase,
    sbyte SubKind,
    uint Value,
    bool ChargeStarted,
    bool ChargeEnded) : IClientEvent;

public sealed record ChatBroadcastEvent(
    ActorKey SenderKey,
    string SenderName,
    byte Channel,
    uint ContextId,
    string Text) : IClientEvent;

public sealed record CharacterListEvent(
    byte ServerId,
    byte ChannelId,
    ImmutableArray<CharacterListSlot> Characters) : IClientEvent;

public readonly record struct CharacterListSlot(
    int SlotIndex,
    string Name,
    ushort Level,
    ushort ServerClass,
    uint CurrentHp,
    float PosX = 0f,
    float PosZ = 0f,
    ushort InternalClass = 0,
    byte AppearanceVariant = 0,
    ushort FaceA = 0,
    ImmutableArray<uint> EquipGids = default,
    byte SlotFlag = 0,
    uint BillingFlags = 0,
    byte LockFlag = 0)
{
    public bool IsBillingPremium => (BillingFlags & 0x1u) != 0;

    public bool IsActionConditionalVisible => LockFlag == 0;

    public bool IsSelectableForEnter => LockFlag == 0 && SlotFlag != 0;
}

public sealed record LocalPlayerSpawnedEvent(
    ActorKey Key,
    int SlotIndex,
    string Name,
    ushort Level,
    Vector3Fixed Position,
    uint CurrentHp,
    uint MaxHp,
    ushort ServerClass,
    ImmutableArray<uint> EquipGids = default,
    ushort InternalClass = 0,
    byte AppearanceVariant = 0) : IClientEvent;

public sealed record InGameWorldBootstrappedEvent(
    ActorKey Key,
    Vector3Fixed Position,
    int AreaId,
    byte ServerHour = 0,
    byte ServerMinute = 0) : IClientEvent;

public sealed record GroundItemSpawnedEvent(
    uint EntityKey,
    uint TemplateId,
    Vector3Fixed Position) : IClientEvent;

public sealed record GuildOverlayEvent(
    uint EntityId,
    string GuildName) : IClientEvent;

public sealed record TitleOverlayEvent(
    uint EntityId,
    byte RelationState,
    byte OverlaySubCode,
    string TitleName) : IClientEvent;

public sealed record AreaPopulatedEvent(
    float AreaCentreX,
    float AreaCentreZ,
    int SpawnedActorCount) : IClientEvent;

public sealed record LocalPlayerSpawnFailedEvent(byte SlotIndex) : IClientEvent;

public sealed record CreateCharacterRequestedEvent(int SlotIndex) : IClientEvent;

public readonly record struct RosterMember(uint ActorId, uint KeepGuard, uint Aux);

public sealed record RosterSnapshotEvent(ImmutableArray<RosterMember> Members) : IClientEvent;

public readonly record struct SceneCategoryEntry(uint Category, int Value);

public sealed record SceneEntitySnapshotEvent(
    ImmutableArray<RosterMember> ActorSlots,
    ImmutableArray<SceneCategoryEntry> Categories) : IClientEvent;

public sealed record ActorSkillActionEvent(
    ActorKey AttackerKey,
    uint SkillId,
    byte ActionCode) : IClientEvent;

public readonly record struct HotbarSlotEntry(int SlotIndex, uint EntryKey, ushort Count);

public sealed record HotbarInitializedEvent(ImmutableArray<HotbarSlotEntry> Slots) : IClientEvent;

public sealed record ActorVisualRefreshedEvent(ActorKey Key, byte RelationVisual) : IClientEvent;

public sealed record ActorDiedEvent(
    ActorKey VictimKey,
    ActorKey KillerKey,
    int DeathCause,
    bool IsPkA,
    bool IsPkB,
    bool IsLocalPlayer) : IClientEvent;

public sealed record LocalPlayerStateSyncedEvent(
    ActorKey Key,
    Vector3Fixed Position,
    int Heading,
    byte Mode) : IClientEvent;

public sealed record PlayerGoldBalanceUpdatedEvent(long Gold) : IClientEvent;

public sealed record ItemShopPurchaseResultEvent(bool Success, byte ResultCode) : IClientEvent;

public sealed record CashShopActionResultEvent(int ResultCode) : IClientEvent;

public sealed record ItemShopBalanceUpdatedEvent(
    bool Success,
    byte FailCode,
    long Gold,
    uint Points) : IClientEvent;

public sealed record EquipChangeResultEvent(
    bool Success,
    byte SlotKind,
    byte SlotIndex) : IClientEvent;

public sealed record ItemUseResultEvent(
    ActorKey Key,
    bool Success,
    byte ResultCode,
    byte Mode,
    byte SlotIndex) : IClientEvent;

public sealed record ItemUseEffectEvent(
    ActorKey Key,
    bool Success,
    byte Kind) : IClientEvent;

public sealed record UpgradeItemResultEvent(
    bool Success,
    byte Reason,
    byte SlotIndex,
    uint NewFlags,
    uint NewActorId,
    uint NewQuantity,
    uint EnchantDelta) : IClientEvent;

public sealed record PopupCodeEvent(uint PopupCode) : IClientEvent;

public sealed record GroundItemSlotResultEvent(
    bool Success,
    byte Mode,
    byte Slot,
    int Count) : IClientEvent;

public sealed record ItemWorldPickupResultEvent(
    bool Success,
    byte Subtype,
    uint ItemId) : IClientEvent;

public sealed record NpcSellItemResultEvent(
    bool Success,
    uint EntityKey,
    byte SubFlag) : IClientEvent;

public sealed record GroundItemRemovedEvent(
    uint EntityKey,
    uint PickerId,
    bool PlayPickupEffect) : IClientEvent;

public sealed record RelationUpdatedEvent(
    ActorKey Key,
    byte RelationType,
    byte PairState,
    int PartnerId,
    int Word1,
    int Word2,
    int Word3,
    bool IsLocalSlot) : IClientEvent;

public enum ActorStateKind : byte
{
    Combat = 0,
    Timed = 1,
    Generic = 2
}

public sealed record ActorStateChangedEvent(
    ActorKey Key,
    ActorStateKind Kind,
    uint StateValue,
    uint TimedValue,
    uint SourceActorId) : IClientEvent;

public sealed record ActorEquipVisualChangedEvent(
    ActorKey Key,
    byte SlotIndex,
    uint ItemId,
    uint ItemUpgrade,
    bool Cleared) : IClientEvent;

public sealed record ActorVisualFlagsChangedEvent(
    ActorKey Key,
    byte VisualFlags) : IClientEvent;

public sealed record PartyRosterEvent(
    byte EventCode,
    byte MemberSlot) : IClientEvent;

public sealed record PartyInviteStateEvent(
    byte Gate,
    byte Error,
    byte State,
    int PartyId,
    ImmutableArray<uint> MemberIds,
    uint TargetId) : IClientEvent;

public sealed record PartyMemberRemovedEvent(
    uint RequesterId,
    byte Submode,
    uint RemovedId,
    ImmutableArray<uint> MemberIds) : IClientEvent;

public sealed record PartyAcceptResultEvent(
    bool Success,
    byte RelationType) : IClientEvent;

public sealed record PartyMemberJoinedEvent(
    uint ActorId,
    byte EventCode,
    byte Sort,
    byte RelationType,
    string Name) : IClientEvent;

public sealed record PartyMemberVitalsEvent(
    uint MemberId,
    string MemberName,
    short StatA,
    short StatBState,
    uint StatC,
    uint StatD,
    uint StatE,
    uint StatF,
    uint StatG,
    uint StatH,
    uint StatI,
    uint StatJ) : IClientEvent;

public readonly record struct GuildMember(
    uint ActorId,
    string Name,
    byte Rank,
    byte Online,
    int Points,
    int Contribution,
    int LastLogin);

public sealed record GuildRosterEvent(
    short GuildId,
    string GuildName,
    byte Gate,
    ImmutableArray<GuildMember> Members) : IClientEvent;

public sealed record GuildMemberPatchEvent(
    ActorKey Key,
    bool Present,
    string Name,
    byte Rank,
    byte Grade) : IClientEvent;

public enum InventoryTable : byte
{
    Equip = 0,
    Bag = 1
}

public readonly record struct InventorySlotRecord(
    uint Flags,
    uint ItemActorId,
    uint QtyOrExpiryLo,
    uint ExpiryHi)
{
    public bool IsEmpty => ItemActorId == 0u;
}

public sealed record InventorySlotsChangedEvent(
    InventoryTable Table,
    int BaseIndex,
    bool ClearAll,
    ImmutableArray<InventorySlotRecord> Slots) : IClientEvent;

public readonly record struct QuestLogEntry(uint QuestId, string Name);

public sealed record QuestLogChangedEvent(
    byte TrackingFlag,
    byte PanelSelectorB,
    byte PanelSelectorC,
    ImmutableArray<QuestLogEntry> Entries) : IClientEvent;

public sealed record QuestCompletedEvent(
    bool Applied,
    byte RewardState,
    bool Granted) : IClientEvent;

public sealed record BillingBalanceUpdatedEvent(long BillingBalance) : IClientEvent;

public sealed record ActorDeathStateEvent(
    uint Sort,
    uint ActorId,
    uint Op,
    uint SubIndex,
    uint LinkedId) : IClientEvent;

public sealed record CubeGambleResultEvent(
    byte SubKind,
    byte ResultCode,
    byte BetType,
    uint Wager) : IClientEvent;

public sealed record CraftingResultEvent(
    bool Success,
    byte ErrorCode,
    byte ResultSubtype,
    uint ResultValueA,
    uint ResultValueB,
    uint ResultValueC,
    byte ProducedSlot,
    uint ProducedItem0,
    uint ProducedItem1,
    uint ProducedItem2,
    uint ProducedItem3) : IClientEvent;

public sealed record PvpDeathFxEvent(
    uint Sort,
    uint ActorId) : IClientEvent;

public sealed record GuildStateChangedEvent(
    byte ApplyGate,
    byte Action,
    byte Result) : IClientEvent;

public sealed record StealthToggleEvent(uint ActorId, bool Stealthed) : IClientEvent;

public sealed record MailLetterArrivedEvent(
    uint LetterId,
    string Sender,
    uint LetterType,
    uint AttachmentGold,
    uint AttachmentItemId,
    uint StatusFlags,
    string Date,
    string Subject,
    string Body) : IClientEvent;

public sealed record DeliveryRecordUpdatedEvent(
    byte ResultCode,
    byte SubAction,
    string Sender,
    long Money,
    int EntryKey) : IClientEvent;

public sealed record TradeSlotUpdatedEvent(
    bool Apply,
    bool IsLocalSide,
    bool IsMoneySlot,
    byte Category,
    byte SlotIndex,
    uint ItemId,
    uint OwnerId) : IClientEvent;

public sealed record TradeSessionPhaseEvent(
    byte Phase,
    bool IsLocalSide,
    long Coin,
    uint OwnerId,
    int RecordCount) : IClientEvent;