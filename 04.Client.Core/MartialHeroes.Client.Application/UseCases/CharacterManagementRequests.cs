namespace MartialHeroes.Client.Application.UseCases;

/// <summary>
///     Character-name validation outcome for create/rename. Message ids are msg.xdb catalogue ids.
///     spec: Docs/RE/specs/frontend_scenes.md §4.4.
/// </summary>
public readonly record struct CharacterNameValidationResult(bool IsValid, int? MessageId)
{
    public static CharacterNameValidationResult Valid { get; } = new(true, null);
}

/// <summary>
///     Presentation-facing character-create request. <paramref name="UiClassIndex" /> is the legacy class
///     button index 0..3; Application remaps it to the internal class id for the 1/6 body.
///     spec: Docs/RE/packets/cmsg_char_create.yaml.
/// </summary>
public readonly record struct CharacterCreateRequest(
    string Name,
    byte UiClassIndex,
    ushort Face,
    ushort Sex,
    ushort HairOrReserved,
    uint Stat0,
    uint Stat1,
    uint Stat2,
    uint Stat3,
    uint Stat4,
    uint PointsRemaining);