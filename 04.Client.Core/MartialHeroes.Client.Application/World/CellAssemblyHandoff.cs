using MartialHeroes.Client.Application.Contracts.Events;

namespace MartialHeroes.Client.Application.World;


public sealed class CellAssemblyHandoff
{
    public delegate IAssembledCellView? CellBake(int MapX, int MapZ, ReadOnlyMemory<byte> Payload);

    private readonly CellBake _bake;

    private readonly IClientEventBus _eventBus;

    public CellAssemblyHandoff(IClientEventBus eventBus, CellBake bake)
    {
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(bake);
        _eventBus = eventBus;
        _bake = bake;
    }

    public bool OnSectorLoaded(SectorLoadedEvent loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);

        var cell = _bake(loaded.MapX, loaded.MapZ, loaded.Payload);
        if (cell is null)
            return false;

        return _eventBus.Publish(new CellAssembledEvent(cell));
    }
}