using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgChatContextual
{
    public const uint OpcodeId = 0x20053;

    public ContextHeaderBuffer ContextHeader;

    [InlineArray(24)]
    public struct ContextHeaderBuffer
    {
        private byte _element0;
    }

}
