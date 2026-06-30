using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;
using MartialHeroes.Network.Protocol.Core.Packets;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 53)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgActorVitalsAndPairState
{
    public const uint OpcodeId = Opcodes.SmsgActorVitalsAndPairState;

    public const int WireSize = 32;

    public readonly byte Sort;

    private readonly byte _pad1;

    private readonly byte _pad2;

    private readonly byte _pad3;

    public readonly uint ActorId;

    private readonly byte _gap8;

    private readonly byte _gap9;

    public readonly byte RelationState;

    public readonly byte RelationState2;

    public readonly uint PartnerActorId;

    public readonly long CurrentHp;

    public readonly int CurrentMp;

    public readonly int CurrentStamina;

    public ActorSort SortKind => (ActorSort)Sort;
}
