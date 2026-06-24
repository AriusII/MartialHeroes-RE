using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 113)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgItemShopPurchaseResult
{
    public const uint OpcodeId = Opcodes.SmsgItemShopPurchaseResult;

    public const int WireSize = 12;

    public readonly LeadingBuffer Leading;

    public readonly byte Success;

    public readonly byte ResultCode;

    public readonly ReservedBuffer Reserved;

    [InlineArray(8)]
    public struct LeadingBuffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct ReservedBuffer
    {
        private byte _element0;
    }
}
