using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 108)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgPlayerGoldBalanceUpdate
{
    public const uint OpcodeId = Opcodes.SmsgPlayerGoldBalanceUpdate;

    public const int WireSize = 16;

    public readonly HeaderBuffer Header;

    public readonly long Gold;

    [InlineArray(8)]
    public struct HeaderBuffer
    {
        private byte _element0;
    }
}