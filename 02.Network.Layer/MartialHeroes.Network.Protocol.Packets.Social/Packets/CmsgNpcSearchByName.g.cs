using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgNpcSearchByName
{
    public const uint OpcodeId = 0x20087;

    public const int Size = 21;

    public SearchNameBuffer SearchName;
    public byte Pad;

    [InlineArray(20)]
    public struct SearchNameBuffer
    {
        private byte _element0;
    }

}
