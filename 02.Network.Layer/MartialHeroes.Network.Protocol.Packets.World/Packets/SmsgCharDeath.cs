using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 10)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgCharDeath
{
    public const uint OpcodeId = Opcodes.SmsgCharDeath;

    public const int WireSize = 20;


    public readonly byte VictimSort;

    private readonly byte _victimPad0;

    private readonly byte _victimPad1;
    private readonly byte _victimPad2;

    public readonly uint VictimId;

    public readonly int
        DeathCause;

    public readonly byte KillerSort;

    private readonly byte _killerPad0;

    private readonly byte _killerPad1;
    private readonly byte _killerPad2;

    public readonly uint KillerId;
}