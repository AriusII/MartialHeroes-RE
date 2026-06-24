using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 136)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgActorTimedStateUpdate
{
    public const uint OpcodeId = Opcodes.SmsgActorTimedStateUpdate;

    public const int WireSize = 16;

    public readonly uint Sort;

    public readonly uint ActorId;

    public readonly uint TimedValue;

    public readonly byte StateByte;

    public readonly Pad0Buffer Pad0;

    [InlineArray(3)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }
}
