
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

[PacketOpcode(3, 14)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgCharSpawnResult
{
    public const uint OpcodeId = Opcodes.SmsgCharSpawnResult;

    public const int WireSize = 16;

    public readonly byte Result;

    public readonly byte Slot;

    public readonly ushort Pad;

    public readonly uint SpawnParam1;

    public readonly uint SpawnParam2;

    public readonly uint SpawnParam3;
}