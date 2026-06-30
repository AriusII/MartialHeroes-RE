using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Domain.Actors.Locomotion;

public static class PlanarDistance
{
    public static Int128 SquaredXz(Vector3Fixed a, Vector3Fixed b)
    {
        var dx = (long)a.RawX - b.RawX;
        var dz = (long)a.RawZ - b.RawZ;
        return (Int128)dx * dx + (Int128)dz * dz;
    }
}
