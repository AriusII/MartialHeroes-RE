
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

[PacketOpcode(3, 100)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgCharActionResult
{
    public const uint OpcodeId = Opcodes.SmsgCharActionResult;

    public const int WireSize = 4;

    public readonly uint Result;
}