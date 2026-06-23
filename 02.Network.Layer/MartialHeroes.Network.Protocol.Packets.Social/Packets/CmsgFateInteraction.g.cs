using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgFateInteraction
{
    public const uint OpcodeId = 0x2004c;

    public const int Size = 20;

    public byte FateState;
    public byte SlotIndex;
    public ActorNameBuffer ActorName;
    public byte ItemListIndex;

    [InlineArray(17)]
    public struct ActorNameBuffer
    {
        private byte _element0;
    }

}
