using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Domain.Actors.Locomotion;

public static class Facing
{
    public static bool TryHeadingRaw(Vector3Fixed from, Vector3Fixed to, out int yawRaw)
    {
        var dx = (long)to.RawX - from.RawX;
        var dz = (long)to.RawZ - from.RawZ;

        if (dx == 0 && dz == 0)
        {
            yawRaw = 0;
            return false;
        }

        var radians = MathF.Atan2(dz, dx);
        yawRaw = (int)(radians * Vector3Fixed.One);
        return true;
    }
}
