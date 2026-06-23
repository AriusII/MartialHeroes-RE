using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 4)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgAreaEntitySnapshot
{
    public const uint OpcodeId = Opcodes.SmsgAreaEntitySnapshot;

    public const int HeaderSize = 17;


    public const int
        ActorRecordSize =
            892;

    public const int
        GroundItemRecordSize =
            24;

    public const int
        GuildRecordSize =
            36;

    public const int
        TitleRecordSize =
            24;


    public const int ActorIdOffset = 0;

    public const int KindByteOffset = 4;

    public const byte
        KindByteVisualRefresh = 5;

    public const int RelationVisualOffset = 5;

    public const int DescriptorOffset = 8;

    public const int
        TrailerOffset = 0x378;


    public readonly byte HeaderFlag;

    public readonly uint ViewerEntityId;

    public readonly uint AreaGrid;

    public readonly float
        AreaCentreZ;

    public readonly float
        AreaCentreX;
}