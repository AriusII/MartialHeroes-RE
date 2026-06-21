using System.Threading.Channels;

namespace MartialHeroes.Client.Application.Contracts.Hud;

/// <summary>
///     The single facade the World-Scene HUD resolves to bind its widgets to application-layer state, and
///     that the packet handlers (the 4/102 buff, 5/9 exp, 5/52 combat, 5/7 chat, and
///     target-selection handlers) publish onto.
/// </summary>
/// <remarks>
///     <para>
///         <b>One stream per event family.</b> The HUD's chat log, buff bar, floating combat numbers, target
///         frame, XP gage, and stat panel are independent widgets with independent lifetimes, so each gets its
///         own <see cref="ChannelReader{T}" /> rather than a single multiplexed marker stream. A widget drains
///         only the stream it cares about with
///         <see cref="ChannelReader{T}.ReadAllAsync(System.Threading.CancellationToken)" />; it never sees
///         (and never pays the type-test cost for) events meant for another widget.
///     </para>
///     <para>
///         <b>Topology and backpressure</b> are documented on <see cref="HudEventHub" />: one logical producer
///         (the network reader that owns Domain mutation), one logical consumer per stream (a HUD widget), and
///         a deliberate bounded/drop-oldest-vs-latest-only policy per family.
///     </para>
/// </remarks>
public interface IHudEventHub
{
    // ---- Subscribe side (HUD widgets) ----------------------------------------------------------

    /// <summary>
    ///     The chat-log stream. Drain with
    ///     <see cref="ChannelReader{T}.ReadAllAsync(System.Threading.CancellationToken)" />.
    /// </summary>
    ChannelReader<ChatLineEvent> ChatLines { get; }

    /// <summary>The buff-bar stream (latest-wins refreshes).</summary>
    ChannelReader<BuffStateEvent> BuffStates { get; }

    /// <summary>The floating-combat-number stream.</summary>
    ChannelReader<CombatTextEvent> CombatTexts { get; }

    /// <summary>The target-frame stream (latest-wins).</summary>
    ChannelReader<TargetChangedEvent> TargetChanges { get; }

    /// <summary>The XP-gage stream (latest-wins).</summary>
    ChannelReader<ExpLevelEvent> ExpLevels { get; }

    /// <summary>The stat-panel stream (latest-wins).</summary>
    ChannelReader<StatAllocationView> StatAllocations { get; }

    /// <summary>
    ///     The zone-type change stream (latest-wins).
    ///     spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3.
    /// </summary>
    ChannelReader<ZoneChangedEvent> ZoneChanges { get; }

    /// <summary>
    ///     The local-player vitals stream (latest-wins). Drain with
    ///     <see cref="ChannelReader{T}.ReadAllAsync(System.Threading.CancellationToken)" /> on each frame tick.
    ///     spec: Docs/RE/specs/combat.md §12.2; Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml.
    /// </summary>
    ChannelReader<HudVitalsEvent> Vitals { get; }
    // ---- Publish side (packet handlers / stubs) ------------------------------------------------

    /// <summary>Publishes one chat line to the chat-log stream. Returns false only if dropped under backpressure.</summary>
    bool PublishChatLine(ChatLineEvent line);

    /// <summary>Publishes a full buff-bar refresh (latest-wins). Returns false only if dropped under backpressure.</summary>
    bool PublishBuffState(BuffStateEvent buffs);

    /// <summary>Publishes one floating combat number. Returns false only if dropped under backpressure.</summary>
    bool PublishCombatText(CombatTextEvent text);

    /// <summary>Publishes a target-frame change (latest-wins). Returns false only if dropped under backpressure.</summary>
    bool PublishTargetChanged(TargetChangedEvent target);

    /// <summary>Publishes an XP/level change (latest-wins). Returns false only if dropped under backpressure.</summary>
    bool PublishExpLevel(ExpLevelEvent exp);

    /// <summary>Publishes a refreshed stat-allocation view (latest-wins). Returns false only if dropped under backpressure.</summary>
    bool PublishStatAllocation(StatAllocationView view);

    /// <summary>
    ///     Publishes a zone-type change (latest-wins). Returns false only if dropped under backpressure.
    ///     spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3.
    /// </summary>
    bool PublishZoneChanged(ZoneChangedEvent zoneChanged);

    /// <summary>
    ///     Publishes a local-player vitals refresh (latest-wins). Returns false only if dropped under
    ///     backpressure.
    ///     spec: Docs/RE/specs/combat.md §12.2 (5/53 = canonical HP-bar source);
    ///     Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml (CurrentHp@0x10 / CurrentMp@0x14 / Stamina@0x18).
    /// </summary>
    bool PublishVitals(HudVitalsEvent v);

    /// <summary>Signals that no further HUD events will be published, completing every stream's reader for clean shutdown.</summary>
    void Complete();
}