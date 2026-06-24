using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Login;
using MartialHeroes.Client.Application.Net;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Inventory.Inventory;
using MartialHeroes.Client.Domain.Skills.Skills;
using MartialHeroes.Client.Domain.Social.Social;
using MartialHeroes.Network.Abstractions.Lobby;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Network.Protocol.Packets.Login.Packets;
using MartialHeroes.Network.Protocol.Packets.Social.Packets;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.UseCases;

public sealed class ApplicationUseCases : IApplicationUseCases
{
    public const int SessionTokenLength = 33;
    private const int EquipChangeWireSize = 12;
    private const int TradeRequestWireSize = 8;
    private const int PartyInviteWireSize = 8;
    private const byte WhisperChannelCode = 9;
    private const int WhisperBodyByteCap = 119;
    private const int WhisperTargetNameBytes = 16;
    public const uint PointBuyStatSeed = 10;
    public const uint PointBuyStatFloor = 10;
    public const uint PointBuyStatCeil = 15;
    public const uint PointBuyBudgetSeed = 5;
    public const uint PointBuyInvariantTotal = 5 * PointBuyStatSeed + PointBuyBudgetSeed;
    private static readonly Encoding Cp949 = CreateCp949();
    private readonly CharacterSelectionStore? _characterSelection;
    private readonly LoginCredentialStore _credentials;
    private readonly IClientEventBus? _eventBus;
    private readonly InFlightLatch? _inFlightLatch;
    private readonly KeepaliveDriver? _keepaliveDriver;
    private readonly ILastServerStore? _lastServerStore;
    private readonly ILobbyClient? _lobbyClient;
    private readonly LocalPlayerState? _localPlayer;
    private readonly IOutboundPacketSink _outbound;

    private readonly CooldownTable _scratchCooldowns = new();

    private readonly SessionId _sessionId;
    private readonly byte[] _sessionToken;
    private readonly uint _versionToken;
    private readonly ClientWorld _world;

    public ApplicationUseCases(
        IOutboundPacketSink outbound,
        ClientWorld world,
        LoginCredentialStore credentials,
        SessionId sessionId,
        ReadOnlySpan<byte> versionToken = default,
        LocalPlayerState? localPlayer = null,
        IClientVersionSource? versionSource = null,
        CharacterSelectionStore? characterSelection = null,
        IClientEventBus? eventBus = null,
        ILobbyClient? lobbyClient = null,
        ILastServerStore? lastServerStore = null,
        InFlightLatch? inFlightLatch = null,
        KeepaliveDriver? keepaliveDriver = null)
    {
        _outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _sessionId = sessionId;
        _localPlayer = localPlayer;
        _characterSelection = characterSelection;
        _eventBus = eventBus;
        _lobbyClient = lobbyClient;
        _lastServerStore = lastServerStore;
        _inFlightLatch = inFlightLatch;
        _keepaliveDriver = keepaliveDriver;

        _sessionToken = new byte[SessionTokenLength];
        if (!versionToken.IsEmpty)
            versionToken[..Math.Min(versionToken.Length, SessionTokenLength)].CopyTo(_sessionToken);

        _versionToken = ClientVersionToken.Derive(
            (versionSource ?? DefaultClientVersionSource.Instance).VersionField);
    }

    public Func<string, bool>? BannedCharacterNamePredicate { get; init; }

    public ValueTask LoginAsync(
        string username, string password, string? pin = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);
        cancellationToken.ThrowIfCancellationRequested();

        _credentials.Stage(username, password, pin);

        return ValueTask.CompletedTask;
    }

    public async ValueTask<IReadOnlyList<LobbyServerRecord>> FetchServerListAsync(
        CancellationToken cancellationToken = default)
    {
        var lobby = _lobbyClient
                    ?? throw new InvalidOperationException(
                        "No ILobbyClient was wired; FetchServerListAsync requires the lobby surface. spec: login_flow.md §2.");

        var records =
            await lobby.FetchServerListAsync(cancellationToken).ConfigureAwait(false);

        var builder = ImmutableArray.CreateBuilder<ServerListEntryView>(records.Count);
        foreach (var r in records)
            builder.Add(new ServerListEntryView(
                r.ServerId, r.StatusCode, r.Load, r.OpenTime,
                ClassifyLoad(r.Load), ClassifyStatus(r.StatusCode), string.Empty));

        _eventBus?.Publish(new ServerListReceivedEvent(builder.ToImmutable()));
        return records;
    }

    public async ValueTask<LobbyChannelEndpoint> SelectServerAsync(
        ushort serverId, CancellationToken cancellationToken = default)
    {
        var lobby = _lobbyClient
                    ?? throw new InvalidOperationException(
                        "No ILobbyClient was wired; SelectServerAsync requires the lobby surface. spec: login_flow.md §2.");

        var endpoint =
            await lobby.FetchChannelEndpointAsync(serverId, cancellationToken).ConfigureAwait(false);

        _lastServerStore?.Save(serverId);

        _eventBus?.Publish(new ChannelEndpointResolvedEvent(serverId, endpoint.Host, endpoint.Port));
        return endpoint;
    }

    public ValueTask RequestMoveAsync(
        Vector3Fixed target, bool running, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var current = _world.LocalActor?.Position ?? Vector3Fixed.Zero;
        var (curX, _, curZ) = current.ToVector3Float();
        var (tgtX, _, tgtZ) = target.ToVector3Float();
        var heading = MathF.Atan2(tgtZ - curZ, tgtX - curX);

        Span<byte> payload = stackalloc byte[CmsgMoveRequest.WireSize];
        payload.Clear();
        BinaryPrimitives.WriteSingleLittleEndian(payload[..4], heading);
        BinaryPrimitives.WriteSingleLittleEndian(payload.Slice(0x04, 4), tgtX);
        BinaryPrimitives.WriteSingleLittleEndian(payload.Slice(0x08, 4), tgtZ);
        const byte moveModeConstant = 3;
        payload[0x0c] = moveModeConstant;
        payload[0x0d] = running ? (byte)1 : (byte)0;

        return SendAsync(2, 13, payload, cancellationToken);
    }

    public ValueTask SelectCharacterAsync(int slotIndex, CancellationToken cancellationToken = default)
    {
        if (slotIndex is < 0 or > CharacterSelectionStore.MaxSlotIndex)
            throw new ArgumentOutOfRangeException(
                nameof(slotIndex), slotIndex, "Slot index must be 0..4 (spec: login_flow.md §3.5).");

        cancellationToken.ThrowIfCancellationRequested();

        if (_characterSelection is not null)
        {
            var outcome = _characterSelection.Confirm(slotIndex);
            switch (outcome)
            {
                case CharacterSelectionStore.SelectOutcome.Blank:
                    _eventBus?.Publish(new CreateCharacterRequestedEvent(slotIndex));
                    return ValueTask.CompletedTask;

                case CharacterSelectionStore.SelectOutcome.Invalid:
                    return ValueTask.CompletedTask;

                case CharacterSelectionStore.SelectOutcome.Confirmed:
                default:
                    break;
            }
        }

        Span<byte> payload = stackalloc byte[CmsgSelectCharacterSlot.WireSize];
        payload[0x00] = (byte)slotIndex;
        const byte selectAndPlayMode = 1;
        payload[0x01] = selectAndPlayMode;

        _inFlightLatch?.Arm();

        return SendAsync(1, 7, payload, cancellationToken);
    }

    public IReadOnlyList<CharacterSlotRecord?> GetCharacterRoster()
    {
        return _characterSelection?.Snapshot() ?? [];
    }

    public ValueTask<CharacterNameValidationResult> CreateCharacterAsync(
        CharacterCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request.Name);
        cancellationToken.ThrowIfCancellationRequested();

        var validation = ValidateCharacterName(request.Name);
        if (!validation.IsValid) return ValueTask.FromResult(validation);

        if (request.UiClassIndex > 3)
            throw new ArgumentOutOfRangeException(
                nameof(request.UiClassIndex), request.UiClassIndex,
                "UI class index must be 0..3 (spec: frontend_scenes.md §4.1 / cmsg_char_create.yaml).");

        if (request.Face is < 1 or > 7)
            throw new ArgumentOutOfRangeException(
                nameof(request.Face), request.Face,
                "Face index must be 1..7 (spec: cmsg_char_create.yaml).");

        var pointBuy = BuildPointBuy(
            request.Stat0, request.Stat1, request.Stat2, request.Stat3, request.Stat4,
            request.PointsRemaining);
        if (!pointBuy.IsValid)
            return ValueTask.FromResult(new CharacterNameValidationResult(false, 12012));

        var payload = new byte[CmsgCreateCharacter.WireSize];
        Span<byte> p = payload;
        WriteFixedCp949(request.Name, p[..18]);
        BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(0x12, 2), request.Face);
        BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(0x14, 2), request.AppearanceA);
        BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(0x16, 2), request.AppearanceB);
        BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(0x18, 2), RemapCreateClass(request.UiClassIndex));
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x1C, 4), pointBuy.Stat0);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x20, 4), pointBuy.Stat1);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x24, 4), pointBuy.Stat2);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x28, 4), pointBuy.Stat3);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x2C, 4), pointBuy.Stat4);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x30, 4), pointBuy.PointsRemaining);

        _inFlightLatch?.Arm();
        return SendResultAsync(1, 6, payload, CharacterNameValidationResult.Valid, cancellationToken);
    }

    public ValueTask<bool> DeleteCharacterAsync(int slotIndex, CancellationToken cancellationToken = default)
    {
        ValidateSlotIndex(slotIndex);
        cancellationToken.ThrowIfCancellationRequested();

        var record = _characterSelection?.Get(slotIndex);
        if (_characterSelection is not null &&
            (record is null || string.Equals(record.Name, CharacterSelectionStore.BlankSlotSentinel,
                StringComparison.Ordinal)))
            return ValueTask.FromResult(false);

        var payload = new byte[CmsgSelectCharacterSlot.WireSize];
        payload[0x00] = (byte)slotIndex;
        const byte deleteMode = 2;
        payload[0x01] = deleteMode;

        _inFlightLatch?.Arm();

        return SendResultAsync(1, 7, payload, true, cancellationToken);
    }

    public ValueTask<CharacterNameValidationResult> RenameCharacterAsync(
        int slotIndex,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ValidateSlotIndex(slotIndex);
        ArgumentNullException.ThrowIfNull(newName);
        cancellationToken.ThrowIfCancellationRequested();

        var validation = ValidateCharacterName(newName);
        if (!validation.IsValid) return ValueTask.FromResult(validation);

        var record = _characterSelection?.Get(slotIndex);
        if (_characterSelection is not null &&
            (record is null || string.Equals(record.Name, CharacterSelectionStore.BlankSlotSentinel,
                StringComparison.Ordinal)))
            return ValueTask.FromResult(new CharacterNameValidationResult(false, 2190));

        var payload = new byte[CmsgRenameCharacter.WireSize];
        Span<byte> p = payload;
        p[0x00] = (byte)slotIndex;
        WriteFixedCp949(newName, p.Slice(0x01, 17));

        _inFlightLatch?.Arm();
        return SendResultAsync(1, 13, payload, CharacterNameValidationResult.Valid, cancellationToken);
    }

    public ValueTask UseSkillAsync(
        byte slot,
        ReadOnlyMemory<uint> targetsA = default,
        ReadOnlyMemory<uint> targetsB = default,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var a = targetsA.Span;
        var b = targetsB.Span;
        if (a.Length > ushort.MaxValue || b.Length > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(targetsA), "Too many skill targets for a u16 count.");

        var tailBytes = (a.Length + b.Length) * sizeof(uint);
        var payload = new byte[CmsgUseSkillHeader.HeaderSize + tailBytes];
        Span<byte> p = payload;
        p.Clear();

        p[0x00] = slot;
        BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(0x14, 2), (ushort)a.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(0x16, 2), (ushort)b.Length);

        var cursor = CmsgUseSkillHeader.HeaderSize;
        foreach (var id in a)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(cursor, 4), id);
            cursor += sizeof(uint);
        }

        foreach (var id in b)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(cursor, 4), id);
            cursor += sizeof(uint);
        }

        return SendAsync(2, 52, payload, cancellationToken);
    }

    public ValueTask SendChatAsync(
        uint channel,
        string text,
        string? recipientName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();

        var isWhisper = !string.IsNullOrEmpty(recipientName) || channel == WhisperChannelCode;

        var routeChannel = isWhisper ? ChatChannel.Whisper : ChatChannel.Channel;
        var route = ChatRouting.Validate(
            routeChannel,
            text.Length);
        if (route != ChatRouteResult.Send)
            return ValueTask.CompletedTask;

        return SendChat27Async(channel, isWhisper ? recipientName : null, text, cancellationToken);
    }

    public ValueTask MoveCharacterSlotAsync(int fromSlot, int toSlot, CancellationToken cancellationToken = default)
    {
        if (fromSlot is < 0 or > CharacterSelectionStore.MaxSlotIndex)
            throw new ArgumentOutOfRangeException(nameof(fromSlot), fromSlot, "Slot index must be 0..4.");
        if (toSlot is < 0 or > CharacterSelectionStore.MaxSlotIndex)
            throw new ArgumentOutOfRangeException(nameof(toSlot), toSlot, "Slot index must be 0..4.");

        cancellationToken.ThrowIfCancellationRequested();

        Span<byte> payload = stackalloc byte[1];
        payload[0x00] = (byte)toSlot;

        _inFlightLatch?.Arm();
        return SendAsync(1, 14, payload, cancellationToken);
    }

    public ValueTask ConfirmTenderAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return SendAsync(2, 118, ReadOnlyMemory<byte>.Empty, cancellationToken);
    }

    public ValueTask EmitEnterWorldRequest(byte slotIndex, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Span<byte> payload = stackalloc byte[CmsgEnterGameRequest.WireSize];
        payload.Clear();
        payload[0x00] = slotIndex;
        _sessionToken.AsSpan(0, SessionTokenChecksum.SessionTokenLength)
            .CopyTo(payload.Slice(0x01, SessionTokenChecksum.SessionTokenLength));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0x24, sizeof(uint)), _versionToken);

        _inFlightLatch?.Arm();

        SeedEnterTimeLocalPlayer(slotIndex);

        return SendAsync(1, 9, payload, cancellationToken);
    }

    public async ValueTask<SkillCastResult> CastSkillAsync(
        byte slot,
        SkillDefinition skill,
        CasterState caster,
        ISkillTargetingQuery targeting,
        Vector3Fixed aimPoint,
        long nowMs,
        ReadOnlyMemory<uint> targetsA = default,
        ReadOnlyMemory<uint> targetsB = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targeting);
        cancellationToken.ThrowIfCancellationRequested();

        var current = _localPlayer?.CastState ?? SkillCastState.Idle;
        var cooldowns = _localPlayer?.Cooldowns ?? _scratchCooldowns;
        var (next, result) =
            current.TryBeginCast(in skill, in caster, cooldowns, targeting, in aimPoint, nowMs);

        if (result != SkillCastResult.Ok) return result;

        if (_localPlayer is not null) _localPlayer.CastState = next;

        await UseSkillAsync(slot, targetsA, targetsB, cancellationToken).ConfigureAwait(false);
        return SkillCastResult.Ok;
    }

    public ValueTask<EquipCheckResult> EquipItemAsync(
        byte mode,
        byte slot,
        byte fromSub,
        byte toSlot,
        byte sub,
        int itemIndex,
        EquipStateGates state,
        EquipRelationContext relation = default,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var check = EquipRules.CheckEquip(itemIndex, toSlot, in state, in relation);
        if (check != EquipCheckResult.Allowed)
            return ValueTask.FromResult(check);

        var payload = new byte[EquipChangeWireSize];
        payload[0x00] = mode;
        payload[0x01] = slot;
        payload[0x02] = fromSub;
        payload[0x03] = toSlot;
        payload[0x04] = sub;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0x08, 4), itemIndex);

        return SendResultAsync(2, 16, payload, EquipCheckResult.Allowed, cancellationToken);
    }

    public ValueTask<bool> MoveItemAsync(
        InventoryGrid grid,
        int fromIndex,
        int toIndex,
        uint quantity = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grid);
        cancellationToken.ThrowIfCancellationRequested();

        if (grid.IsEmpty(fromIndex)) return ValueTask.FromResult(false);

        var moved = quantity > 0
            ? ItemStackOps.Split(grid, fromIndex, toIndex, quantity)
            : grid.Move(fromIndex, toIndex);

        if (!moved) return ValueTask.FromResult(false);

        var payload = new byte[EquipChangeWireSize];
        payload[0x00] = 1;
        payload[0x02] = unchecked((byte)fromIndex);
        payload[0x03] = unchecked((byte)toIndex);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0x08, 4), fromIndex);

        return SendResultAsync(2, 16, payload, true, cancellationToken);
    }

    public ValueTask<(TradeSession Next, bool Accepted)> TradeRequestAsync(
        TradeSession session,
        uint partnerActorId,
        byte requestMode = 0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_world.LocalActorKey is { } self && self.RawId == partnerActorId)
            return ValueTask.FromResult((session, false));

        var (next, accepted) = session.Request(partnerActorId);
        if (!accepted) return ValueTask.FromResult((session, false));

        var payload = new byte[TradeRequestWireSize];
        payload[0x00] = requestMode;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x04, 4), partnerActorId);

        return SendResultAsync(2, 23, payload, (next, true), cancellationToken);
    }

    public ValueTask<bool> PartyInviteAsync(
        uint targetActorId,
        byte subOp = 0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (targetActorId == 0 || (_world.LocalActorKey is { } self && self.RawId == targetActorId))
            return ValueTask.FromResult(false);

        var payload = new byte[PartyInviteWireSize];
        payload[0x00] = subOp;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x04, 4), targetActorId);

        return SendResultAsync(2, 35, payload, true, cancellationToken);
    }

    public ValueTask LogoutAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return SendAsync(1, 0, ReadOnlyMemory<byte>.Empty, cancellationToken);
    }

    public async ValueTask LeaveWorldAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_keepaliveDriver is not null)
            await _keepaliveDriver.OnWorldExitedAsync(cancellationToken).ConfigureAwait(false);

        await SendAsync(2, 0, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
    }

    private void SeedEnterTimeLocalPlayer(byte slotIndex)
    {
        if (_localPlayer is null) return;

        var chosen = _characterSelection?.Chosen;
        if (chosen is null || chosen.RawDescriptor.Length < SpawnDescriptorReader.Size)
        {
            _localPlayer.SeedEnterChoice(slotIndex, 0, 0);
            return;
        }

        var reader = new SpawnDescriptorReader(chosen.RawDescriptor.Span[..SpawnDescriptorReader.Size]);
        var level = reader.ReadLevel();

        const byte statusByteGapDefault = 0;
        _localPlayer.SeedEnterChoice(slotIndex, level, statusByteGapDefault);
    }

    private static Encoding CreateCp949()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949);
    }


    private static ServerLoadBand ClassifyLoad(short load)
    {
        return load switch
        {
            > 1200 => ServerLoadBand.Full,
            > 800 => ServerLoadBand.Busy,
            > 500 => ServerLoadBand.Moderate,
            _ => ServerLoadBand.Light
        };
    }

    private static ServerStatusHint ClassifyStatus(short statusCode)
    {
        return statusCode switch
        {
            0 => ServerStatusHint.Normal,
            3 => ServerStatusHint.Special,
            >= 1 and <= 39 => ServerStatusHint.Caption,
            _ => ServerStatusHint.Invalid
        };
    }

    private ValueTask SendChat27Async(uint channel, string? recipientName, string text, CancellationToken ct)
    {
        var isWhisper = !string.IsNullOrEmpty(recipientName) || channel == WhisperChannelCode;
        var bodyByteCount = Cp949.GetByteCount(text);
        if (isWhisper && bodyByteCount > WhisperBodyByteCap)
            bodyByteCount = TruncateCp949(text, WhisperBodyByteCap, out _);

        var payload = new byte[CmsgWhisperHeader.HeaderSize + sizeof(uint) + bodyByteCount + 1];
        Span<byte> p = payload;
        p.Clear();

        p[0x00] = (byte)channel;
        if (isWhisper && recipientName is { Length: > 0 })
            WriteFixedCp949(recipientName, p.Slice(0x02, WhisperTargetNameBytes));


        var tail = p[CmsgWhisperHeader.HeaderSize..];
        BinaryPrimitives.WriteUInt32LittleEndian(tail[..sizeof(uint)], (uint)bodyByteCount);
        Cp949.GetBytes(text.AsSpan(0, Cp949CharCountForBytes(text, bodyByteCount)),
            tail.Slice(sizeof(uint), bodyByteCount));

        return SendAsync(2, 7, payload, ct);
    }

    private static void WriteFixedCp949(string value, Span<byte> destination)
    {
        destination.Clear();
        var charCount = Cp949CharCountForBytes(value, destination.Length - 1);
        if (charCount > 0) Cp949.GetBytes(value.AsSpan(0, charCount), destination);
    }

    private static int TruncateCp949(string text, int byteCap, out int charCount)
    {
        charCount = Cp949CharCountForBytes(text, byteCap);
        return Cp949.GetByteCount(text.AsSpan(0, charCount));
    }

    private static int Cp949CharCountForBytes(string text, int byteCap)
    {
        if (byteCap <= 0) return 0;

        var total = Cp949.GetByteCount(text);
        if (total <= byteCap) return text.Length;

        var chars = 0;
        var bytes = 0;
        while (chars < text.Length)
        {
            var next = Cp949.GetByteCount(text.AsSpan(chars, 1));
            if (bytes + next > byteCap) break;

            bytes += next;
            chars++;
        }

        return chars;
    }

    private static void ValidateSlotIndex(int slotIndex)
    {
        if (slotIndex is < 0 or > CharacterSelectionStore.MaxSlotIndex)
            throw new ArgumentOutOfRangeException(
                nameof(slotIndex), slotIndex,
                "Slot index must be 0..4 (spec: frontend_scenes.md §5 / §7).");
    }

    private CharacterNameValidationResult ValidateCharacterName(string name)
    {
        if (name.Length == 0 ||
            string.Equals(name, CharacterSelectionStore.BlankSlotSentinel, StringComparison.Ordinal))
            return new CharacterNameValidationResult(false, 2190);

        if (BannedCharacterNamePredicate?.Invoke(name) == true) return new CharacterNameValidationResult(false, 2075);

        var cp949Bytes = Cp949.GetByteCount(name);
        if (name.Length < 2 || cp949Bytes > 16) return new CharacterNameValidationResult(false, 12012);

        foreach (var ch in name)
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') continue;

            if (ch is >= '\uAC00' and <= '\uD7A3') continue;

            return new CharacterNameValidationResult(false, 12012);
        }

        return CharacterNameValidationResult.Valid;
    }

    private static ushort RemapCreateClass(byte uiClassIndex)
    {
        return uiClassIndex switch
        {
            0 => 4,
            1 => 1,
            2 => 3,
            3 => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(uiClassIndex), uiClassIndex,
                "UI class index must be 0..3 (spec: character_creation.md §3).")
        };
    }

    public static PointBuyResult BuildPointBuy(
        uint stat0, uint stat1, uint stat2, uint stat3, uint stat4, uint pointsRemaining)
    {
        var statsInRange =
            InClamp(stat0) && InClamp(stat1) && InClamp(stat2) && InClamp(stat3) && InClamp(stat4);
        var budgetInRange = pointsRemaining <= PointBuyBudgetSeed;
        var invariantHolds =
            (ulong)stat0 + stat1 + stat2 + stat3 + stat4 + pointsRemaining == PointBuyInvariantTotal;

        var valid = statsInRange && budgetInRange && invariantHolds;
        return new PointBuyResult(valid, stat0, stat1, stat2, stat3, stat4, pointsRemaining);

        static bool InClamp(uint v)
        {
            return v is >= PointBuyStatFloor and <= PointBuyStatCeil;
        }
    }

    private ValueTask SendAsync(ushort major, ushort minor, ReadOnlySpan<byte> payload, CancellationToken ct)
    {
        var owned = payload.ToArray();
        return _outbound.SendAsync(_sessionId, major, minor, owned, ct);
    }

    private ValueTask SendAsync(ushort major, ushort minor, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        return _outbound.SendAsync(_sessionId, major, minor, payload, ct);
    }

    private async ValueTask<T> SendResultAsync<T>(
        ushort major, ushort minor, byte[] payload, T result, CancellationToken ct)
    {
        await _outbound.SendAsync(_sessionId, major, minor, payload, ct).ConfigureAwait(false);
        return result;
    }
}