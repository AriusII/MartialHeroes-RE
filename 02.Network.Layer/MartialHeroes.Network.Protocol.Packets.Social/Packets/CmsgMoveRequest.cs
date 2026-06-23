using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Social.Packets;

[PacketOpcode(2, 13)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgMoveRequest
{
    public const uint OpcodeId = Opcodes.CmsgMoveRequest;

    public const int WireSize = 16;

    public readonly float Heading;

    public readonly float TargetX;

    public readonly float TargetZ;

    public readonly byte ModeByte;

    public readonly byte RunFlag;

    private readonly byte _reserved0E_0;

    private readonly byte _reserved0E_1;
}