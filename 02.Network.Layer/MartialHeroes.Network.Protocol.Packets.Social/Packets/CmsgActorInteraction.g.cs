using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgActorInteraction
{
    public const uint OpcodeId = 0x20005;

    public const int Size = 28;

    public byte SelectMode;
    public byte SubIndex;
    public byte ClassOrFlag;
    public byte Reserved0;
    public uint TargetActorId;
    public FlagsBuffer Flags;
    public NameBuffer Name;

    [InlineArray(3)]
    public struct FlagsBuffer
    {
        private byte _element0;
    }

    [InlineArray(17)]
    public struct NameBuffer
    {
        private byte _element0;
    }

}
