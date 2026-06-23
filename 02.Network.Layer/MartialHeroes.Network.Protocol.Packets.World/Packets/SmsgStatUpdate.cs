
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 29)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgStatUpdate
{
    public const uint OpcodeId = Opcodes.SmsgStatUpdate;

    public const int WireSize = 36;

    public readonly uint Handle;

    public readonly uint SessionToken;

    public readonly byte ResultOk;

    private readonly byte _pad0_0;

    private readonly byte _pad0_1;
    private readonly byte _pad0_2;

    public readonly uint Stat0;

    public readonly uint Stat1;

    public readonly uint Stat2;

    public readonly uint Stat3;

    public readonly uint Stat4;

    public readonly uint RemainingStatPoints;
}