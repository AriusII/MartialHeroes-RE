using MartialHeroes.Client.Application.Contracts.Events;

namespace MartialHeroes.Client.Application.World;

public sealed class AreaAssemblyHandoff
{
    public delegate IAssembledAreaView? AreaBake(int AreaId);

    private readonly AreaBake _bake;

    private readonly IClientEventBus _eventBus;


    public AreaAssemblyHandoff(IClientEventBus eventBus, AreaBake bake)
    {
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(bake);
        _eventBus = eventBus;
        _bake = bake;
    }

    public int? PublishedAreaId { get; private set; }

    public bool OnAreaBound(int areaId)
    {
        if (PublishedAreaId == areaId) return false;

        var area = _bake(areaId);
        if (area is null)
            return false;

        PublishedAreaId = areaId;
        return _eventBus.Publish(new AreaAssembledEvent(area));
    }
}