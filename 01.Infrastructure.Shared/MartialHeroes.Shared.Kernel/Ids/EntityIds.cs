namespace MartialHeroes.Shared.Kernel.Ids;

/// <summary>
/// Strongly-typed identifier for a player character.
/// </summary>
/// <remarks>
/// Backed by <see cref="uint"/> because the legacy 32-bit client stores all entity identifiers
/// as unsigned 32-bit integers in every known packet field (wire layout confirmed by RE convention;
/// no packet spec assigned yet — tracking issue: protocol-spec-author to confirm field widths).
/// Using <c>uint</c> rather than <c>Guid</c> matches the server-assigned identity model: the
/// server owns the counter and the client receives the value over the wire.
/// </remarks>
/// <param name="Value">The raw 32-bit identifier value.</param>
public readonly record struct PlayerId(uint Value) : IComparable<PlayerId>
{
    /// <summary>Sentinel representing the absence of a player (value 0).</summary>
    public static readonly PlayerId None = new(0u);

    /// <inheritdoc />
    public int CompareTo(PlayerId other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public override string ToString() => $"Player({Value})";
}

/// <summary>
/// Strongly-typed identifier for a monster (hostile NPC) instance.
/// </summary>
/// <remarks>
/// Backed by <see cref="uint"/> — same rationale as <see cref="PlayerId"/>: server-assigned
/// 32-bit entity slot in the legacy protocol.
/// </remarks>
/// <param name="Value">The raw 32-bit identifier value.</param>
public readonly record struct MonsterId(uint Value) : IComparable<MonsterId>
{
    /// <summary>Sentinel representing the absence of a monster (value 0).</summary>
    public static readonly MonsterId None = new(0u);

    /// <inheritdoc />
    public int CompareTo(MonsterId other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public override string ToString() => $"Monster({Value})";
}

/// <summary>
/// Strongly-typed identifier for an item instance in the world or an inventory slot.
/// </summary>
/// <remarks>
/// Backed by <see cref="uint"/> — server-assigned 32-bit item instance id in the legacy
/// protocol. Do not confuse with an item <em>template</em> id (catalog entry); that would
/// be a separate type once the asset spec is available.
/// </remarks>
/// <param name="Value">The raw 32-bit identifier value.</param>
public readonly record struct ItemId(uint Value) : IComparable<ItemId>
{
    /// <summary>Sentinel representing the absence of an item (value 0).</summary>
    public static readonly ItemId None = new(0u);

    /// <inheritdoc />
    public int CompareTo(ItemId other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public override string ToString() => $"Item({Value})";
}

/// <summary>
/// Strongly-typed identifier for an active skill.
/// </summary>
/// <remarks>
/// Backed by <see cref="uint"/> — skill ids are expected to be server-side catalog entries
/// transmitted as 32-bit unsigned integers. Underlying size to be confirmed by
/// <c>protocol-spec-author</c> once skill-related packets are documented.
/// </remarks>
/// <param name="Value">The raw 32-bit identifier value.</param>
public readonly record struct SkillId(uint Value) : IComparable<SkillId>
{
    /// <summary>Sentinel representing the absence of a skill (value 0).</summary>
    public static readonly SkillId None = new(0u);

    /// <inheritdoc />
    public int CompareTo(SkillId other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public override string ToString() => $"Skill({Value})";
}