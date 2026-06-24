using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 25)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgUserTradeFullResponse
{
    public const uint OpcodeId = Opcodes.SmsgUserTradeFullResponse;

    public const int HeaderSize = 28;

    public readonly Head0Buffer Head0;

    public readonly byte Phase;

    public readonly Pad0Buffer Pad0;

    public readonly long Coin;

    public readonly uint OwnerId;

    public readonly uint Count;

    [InlineArray(8)]
    public struct Head0Buffer
    {
        private byte _element0;
    }

    [InlineArray(3)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }
}
