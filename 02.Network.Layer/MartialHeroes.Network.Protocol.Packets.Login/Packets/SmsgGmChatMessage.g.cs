using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SmsgGmChatMessage
{
    public const uint OpcodeId = 0x3c350;

    public byte ChatType;
    public uint TextLength;
}
