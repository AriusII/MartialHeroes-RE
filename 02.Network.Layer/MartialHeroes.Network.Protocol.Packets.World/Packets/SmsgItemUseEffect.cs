using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 139)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgItemUseEffect
{
    public const uint OpcodeId = Opcodes.SmsgItemUseEffect;

    public const int WireSize = 24;

    public readonly LeadingBlockBuffer LeadingBlock;

    public readonly uint ActorId;

    public readonly byte Result;

    public readonly byte FlagB;

    public readonly byte FlagC;

    public readonly byte Kind;

    public readonly TailBlockBuffer TailBlock;

    [InlineArray(4)]
    public struct LeadingBlockBuffer
    {
        private byte _element0;
    }

    [InlineArray(12)]
    public struct TailBlockBuffer
    {
        private byte _element0;
    }
}