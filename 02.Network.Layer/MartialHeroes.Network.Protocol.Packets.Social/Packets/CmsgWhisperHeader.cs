
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Social.Packets;

[PacketOpcode(2, 7)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgWhisperHeader
{
    public const uint OpcodeId = Opcodes.CmsgWhisper;

    public const int HeaderSize = 19;

    public readonly byte Channel;

    public readonly byte Selector;

    public readonly TargetNameBuffer TargetName;

    [InlineArray(17)]
    public struct TargetNameBuffer
    {
        private byte _element0;
    }
}