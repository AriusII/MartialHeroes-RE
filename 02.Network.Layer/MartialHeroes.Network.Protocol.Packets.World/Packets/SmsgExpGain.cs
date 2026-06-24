using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 9)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgExpGain
{
    public const uint OpcodeId = Opcodes.SmsgExpGain;

    public const int WireSize = 32;

    public readonly uint ActorSort;

    public readonly uint ActorId;

    public readonly uint SourceSort;

    public readonly uint SourceId;

    public readonly long XpAmount;

    public readonly int ProficiencyA;

    public readonly int ProficiencyB;
}
