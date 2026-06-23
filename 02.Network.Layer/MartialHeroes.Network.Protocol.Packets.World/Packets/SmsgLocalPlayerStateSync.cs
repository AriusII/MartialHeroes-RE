
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 13)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgLocalPlayerStateSync
{
    public const uint OpcodeId = Opcodes.SmsgLocalPlayerStateSync;

    public const int WireSize = 56;


    public readonly uint TargetSort;

    public readonly uint TargetId;

    public readonly float Y;

    public readonly float Heading;

    public readonly float X;

    public readonly float Z;


    private readonly byte _reserved18_0;
    private readonly byte _reserved18_1;
    private readonly byte _reserved18_2;
    private readonly byte _reserved18_3;
    private readonly byte _reserved18_4;
    private readonly byte _reserved18_5;
    private readonly byte _reserved18_6;
    private readonly byte _reserved18_7;
    private readonly byte _reserved18_8;

    public readonly byte
        Mode;

    private readonly byte _padding0;

    private readonly byte _padding1;

    public readonly long QwordValue;

    public readonly uint DwordValue1;

    public readonly uint DwordValue2;

    public readonly uint
        ArgValue;
}