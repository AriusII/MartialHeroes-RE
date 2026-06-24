using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 146)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgAckRequest
{
    public const uint OpcodeId = Opcodes.SmsgPacketResponseAckRequest;

    public const int WireSize = 8;

    public readonly uint Token;

    public readonly uint ValidationValue;
}
