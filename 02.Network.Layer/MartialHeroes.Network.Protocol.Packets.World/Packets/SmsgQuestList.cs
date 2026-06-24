using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 68)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgQuestList
{
    public const uint OpcodeId = Opcodes.SmsgQuestList;

    public const int WireSize = 452;

    public readonly LeadRegionBuffer LeadRegion;

    public readonly byte MirrorFlagC;

    public readonly byte PanelOpenGate;

    public readonly byte MirrorFlagB;

    public readonly ByteAColumnBuffer ByteAColumn;

    public readonly ByteBColumnBuffer ByteBColumn;

    public readonly Gap0Buffer Gap0;

    public readonly ValueColumnBuffer ValueColumn;

    public readonly NameColumnBuffer NameColumn;

    [InlineArray(8)]
    public struct LeadRegionBuffer
    {
        private byte _element0;
    }

    [InlineArray(10)]
    public struct ByteAColumnBuffer
    {
        private byte _element0;
    }

    [InlineArray(10)]
    public struct ByteBColumnBuffer
    {
        private byte _element0;
    }

    [InlineArray(1)]
    public struct Gap0Buffer
    {
        private byte _element0;
    }

    [InlineArray(80)]
    public struct ValueColumnBuffer
    {
        private byte _element0;
    }

    [InlineArray(340)]
    public struct NameColumnBuffer
    {
        private byte _element0;
    }
}