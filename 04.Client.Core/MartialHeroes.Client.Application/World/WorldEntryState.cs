using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.World;

public sealed class WorldEntryState
{
    public bool IsActive { get; private set; }

    public int AreaId { get; private set; }

    public Vector3Fixed SpawnPosition { get; private set; } = Vector3Fixed.Zero;

    public void Record(int areaId, Vector3Fixed spawnPosition)
    {
        IsActive = true;
        AreaId = areaId;
        SpawnPosition = spawnPosition;
    }

    public void Clear()
    {
        IsActive = false;
        AreaId = 0;
        SpawnPosition = Vector3Fixed.Zero;
    }
}