using MartialHeroes.Assets.Mapping;
using MartialHeroes.Client.Application.World;

namespace MartialHeroes.Client.Presentation.Adapters;

public sealed class AssembledCellViewAdapter : IAssembledCellView
{
    public AssembledCellViewAdapter(AssembledCell cell)
    {
        ConcreteCell = cell ?? throw new ArgumentNullException(nameof(cell));
    }

    public AssembledCell ConcreteCell { get; }

    public int MapX => ConcreteCell.MapX;

    public int MapZ => ConcreteCell.MapZ;

    public bool IsResolved =>
        ConcreteCell.Slot0GroundTexGrid is not null || ConcreteCell.Slot1BuildingObjectGrid is not null;
}