using MartialHeroes.Client.Domain.Actors.Actors;

namespace MartialHeroes.Client.Application.World;

/// <summary>
///     The application-owned registry of live actors, keyed by the composite
///     <see cref="ActorKey" /> <c>(rawId, sort)</c>.
/// </summary>
/// <remarks>
///     <para>
///         The Domain has no actor manager yet (it owns the individual <see cref="Actor" /> aggregate, not a
///         collection), so the orchestration layer keeps the world map here. spec: Docs/RE/structs/actor.md
///         ("<c>sort</c> doubles as the entity-type discriminator AND part of the actor-manager composite
///         key <c>(id, sort)</c>. Lookups use both.").
///     </para>
///     <para>
///         <b>Threading.</b> This registry is mutated only by the single network-reader logical owner (the
///         same owner that drives Domain mutation), so it is deliberately lock-free. Do not mutate it from
///         multiple threads; funnel additional producers through the inbound channel instead.
///     </para>
/// </remarks>
public sealed class ClientWorld
{
    private readonly Dictionary<ActorKey, Actor> _actors = new();

    /// <summary>Number of live actors currently registered.</summary>
    public int Count => _actors.Count;

    /// <summary>
    ///     The local (controlled) player's <see cref="ActorKey" />, or <see langword="null" /> until the
    ///     local player has been identified (e.g. via the 3/5 enter-game ack or the first self-spawn).
    /// </summary>
    /// <remarks>
    ///     The use-case layer reads this to source the local player's current position when building the
    ///     2/13 move request's Heading delta. spec: Docs/RE/packets/2-13_move_request.yaml (Heading is
    ///     atan2 of target - current). Set by the composition root / handlers when the self actor is known.
    /// </remarks>
    public ActorKey? LocalActorKey { get; set; }

    /// <summary>
    ///     The local player's live actor, or <see langword="null" /> when <see cref="LocalActorKey" /> is
    ///     unset or no matching actor is registered.
    /// </summary>
    public Actor? LocalActor =>
        LocalActorKey is { } key && _actors.TryGetValue(key, out var actor) ? actor : null;

    /// <summary>Enumerates the live actors. For diagnostics/tests; avoid on the hot receive path.</summary>
    public IReadOnlyCollection<Actor> Actors => _actors.Values;

    /// <summary>
    ///     The live actors as the concrete <see cref="Dictionary{TKey,TValue}.ValueCollection" /> so the
    ///     per-tick loop can <c>foreach</c> over the <b>struct</b> enumerator without boxing it (iterating
    ///     via the <see cref="IReadOnlyCollection{T}" /> interface would box the enumerator each tick). Use
    ///     this on the hot per-tick path; <see cref="Actors" /> stays for diagnostics/existing callers.
    /// </summary>
    public Dictionary<ActorKey, Actor>.ValueCollection ActorValues => _actors.Values;

    /// <summary>Registers or replaces the actor under its <see cref="Actor.Key" />.</summary>
    public void Add(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        _actors[actor.Key] = actor;
    }

    /// <summary>Looks up a live actor by key; returns <see langword="false" /> if absent.</summary>
    public bool TryGet(ActorKey key, out Actor actor)
    {
        return _actors.TryGetValue(key, out actor!);
    }

    /// <summary>Removes the actor under <paramref name="key" />, returning <see langword="true" /> if it existed.</summary>
    public bool Remove(ActorKey key)
    {
        return _actors.Remove(key);
    }

    /// <summary>Clears every registered actor (e.g. on world unload / disconnect).</summary>
    public void Clear()
    {
        _actors.Clear();
    }
}