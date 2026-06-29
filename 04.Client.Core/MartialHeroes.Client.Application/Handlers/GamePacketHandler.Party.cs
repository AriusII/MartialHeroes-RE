using System.Buffers.Binary;
using System.Collections.Immutable;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Network.Protocol.Packets.World.Packets;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    private const int PartyRosterStoreCount = 8;

    public void Handle(in SmsgPartyRosterEvent packet)
    {
        _eventBus.Publish(new PartyRosterEvent(packet.Event, packet.MemberSlot));
    }

    public void Handle(in SmsgPartyMemberStats packet)
    {
        ReadOnlySpan<byte> nameSpan = packet.MemberName;
        var name = DecodeFixedText(nameSpan);

        _eventBus.Publish(new PartyMemberVitalsEvent(
            packet.MemberId, name, packet.StatA, packet.StatBState,
            packet.StatC, packet.StatD, packet.StatE, packet.StatF,
            packet.StatG, packet.StatH, packet.StatI, packet.StatJ));
    }

    public void Handle(in SmsgPartyMemberRemoveResult packet)
    {
        const byte expelSubmode = 1;
        var removedId = packet.Submode == expelSubmode ? packet.RemovedIdExpel : packet.RemovedIdLeft;

        ReadOnlySpan<byte> idBytes = packet.MemberIds;
        Span<uint> members = stackalloc uint[PartyRosterStoreCount];
        var builder = ImmutableArray.CreateBuilder<uint>(PartyRosterStoreCount);
        for (var i = 0; i < PartyRosterStoreCount; i++)
        {
            var id = BinaryPrimitives.ReadUInt32LittleEndian(idBytes.Slice(i * sizeof(uint), sizeof(uint)));
            members[i] = id;
            builder.Add(id);
        }

        Party.SetMembers(members);

        _eventBus.Publish(new PartyMemberRemovedEvent(
            packet.RequesterId, packet.Submode, removedId, builder.MoveToImmutable()));
    }

    private bool HandlePartyInviteState(ReadOnlySpan<byte> payload)
    {
        const int memberIdsOffset = 20;
        const int targetIdOffset = 52;
        const int minSize = targetIdOffset + sizeof(int);
        if (payload.Length < minSize) return false;

        var gate = payload[8];
        var error = payload[9];
        var state = payload[10];
        var partyId = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(16, sizeof(int)));
        var targetId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(targetIdOffset, sizeof(uint)));

        Span<uint> members = stackalloc uint[PartyRosterStoreCount];
        var builder = ImmutableArray.CreateBuilder<uint>(PartyRosterStoreCount);
        for (var i = 0; i < PartyRosterStoreCount; i++)
        {
            var id = BinaryPrimitives.ReadUInt32LittleEndian(
                payload.Slice(memberIdsOffset + i * sizeof(uint), sizeof(uint)));
            members[i] = id;
            builder.Add(id);
        }

        Party.SetPartyId(partyId);
        Party.SetMembers(members);

        _eventBus.Publish(new PartyInviteStateEvent(
            gate, error, state, partyId, builder.MoveToImmutable(), targetId));
        return true;
    }

    private bool HandlePartyAcceptResult(ReadOnlySpan<byte> payload)
    {
        const int minSize = 11;
        if (payload.Length < minSize) return false;

        const byte ok = 1;
        var success = payload[8] == ok;
        var relationType = payload[10];

        _eventBus.Publish(new PartyAcceptResultEvent(success, relationType));
        return true;
    }

    private bool HandlePartyMemberJoined(ReadOnlySpan<byte> payload)
    {
        const int nameOffset = 18;
        const int nameLength = 17;
        const int minSize = nameOffset + nameLength;
        if (payload.Length < minSize) return false;

        var actorId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, sizeof(uint)));
        var eventCode = payload[8];
        var sort = payload[9];
        var relationType = payload[10];
        var name = DecodeFixedText(payload.Slice(nameOffset, nameLength));

        _eventBus.Publish(new PartyMemberJoinedEvent(actorId, eventCode, sort, relationType, name));
        return true;
    }
}