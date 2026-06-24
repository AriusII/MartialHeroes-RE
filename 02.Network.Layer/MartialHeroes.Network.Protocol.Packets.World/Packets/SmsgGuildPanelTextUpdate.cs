using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 103)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgGuildPanelTextUpdate
{
    public const uint OpcodeId = Opcodes.SmsgGuildPanelTextUpdate;

    public const int WireSize = 204;

    public readonly HeaderPad0Buffer HeaderPad0;

    public readonly TextCell1Buffer TextCell1;

    public readonly TextCell2Buffer TextCell2;

    public readonly TextCell3Buffer TextCell3;

    public readonly TextCell4Buffer TextCell4;

    [InlineArray(8)]
    public struct HeaderPad0Buffer
    {
        private byte _element0;
    }

    [InlineArray(49)]
    public struct TextCell1Buffer
    {
        private byte _element0;
    }

    [InlineArray(49)]
    public struct TextCell2Buffer
    {
        private byte _element0;
    }

    [InlineArray(49)]
    public struct TextCell3Buffer
    {
        private byte _element0;
    }

    [InlineArray(49)]
    public struct TextCell4Buffer
    {
        private byte _element0;
    }
}
