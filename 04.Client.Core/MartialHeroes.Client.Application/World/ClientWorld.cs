using MartialHeroes.Client.Domain.Actors.Actors;

namespace MartialHeroes.Client.Application.World;

public sealed class ClientWorld
{
    private readonly Dictionary<ActorKey, Actor> _actors = new();

    public int Count => _actors.Count;

    public ActorKey? LocalActorKey { get; set; }

    public Actor? LocalActor =>
        LocalActorKey is { } key && _actors.TryGetValue(key, out var actor) ? actor : null;

    public IReadOnlyCollection<Actor> Actors => _actors.Values;

    public Dictionary<ActorKey, Actor>.ValueCollection ActorValues => _actors.Values;

    public void Add(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        _actors[actor.Key] = actor;
    }

    public bool TryGet(ActorKey key, out Actor actor)
    {
        return _actors.TryGetValue(key, out actor!);
    }

    public bool Remove(ActorKey key)
    {
        return _actors.Remove(key);
    }

    public void Clear()
    {
        _actors.Clear();
    }
}