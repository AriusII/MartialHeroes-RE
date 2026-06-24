using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 92)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgPvpRequestOrNotice
{
    public const uint OpcodeId = Opcodes.SmsgPvpRequestOrNotice;

    public const int WireSize = 40;

    public readonly byte Sort;

    public readonly GapABuffer GapA;

    public readonly uint ActorId;

    public readonly uint DwordB;

    public readonly uint DwordC;

    public readonly uint TargetId;

    public readonly NameBuffer Name;

    public readonly TailPadBuffer TailPad;

    [InlineArray(3)]
    public struct GapABuffer
    {
        private byte _element0;
    }

    [InlineArray(17)]
    public struct NameBuffer
    {
        private byte _element0;
    }

    [InlineArray(3)]
    public struct TailPadBuffer
    {
        private byte _element0;
    }
}