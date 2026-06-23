using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Domain.Actors.Actors;

namespace MartialHeroes.Client.Application.World;


public sealed class ActorSpawnService
{
    private readonly ActorComposer _composer;
    private readonly IClientEventBus _eventBus;
    private readonly ClientWorld _world;

    public ActorSpawnService(ActorComposer composer, IClientEventBus eventBus, ClientWorld world)
    {
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(world);
        _composer = composer;
        _eventBus = eventBus;
        _world = world;
    }

    public AssembledActor Spawn(in ActorSpawn spawn)
    {
        var actor = _composer.Compose(in spawn);
        _eventBus.Publish(new ActorAssembledEvent(actor));
        return actor;
    }

    public AssembledActor Spawn(ReadOnlySpan<byte> descriptor, in ActorSpawn identity, Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var assembled = _composer.Compose(descriptor, in identity);

        _world.Add(actor);

        _eventBus.Publish(new ActorAssembledEvent(assembled));
        return assembled;
    }
}