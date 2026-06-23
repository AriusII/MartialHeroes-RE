using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgChatChannel
{
    public const uint OpcodeId = 0x30015;

    public HeaderPrefixBuffer HeaderPrefix;
    public uint ChannelSelector;
    public HeaderRestBuffer HeaderRest;

    [InlineArray(4)]
    public struct HeaderPrefixBuffer
    {
        private byte _element0;
    }

    [InlineArray(48)]
    public struct HeaderRestBuffer
    {
        private byte _element0;
    }

}
