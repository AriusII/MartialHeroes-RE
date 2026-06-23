using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Domain.Inventory.Inventory;
using MartialHeroes.Client.Domain.Skills.Skills;
using MartialHeroes.Network.Abstractions.Lobby;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.UseCases;

public interface IApplicationUseCases
{
    ValueTask LoginAsync(
        string username, string password, string? pin = null, CancellationToken cancellationToken = default);


    ValueTask<IReadOnlyList<LobbyServerRecord>> FetchServerListAsync(
        CancellationToken cancellationToken = default);

    ValueTask<LobbyChannelEndpoint> SelectServerAsync(
        ushort serverId, CancellationToken cancellationToken = default);

    ValueTask RequestMoveAsync(Vector3Fixed target, bool running, CancellationToken cancellationToken = default);

    ValueTask SelectCharacterAsync(int slotIndex, CancellationToken cancellationToken = default);

    ValueTask EmitEnterWorldRequest(byte slotIndex, CancellationToken cancellationToken = default);

    IReadOnlyList<CharacterSlotRecord?> GetCharacterRoster();

    ValueTask<CharacterNameValidationResult> CreateCharacterAsync(
        CharacterCreateRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteCharacterAsync(int slotIndex, CancellationToken cancellationToken = default);

    ValueTask<CharacterNameValidationResult> RenameCharacterAsync(
        int slotIndex,
        string newName,
        CancellationToken cancellationToken = default);

    ValueTask UseSkillAsync(
        byte slot,
        ReadOnlyMemory<uint> targetsA = default,
        ReadOnlyMemory<uint> targetsB = default,
        CancellationToken cancellationToken = default);

    ValueTask SendChatAsync(
        uint channel,
        string text,
        string? recipientName = null,
        CancellationToken cancellationToken = default);

    ValueTask<SkillCastResult> CastSkillAsync(
        byte slot,
        SkillDefinition skill,
        CasterState caster,
        ISkillTargetingQuery targeting,
        Vector3Fixed aimPoint,
        long nowMs,
        ReadOnlyMemory<uint> targetsA = default,
        ReadOnlyMemory<uint> targetsB = default,
        CancellationToken cancellationToken = default);

    ValueTask<EquipCheckResult> EquipItemAsync(
        byte mode,
        byte slot,
        byte fromSub,
        byte toSlot,
        byte sub,
        int itemIndex,
        EquipStateGates state,
        EquipRelationContext relation = default,
        CancellationToken cancellationToken = default);

    ValueTask<bool> MoveItemAsync(
        InventoryGrid grid,
        int fromIndex,
        int toIndex,
        uint quantity = 0,
        CancellationToken cancellationToken = default);

    ValueTask<(TradeSession Next, bool Accepted)> TradeRequestAsync(
        TradeSession session,
        uint partnerActorId,
        byte requestMode = 0,
        CancellationToken cancellationToken = default);

    ValueTask<bool> PartyInviteAsync(
        uint targetActorId,
        byte subOp = 0,
        CancellationToken cancellationToken = default);

    ValueTask LogoutAsync(CancellationToken cancellationToken = default);

    ValueTask LeaveWorldAsync(CancellationToken cancellationToken = default);
}