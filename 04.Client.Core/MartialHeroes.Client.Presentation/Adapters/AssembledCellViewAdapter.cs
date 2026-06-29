using MartialHeroes.Assets.Mapping;
using MartialHeroes.Client.Application.World;

namespace MartialHeroes.Client.Presentation.Adapters;

public sealed class AssembledCellViewAdapter(AssembledCell cell) : IAssembledCellView
{
    public AssembledCell ConcreteCell { get; } = cell ?? throw new ArgumentNullException(nameof(cell));

    public int MapX => ConcreteCell.MapX;

    public int MapZ => ConcreteCell.MapZ;

    public bool IsResolved =>
        ConcreteCell.Slot0GroundTexGrid is not null
        || ConcreteCell.Slot1BuildingObjectGrid is not null
        || ConcreteCell.Slot2Fx1 is not null
        || ConcreteCell.Slot3Fx2 is not null
        || ConcreteCell.Slot4Fx3 is not null
        || ConcreteCell.Slot5Fx4 is not null
        || ConcreteCell.Slot6Fx5 is not null
        || ConcreteCell.Slot7Fx6 is not null
        || ConcreteCell.Slot8Fx7 is not null
        || ConcreteCell.Collision is not null
        || ConcreteCell.ExtraTerrainTriangles is not null
        || ConcreteCell.OverhangTriangles is not null
        || ConcreteCell.SoundGrid is not null;
}