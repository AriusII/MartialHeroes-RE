using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Social.Packets;

[PacketOpcode(2, 118)]
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 1)]
public readonly struct CmsgTenderConfirm
{
    public const uint OpcodeId = Opcodes.CmsgTenderConfirm;

    public const int WireSize = 0;
}
