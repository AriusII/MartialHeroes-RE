using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 99)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgCubeGambleResult
{
    public const uint OpcodeId = Opcodes.SmsgCubeGambleResult;

    public const int WireSize = 16;

    public readonly LeadingBuffer Leading;

    public readonly byte SubKind;

    public readonly byte ResultCode;

    public readonly byte BetType;

    public readonly byte Pad0;

    public readonly uint Wager;

    [InlineArray(8)]
    public struct LeadingBuffer
    {
        private byte _element0;
    }
}
