using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 50)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgUpgradeItemResult
{
    public const uint OpcodeId = Opcodes.SmsgUpgradeItemResult;

    public const int WireSize = 32;

    public readonly HeaderBuffer Header;

    public readonly byte Success;

    public readonly byte Reason;

    public readonly Pad0Buffer Pad0;

    public readonly byte SlotIndex;

    public readonly Pad1Buffer Pad1;

    public readonly uint NewFlags;

    public readonly uint NewActorId;

    public readonly uint NewQty;

    public readonly uint EnchantDelta;

    [InlineArray(8)]
    public struct HeaderBuffer
    {
        private byte _element0;
    }

    [InlineArray(1)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

    [InlineArray(4)]
    public struct Pad1Buffer
    {
        private byte _element0;
    }
}