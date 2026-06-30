using System.Threading.Channels;
using MartialHeroes.Client.Application.Contracts.Events;

namespace MartialHeroes.Client.Application.Contracts.Hud;

public sealed class HudEventHub : IHudEventHub
{
    public const int AppendCapacity = 256;

    public const int LatestWinsCapacity = 1;

    private readonly Channel<BuffStateEvent> _buffStates = CreateBounded<BuffStateEvent>(LatestWinsCapacity);

    private readonly Channel<ChatLineEvent> _chatLines = CreateBounded<ChatLineEvent>(AppendCapacity);
    private readonly Channel<CombatTextEvent> _combatTexts = CreateBounded<CombatTextEvent>(AppendCapacity);
    private readonly Channel<ExpLevelEvent> _expLevels = CreateBounded<ExpLevelEvent>(LatestWinsCapacity);

    private readonly Channel<InventorySlotsChangedEvent> _inventorySlots =
        CreateBounded<InventorySlotsChangedEvent>(AppendCapacity);

    private readonly Channel<QuestCompletedEvent> _questCompleted =
        CreateBounded<QuestCompletedEvent>(AppendCapacity);

    private readonly Channel<QuestLogChangedEvent> _questLog = CreateBounded<QuestLogChangedEvent>(LatestWinsCapacity);

    private readonly Channel<MailLetterArrivedEvent> _mailLetters = CreateBounded<MailLetterArrivedEvent>(AppendCapacity);

    private readonly Channel<DeliveryRecordUpdatedEvent> _deliveryRecords =
        CreateBounded<DeliveryRecordUpdatedEvent>(AppendCapacity);

    private readonly Channel<SkillHotbarSlotSetEvent> _skillHotbarSlots =
        CreateBounded<SkillHotbarSlotSetEvent>(AppendCapacity);

    private readonly Channel<StatAllocationView> _statAllocations =
        CreateBounded<StatAllocationView>(LatestWinsCapacity);

    private readonly Channel<TargetChangedEvent> _targetChanges = CreateBounded<TargetChangedEvent>(LatestWinsCapacity);
    private readonly Channel<HudVitalsEvent> _vitals = CreateBounded<HudVitalsEvent>(LatestWinsCapacity);
    private readonly Channel<HudVitalsEvent> _vitalsGauge = CreateBounded<HudVitalsEvent>(LatestWinsCapacity);
    private readonly Channel<ZoneChangedEvent> _zoneChanges = CreateBounded<ZoneChangedEvent>(LatestWinsCapacity);

    public ChannelReader<ExpLevelEvent> ExpLevels => _expLevels.Reader;


    public bool PublishChatLine(ChatLineEvent line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return _chatLines.Writer.TryWrite(line);
    }

    public bool PublishBuffState(BuffStateEvent buffs)
    {
        ArgumentNullException.ThrowIfNull(buffs);
        return _buffStates.Writer.TryWrite(buffs);
    }

    public bool PublishCombatText(CombatTextEvent text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return _combatTexts.Writer.TryWrite(text);
    }

    public bool PublishExpLevel(ExpLevelEvent exp)
    {
        ArgumentNullException.ThrowIfNull(exp);
        return _expLevels.Writer.TryWrite(exp);
    }

    public bool PublishZoneChanged(ZoneChangedEvent zoneChanged)
    {
        ArgumentNullException.ThrowIfNull(zoneChanged);
        return _zoneChanges.Writer.TryWrite(zoneChanged);
    }

    public bool PublishVitals(HudVitalsEvent v)
    {
        ArgumentNullException.ThrowIfNull(v);
        var primary = _vitals.Writer.TryWrite(v);
        var gauge = _vitalsGauge.Writer.TryWrite(v);
        return primary && gauge;
    }


    public ChannelReader<ChatLineEvent> ChatLines => _chatLines.Reader;

    public ChannelReader<BuffStateEvent> BuffStates => _buffStates.Reader;

    public ChannelReader<CombatTextEvent> CombatTexts => _combatTexts.Reader;

    public ChannelReader<TargetChangedEvent> TargetChanges => _targetChanges.Reader;

    public ChannelReader<StatAllocationView> StatAllocations => _statAllocations.Reader;

    public ChannelReader<ZoneChangedEvent> ZoneChanges => _zoneChanges.Reader;

    public ChannelReader<HudVitalsEvent> Vitals => _vitals.Reader;

    public ChannelReader<HudVitalsEvent> VitalsGauge => _vitalsGauge.Reader;

    public ChannelReader<InventorySlotsChangedEvent> InventorySlots => _inventorySlots.Reader;

    public ChannelReader<QuestLogChangedEvent> QuestLog => _questLog.Reader;

    public ChannelReader<QuestCompletedEvent> QuestCompleted => _questCompleted.Reader;

    public ChannelReader<MailLetterArrivedEvent> MailLetters => _mailLetters.Reader;

    public ChannelReader<DeliveryRecordUpdatedEvent> DeliveryRecords => _deliveryRecords.Reader;

    public ChannelReader<SkillHotbarSlotSetEvent> SkillHotbarSlots => _skillHotbarSlots.Reader;

    public bool PublishMailLetter(MailLetterArrivedEvent letter)
    {
        ArgumentNullException.ThrowIfNull(letter);
        return _mailLetters.Writer.TryWrite(letter);
    }

    public bool PublishDeliveryRecord(DeliveryRecordUpdatedEvent record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return _deliveryRecords.Writer.TryWrite(record);
    }

    public bool PublishSkillHotbarSlot(SkillHotbarSlotSetEvent slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        return _skillHotbarSlots.Writer.TryWrite(slot);
    }

    public bool PublishInventorySlots(InventorySlotsChangedEvent slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        return _inventorySlots.Writer.TryWrite(slots);
    }

    public bool PublishQuestLog(QuestLogChangedEvent log)
    {
        ArgumentNullException.ThrowIfNull(log);
        return _questLog.Writer.TryWrite(log);
    }

    public bool PublishQuestCompleted(QuestCompletedEvent completed)
    {
        ArgumentNullException.ThrowIfNull(completed);
        return _questCompleted.Writer.TryWrite(completed);
    }

    public void Complete()
    {
        _chatLines.Writer.TryComplete();
        _buffStates.Writer.TryComplete();
        _combatTexts.Writer.TryComplete();
        _targetChanges.Writer.TryComplete();
        _expLevels.Writer.TryComplete();
        _statAllocations.Writer.TryComplete();
        _zoneChanges.Writer.TryComplete();
        _vitals.Writer.TryComplete();
        _vitalsGauge.Writer.TryComplete();
        _inventorySlots.Writer.TryComplete();
        _questLog.Writer.TryComplete();
        _questCompleted.Writer.TryComplete();
        _mailLetters.Writer.TryComplete();
        _deliveryRecords.Writer.TryComplete();
        _skillHotbarSlots.Writer.TryComplete();
    }

    public bool PublishTargetChanged(TargetChangedEvent target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return _targetChanges.Writer.TryWrite(target);
    }

    public bool PublishStatAllocation(StatAllocationView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return _statAllocations.Writer.TryWrite(view);
    }

    private static Channel<T> CreateBounded<T>(int capacity)
    {
        return Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }
}