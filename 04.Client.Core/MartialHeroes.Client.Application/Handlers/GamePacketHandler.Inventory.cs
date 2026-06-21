using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Network.Protocol.Packets.World.Packets;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    // -------------------------------------------------------------------------
    // 4/12 — equip/unequip result
    // -------------------------------------------------------------------------

    /// <summary>
    ///     4/12 — equip/unequip result. On success applies the equipment-slot/visual update to the local
    ///     player and triggers a combat-stat recompute (equipment changed); ToSlot 15 forces a title-slot
    ///     visual rebuild. spec: Docs/RE/specs/handlers.md §3 (4/12); Docs/RE/structs/item.md.
    /// </summary>
    public void Handle(in SmsgEquipItemResult packet)
    {
        const byte ok = 1; // result 1 = success. spec: handlers.md §3 (4/12 result byte).
        const byte titleSlot = 15; // ToSlot 15 = title/gear visual rebuild. spec: handlers.md §3 / item.md.
        var success = packet.Result == ok;

        if (success)
            // Equipment changed -> the derived combat-stat aggregate must be re-accumulated. spec: combat.md §2.
            RecomputeCombatStats();

        _eventBus.Publish(new EquipResultEvent(
            success, packet.FromSlot, packet.ToSlot, packet.ToSlot == titleSlot));
    }

    // -------------------------------------------------------------------------
    // 4/22 — item-slot state ack
    // -------------------------------------------------------------------------

    /// <summary>
    ///     4/22 — item-slot state ack: a slot's state plus stat/enchant fields. On success a recompute is
    ///     triggered (the slot's stats may feed the aggregate). spec: Docs/RE/specs/handlers.md §13 Group B
    ///     (4/22); Docs/RE/structs/item.md.
    /// </summary>
    public void Handle(in SmsgItemSlotStateAck packet)
    {
        const byte ok = 1; // result 1 = ok. spec: item.md (4/22 result byte).
        var success = packet.Result == ok;

        if (success)
            RecomputeCombatStats(); // a slot's stat/enchant fields may change the aggregate. spec: combat.md §2.

        _eventBus.Publish(new ItemSlotStateEvent(
            success, packet.FromSlot, packet.ToSlot, packet.BonusField1, packet.BonusField2, packet.BonusField3));
    }

    // -------------------------------------------------------------------------
    // 4/19 — NPC buy / acquire ack
    // -------------------------------------------------------------------------

    /// <summary>
    ///     4/19 — NPC buy / inventory-acquire ack. Publishes the acquire outcome (slot, item actor id, gold).
    ///     spec: Docs/RE/specs/handlers.md §13 Group B (4/19); Docs/RE/structs/item.md.
    /// </summary>
    public void Handle(in SmsgNpcBuyOrAcquireAck packet)
    {
        const byte ok = 1; // result 1 = ok. spec: item.md (4/19 result byte).
        var success = packet.Result == ok;

        _eventBus.Publish(new NpcAcquireResultEvent(
            success, packet.ReasonCode, packet.BagSlotIndex, packet.ItemQuadB, packet.GoldLo));
    }
}