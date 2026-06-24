using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 109)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgLocalActorSkillStateFlag
{
    public const uint OpcodeId = Opcodes.SmsgLocalActorSkillStateFlag;

    public const int WireSize = 12;

    public readonly uint Leading;

    public readonly uint ActorId;

    public readonly byte Flag;

    public readonly Pad0Buffer Pad0;

    [InlineArray(3)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }
}
