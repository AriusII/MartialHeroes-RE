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

    private readonly ushort _pad0;

    public readonly uint WorldKey;

    public readonly uint SlotParam;

    private readonly GapBuffer _gap1;

    public readonly uint ItemId;

    public readonly int Count;

    public readonly int Opaque;

    [InlineArray(8)]
    public struct HeaderBuffer
    {
        private byte _element0;
    }

    [InlineArray(4)]
    public struct GapBuffer
    {
        private byte _element0;
    }
}
