using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgRepairCommit
{
    public const uint OpcodeId = 0x20071;

    public uint TargetId;
    public uint SelIndex;
}
