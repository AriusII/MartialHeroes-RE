using System.Runtime.InteropServices;

namespace MartialHeroes.Client.Domain.Inventory.Inventory;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ItemSlotRecord
{
    public const int WireSize = 16;

    public const uint StackCap = 1000;

    public const byte EmptyMarker = 0xFF;

    public static readonly ItemSlotRecord Empty = default;

    public readonly byte FlagsA;

    public readonly byte FlagsB;

    public readonly byte ClientFlagSrc;

    public readonly byte ClientFlagDst;

    public readonly uint ItemActorId;

    public readonly uint QtyOrExpiryLo;

    public readonly uint EnchantOrExpiryHi;

    public ItemSlotRecord(
        byte flagsA,
        byte flagsB,
        byte clientFlagSrc,
        byte clientFlagDst,
        uint itemActorId,
        uint qtyOrExpiryLo,
        uint enchantOrExpiryHi)
    {
        FlagsA = flagsA;
        FlagsB = flagsB;
        ClientFlagSrc = clientFlagSrc;
        ClientFlagDst = clientFlagDst;
        ItemActorId = itemActorId;
        QtyOrExpiryLo = qtyOrExpiryLo;
        EnchantOrExpiryHi = enchantOrExpiryHi;
    }

    public bool IsEmpty => ItemActorId == 0;

    public uint Quantity => QtyOrExpiryLo;

    public long ExpiryTimeT => (long)(((ulong)EnchantOrExpiryHi << 32) | QtyOrExpiryLo);

    public ItemSlotRecord WithStagingFlags(byte clientFlagSrc, byte clientFlagDst)
    {
        return new ItemSlotRecord(
            FlagsA,
            FlagsB,
            clientFlagSrc,
            clientFlagDst,
            ItemActorId,
            QtyOrExpiryLo,
            EnchantOrExpiryHi);
    }
}
