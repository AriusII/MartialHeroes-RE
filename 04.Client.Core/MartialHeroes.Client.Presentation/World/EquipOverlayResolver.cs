
namespace MartialHeroes.Client.Presentation.World;

public static class EquipOverlayResolver
{
    public const int SkinLevelThreshold = 1000;

    public const long NonWeaponGidScale = 10000L;

    public const int CategoryBaseEntryCount = 47;

    public const int WeaponSlot = 14;

    public const int
        WeaponHandBoneId = 0;


    public const int NodeFlagMainHand = 2;

    public const int NodeFlagOffHand = 1;

    public const int DualWieldBindClass = 3;

    private const long CatalogueKeyGidRadix = 1_000_000_000L;
    private const long CatalogueKeySlotRadix = 100L;

    private static readonly int[] FullSlotSet = [3, 4, 6, 2, 11, 14];

    private static readonly int[]
        ReducedSlotSet = [3];

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

    public static long ResolveWeaponGid(int b, int c, int d, int partActorId)
    {
        var weaponGid = 1000L * (b + 10L * (c + 10L * (d + 10L * (partActorId / 1_000_000L))));
        return weaponGid + partActorId % 1000L;
    }

    public static long ResolveNonWeaponGid(int partActorId)
    {
        return NonWeaponGidScale * (partActorId / 10000L) + partActorId % 100L;
    }

    public static long ResolvePartGid(int slot, int partActorId, int b, int c, int d)
    {
        return slot == WeaponSlot
            ? ResolveWeaponGid(b, c, d, partActorId)
            : ResolveNonWeaponGid(partActorId);
    }

    public static long ResolveBaseTerm(int variantField, int classField)
    {
        return 5L * (variantField + 4L * classField) - 24L;
    }

    public static long ComposeCatalogueKey64(int slot, long baseTerm, long gid)
    {
        return gid + CatalogueKeyGidRadix *
            (slot + CatalogueKeySlotRadix * baseTerm);
    }

    public static int WeaponNodeFlag(int bindClass, bool offHand)
    {
        if (bindClass != DualWieldBindClass)
            return bindClass;

        return offHand ? NodeFlagOffHand : NodeFlagMainHand;
    }
}