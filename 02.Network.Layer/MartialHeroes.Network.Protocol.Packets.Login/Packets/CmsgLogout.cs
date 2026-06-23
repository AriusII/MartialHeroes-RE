using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

[PacketOpcode(1, 0)]
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 1)]
public readonly struct CmsgLogout
{
    public const uint OpcodeId = Opcodes.CmsgLogout;

    public const int WireSize = 0;
}