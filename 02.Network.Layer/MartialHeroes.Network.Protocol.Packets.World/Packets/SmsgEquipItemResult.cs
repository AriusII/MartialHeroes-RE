using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 12)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgEquipItemResult
{
    public const uint OpcodeId = Opcodes.SmsgEquipItemResult;

    public const int WireSize = 16;

    public readonly byte Guard;

    private readonly byte _pad0_0;

    private readonly byte _pad0_1;
    private readonly byte _pad0_2;

    public readonly uint ActorSortKey;

    public readonly byte Result;

    private readonly byte _unused09;

    public readonly byte FromSlot;

    public readonly byte FromSub;

    public readonly byte ToSlot;

    private readonly byte _padEnd0;

    private readonly byte _padEnd1;
    private readonly byte _padEnd2;
}