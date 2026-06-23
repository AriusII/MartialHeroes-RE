using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Network.Protocol.Packets.World.Packets;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    public void Handle(in SmsgEquipItemResult packet)
    {
        const byte ok = 1;
        const byte titleSlot = 15;
        var success = packet.Result == ok;

        if (success)
            RecomputeCombatStats();

        _eventBus.Publish(new EquipResultEvent(
            success, packet.FromSlot, packet.ToSlot, packet.ToSlot == titleSlot));
    }


    public void Handle(in SmsgItemSlotStateAck packet)
    {
        const byte ok = 1;
        var success = packet.Result == ok;

        if (success)
            RecomputeCombatStats();

        _eventBus.Publish(new ItemSlotStateEvent(
            success, packet.FromSlot, packet.ToSlot, packet.BonusField1, packet.BonusField2, packet.BonusField3));
    }


    public void Handle(in SmsgNpcBuyOrAcquireAck packet)
    {
        const byte ok = 1;
        var success = packet.Result == ok;

        _eventBus.Publish(new NpcAcquireResultEvent(
            success, packet.ReasonCode, packet.BagSlotIndex, packet.ItemQuadB, packet.GoldLo));
    }
}