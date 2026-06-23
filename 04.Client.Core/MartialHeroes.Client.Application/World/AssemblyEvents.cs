using MartialHeroes.Client.Application.Contracts.Events;

namespace MartialHeroes.Client.Application.World;

public interface IAssembledCellView
{
    int MapX { get; }

    int MapZ { get; }

    bool IsResolved { get; }
}

public interface IAssembledAreaView
{
    int AreaId { get; }

    int CellKeyCount { get; }

    IReadOnlyList<AreaSpawnDescriptor> Spawns { get; }
}

public readonly record struct AreaSpawnDescriptor(
    float WorldX,
    float WorldZ,
    float Yaw,
    int VisualId,
    bool IsNpc);

public sealed record AreaAssembledEvent(IAssembledAreaView Area) : IClientEvent;

public sealed record CellAssembledEvent(IAssembledCellView Cell) : IClientEvent;