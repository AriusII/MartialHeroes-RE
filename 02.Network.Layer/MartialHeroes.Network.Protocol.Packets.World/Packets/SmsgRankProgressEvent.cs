using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 48)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgRankProgressEvent
{
    public const uint OpcodeId = Opcodes.SmsgRankProgressEvent;

    public const int WireSize = 236;

    public readonly HeaderPad0Buffer HeaderPad0;

    public readonly byte RouteByte;

    public readonly byte SubSelect;

    public readonly ReservedABuffer ReservedA;

    public readonly RecordArrayBuffer RecordArray;

    [InlineArray(8)]
    public struct HeaderPad0Buffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct ReservedABuffer
    {
        private byte _element0;
    }

    [InlineArray(224)]
    public struct RecordArrayBuffer
    {
        private byte _element0;
    }
}
