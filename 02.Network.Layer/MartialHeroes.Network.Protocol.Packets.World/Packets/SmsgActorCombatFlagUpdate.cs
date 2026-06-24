using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 147)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgActorCombatFlagUpdate
{
    public const uint OpcodeId = Opcodes.SmsgActorCombatFlagUpdate;

    public const int WireSize = 8;

    public readonly uint ActorId;

    public readonly int CombatFlag;
}