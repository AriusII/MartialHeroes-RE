using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Network.Protocol.Packets.World.Packets;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    private const byte ResultOk = 1;

    public void Handle(in SmsgPlayerGoldBalanceUpdate packet)
    {
        _eventBus.Publish(new PlayerGoldBalanceUpdatedEvent(packet.Gold));
    }

    public void Handle(in SmsgItemShopPurchaseResult packet)
    {
        _eventBus.Publish(new ItemShopPurchaseResultEvent(packet.Success == ResultOk, packet.ResultCode));
    }

    public void Handle(in SmsgCashShopActionResult packet)
    {
        _eventBus.Publish(new CashShopActionResultEvent(packet.ResultCode));
    }

    public void Handle(in SmsgItemShopBalanceUpdate packet)
    {
        _eventBus.Publish(new ItemShopBalanceUpdatedEvent(
            packet.Success == ResultOk, packet.FailCode, packet.Gold, packet.Points));
    }

    public void Handle(in SmsgEquipChangeResult packet)
    {
        var success = packet.Result == ResultOk;
        if (success) RecomputeCombatStats();

        _eventBus.Publish(new EquipChangeResultEvent(success, packet.SlotKind, packet.SlotIndex));
    }

    public void Handle(in SmsgItemUseResult packet)
    {
        var success = packet.Success == ResultOk;
        if (success) RecomputeCombatStats();

        _eventBus.Publish(new ItemUseResultEvent(
            ResolveActorKey(packet.ActorKey), success, packet.ResultCode, packet.Mode, packet.SlotIndex));
    }

    public void Handle(in SmsgItemUseEffect packet)
    {
        _eventBus.Publish(new ItemUseEffectEvent(
            ResolveActorKey(packet.ActorId), packet.Result == ResultOk, packet.Kind));
    }

    public void Handle(in SmsgUpgradeItemResult packet)
    {
        var success = packet.Success == ResultOk;
        if (success) RecomputeCombatStats();

        _eventBus.Publish(new UpgradeItemResultEvent(
            success, packet.Reason, packet.SlotIndex, packet.NewFlags, packet.NewActorId, packet.NewQty,
            packet.EnchantDelta));
    }

    public void Handle(in SmsgShowPopupByCode packet)
    {
        _eventBus.Publish(new PopupCodeEvent(packet.PopupCode));
    }

    public void Handle(in SmsgGroundItemSlotAck packet)
    {
        var success = packet.Result == ResultOk;
        if (success) RecomputeCombatStats();

        _eventBus.Publish(new GroundItemSlotResultEvent(success, packet.Mode, packet.Slot, packet.Count));
    }

    public void Handle(in SmsgItemWorldPickupAck packet)
    {
        var success = packet.Result == ResultOk;
        if (success) RecomputeCombatStats();

        _eventBus.Publish(new ItemWorldPickupResultEvent(success, packet.Subtype, packet.ItemId));
    }

    public void Handle(in SmsgNpcSellItemAck packet)
    {
        var success = packet.Result == ResultOk;
        if (success) RecomputeCombatStats();

        _eventBus.Publish(new NpcSellItemResultEvent(success, packet.EntityKey, packet.SubFlag));
    }

    private ActorKey ResolveActorKey(uint rawId)
    {
        if (_world.LocalActorKey is { } local && local.RawId == rawId) return local;

        return new ActorKey(rawId, EntitySort.PlayerCharacter);
    }
}
