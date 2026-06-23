using System.Threading.Channels;

namespace MartialHeroes.Client.Application.Contracts.Hud;

public sealed class HudEventHub : IHudEventHub
{
    public const int AppendCapacity = 256;

    public const int LatestWinsCapacity = 1;

    private readonly Channel<BuffStateEvent> _buffStates = CreateBounded<BuffStateEvent>(LatestWinsCapacity);

    private readonly Channel<ChatLineEvent> _chatLines = CreateBounded<ChatLineEvent>(AppendCapacity);
    private readonly Channel<CombatTextEvent> _combatTexts = CreateBounded<CombatTextEvent>(AppendCapacity);
    private readonly Channel<ExpLevelEvent> _expLevels = CreateBounded<ExpLevelEvent>(LatestWinsCapacity);

    private readonly Channel<StatAllocationView> _statAllocations =
        CreateBounded<StatAllocationView>(LatestWinsCapacity);

    private readonly Channel<TargetChangedEvent> _targetChanges = CreateBounded<TargetChangedEvent>(LatestWinsCapacity);
    private readonly Channel<HudVitalsEvent> _vitals = CreateBounded<HudVitalsEvent>(LatestWinsCapacity);
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
        return _vitals.Writer.TryWrite(v);
    }


    public ChannelReader<ChatLineEvent> ChatLines => _chatLines.Reader;

    public ChannelReader<BuffStateEvent> BuffStates => _buffStates.Reader;

    public ChannelReader<CombatTextEvent> CombatTexts => _combatTexts.Reader;

    public ChannelReader<TargetChangedEvent> TargetChanges => _targetChanges.Reader;

    public ChannelReader<StatAllocationView> StatAllocations => _statAllocations.Reader;

    public ChannelReader<ZoneChangedEvent> ZoneChanges => _zoneChanges.Reader;

    public ChannelReader<HudVitalsEvent> Vitals => _vitals.Reader;

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