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
}