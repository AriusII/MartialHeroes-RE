using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 15)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgGroundItemRemove
{
    public const uint OpcodeId = Opcodes.SmsgTrackedWorldObjectRemove;

    public const int WireSize = 16;

    public readonly uint Sort;

    public readonly uint PickerId;

    public readonly uint TrackedId;

    public readonly byte NotifyFlag;

    public readonly Pad0Buffer Pad0;

    [InlineArray(3)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }
}