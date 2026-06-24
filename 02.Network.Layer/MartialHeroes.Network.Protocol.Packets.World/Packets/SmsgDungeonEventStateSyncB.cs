using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 86)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgDungeonEventStateSyncB
{
    public const uint OpcodeId = Opcodes.SmsgDungeonEventStateSyncB;

    public const int WireSize = 20;

    public readonly HeaderBuffer Header;

    public readonly byte Mode;

    public readonly Pad1Buffer Pad1;

    public readonly uint ValueA;

    public readonly uint ValueB;

    [InlineArray(8)]
    public struct HeaderBuffer
    {
        private byte _element0;
    }

    [InlineArray(3)]
    public struct Pad1Buffer
    {
        private byte _element0;
    }
}
