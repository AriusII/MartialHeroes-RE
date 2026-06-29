using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Network.Protocol.Packets.World.Packets;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    private const int GuildMaxMembers = 50;
    private const int GuildGateOffset = 8;
    private const int GuildNameOffset = 10;
    private const int GuildNameLength = 18;
    private const int GuildIdOffset = 28;
    private const int GuildMemberIdsOffset = 60;
    private const int GuildMemberRanksOffset = 260;
    private const int GuildMemberNamesOffset = 310;
    private const int GuildMemberNameStride = 17;
    private const int GuildMemberOnlineOffset = 1160;
    private const int GuildMemberPointsOffset = 1212;
    private const int GuildMemberContribOffset = 1412;
    private const int GuildMemberLoginOffset = 1612;
    private const byte GuildLeaveGate = 1;

    public void Handle(in SmsgGuildInfoFullSync packet)
    {
        var body = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<SmsgGuildInfoFullSync, byte>(ref Unsafe.AsRef(in packet)),
            SmsgGuildInfoFullSync.WireSize);

        var gate = body[GuildGateOffset];
        var guildName = DecodeFixedText(body.Slice(GuildNameOffset, GuildNameLength));
        var guildId = BinaryPrimitives.ReadInt16LittleEndian(body.Slice(GuildIdOffset, sizeof(short)));

        if (gate == GuildLeaveGate)
        {
            Guild.Clear();
            _eventBus.Publish(new GuildRosterEvent(guildId, guildName, gate, ImmutableArray<GuildMember>.Empty));
            return;
        }

        var builder = ImmutableArray.CreateBuilder<GuildMember>(GuildMaxMembers);
        for (var i = 0; i < GuildMaxMembers; i++)
        {
            var actorId = BinaryPrimitives.ReadUInt32LittleEndian(
                body.Slice(GuildMemberIdsOffset + i * sizeof(uint), sizeof(uint)));
            if (actorId == 0u) continue;

            var rank = body[GuildMemberRanksOffset + i];
            var name = DecodeFixedText(body.Slice(GuildMemberNamesOffset + i * GuildMemberNameStride,
                GuildMemberNameStride));
            var online = body[GuildMemberOnlineOffset + i];
            var points = BinaryPrimitives.ReadInt32LittleEndian(
                body.Slice(GuildMemberPointsOffset + i * sizeof(int), sizeof(int)));
            var contrib = BinaryPrimitives.ReadInt32LittleEndian(
                body.Slice(GuildMemberContribOffset + i * sizeof(int), sizeof(int)));
            var login = BinaryPrimitives.ReadInt32LittleEndian(
                body.Slice(GuildMemberLoginOffset + i * sizeof(int), sizeof(int)));

            builder.Add(new GuildMember(actorId, name, rank, online, points, contrib, login));
        }

        var members = builder.ToImmutable();
        Guild.SetGuild(guildId, guildName, members.Length);

        _eventBus.Publish(new GuildRosterEvent(guildId, guildName, gate, members));
    }

    public void Handle(in SmsgGuildMemberRosterUpdate packet)
    {
        var key = new ActorKey(packet.Sort, ToEntitySort(unchecked((byte)packet.MemberKey)));
        var present = packet.NameGate != 0;

        ReadOnlySpan<byte> nameSpan = packet.Name;
        var name = present ? DecodeFixedText(nameSpan) : string.Empty;

        _eventBus.Publish(new GuildMemberPatchEvent(key, present, name, packet.ByteC, packet.ByteD));
    }
}