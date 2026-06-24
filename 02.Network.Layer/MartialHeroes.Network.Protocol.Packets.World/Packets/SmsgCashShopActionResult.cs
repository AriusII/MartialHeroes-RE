using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 114)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgCashShopActionResult
{
    public const uint OpcodeId = Opcodes.SmsgCashShopActionResult;

    public const int WireSize = 12;

    public readonly LeadingBuffer Leading;

    public readonly int ResultCode;

    [InlineArray(8)]
    public struct LeadingBuffer
    {
        private byte _element0;
    }
}
