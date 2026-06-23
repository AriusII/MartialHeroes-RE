using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgAutoInteract
{
    public const uint OpcodeId = 0x2008b;

    public const int Size = 32;

    public byte InteractionType;
    public byte SlotIndex;
    public byte ChannelByte;
    public byte Reserved0;
    public uint TargetActorId;
    public ContextBuffer Context;

    [InlineArray(24)]
    public struct ContextBuffer
    {
        private byte _element0;
    }

}
