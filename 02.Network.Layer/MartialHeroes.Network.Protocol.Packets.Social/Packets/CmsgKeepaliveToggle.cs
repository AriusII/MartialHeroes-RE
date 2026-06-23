using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Social.Packets;

[PacketOpcode(2, 112)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgKeepaliveToggle
{
    public const uint OpcodeId = Opcodes.CmsgKeepaliveToggle;

    public const int WireSize = 1;

    public readonly byte Flag;
}