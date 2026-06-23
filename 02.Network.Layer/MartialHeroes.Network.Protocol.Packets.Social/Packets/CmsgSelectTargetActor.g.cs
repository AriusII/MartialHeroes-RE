using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgSelectTargetActor
{
    public const uint OpcodeId = 0x2000c;

    public const int Size = 3;

    public byte SelectMode;
    public byte SubIndex;
    public byte SlotIndex;
}
