using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgNpcMediatedNameRequest
{
    public const uint OpcodeId = 0x20054;

    public const int Size = 19;

    public byte Kind;
    public byte NearbyNpcIdx;
    public NameBuffer Name;

    [InlineArray(17)]
    public struct NameBuffer
    {
        private byte _element0;
    }

}
