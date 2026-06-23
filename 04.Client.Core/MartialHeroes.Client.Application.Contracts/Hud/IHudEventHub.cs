using System.Threading.Channels;

namespace MartialHeroes.Client.Application.Contracts.Hud;

public interface IHudEventHub
{
    ChannelReader<ChatLineEvent> ChatLines { get; }

    ChannelReader<BuffStateEvent> BuffStates { get; }

    ChannelReader<CombatTextEvent> CombatTexts { get; }

    ChannelReader<TargetChangedEvent> TargetChanges { get; }

    ChannelReader<ExpLevelEvent> ExpLevels { get; }

    ChannelReader<StatAllocationView> StatAllocations { get; }

    ChannelReader<ZoneChangedEvent> ZoneChanges { get; }

    ChannelReader<HudVitalsEvent> Vitals { get; }

    bool PublishChatLine(ChatLineEvent line);

    bool PublishBuffState(BuffStateEvent buffs);

    bool PublishCombatText(CombatTextEvent text);

    bool PublishTargetChanged(TargetChangedEvent target);

    bool PublishExpLevel(ExpLevelEvent exp);

    bool PublishStatAllocation(StatAllocationView view);

    bool PublishZoneChanged(ZoneChangedEvent zoneChanged);

    bool PublishVitals(HudVitalsEvent v);

    void Complete();
}