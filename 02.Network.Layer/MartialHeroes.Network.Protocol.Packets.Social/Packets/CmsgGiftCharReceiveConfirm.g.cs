using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgGiftCharReceiveConfirm
{
    public const uint OpcodeId = 0x2007b;

    public const int Size = 12;

    public byte AcceptFlag;
    public uint OfferId;
    public byte SubField;
    public Pad06Buffer Pad06;

    [InlineArray(6)]
    public struct Pad06Buffer
    {
        private byte _element0;
    }

}
