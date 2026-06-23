using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgRelationNamedAction
{
    public const uint OpcodeId = 0x2003f;

    public const int Size = 17;

    public NameBuffer Name;
    public byte Mode;

    [InlineArray(16)]
    public struct NameBuffer
    {
        private byte _element0;
    }

}
