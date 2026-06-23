namespace MartialHeroes.Client.Presentation.World;

public static class EquipOverlayResolver
{
    public const int SkinLevelThreshold = 1000;
    public const long NonWeaponGidScale = 10000L;
    public const int WeaponSlot = 14;
    private static readonly int[] FullSlotSet = [3, 4, 6, 2, 11, 14];
    private static readonly int[] ReducedSlotSet = [3];
    private static readonly int[] OtherActorSlotSet = [3, 4, 6, 2, 11];

    public static bool RunsOverlayResolution(int baseSkinId)
    {
        return baseSkinId <= SkinLevelThreshold;
    }

    public static ReadOnlySpan<int> LocalPlayerRebuildSlots(int baseSkinId)
    {
        return baseSkinId <= SkinLevelThreshold ? FullSlotSet : ReducedSlotSet;
    }

    public static ReadOnlySpan<int> OtherActorRebuildSlots()
    {
        return OtherActorSlotSet;
    }

    public static long ResolveNonWeaponGid(int partActorId)
    {
        return NonWeaponGidScale * (partActorId / 10000L) + partActorId % 100L;
    }
}