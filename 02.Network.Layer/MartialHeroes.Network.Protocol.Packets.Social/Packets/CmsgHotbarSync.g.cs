using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgHotbarSync
{
    public const uint OpcodeId = 0x20008;

    public const int Size = 241;

    public byte Count;
    public SlotIndicesBuffer SlotIndices;

    [InlineArray(240)]
    public struct SlotIndicesBuffer
    {
        private byte _element0;
    }

}
