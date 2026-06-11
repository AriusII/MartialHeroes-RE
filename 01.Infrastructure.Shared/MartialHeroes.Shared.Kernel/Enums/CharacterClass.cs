namespace MartialHeroes.Shared.Kernel.Enums;

/// <summary>
/// The playable character classes available in Martial Heroes.
/// </summary>
/// <remarks>
/// The underlying type is <see cref="byte"/> because this value crosses the wire as a single
/// byte in character-select and login-related packets. Numeric values are provisional and must
/// be confirmed by <c>protocol-spec-author</c> once the relevant packet specs are documented
/// (see <c>Docs/RE/packets/</c>). Do not assign non-zero values here without a spec citation.
/// </remarks>
public enum CharacterClass : byte
{
    /// <summary>A melee combat class specialising in high defence and sustained damage.</summary>
    Warrior = 0,

    /// <summary>A ranged/magic combat class specialising in elemental and area attacks.</summary>
    Mage = 1,

    /// <summary>A swift melee class specialising in critical strikes and evasion.</summary>
    Assassin = 2,

    /// <summary>A support/hybrid class drawing on internal energy and healing arts.</summary>
    Monk = 3,
}
