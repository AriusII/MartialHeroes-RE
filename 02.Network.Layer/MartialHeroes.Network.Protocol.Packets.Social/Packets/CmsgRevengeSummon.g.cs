using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgRevengeSummon
{
    public const uint OpcodeId = 0x2005c;

    public const int Size = 4;

    public uint TargetRef;
}
