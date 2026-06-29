using System.Globalization;
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

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SoundTableEntry
{
    public readonly uint SoundEntryId;

    public readonly HourSchedule24 HourSchedule;

    public readonly float Weight;

    public readonly float PosX;

    public readonly uint Unlabeled24;

    public readonly float PosZ;

    public readonly float Radius;

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

    public const byte SoundCat2dMax = 5;

    public const byte SoundCatBgmBge = 0;

    public const byte SoundCatEff = 6;

    public const byte SoundType2d = 1;

    public const byte SoundType3d = 3;


    public required SoundTableExtension Extension { get; init; }

    public required SoundTableEntry[] Entries { get; init; }

    public ReadOnlyMemory<byte> PresentFlags { get; init; }

    public byte? Category => Extension switch
    {
        SoundTableExtension.Bgm => SoundCatBgmBge,
        SoundTableExtension.Bge => SoundCatBgmBge,
        SoundTableExtension.Eff => SoundCatEff,
        _ => null
    };

    public byte? TypeCode => Category is { } category
        ? category < SoundCat2dMax ? SoundType2d : SoundType3d
        : null;

    public string? AudioDirectory => Category is { } category
        ? category < SoundCat2dMax ? "data/sound/2d/" : "data/sound/3d/"
        : null;

    public string? BuildOggPath(uint soundEntryId)
    {
        var directory = AudioDirectory;
        if (directory is null)
            return null;

        return directory + soundEntryId.ToString(CultureInfo.InvariantCulture) + ".ogg";
    }
}