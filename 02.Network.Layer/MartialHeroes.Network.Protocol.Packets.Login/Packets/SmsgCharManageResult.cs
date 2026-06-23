
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

[PacketOpcode(3, 7)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgCharManageResult
{
    public const uint OpcodeId = Opcodes.SmsgCharManageResult;

    public const int WireSize = 8;

    public readonly byte Result;

    public readonly byte Reserved1;

    public readonly byte Subtype;

    public readonly byte Reserved3;

    public readonly uint ReadyTime;
}