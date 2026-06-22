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
///     Presentation-facing character-create request. <paramref name="UiClassIndex" /> is the UI class
///     button index 0..3; Application remaps it to the internal class id for the 1/6 body (§3). The five
///     stats + the points budget are validated/normalised by the deterministic point-buy builder
///     (<see cref="ApplicationUseCases.BuildPointBuy" />) before the 1/6 body is written.
///     spec: Docs/RE/packets/cmsg_char_create.yaml; Docs/RE/specs/character_creation.md §1.2 / §2.1 / §3.
/// </summary>
/// <param name="Name">Character name (validated locally; CP949). spec: character_creation.md §2.2.</param>
/// <param name="UiClassIndex">UI class button index 0..3, remapped to the internal id. spec: §3.</param>
/// <param name="Face">Face index 1..7. spec: cmsg_char_create.yaml Face @0x12.</param>
/// <param name="AppearanceA">
///     The AppearanceA word @0x14 (sex-vs-appearance meaning capture-pending; class-implied, not stepped on
///     the create path — defaults to 1). spec: character_creation.md §1.2; cmsg_char_create.yaml AppearanceA @0x14.
/// </param>
/// <param name="AppearanceB">
///     The AppearanceB word @0x16 (appearance index or reserved; capture-pending; defaults to 0). spec:
///     character_creation.md §1.2; cmsg_char_create.yaml AppearanceB @0x16.
/// </param>
/// <param name="Stat0">Point-buy stat 0 (seed 10, clamp [10,15]). spec: §2.1.</param>
/// <param name="Stat1">Point-buy stat 1 (seed 10, clamp [10,15]). spec: §2.1.</param>
/// <param name="Stat2">Point-buy stat 2 (seed 10, clamp [10,15]). spec: §2.1.</param>
/// <param name="Stat3">Point-buy stat 3 (seed 10, clamp [10,15]). spec: §2.1.</param>
/// <param name="Stat4">Point-buy stat 4 (seed 10, clamp [10,15]). spec: §2.1.</param>
/// <param name="PointsRemaining">The remaining point budget (seed 5). spec: §2.1.</param>
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

/// <summary>
///     The result of validating/normalising a five-stat point-buy allocation against the create form's
///     budget rules. <see cref="IsValid" /> is true only when every stat is in [10,15], the budget is in
///     [0,5], and the invariant <c>sum(stats) + points = 55</c> holds. spec:
///     Docs/RE/specs/character_creation.md §2.1.
/// </summary>
public readonly record struct PointBuyResult(
    bool IsValid,
    uint Stat0,
    uint Stat1,
    uint Stat2,
    uint Stat3,
    uint Stat4,
    uint PointsRemaining);