using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgQuestNpcStep
{
    public const uint OpcodeId = 0x2006e;

    public const int Size = 4;

    public byte Mode;
    public byte ArgA;
    public byte ArgB;
    public byte ArgC;
}
