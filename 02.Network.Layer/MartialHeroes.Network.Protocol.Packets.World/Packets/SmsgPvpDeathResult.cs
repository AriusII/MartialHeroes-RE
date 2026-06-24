using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 80)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgPvpDeathResult
{
    public const uint OpcodeId = Opcodes.SmsgPvpDeathResult;

    public const int WireSize = 80;

    public readonly EchoHeadBuffer EchoHead;

    public readonly byte SelfGate;

    public readonly byte ReasonCode;

    public readonly byte Subtype;

    public readonly GapABuffer GapA;

    public readonly uint TargetId;

    public readonly OpaqueTailBuffer OpaqueTail;

    [InlineArray(8)]
    public struct EchoHeadBuffer
    {
        private byte _element0;
    }

    [InlineArray(9)]
    public struct GapABuffer
    {
        private byte _element0;
    }

    [InlineArray(56)]
    public struct OpaqueTailBuffer
    {
        private byte _element0;
    }
}