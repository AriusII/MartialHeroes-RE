
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;
using MartialHeroes.Network.Protocol.Core.Packets;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 33)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgSkillHotbarSlotSet
{
    public const uint OpcodeId = Opcodes.SmsgSkillHotbarSlotSet;

    public const int WireSize = 20;

    public const int HotbarSlotCount = 240;

    public readonly int Sort;

    public readonly int ActorId;

    public readonly byte HotbarSlot;

    private readonly byte _pad0;

    private readonly byte _pad1;
    private readonly byte _pad2;

    public readonly int SkillId;

    public readonly short SkillPoints;

    private readonly byte _padEnd0;

    private readonly byte _padEnd1;

    public ActorSort SortKind => (ActorSort)(byte)Sort;
}