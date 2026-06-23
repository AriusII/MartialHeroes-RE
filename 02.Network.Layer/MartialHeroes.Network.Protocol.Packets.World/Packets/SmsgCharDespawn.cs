using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;
using MartialHeroes.Network.Protocol.Core.Packets;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 0)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgCharDespawn
{
    public const uint OpcodeId = Opcodes.SmsgCharDespawn;

    public const int WireSize = 12;

    public readonly uint Sort;

    public readonly uint ActorId;

    public readonly byte Flags;

    private readonly byte _pad0;

    private readonly byte _pad1;
    private readonly byte _pad2;

    public ActorSort SortKind => (ActorSort)(byte)Sort;
}