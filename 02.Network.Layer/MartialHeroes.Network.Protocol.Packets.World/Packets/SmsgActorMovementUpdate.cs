
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;
using MartialHeroes.Network.Protocol.Core.Packets;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 13)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgActorMovementUpdate
{
    public const uint OpcodeId = Opcodes.SmsgActorMovementUpdate;

    public const int WireSize = 40;

    public readonly byte Sort;

    private readonly byte _pad0_0;

    private readonly byte _pad0_1;
    private readonly byte _pad0_2;

    public readonly uint ActorId;

    public readonly float Yaw;

    public readonly float PosX;

    public readonly float PosZ;

    public readonly float DestX;

    public readonly float DestZ;

    public readonly byte RunFlag;

    private readonly byte _pad1_0;

    private readonly byte _pad1_1;
    private readonly byte _pad1_2;

    public readonly uint Reserved0x20;

    public readonly byte MotionCode;

    private readonly byte _pad2_0;

    public readonly byte StanceByte;

    private readonly byte _pad3_0;

    public ActorSort SortKind => (ActorSort)Sort;
}