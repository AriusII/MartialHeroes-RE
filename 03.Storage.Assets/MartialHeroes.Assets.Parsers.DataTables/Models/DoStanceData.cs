using System.Diagnostics.CodeAnalysis;

namespace MartialHeroes.Assets.Parsers.DataTables.Models;

public sealed class DoStanceOverlay
{
    public required bool Present { get; init; }

    public required uint Dx { get; init; }

    public required uint Dy { get; init; }

    public required uint SrcX { get; init; }

    public required uint SrcY { get; init; }

    public required uint Width { get; init; }

    public required uint Height { get; init; }
}

public sealed class DoStanceRecord
{
    public const int Stride = 116;

    public required uint InstanceKey { get; init; }

    public required byte StanceFilter { get; init; }

    public required uint SlotIndex { get; init; }

    public required uint ClassStanceRef { get; init; }

    public required uint WidgetPosX { get; init; }

    public required uint WidgetPosYRaw { get; init; }

    public required uint IconSrcX { get; init; }

    public required uint IconSrcY { get; init; }

    public required uint LevelBarSrcX { get; init; }

    public required uint LevelBarSrcY { get; init; }

    public required DoStanceOverlay Overlay0 { get; init; }

    public required DoStanceOverlay Overlay1 { get; init; }

    public required DoStanceOverlay Overlay2 { get; init; }
}

public sealed class DoStanceTable
{
    public const int RecordStride = DoStanceRecord.Stride;

    private readonly Dictionary<uint, DoStanceRecord> _byInstanceKey;

    private readonly Dictionary<uint, DoStanceRecord> _bySlotIndex;

    [SetsRequiredMembers]
    public DoStanceTable(
        IReadOnlyList<DoStanceRecord> records,
        int totalRecordCount,
        int trailingByteCount)
    {
        Records = records;
        TotalRecordCount = totalRecordCount;
        TrailingByteCount = trailingByteCount;

        _byInstanceKey = new Dictionary<uint, DoStanceRecord>(records.Count);
        _bySlotIndex = new Dictionary<uint, DoStanceRecord>(records.Count);
        foreach (var r in records)
        {
            _byInstanceKey[r.InstanceKey] = r;
            _bySlotIndex[r.SlotIndex] = r;
        }
    }


    public required IReadOnlyList<DoStanceRecord> Records { get; init; }

    public required int TotalRecordCount { get; init; }

    public required int TrailingByteCount { get; init; }

    public DoStanceRecord? GetByInstanceKey(uint instanceKey)
    {
        return _byInstanceKey.TryGetValue(instanceKey, out var r) ? r : null;
    }

    public DoStanceRecord? GetBySlotIndex(uint slotIndex)
    {
        return _bySlotIndex.TryGetValue(slotIndex, out var r) ? r : null;
    }
}