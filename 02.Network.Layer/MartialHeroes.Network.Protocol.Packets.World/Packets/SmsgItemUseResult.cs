using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 5)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgItemUseResult
{
    public const uint OpcodeId = Opcodes.SmsgItemUseResult;

    public const int WireSize = 44;

    public readonly Opaque0Buffer Opaque0;

    public readonly uint ActorKey;

    public readonly byte Success;

    public readonly byte ResultCode;

    public readonly byte Mode;

    public readonly byte SlotIndex;

    public readonly TrailerBuffer Trailer;

    [InlineArray(4)]
    public struct Opaque0Buffer
    {
        private byte _element0;
    }

    [InlineArray(32)]
    public struct TrailerBuffer
    {
        private byte _element0;
    }
}
