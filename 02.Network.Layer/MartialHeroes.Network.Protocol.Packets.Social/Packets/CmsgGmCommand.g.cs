using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgGmCommand
{
    public const uint OpcodeId = 0x20048;

    public const int Size = 32;

    public byte Mode;
    public TargetNameBuffer TargetName;
    public PaddingBuffer Padding;
    public uint Param1;
    public float Param2;
    public float Param3;

    [InlineArray(16)]
    public struct TargetNameBuffer
    {
        private byte _element0;
    }

    [InlineArray(3)]
    public struct PaddingBuffer
    {
        private byte _element0;
    }

}
