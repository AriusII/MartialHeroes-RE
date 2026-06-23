
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;
using MartialHeroes.Network.Protocol.Core.Packets;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 7)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgChatBroadcastHeader
{
    public const uint OpcodeId = Opcodes.SmsgChatBroadcast;

    public const int HeaderSize = 36;

    public const int
        BodyLengthOffset = HeaderSize;


    public const uint ColourSay = 0xFFFFFFFF;

    public const uint ColourWhisper = 0xFFCC99FF;

    public const uint ColourParty = 0xFF00FFFF;

    public const uint ColourGuild = 0xFF33FF66;

    public const uint ColourMisia = 0xFFFFFF00;

    public const uint ColourSpecialMisia = 0xFFFF797C;

    public const uint ColourGmSystem = 0xFFFF797C;

    public const uint ColourNoticeYellow = 0xFFFFFF00;

    public const uint ColourNoticeRed = 0xFFFF4040;

    public const uint ColourAlliance = 0xFF82C4FF;


    public readonly uint SenderKeyA;

    public readonly uint SenderId;

    public readonly uint ContextId;

    public readonly byte Reserved0C;

    public readonly byte SubCommand;

    public readonly byte Channel;

    public readonly byte Reserved0F;

    public readonly SenderNameBuffer
        SenderName;


    public ActorSort SenderSortKind => (ActorSort)(byte)SenderKeyA;

    public byte SenderSort => (byte)SenderKeyA;

    public bool IsLogChannel => Channel < 100;

    [InlineArray(20)]
    public struct SenderNameBuffer
    {
        private byte _element0;
    }
}