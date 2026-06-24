using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 56)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgSceneEnterSpawnSnapshot
{
    public const uint OpcodeId = Opcodes.SmsgResponseSlot56;

    public const int WireSize = 1552;

    public readonly HeaderPad0Buffer HeaderPad0;

    public readonly byte Subtype;

    public readonly ReservedABuffer ReservedA;

    public readonly uint ActorId;

    public readonly TransformArrayABuffer TransformArrayA;

    public readonly TransformArrayBBuffer TransformArrayB;

    [InlineArray(8)]
    public struct HeaderPad0Buffer
    {
        private byte _element0;
    }

    [InlineArray(3)]
    public struct ReservedABuffer
    {
        private byte _element0;
    }

    [InlineArray(1024)]
    public struct TransformArrayABuffer
    {
        private byte _element0;
    }

    [InlineArray(512)]
    public struct TransformArrayBBuffer
    {
        private byte _element0;
    }
}