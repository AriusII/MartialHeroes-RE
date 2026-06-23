
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 5)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgActorStateEvent
{
    public const uint OpcodeId = Opcodes.SmsgActorStateEvent;

    public const int WireSize = 32;

    public readonly uint TargetSort;

    public readonly uint TargetId;

    public readonly uint ActorId;

    public readonly NameBuffer Name;

    [InlineArray(20)]
    public struct NameBuffer
    {
        private byte _element0;
    }
}