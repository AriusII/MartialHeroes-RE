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

    IReadOnlyList<CharacterSlotRecord?> GetCharacterRoster();

    ValueTask<CharacterNameValidationResult> CreateCharacterAsync(
        CharacterCreateRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteCharacterAsync(int slotIndex, CancellationToken cancellationToken = default);

    ValueTask<CharacterNameValidationResult> RenameCharacterAsync(
        int slotIndex,
        string newName,
        CancellationToken cancellationToken = default);

    ValueTask MoveCharacterSlotAsync(int fromSlot, int toSlot, CancellationToken cancellationToken = default);

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

    ValueTask ConfirmTenderAsync(CancellationToken cancellationToken = default);

    ValueTask SendPartyInviteAsync(uint targetActorId, CancellationToken ct = default);

    ValueTask RespondToPartyInviteAsync(bool accept, CancellationToken ct = default);

    ValueTask LeavePartyAsync(CancellationToken ct = default);

    ValueTask KickPartyMemberAsync(uint memberId, CancellationToken ct = default);

    ValueTask TransferPartyLeaderAsync(uint newLeaderId, CancellationToken ct = default);

    ValueTask RequestTradeAsync(uint targetActorId, CancellationToken ct = default);

    ValueTask StorageOperationAsync(byte op, CancellationToken ct = default);

    ValueTask SubmitCubeGambleBetAsync(ReadOnlyMemory<byte> betSheet76, CancellationToken ct = default);

    ValueTask BuyProductAsync(uint productId, ushort qty, CancellationToken ct = default);

    ValueTask ConfirmProductPurchaseAsync(uint confirmId, CancellationToken ct = default);

    ValueTask SendGuildActionAsync(byte mode, uint targetId, CancellationToken ct = default);

    ValueTask SendMailAsync(string recipient, string body, CancellationToken ct = default);

    ValueTask ClaimDeliveryAsync(int cellIndex, CancellationToken ct = default);

    ValueTask AddFriendAsync(string name, CancellationToken ct = default);

    ValueTask RemoveFriendAsync(string name, CancellationToken ct = default);

    ValueTask RefreshFriendListAsync(CancellationToken ct = default);
}