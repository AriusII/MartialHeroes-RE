using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 74)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgStallListRefill
{
    public const uint OpcodeId = Opcodes.SmsgResponseSlot74;

    public const int HeaderSize = 6;

    public const int RecordStride = 36;

    public readonly byte Reserved0;

    public readonly uint Reserved1;

    public readonly byte Flag;
}