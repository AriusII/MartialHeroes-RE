namespace MartialHeroes.Client.Domain.Quests.Quests;

public readonly record struct QuestLogEntry(uint QuestId, string Name)
{
    public static QuestLogEntry Empty => new(0u, string.Empty);

    public bool IsEmpty => QuestId == 0;
}

public sealed class QuestLog
{
    public const int EntryCount = 20;

    public const int SlotFlagCount = 10;

    private readonly QuestLogEntry[] _entries = new QuestLogEntry[EntryCount];
    private readonly byte[] _slotAFlags = new byte[SlotFlagCount];
    private readonly byte[] _slotBFlags = new byte[SlotFlagCount];

    public byte TrackingFlag { get; private set; }

    public byte PanelB { get; private set; }

    public byte PanelC { get; private set; }

    public int ActiveEntryCount
    {
        get
        {
            var c = 0;
            for (var i = 0; i < _entries.Length; i++)
                if (!_entries[i].IsEmpty)
                    c++;

            return c;
        }
    }

    public QuestLogEntry EntryAt(int index)
    {
        return _entries[index];
    }

    public byte SlotAFlag(int index)
    {
        return _slotAFlags[index];
    }

    public byte SlotBFlag(int index)
    {
        return _slotBFlags[index];
    }

    public bool ApplySnapshot(
        ReadOnlySpan<QuestLogEntry> entries,
        ReadOnlySpan<byte> slotAFlags,
        ReadOnlySpan<byte> slotBFlags,
        byte trackingFlag,
        byte panelB,
        byte panelC)
    {
        if (entries.Length > EntryCount)
            throw new ArgumentException($"At most {EntryCount} entries are allowed.", nameof(entries));

        if (slotAFlags.Length > SlotFlagCount || slotBFlags.Length > SlotFlagCount)
            throw new ArgumentException($"At most {SlotFlagCount} slot flags are allowed.");

        var previousTracking = TrackingFlag;

        Array.Clear(_entries);
        for (var i = 0; i < entries.Length; i++) _entries[i] = entries[i];

        Array.Clear(_slotAFlags);
        for (var i = 0; i < slotAFlags.Length; i++) _slotAFlags[i] = slotAFlags[i];

        Array.Clear(_slotBFlags);
        for (var i = 0; i < slotBFlags.Length; i++) _slotBFlags[i] = slotBFlags[i];

        TrackingFlag = trackingFlag;
        PanelB = panelB;
        PanelC = panelC;

        return previousTracking == 0 && trackingFlag != 0;
    }

    public bool Contains(uint questId)
    {
        if (questId == 0) return false;

        for (var i = 0; i < _entries.Length; i++)
            if (_entries[i].QuestId == questId)
                return true;

        return false;
    }

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