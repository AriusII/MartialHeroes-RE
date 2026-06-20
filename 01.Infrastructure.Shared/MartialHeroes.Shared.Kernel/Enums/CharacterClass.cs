namespace MartialHeroes.Shared.Kernel.Enums;

/// <summary>
///     The four playable character classes in Martial Heroes (D.O. Online).
/// </summary>
/// <remarks>
///     <para>
///         Values are <b>1-based</b> (Musa = 1 .. Seungnyeo = 4) as confirmed by the per-class
///         stat-grid loader in the original client: the class index window formula addresses a
///         124-byte block inside <c>data/script/users.scr</c> using class ids 1..4.
///         spec: Docs/RE/formats/config_tables.md §2.6 — "Class names (CONFIRMED)".
///     </para>
///     <para>
///         The underlying type is <see cref="byte" /> because this value crosses the wire as a single
///         byte in character-select and class-descriptor packets.
///         spec: Docs/RE/formats/config_tables.md §3.5 — "current class index: word (u16) active
///         character class, 1..4"; the wire byte width is confirmed by the stance-selector field.
///     </para>
/// </remarks>
public enum CharacterClass : byte
{
    /// <summary>
    ///     무사 (武士) — Musa, the warrior archetype (melee/heavy combat).
    ///     spec: Docs/RE/formats/config_tables.md §2.6 — Class ID 1 = Musa (CONFIRMED).
    /// </summary>
    Musa = 1,

    /// <summary>
    ///     자객 (刺客) — Jagaek, the assassin archetype (swift/critical strikes).
    ///     spec: Docs/RE/formats/config_tables.md §2.6 — Class ID 2 = Jagaek (CONFIRMED).
    /// </summary>
    Jagaek = 2,

    /// <summary>
    ///     도사 (道士) — Dosa, the mystic/Taoist archetype (ranged/elemental).
    ///     spec: Docs/RE/formats/config_tables.md §2.6 — Class ID 3 = Dosa (CONFIRMED).
    /// </summary>
    Dosa = 3,

    /// <summary>
    ///     승려 (僧侶) — Seungnyeo, the monk archetype (support/internal energy).
    ///     spec: Docs/RE/formats/config_tables.md §2.6 — Class ID 4 = Seungnyeo (CONFIRMED).
    /// </summary>
    Seungnyeo = 4
}