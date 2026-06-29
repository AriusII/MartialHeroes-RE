namespace MartialHeroes.Client.Domain.Quests.Quests;

public sealed class QuestLog
{
    public const int EntryCount = 20;

    public const int FlagTableCount = 10;

    private readonly QuestLogEntry[] _entries = new QuestLogEntry[EntryCount];

    private readonly byte[] _slotAFlags = new byte[FlagTableCount];

    private readonly byte[] _slotBFlags = new byte[FlagTableCount];

    public byte TrackingFlag { get; private set; }

    public byte PanelB { get; private set; }

    public byte PanelC { get; private set; }

    public QuestLogEntry this[int index] => (uint)index < (uint)EntryCount ? _entries[index] : QuestLogEntry.Empty;

    public byte SlotAFlag(int index)
    {
        return (uint)index < (uint)FlagTableCount ? _slotAFlags[index] : (byte)0;
    }

    public byte SlotBFlag(int index)
    {
        return (uint)index < (uint)FlagTableCount ? _slotBFlags[index] : (byte)0;
    }

    public void SetEntry(int index, in QuestLogEntry entry)
    {
        if ((uint)index >= (uint)EntryCount) throw new ArgumentOutOfRangeException(nameof(index));

        _entries[index] = entry;
    }

    public void SetState(int index, QuestProgressState state)
    {
        if ((uint)index >= (uint)EntryCount) throw new ArgumentOutOfRangeException(nameof(index));

        _entries[index] = _entries[index] with { State = state };
    }

    public void SetSlotAFlag(int index, byte value)
    {
        if ((uint)index >= (uint)FlagTableCount) throw new ArgumentOutOfRangeException(nameof(index));

        _slotAFlags[index] = value;
    }

    public void SetSlotBFlag(int index, byte value)
    {
        if ((uint)index >= (uint)FlagTableCount) throw new ArgumentOutOfRangeException(nameof(index));

        _slotBFlags[index] = value;
    }

    public bool SetScalars(byte trackingFlag, byte panelB, byte panelC)
    {
        var trackingTurnedOn = TrackingFlag == 0 && trackingFlag != 0;
        TrackingFlag = trackingFlag;
        PanelB = panelB;
        PanelC = panelC;
        return trackingTurnedOn;
    }

    public int CountInState(QuestProgressState state)
    {
        var count = 0;
        for (var i = 0; i < EntryCount; i++)
            if (_entries[i].State == state)
                count++;

        return count;
    }

    public bool TryFindByQuestId(uint questId, out QuestLogEntry entry, out int index)
    {
        for (var i = 0; i < EntryCount; i++)
        {
            if (_entries[i].QuestId == questId)
            {
                entry = _entries[i];
                index = i;
                return true;
            }
        }

        entry = QuestLogEntry.Empty;
        index = -1;
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
