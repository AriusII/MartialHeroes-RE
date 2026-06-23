using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Assets.Parsers.DataTables.Models;

[InlineArray(DoStanceRecord.TailByteCount)]
public struct DoStanceTail72
{
    private byte _e0;

    public Span<byte> AsSpan()
    {
        return MemoryMarshal.CreateSpan(ref _e0, DoStanceRecord.TailByteCount);
    }

    public ReadOnlySpan<byte> AsReadOnlySpan()
    {
        return MemoryMarshal.CreateReadOnlySpan(ref _e0, DoStanceRecord.TailByteCount);
    }
}

public sealed class DoStanceRecord
{
    public const int Stride = 116;

    public const int TailByteCount = 76;


    public required uint InstanceKey { get; init; }

    public required uint GroupSubIndex { get; init; }

    public required uint SlotIndex { get; init; }

    public required uint ClassStanceRef { get; init; }

    public required uint GroupId { get; init; }

    public required ushort SecondaryXVariant { get; init; }

    public required short IconSrcX { get; init; }

    public required short IconSrcY { get; init; }

    public required ushort SecondarySpriteX { get; init; }

    public required ushort SecondarySpriteY { get; init; }


    public required DoStanceTail72 Tail { get; init; }
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