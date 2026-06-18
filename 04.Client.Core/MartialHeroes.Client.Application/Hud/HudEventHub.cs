using System.Threading.Channels;

namespace MartialHeroes.Client.Application.Hud;

/// <summary>
/// Default <see cref="IHudEventHub"/>: one <see cref="Channel{T}"/> per HUD event family, sized by the
/// family's consumption shape.
/// </summary>
/// <remarks>
/// <para>
/// <b>Topology.</b> Every stream is configured <c>SingleReader = true</c>, <c>SingleWriter = true</c>:
/// the lone producer is the network reader that owns Domain mutation (today, the stub publishers), and
/// the lone consumer is the one HUD widget that binds to that family. This is the cheapest, lock-light
/// synchronisation the BCL offers for this fan-in/fan-out shape and matches
/// <see cref="MartialHeroes.Client.Application.Events.ClientEventBus"/>. If a future topology adds a
/// second producer, funnel it through the inbound ingestion channel rather than relaxing this flag.
/// </para>
/// <para>
/// <b>Backpressure — chosen per family.</b> Two policies, both non-blocking
/// (<see cref="ChannelWriter{T}.TryWrite"/> never stalls the network reader):
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <b>Append families</b> — <see cref="ChatLineEvent"/> and <see cref="CombatTextEvent"/> — are
/// per-event and each one matters (a dropped chat line / damage number is a visible gap), so they use
/// a generous bound (<see cref="AppendCapacity"/>) with <see cref="BoundedChannelFullMode.DropOldest"/>:
/// under an extreme burst the oldest pending item is shed rather than stalling the reader or growing
/// unbounded.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Latest-wins families</b> — <see cref="BuffStateEvent"/>, <see cref="TargetChangedEvent"/>,
/// <see cref="ExpLevelEvent"/>, and <see cref="StatAllocationView"/> — are full-state snapshots where
/// only the freshest value is meaningful (a buff-bar refresh supersedes the previous one entirely;
/// the XP gage only shows the current fill). They use capacity <see cref="LatestWinsCapacity"/> with
/// <see cref="BoundedChannelFullMode.DropOldest"/>, so a burst collapses to the newest snapshot — the
/// HUD never repaints stale intermediate state.
/// </description>
/// </item>
/// </list>
/// <para>
/// Each publish stays allocation-light: it forwards an already-built immutable record to
/// <see cref="ChannelWriter{T}.TryWrite"/> with no per-call LINQ or closures.
/// </para>
/// </remarks>
public sealed class HudEventHub : IHudEventHub
{
    /// <summary>Bound for append-style streams (chat, combat text): a generous one-burst window.</summary>
    public const int AppendCapacity = 256;

    /// <summary>Bound for latest-wins streams (buff/target/exp/stat): keep only the freshest snapshot.</summary>
    public const int LatestWinsCapacity = 1;

    private readonly Channel<ChatLineEvent> _chatLines;
    private readonly Channel<BuffStateEvent> _buffStates;
    private readonly Channel<CombatTextEvent> _combatTexts;
    private readonly Channel<TargetChangedEvent> _targetChanges;
    private readonly Channel<ExpLevelEvent> _expLevels;
    private readonly Channel<StatAllocationView> _statAllocations;
    private readonly Channel<ZoneChangedEvent> _zoneChanges;
    private readonly Channel<HudVitalsEvent> _vitals;

    /// <summary>Creates a hub with one channel per event family sized per the documented policy.</summary>
    public HudEventHub()
    {
        _chatLines = CreateBounded<ChatLineEvent>(AppendCapacity);
        _combatTexts = CreateBounded<CombatTextEvent>(AppendCapacity);
        _buffStates = CreateBounded<BuffStateEvent>(LatestWinsCapacity);
        _targetChanges = CreateBounded<TargetChangedEvent>(LatestWinsCapacity);
        _expLevels = CreateBounded<ExpLevelEvent>(LatestWinsCapacity);
        _statAllocations = CreateBounded<StatAllocationView>(LatestWinsCapacity);
        _zoneChanges = CreateBounded<ZoneChangedEvent>(LatestWinsCapacity);
        // spec: Docs/RE/specs/combat.md §12.2 — latest-wins: only the freshest absolute vitals matter.
        _vitals = CreateBounded<HudVitalsEvent>(LatestWinsCapacity);
    }

    private static Channel<T> CreateBounded<T>(int capacity) =>
        Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    // ---- Publish side --------------------------------------------------------------------------

    /// <inheritdoc />
    public bool PublishChatLine(ChatLineEvent line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return _chatLines.Writer.TryWrite(line);
    }

    /// <inheritdoc />
    public bool PublishBuffState(BuffStateEvent buffs)
    {
        ArgumentNullException.ThrowIfNull(buffs);
        return _buffStates.Writer.TryWrite(buffs);
    }

    /// <inheritdoc />
    public bool PublishCombatText(CombatTextEvent text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return _combatTexts.Writer.TryWrite(text);
    }

    /// <inheritdoc />
    public bool PublishTargetChanged(TargetChangedEvent target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return _targetChanges.Writer.TryWrite(target);
    }

    /// <inheritdoc />
    public bool PublishExpLevel(ExpLevelEvent exp)
    {
        ArgumentNullException.ThrowIfNull(exp);
        return _expLevels.Writer.TryWrite(exp);
    }

    /// <inheritdoc />
    public bool PublishStatAllocation(StatAllocationView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return _statAllocations.Writer.TryWrite(view);
    }

    /// <inheritdoc />
    public bool PublishZoneChanged(ZoneChangedEvent zoneChanged)
    {
        ArgumentNullException.ThrowIfNull(zoneChanged);
        return _zoneChanges.Writer.TryWrite(zoneChanged);
    }

    /// <inheritdoc />
    public bool PublishVitals(HudVitalsEvent v)
    {
        ArgumentNullException.ThrowIfNull(v);
        return _vitals.Writer.TryWrite(v);
    }

    // ---- Subscribe side ------------------------------------------------------------------------

    /// <inheritdoc />
    public ChannelReader<ChatLineEvent> ChatLines => _chatLines.Reader;

    /// <inheritdoc />
    public ChannelReader<BuffStateEvent> BuffStates => _buffStates.Reader;

    /// <inheritdoc />
    public ChannelReader<CombatTextEvent> CombatTexts => _combatTexts.Reader;

    /// <inheritdoc />
    public ChannelReader<TargetChangedEvent> TargetChanges => _targetChanges.Reader;

    /// <inheritdoc />
    public ChannelReader<ExpLevelEvent> ExpLevels => _expLevels.Reader;

    /// <inheritdoc />
    public ChannelReader<StatAllocationView> StatAllocations => _statAllocations.Reader;

    /// <inheritdoc />
    public ChannelReader<ZoneChangedEvent> ZoneChanges => _zoneChanges.Reader;

    /// <inheritdoc />
    public ChannelReader<HudVitalsEvent> Vitals => _vitals.Reader;

    /// <inheritdoc />
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
}