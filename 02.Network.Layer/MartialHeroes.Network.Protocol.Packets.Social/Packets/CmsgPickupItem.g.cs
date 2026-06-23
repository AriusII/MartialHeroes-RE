using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgPickupItem
{
    public const uint OpcodeId = 0x2000f;

    public const int Size = 12;

    public uint Key;
    public byte Share0;
    public byte Share1;
    public byte Share2;
    public Pad0Buffer Pad0;
    public uint Amount;

    [InlineArray(1)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

}
