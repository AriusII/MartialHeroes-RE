using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgChat
{
    public const uint OpcodeId = 0x20007;

    public byte Channel;
    public byte Selector;
    public TargetNameBuffer TargetName;

    [InlineArray(17)]
    public struct TargetNameBuffer
    {
        private byte _element0;
    }

}
