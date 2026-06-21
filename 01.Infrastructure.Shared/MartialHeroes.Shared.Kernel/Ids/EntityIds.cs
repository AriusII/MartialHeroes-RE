namespace MartialHeroes.Shared.Kernel.Ids;

// Strongly-typed entity ids. Each is a one-line positional record struct tagged [StronglyTypedId];
// the source generator (00.SourcesGenerators/MartialHeroes.Shared.Kernel.Generators) emits the
// IComparable<T> impl, the `None` zero-sentinel, and the `Prefix(Value)` ToString() — the surface
// every id here used to hand-roll identically. record-struct equality and the primary ctor stay
// compiler-synthesized. spec: Docs/RE/structs/actor.md — server-assigned 32-bit entity ids.

/// <summary>
///     Strongly-typed identifier for a player character.
/// </summary>
/// <remarks>
///     Backed by <see cref="uint" /> because the legacy client stores all in-world entity identities
///     as server-assigned 32-bit integer ids. The Actor's <c>id</c> field at object offset +0x5C
///     is a 4-byte numeric actor id and map key, initialised to 0xFFFFFFFF and set on spawn.
///     spec: Docs/RE/structs/actor.md §"Engine header and identity (+0x00..+0x73)" — field
///     <c>id</c> at offset +0x5C, type int32, "numeric actor id and map key": CONFIRMED.
///     Using <c>uint</c> rather than <c>Guid</c> matches the server-assigned identity model: the
///     server owns the counter and the client receives the value over the wire.
/// </remarks>
/// <param name="Value">The raw 32-bit identifier value.</param>
[StronglyTypedId]
public readonly partial record struct PlayerId(uint Value);

/// <summary>
///     Strongly-typed identifier for a monster (hostile NPC) instance.
/// </summary>
/// <remarks>
///     Backed by <see cref="uint" /> — same rationale as <see cref="PlayerId" />: the Actor
///     <c>id</c> field is a 4-byte server-assigned identity common to all spawn kinds (sort=1 PC,
///     sort=2 Mob, sort=3 NPC, sort=15/17 special).
///     spec: Docs/RE/structs/actor.md §"Engine header and identity (+0x00..+0x73)" — field
///     <c>id</c> at +0x5C, and <c>sort</c> at +0x60 (Mob = default branch): CONFIRMED.
/// </remarks>
/// <param name="Value">The raw 32-bit identifier value.</param>
[StronglyTypedId]
public readonly partial record struct MonsterId(uint Value);

/// <summary>
///     Strongly-typed identifier for an item instance in the world or an inventory slot.
/// </summary>
/// <remarks>
///     Backed by <see cref="uint" /> — the equip-id table at Actor +0xCC stores each worn slot's
///     item actor id as the leading dword of a 16-byte entry, confirming 32-bit item instance ids.
///     spec: Docs/RE/structs/actor.md §"Status header" — "Equipment-id table (Actor +0xCC = SD
///     +0x58) — 20 entries × 16 bytes; each entry's leading dword is a worn-item actor id":
///     CONFIRMED.
///     Do not confuse with an item <em>template</em> id (catalogue entry); that would be a
///     separate type.
/// </remarks>
/// <param name="Value">The raw 32-bit identifier value.</param>
[StronglyTypedId]
public readonly partial record struct ItemId(uint Value);

/// <summary>
///     Strongly-typed identifier for an active skill.
/// </summary>
/// <remarks>
///     Backed by <see cref="uint" /> — skill ids are server-side catalogue entries transmitted as
///     32-bit unsigned integers, consistent with the Actor's 32-bit entity-id convention.
///     spec: Docs/RE/structs/actor.md §"Engine header and identity (+0x00..+0x73)" — 4-byte
///     server-assigned id pattern; skill packet field widths are CAPTURE-PENDING but follow the
///     same 32-bit convention.
/// </remarks>
/// <param name="Value">The raw 32-bit identifier value.</param>
[StronglyTypedId]
public readonly partial record struct SkillId(uint Value);