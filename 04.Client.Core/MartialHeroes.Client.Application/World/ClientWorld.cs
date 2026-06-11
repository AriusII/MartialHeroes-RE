using MartialHeroes.Client.Domain.Actors;

namespace MartialHeroes.Client.Application.World;

/// <summary>
/// The application-owned registry of live actors, keyed by the composite
/// <see cref="ActorKey"/> <c>(rawId, sort)</c>.
/// </summary>
/// <remarks>
/// <para>
/// The Domain has no actor manager yet (it owns the individual <see cref="Actor"/> aggregate, not a
/// collection), so the orchestration layer keeps the world map here. spec: Docs/RE/structs/actor.md
/// ("<c>sort</c> doubles as the entity-type discriminator AND part of the actor-manager composite
/// key <c>(id, sort)</c>. Lookups use both.").
/// </para>
/// <para>
/// <b>Threading.</b> This registry is mutated only by the single network-reader logical owner (the
/// same owner that drives Domain mutation), so it is deliberately lock-free. Do not mutate it from
/// multiple threads; funnel additional producers through the inbound channel instead.
/// </para>
/// </remarks>
public sealed class ClientWorld
{
    private readonly Dictionary<ActorKey, Actor> _actors = new();

    /// <summary>Number of live actors currently registered.</summary>
    public int Count => _actors.Count;

    /// <summary>Registers or replaces the actor under its <see cref="Actor.Key"/>.</summary>
    public void Add(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        _actors[actor.Key] = actor;
    }

    /// <summary>Looks up a live actor by key; returns <see langword="false"/> if absent.</summary>
    public bool TryGet(ActorKey key, out Actor actor) => _actors.TryGetValue(key, out actor!);

    /// <summary>Removes the actor under <paramref name="key"/>, returning <see langword="true"/> if it existed.</summary>
    public bool Remove(ActorKey key) => _actors.Remove(key);

    /// <summary>Clears every registered actor (e.g. on world unload / disconnect).</summary>
    public void Clear() => _actors.Clear();

    /// <summary>Enumerates the live actors. For diagnostics/tests; avoid on the hot receive path.</summary>
    public IReadOnlyCollection<Actor> Actors => _actors.Values;
}
