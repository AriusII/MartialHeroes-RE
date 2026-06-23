using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Social.Packets;

[PacketOpcode(2, 83)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgChatContextualHeader
{
    public const uint OpcodeId = Opcodes.CmsgChatContextual;

    public const int HeaderSize = 24;

    public readonly ContextHeaderBuffer ContextHeader;

    [InlineArray(24)]
    public struct ContextHeaderBuffer
    {
        private byte _element0;
    }
}