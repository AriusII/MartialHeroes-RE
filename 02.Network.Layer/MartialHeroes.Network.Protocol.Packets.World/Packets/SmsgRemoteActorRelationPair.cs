using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 64)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgRemoteActorRelationPair
{
    public const uint OpcodeId = Opcodes.SmsgRemoteActorRelationPair;

    public const int WireSize = 16;

    public const byte ReciprocalRelationCode = 7;

    public readonly Pad0Buffer Pad0;

    public readonly int ActorIdA;

    public readonly int ActorIdB;

    public readonly byte RelationCode;

    public readonly Pad13Buffer Pad13;

    [InlineArray(4)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

    [InlineArray(3)]
    public struct Pad13Buffer
    {
        private byte _element0;
    }
}