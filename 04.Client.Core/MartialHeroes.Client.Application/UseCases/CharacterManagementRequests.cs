namespace MartialHeroes.Client.Application.UseCases;

public readonly record struct CharacterNameValidationResult(bool IsValid, int? MessageId)
{
    public static CharacterNameValidationResult Valid { get; } = new(true, null);
}

public readonly record struct CharacterCreateRequest(
    string Name,
    byte UiClassIndex,
    ushort Face,
    ushort AppearanceA,
    ushort AppearanceB,
    uint Stat0,
    uint Stat1,
    uint Stat2,
    uint Stat3,
    uint Stat4,
    uint PointsRemaining);

public readonly record struct PointBuyResult(
    bool IsValid,
    uint Stat0,
    uint Stat1,
    uint Stat2,
    uint Stat3,
    uint Stat4,
    uint PointsRemaining);