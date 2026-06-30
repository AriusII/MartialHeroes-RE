using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Domain.Actors.Locomotion;

public static class LocomotionThresholds
{
    private static readonly Int128 OneSquared = (Int128)Vector3Fixed.One * Vector3Fixed.One;

    public static readonly Int128 ArrivalSquared = 4 * OneSquared;

    public static readonly Int128 ExactSetSquared = OneSquared / 2;

    public static readonly Int128 ClickRecenterSquared = 400 * OneSquared;

    public static readonly Int128 NormalInterpMaxSquared = 40000 * OneSquared;

    public static readonly Int128 FastCatchUpMaxSquared = 90000 * OneSquared;
}
