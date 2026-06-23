using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgStanceSwap
{
    public const uint OpcodeId = 0x20006;

    public const int Size = 8;

    public byte StanceId;
    public byte Flag;
    public ushort Reserved0;
    public uint TargetActorId;
}
