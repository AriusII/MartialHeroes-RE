using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 115)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgItemShopBalanceUpdate
{
    public const uint OpcodeId = Opcodes.SmsgItemShopBalanceUpdate;

    public const int WireSize = 24;

    public readonly HeaderBuffer Header;

    public readonly byte Success;

    public readonly byte FailCode;

    public readonly Pad0Buffer Pad0;

    public readonly long Gold;

    public readonly uint Points;

    [InlineArray(8)]
    public struct HeaderBuffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }
}
