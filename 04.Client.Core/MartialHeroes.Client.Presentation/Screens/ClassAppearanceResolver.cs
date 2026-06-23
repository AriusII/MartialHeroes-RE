namespace MartialHeroes.Client.Presentation.Screens;

public static class ClassAppearanceResolver
{
    private const long Slot14BodySlot = 14;
    public static readonly int[] OverlaySlots = [3, 4, 6, 2, 11, 14];

    public static int ModelClassId(int internalClass, int appearanceVariant)
    {
        if (appearanceVariant == 3) return 0;

        return 5 * (internalClass + 4 * appearanceVariant) - 24;
    }

    public static int SkeletonIdBForModelClassId(int modelClassId)
    {
        return modelClassId switch
        {
            1 => 1,
            26 => 2,
            11 => 3,
            16 => 4,
            _ => 0
        };
    }

    public static long ResolveBodyGidSlot14(int d, int a, int b, int partId)
    {
        return 1000L * (d + 10L * (a + 10L * (b + 10L * (partId / 1_000_000L))));
    }

    public static long ResolveWornItemGid(int partId)
    {
        return 10000L * (partId / 10000) + partId % 100;
    }

    public static long ResolvePartGid(int slot, int partId, int d, int a, int b)
    {
        return slot == Slot14BodySlot
            ? ResolveBodyGidSlot14(d, a, b, partId)
            : ResolveWornItemGid(partId);
    }

    public static string DeformSkinPathForGid(long gid)
    {
        return $"data/char/skin/g{gid}.skn";
    }

    public static int StarterAppearanceVariant(int internalClass)
    {
        return internalClass switch
        {
            1 => 1,
            2 => 2,
            3 => 1,
            4 => 1,
            _ => 0
        };
    }

    public static int StarterBodyModelClassId(int internalClass)
    {
        var variant = StarterAppearanceVariant(internalClass);
        return variant == 0 ? 0 : ModelClassId(internalClass, variant);
    }

    public static string BodySknPathForMeshGid(int meshGid)
    {
        return $"data/char/skin/g{meshGid}.skn";
    }
}