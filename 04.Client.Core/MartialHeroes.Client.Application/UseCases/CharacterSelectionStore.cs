using MartialHeroes.Client.Application.World;

namespace MartialHeroes.Client.Application.UseCases;

/// <summary>
/// Session-scoped store bridging the character-select flow across the network/use-case boundary:
/// the 3/1 SmsgCharacterList handler retains each occupied slot's <b>raw</b> per-slot record (the
/// 880-byte SpawnDescriptor + 96-byte stats block + flag byte); <see cref="ApplicationUseCases.SelectCharacterAsync"/>
/// confirms the chosen slot (caching it as <see cref="Chosen"/>); and the 3/14 SmsgCharSpawnResponse
/// handler materializes the local player from that cached descriptor.
/// spec: Docs/RE/specs/login_flow.md §3.5 ("caches the chosen slot's record locally ... consumed on 3/14").
/// </summary>
/// <remarks>
/// <para>
/// <b>The empty-slot sentinel.</b> A slot whose descriptor name equals the literal
/// <see cref="BlankSlotSentinel"/> (<c>"@BLANK@"</c>) is an unoccupied slot; confirming it routes to
/// character creation instead of enter-game. spec: login_flow.md §3.3 / §3.5 / §7.
/// </para>
/// <para>
/// <b>The hard 5-slot bound.</b> The char list supports a maximum of 5 slots (indices 0..4), enforced
/// both at parse time and as the enter-game slot-range guard. spec: login_flow.md §3.2 / §3.5 / §7
/// ("slot index ≤ 4").
/// </para>
/// <para>
/// <b>Threading.</b> Mutated only by the single logical owner (the network reader / use-case caller);
/// deliberately lock-free, like <see cref="ClientWorld"/> and <see cref="LocalPlayerState"/>.
/// </para>
/// </remarks>
public sealed class CharacterSelectionStore
{
    /// <summary>
    /// Maximum number of character slots (indices 0..4). Hard loop bound; also the enter-game
    /// slot-range guard. spec: Docs/RE/specs/login_flow.md §7 ("Char-list maximum slots = 5").
    /// </summary>
    public const int MaxSlots = 5;

    /// <summary>The highest valid slot index (4). spec: Docs/RE/specs/login_flow.md §3.5 ("slot index ≤ 4").</summary>
    public const int MaxSlotIndex = MaxSlots - 1;

    /// <summary>
    /// The literal empty-slot sentinel name. A slot whose descriptor name equals this marks an
    /// unoccupied slot and routes to character creation on confirm. spec:
    /// Docs/RE/specs/login_flow.md §3.3 / §7 (Empty-slot sentinel = "@BLANK@").
    /// </summary>
    public const string BlankSlotSentinel = "@BLANK@";

    // Indexed by slot 0..4; null = no record retained for that slot. Bounded, fixed-size — no churn.
    private readonly CharacterSlotRecord?[] _slots = new CharacterSlotRecord?[MaxSlots];

    /// <summary>
    /// The slot the player confirmed via <see cref="ApplicationUseCases.SelectCharacterAsync"/>, cached
    /// for the 3/14 spawn. <see langword="null"/> until a real (non-blank, in-range) slot is confirmed.
    /// spec: Docs/RE/specs/login_flow.md §3.5.
    /// </summary>
    public CharacterSlotRecord? Chosen { get; private set; }

    /// <summary>
    /// Clears all retained slots and the chosen cache (e.g. on a fresh 3/1 list or disconnect). spec:
    /// Docs/RE/specs/login_flow.md §3.2 (the list replaces the prior roster).
    /// </summary>
    public void Reset()
    {
        Array.Clear(_slots);
        Chosen = null;
    }

    /// <summary>
    /// Retains the raw per-slot record for an occupied slot, decoded by the 3/1 handler. Out-of-range
    /// slot indices are ignored (the parse loop never references a slot beyond 4). spec:
    /// Docs/RE/specs/login_flow.md §3.2 / §3.5.
    /// </summary>
    public void Retain(in CharacterSlotRecord record)
    {
        if ((uint)record.SlotIndex >= (uint)MaxSlots)
        {
            return; // hard 5-slot bound: ignore any slot index beyond 4. spec: §3.2.
        }

        _slots[record.SlotIndex] = record;
    }

    /// <summary>Gets the retained record for <paramref name="slotIndex"/>, or <see langword="null"/> if none.</summary>
    public CharacterSlotRecord? Get(int slotIndex) =>
        (uint)slotIndex < (uint)MaxSlots ? _slots[slotIndex] : null;

    /// <summary>
    /// The outcome of confirming a slot for enter-game. spec: Docs/RE/specs/login_flow.md §3.3 / §3.5.
    /// </summary>
    public enum SelectOutcome
    {
        /// <summary>The slot index is out of range (&gt; 4) or unoccupied (no retained record).</summary>
        Invalid,

        /// <summary>The slot holds the "@BLANK@" sentinel; route to character creation. spec: §3.3.</summary>
        Blank,

        /// <summary>A real character: cached as <see cref="Chosen"/>; proceed to send 1/9. spec: §3.5.</summary>
        Confirmed,
    }

    /// <summary>
    /// Validates and confirms a slot selection. Enforces the slot-range guard (≤ 4), detects the
    /// "@BLANK@" empty-slot sentinel, and on a real character caches the slot's record as
    /// <see cref="Chosen"/> for the 3/14 spawn. spec: Docs/RE/specs/login_flow.md §3.3 / §3.5.
    /// </summary>
    public SelectOutcome Confirm(int slotIndex)
    {
        if ((uint)slotIndex >= (uint)MaxSlots)
        {
            return SelectOutcome.Invalid; // slot must be 0..4. spec: §3.5 ("slot index ≤ 4").
        }

        CharacterSlotRecord? record = _slots[slotIndex];
        if (record is null)
        {
            return SelectOutcome.Invalid; // no record retained for an unoccupied/unknown slot.
        }

        if (string.Equals(record.Name, BlankSlotSentinel, StringComparison.Ordinal))
        {
            return SelectOutcome.Blank; // empty-slot sentinel: route to creation. spec: §3.3.
        }

        Chosen = record; // cache for the 3/14 spawn. spec: §3.5.
        return SelectOutcome.Confirmed;
    }
}

/// <summary>
/// A retained character-select slot record: the decoded display fields plus the <b>raw</b> 880-byte
/// SpawnDescriptor (and the 96-byte stats block + flag byte) the 3/14 handler consumes to materialize
/// the local player. spec: Docs/RE/specs/login_flow.md §3.2 / §3.5; Docs/RE/structs/spawn_descriptor.md.
/// </summary>
/// <remarks>
/// The raw descriptor is copied into an owning array at retain time (the 3/1 payload span does not
/// outlive the handler call). One small allocation per occupied slot at list time — not on a hot path.
/// </remarks>
public sealed class CharacterSlotRecord
{
    /// <summary>The slot index (0..4). spec: Docs/RE/specs/login_flow.md §3.2.</summary>
    public int SlotIndex { get; }

    /// <summary>Decoded display name (compared against the "@BLANK@" sentinel). spec: §3.5.</summary>
    public string Name { get; }

    /// <summary>Character level. spec: Docs/RE/structs/spawn_descriptor.md (+0x3A).</summary>
    public ushort Level { get; }

    /// <summary>Server-assigned class id. spec: Docs/RE/structs/spawn_descriptor.md (+0x74).</summary>
    public ushort ServerClass { get; }

    /// <summary>Current hit points from the descriptor. spec: Docs/RE/structs/spawn_descriptor.md (+0x3C).</summary>
    public uint CurrentHp { get; }

    /// <summary>Current mana / ki. spec: Docs/RE/structs/spawn_descriptor.md (+0x40).</summary>
    public uint CurrentMp { get; }

    /// <summary>Current stamina. spec: Docs/RE/structs/spawn_descriptor.md (+0x44).</summary>
    public uint CurrentStamina { get; }

    /// <summary>World X (float). spec: Docs/RE/structs/spawn_descriptor.md (+0x4C).</summary>
    public float WorldX { get; }

    /// <summary>World Z (float). spec: Docs/RE/structs/spawn_descriptor.md (+0x50).</summary>
    public float WorldZ { get; }

    /// <summary>
    /// The raw 880-byte SpawnDescriptor + 96-byte stats block + 1 flag byte, copied verbatim from the
    /// 3/1 per-slot record. The world load consumes these on 3/14. spec: login_flow.md §3.5
    /// (caches the 880-byte descriptor + 96-byte stats block + flag).
    /// </summary>
    public ReadOnlyMemory<byte> RawDescriptor { get; }

    /// <summary>The per-slot flag byte (state / availability class). spec: login_flow.md §3.2 (+976).</summary>
    public byte SlotFlag { get; }

    /// <summary>
    /// Builds a retained record by decoding the descriptor's display fields and copying the raw bytes.
    /// </summary>
    /// <param name="slotIndex">The slot index (0..4).</param>
    /// <param name="rawDescriptorAndStats">The 880-byte descriptor + 96-byte stats block (raw); copied.</param>
    /// <param name="slotFlag">The per-slot flag byte (+976 in the 3/1 record).</param>
    public CharacterSlotRecord(int slotIndex, ReadOnlySpan<byte> rawDescriptorAndStats, byte slotFlag)
    {
        SlotIndex = slotIndex;
        SlotFlag = slotFlag;
        RawDescriptor = rawDescriptorAndStats.ToArray();

        // Decode the display fields from the embedded 880-byte descriptor (the first part of the blob).
        // spec: Docs/RE/structs/spawn_descriptor.md (descriptor sub-offsets).
        var reader = new SpawnDescriptorReader(rawDescriptorAndStats[..SpawnDescriptorReader.Size]);
        Name = reader.ReadName();
        Level = reader.ReadLevel();
        ServerClass = reader.ReadServerClass();
        CurrentHp = reader.ReadCurrentHp();
        CurrentMp = reader.ReadCurrentMp();
        CurrentStamina = reader.ReadCurrentStamina();
        WorldX = reader.ReadWorldX();
        WorldZ = reader.ReadWorldZ();
    }
}