
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

[PacketOpcode(3, 5)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgEnterGameAck
{
    public const uint OpcodeId = Opcodes.SmsgEnterGameAck;

    public const int WireSize = 44;

    public readonly NameBlockBuffer NameBlock;

    public readonly uint BillingFlag;

    public readonly BlockTailBuffer BlockTail;

    public readonly uint CharacterCount;

    [InlineArray(28)]
    public struct NameBlockBuffer
    {
        private byte _element0;
    }

    [InlineArray(8)]
    public struct BlockTailBuffer
    {
        private byte _element0;
    }
}