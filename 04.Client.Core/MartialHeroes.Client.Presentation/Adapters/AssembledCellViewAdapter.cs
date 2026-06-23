using MartialHeroes.Assets.Mapping;
using MartialHeroes.Client.Application.World;

namespace MartialHeroes.Client.Presentation.Adapters;

public sealed class AssembledCellViewAdapter(AssembledCell cell) : IAssembledCellView
{
    public AssembledCell ConcreteCell { get; } = cell ?? throw new ArgumentNullException(nameof(cell));

    public int MapX => ConcreteCell.MapX;

    public int MapZ => ConcreteCell.MapZ;

    public bool IsResolved =>
        ConcreteCell.Slot0GroundTexGrid is not null || ConcreteCell.Slot1BuildingObjectGrid is not null;
}