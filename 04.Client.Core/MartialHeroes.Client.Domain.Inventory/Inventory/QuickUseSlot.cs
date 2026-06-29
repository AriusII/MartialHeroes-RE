using System.Runtime.InteropServices;

namespace MartialHeroes.Client.Domain.Inventory.Inventory;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct QuickUseSlot
{
    public const int WireSize = 8;

    public static readonly QuickUseSlot Empty;

    public readonly uint Occupancy;

    public readonly uint Secondary;

    public QuickUseSlot(uint occupancy, uint secondary)
    {
        Occupancy = occupancy;
        Secondary = secondary;
    }

    public bool IsOccupied => Occupancy != 0;
}