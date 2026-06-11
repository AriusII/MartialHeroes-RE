namespace MartialHeroes.Client.Domain.Quests;

/// <summary>
/// One quest-log entry: a quest id paired (index-aligned) with its display name.
/// spec: Docs/RE/specs/quests.md §6.1 (quest_ids / quest_names parallel arrays) / §6.2.
/// </summary>
/// <remarks>
/// The id table and name table are kept index-aligned (entry i in the id table pairs with entry i in
/// the name table). The name is CP949-decoded upstream and carried as a managed string here; an empty
/// id marks an unused entry. spec: quests.md §6.1/§6.2.
/// </remarks>
public readonly record struct QuestLogEntry(uint QuestId, string Name)
{
    /// <summary>An empty entry. spec: quests.md §6.2.</summary>
    public static QuestLogEntry Empty => new(0u, string.Empty);

    /// <summary>True when the entry holds no quest. spec: quests.md §6.2.</summary>
    public bool IsEmpty => QuestId == 0;
}

/// <summary>
/// The client-side quest-log state mirrored from a 5/68 snapshot: 20 quest entries, two 10-entry slot
/// flag tables, the tracking flag, and the two UI selectors. Deterministic; rebuilt on each snapshot.
/// spec: Docs/RE/specs/quests.md §6.
/// </summary>
/// <remarks>
/// The shape (not the legacy offsets) is modelled per §6.2: a <c>QuestLogEntry[20]</c> plus two
/// <c>byte[10]</c> flag tables and three scalar selectors. The wire field offsets / sizes are
/// <c>UNVERIFIED</c> (no capture); only the logical shape and the entry counts are modelled. The
/// tracking-flag 0 → non-zero transition is what opens the tracking panel. spec: quests.md §6.1/§6.2/§13.
/// </remarks>
public sealed class QuestLog
{
    /// <summary>Number of quest-log entries (ids + names). spec: quests.md §6.1 / §10 (20).</summary>
    public const int EntryCount = 20;

    /// <summary>Number of slot-A / slot-B flags (each table). spec: quests.md §6.1 / §10 (10 + 10).</summary>
    public const int SlotFlagCount = 10;

    private readonly QuestLogEntry[] _entries = new QuestLogEntry[EntryCount];
    private readonly byte[] _slotAFlags = new byte[SlotFlagCount];
    private readonly byte[] _slotBFlags = new byte[SlotFlagCount];

    /// <summary>The active / tracking flag — drives the tracking-panel open/close. spec: quests.md §6 / §6.1.</summary>
    public byte TrackingFlag { get; private set; }

    /// <summary>UI selector B (panel_b), stored from the snapshot. spec: quests.md §6.1 (panel_b; UNVERIFIED).</summary>
    public byte PanelB { get; private set; }

    /// <summary>UI selector C (panel_c), stored from the snapshot. spec: quests.md §6.1 (panel_c; UNVERIFIED).</summary>
    public byte PanelC { get; private set; }

    /// <summary>Reads the quest-log entry at <paramref name="index"/> (0..19). spec: quests.md §6.2.</summary>
    public QuestLogEntry EntryAt(int index) => _entries[index];

    /// <summary>Reads the slot-A flag at <paramref name="index"/> (0..9). spec: quests.md §6.1.</summary>
    public byte SlotAFlag(int index) => _slotAFlags[index];

    /// <summary>Reads the slot-B flag at <paramref name="index"/> (0..9). spec: quests.md §6.1.</summary>
    public byte SlotBFlag(int index) => _slotBFlags[index];

    /// <summary>The number of non-empty quest entries. spec: quests.md §6.2.</summary>
    public int ActiveEntryCount
    {
        get
        {
            int c = 0;
            for (int i = 0; i < _entries.Length; i++)
            {
                if (!_entries[i].IsEmpty)
                {
                    c++;
                }
            }

            return c;
        }
    }

    /// <summary>
    /// Applies a 5/68 quest-log snapshot, returning whether the tracking flag transitioned 0 → non-zero
    /// (which opens the tracking panel and plays the quest-tracking-on sound). The two slot-table header
    /// blocks are zero-cleared on every refresh. spec: Docs/RE/specs/quests.md §6 / §6.1 / §6.2.
    /// </summary>
    /// <param name="entries">Up to 20 (id, name) entries (index-aligned). spec: §6.1.</param>
    /// <param name="slotAFlags">Up to 10 slot-A flags. spec: §6.1.</param>
    /// <param name="slotBFlags">Up to 10 slot-B flags. spec: §6.1.</param>
    /// <param name="trackingFlag">The active / tracking flag. spec: §6.1.</param>
    /// <param name="panelB">UI selector B. spec: §6.1.</param>
    /// <param name="panelC">UI selector C. spec: §6.1.</param>
    /// <returns><c>true</c> when tracking transitioned 0 → non-zero (open tracking panel). spec: §6.</returns>
    public bool ApplySnapshot(
        ReadOnlySpan<QuestLogEntry> entries,
        ReadOnlySpan<byte> slotAFlags,
        ReadOnlySpan<byte> slotBFlags,
        byte trackingFlag,
        byte panelB,
        byte panelC)
    {
        if (entries.Length > EntryCount)
        {
            throw new ArgumentException($"At most {EntryCount} entries are allowed.", nameof(entries));
        }

        if (slotAFlags.Length > SlotFlagCount || slotBFlags.Length > SlotFlagCount)
        {
            throw new ArgumentException($"At most {SlotFlagCount} slot flags are allowed.");
        }

        byte previousTracking = TrackingFlag;

        // Zero-clear then copy the provided rows. spec: §6.2 ("zero-cleared on every refresh").
        Array.Clear(_entries);
        for (int i = 0; i < entries.Length; i++)
        {
            _entries[i] = entries[i];
        }

        Array.Clear(_slotAFlags);
        for (int i = 0; i < slotAFlags.Length; i++)
        {
            _slotAFlags[i] = slotAFlags[i];
        }

        Array.Clear(_slotBFlags);
        for (int i = 0; i < slotBFlags.Length; i++)
        {
            _slotBFlags[i] = slotBFlags[i];
        }

        TrackingFlag = trackingFlag;
        PanelB = panelB;
        PanelC = panelC;

        // Tracking 0 → non-zero opens the tracking panel + plays sound. spec: §6.
        return previousTracking == 0 && trackingFlag != 0;
    }

    /// <summary>True when <paramref name="questId"/> is present in the log. spec: quests.md §6.2.</summary>
    public bool Contains(uint questId)
    {
        if (questId == 0)
        {
            return false;
        }

        for (int i = 0; i < _entries.Length; i++)
        {
            if (_entries[i].QuestId == questId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Clears the entire quest-log state. spec: quests.md §6.2.</summary>
    public void Clear()
    {
        Array.Clear(_entries);
        Array.Clear(_slotAFlags);
        Array.Clear(_slotBFlags);
        TrackingFlag = 0;
        PanelB = 0;
        PanelC = 0;
    }
}
