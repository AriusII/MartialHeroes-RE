using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

[PacketOpcode(3, 21)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgChatChannelHeader
{
    public const uint OpcodeId = Opcodes.CmsgChatChannel;

    public const int HeaderSize = 56;

    public readonly HeaderPrefixBuffer HeaderPrefix;

    public readonly uint ChannelSelector;

    public readonly HeaderRestBuffer HeaderRest;

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