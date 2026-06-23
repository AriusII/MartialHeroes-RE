using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgCraftSubmit
{
    public const uint OpcodeId = 0x2008d;

    public const int Size = 76;

    public MaterialSlotsBuffer MaterialSlots;
    public ExtraParamsBuffer ExtraParams;
    public byte NpcActorIndex;
    public PaddingBuffer Padding;

    [InlineArray(56)]
    public struct MaterialSlotsBuffer
    {
        private byte _element0;
    }

    [InlineArray(16)]
    public struct ExtraParamsBuffer
    {
        private byte _element0;
    }

    [InlineArray(3)]
    public struct PaddingBuffer
    {
        private byte _element0;
    }

}
