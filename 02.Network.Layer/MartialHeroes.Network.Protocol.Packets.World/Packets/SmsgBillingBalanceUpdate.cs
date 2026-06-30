using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 82)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgBillingBalanceUpdate
{
    public const uint OpcodeId = Opcodes.SmsgBillingBalanceUpdate;

    public const int WireSize = 16;

    public readonly HeaderBuffer Header;

    public readonly byte Mode;

    public readonly byte Submode;

    private readonly ushort _pad;

    public readonly uint BillingPoints;

    [InlineArray(8)]
    public struct HeaderBuffer
    {
        private byte _element0;
    }
}
