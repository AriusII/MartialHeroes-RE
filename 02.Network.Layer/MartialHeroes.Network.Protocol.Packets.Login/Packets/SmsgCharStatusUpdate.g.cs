using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

[PacketOpcode(3, 13)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SmsgCharStatusUpdate
{
    public const uint OpcodeId = 0x3000d;

    public const int Size = 19;

    public byte Result;
    public byte ErrorCode;
    public NameBuffer Name;

    [InlineArray(17)]
    public struct NameBuffer
    {
        private byte _element0;
    }

}
