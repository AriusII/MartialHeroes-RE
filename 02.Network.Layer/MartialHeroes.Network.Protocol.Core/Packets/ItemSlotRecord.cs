using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Core.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ItemSlotRecord
{
    public const int WireSize = 16;

    public readonly uint Word0;

    public readonly uint ItemActorId;

    public readonly uint ExpiryLo;

    public readonly uint ExpiryHi;
}