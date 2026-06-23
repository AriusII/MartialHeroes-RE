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

    private readonly byte _pad0_0;

    private readonly byte _pad0_1;
    private readonly byte _pad0_2;

    public readonly uint ActorId;

    public readonly byte Byte08;

    public readonly byte Byte09;

    public readonly byte LevelOrState;

    public readonly byte StateByte;

    public readonly uint PartnerId;

    public readonly uint CurrentHp;

    public readonly uint VitalB;

    public readonly uint Stamina;

    public readonly uint VitalC;

    public ActorSort SortKind => (ActorSort)Sort;
}