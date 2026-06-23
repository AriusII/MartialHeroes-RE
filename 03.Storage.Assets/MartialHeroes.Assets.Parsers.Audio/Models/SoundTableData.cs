using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Assets.Parsers.Audio.Models;

public enum SoundTableExtension : byte
{
    Bgm,

    Bge,

    Eff,

    Wlk,

    Run
}

[InlineArray(SoundTableData.HoursPerDay)]
public struct HourSchedule24
{
    private byte _e0;

    public int Length => SoundTableData.HoursPerDay;

    public Span<byte> AsSpan()
    {
        return MemoryMarshal.CreateSpan(ref _e0, SoundTableData.HoursPerDay);
    }

    public ReadOnlySpan<byte> AsReadOnlySpan()
    {
        return MemoryMarshal.CreateReadOnlySpan(ref _e0, SoundTableData.HoursPerDay);
    }
}

public sealed class SoundTableEntry
{
    public required uint SoundEntryId { get; init; }

    public required HourSchedule24 HourSchedule { get; init; }

    public required float Weight { get; init; }

    public required float PosX { get; init; }

    public required uint Unlabeled24 { get; init; }

    public required float PosZ { get; init; }

    public required float Radius { get; init; }

    public bool IsAssigned => SoundEntryId != 0;
}

public sealed class SoundTableData
{
    public const int FixedFileSize = 0x3400;

    public const int EntryCount = 256;

    public const int EntryStride = 48;

    public const int ReadSize = 0x3000;

    public const int TrailerSize = 0x0400;

    public const int HoursPerDay = 24;


    public required SoundTableExtension Extension { get; init; }

    public required SoundTableEntry[] Entries { get; init; }

    public string? AudioDirectory => Extension switch
    {
        SoundTableExtension.Bgm => "data/sound/2d/",
        SoundTableExtension.Bge => "data/sound/2d/",
        SoundTableExtension.Eff => "data/sound/3d/",
        _ => null
    };
}