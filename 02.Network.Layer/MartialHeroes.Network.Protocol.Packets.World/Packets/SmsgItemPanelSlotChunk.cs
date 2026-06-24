using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 149)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgItemPanelSlotChunk
{
    public const uint OpcodeId = Opcodes.SmsgItemPanelSlotChunk;

    public const int HeaderSize = 12;

    public readonly byte Guard;

    public readonly Pad0Buffer Pad0;

    public readonly uint PlayerId;

    public readonly byte ChunkType;

    public readonly byte StartIndex;

    public readonly byte Count;

    public readonly Pad1Buffer Pad1;

    [InlineArray(3)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

    [InlineArray(1)]
    public struct Pad1Buffer
    {
        private byte _element0;
    }
}