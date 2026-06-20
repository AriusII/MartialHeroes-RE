namespace MartialHeroes.Client.Application.UseCases;

/// <summary>
///     Session-scoped holder for the account's character count, an orchestration counter the
///     character-management handlers keep in step with the server: a 3/23 create-success increments it and
///     a 3/4 subtype-2 delete-confirm decrements it. spec: Docs/RE/specs/login_flow.md §5.4 ("on success
///     the account character count is incremented") / §5.5 ("subtype 2 = delete-confirm ... decrements the
///     account character count").
/// </summary>
/// <remarks>
///     <para>
///         This is NOT game-rule state — it is a plain counter the select screen mirrors. It is clamped to the
///         0..<see cref="MaxCount" /> range so an out-of-order result never drives it negative or past the
///         5-slot bound. The authoritative seed is the 3/5 enter-game ack's char-count field (or the 3/1
///         occupied-slot population); this holder tracks the deltas the management results imply afterward.
///     </para>
///     <para>
///         <b>Threading.</b> Mutated only by the single logical owner (the network reader). Lock-free, like
///         <see cref="CharacterSelectionStore" /> and <see cref="World.ClientWorld" />.
///     </para>
/// </remarks>
public sealed class AccountCharacterState
{
    /// <summary>
    ///     The hard upper bound on the character count, equal to the 5-slot char-list maximum. spec:
    ///     Docs/RE/specs/login_flow.md §7 ("Char-list maximum slots = 5").
    /// </summary>
    public const int MaxCount = CharacterSelectionStore.MaxSlots;

    /// <summary>Creates the holder with an initial count (clamped to 0..<see cref="MaxCount" />).</summary>
    public AccountCharacterState(int initialCount = 0)
    {
        CharacterCount = Clamp(initialCount);
    }

    /// <summary>The current account character count (0..<see cref="MaxCount" />).</summary>
    public int CharacterCount { get; private set; }

    /// <summary>
    ///     Seeds the count authoritatively (e.g. from the 3/5 enter-game ack or the 3/1 occupied-slot
    ///     population). Clamped to 0..<see cref="MaxCount" />.
    /// </summary>
    public void Set(int count)
    {
        CharacterCount = Clamp(count);
    }

    /// <summary>
    ///     Increments the count on a create-success (3/23). Clamped at <see cref="MaxCount" />. Returns the
    ///     new count. spec: Docs/RE/specs/login_flow.md §5.4.
    /// </summary>
    public int Increment()
    {
        CharacterCount = Clamp(CharacterCount + 1);
        return CharacterCount;
    }

    /// <summary>
    ///     Decrements the count on a delete-confirm (3/4 subtype 2). Clamped at 0. Returns the new count.
    ///     spec: Docs/RE/specs/login_flow.md §5.5.
    /// </summary>
    public int Decrement()
    {
        CharacterCount = Clamp(CharacterCount - 1);
        return CharacterCount;
    }

    private static int Clamp(int value)
    {
        return Math.Clamp(value, 0, MaxCount);
    }
}