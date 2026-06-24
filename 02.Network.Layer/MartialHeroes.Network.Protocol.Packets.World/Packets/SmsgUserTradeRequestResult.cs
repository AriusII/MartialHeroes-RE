using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 23)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgUserTradeRequestResult
{
    public const uint OpcodeId = Opcodes.SmsgUserTradeRequestResult;

    public const int WireSize = 20;

    public readonly HeadBuffer Head;

    public readonly byte Selector;

    public readonly byte Reason;

    public readonly BodyBuffer Body;

    [InlineArray(8)]
    public struct HeadBuffer
    {
        private byte _element0;
    }

    [InlineArray(10)]
    public struct BodyBuffer
    {
        private byte _element0;
    }
}
