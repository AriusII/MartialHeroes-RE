using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 15)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgItemWorldPickupAck
{
    public const uint OpcodeId = Opcodes.SmsgItemWorldPickupAck;

    public const int WireSize = 36;

    public readonly HeaderBuffer Header;

    public readonly byte Result;

    public readonly byte Subtype;

    public readonly Pad0Buffer Pad0;

    public readonly int Echo0;

    public readonly Echo1Buffer Echo1;

    public readonly uint ItemId;

    public readonly int Echo2;

    public readonly int Echo3;

    [InlineArray(8)]
    public struct HeaderBuffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

    [InlineArray(8)]
    public struct Echo1Buffer
    {
        private byte _element0;
    }
}
