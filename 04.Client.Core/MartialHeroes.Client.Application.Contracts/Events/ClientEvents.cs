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
    int AreaId) : IClientEvent;

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
    uint SkillId) : IClientEvent;

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