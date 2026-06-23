using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgActorInteractMode100
{
    public const uint OpcodeId = 0x20010;

    public const int Size = 12;

    public byte Mode;
    public byte B1;
    public byte B2;
    public byte B3;
    public byte B4;
    public Pad0Buffer Pad0;
    public uint ActorOrArg;

    [InlineArray(3)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

}
