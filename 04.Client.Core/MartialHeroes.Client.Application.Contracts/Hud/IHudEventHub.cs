using System.Threading.Channels;
using MartialHeroes.Client.Application.Contracts.Events;

namespace MartialHeroes.Client.Application.Contracts.Hud;

public interface IHudEventHub
{
    ChannelReader<ChatLineEvent> ChatLines { get; }

    ChannelReader<BuffStateEvent> BuffStates { get; }

    ChannelReader<CombatTextEvent> CombatTexts { get; }

    ChannelReader<TargetChangedEvent> TargetChanges { get; }

    ChannelReader<StatAllocationView> StatAllocations { get; }

    ChannelReader<ZoneChangedEvent> ZoneChanges { get; }

    ChannelReader<HudVitalsEvent> Vitals { get; }

    ChannelReader<HudVitalsEvent> VitalsGauge { get; }

    ChannelReader<ExpLevelEvent> ExpLevels { get; }

    ChannelReader<InventorySlotsChangedEvent> InventorySlots { get; }

    ChannelReader<QuestLogChangedEvent> QuestLog { get; }

    ChannelReader<QuestCompletedEvent> QuestCompleted { get; }

    bool PublishChatLine(ChatLineEvent line);

    bool PublishBuffState(BuffStateEvent buffs);

    bool PublishCombatText(CombatTextEvent text);

    bool PublishExpLevel(ExpLevelEvent exp);

    bool PublishTargetChanged(TargetChangedEvent target);

    bool PublishStatAllocation(StatAllocationView view);

    bool PublishZoneChanged(ZoneChangedEvent zoneChanged);

    bool PublishVitals(HudVitalsEvent v);

    bool PublishInventorySlots(InventorySlotsChangedEvent slots);

    bool PublishQuestLog(QuestLogChangedEvent log);

    bool PublishQuestCompleted(QuestCompletedEvent completed);

    void Complete();
}