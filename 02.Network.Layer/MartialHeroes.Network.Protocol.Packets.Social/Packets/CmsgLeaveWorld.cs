
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Social.Packets;

[PacketOpcode(2, 0)]
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 1)]
public readonly struct CmsgLeaveWorld
{
    public const uint OpcodeId = Opcodes.CmsgLeaveWorld;

    public const int WireSize = 0;
}