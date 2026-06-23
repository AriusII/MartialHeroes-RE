using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Audio.Models;

namespace MartialHeroes.Assets.Parsers.Audio;

public static class SoundTableParser
{

    private const int OffSoundEntryId = 0x00;

    private const int OffTodEnable = 0x04;

    private const int OffWeight = 0x1C;

    private const int OffPosX = 0x20;

    private const int OffUnlabeled24 = 0x24;

    private const int OffPosZ = 0x28;

    private const int OffRadius = 0x2C;


    public static SoundTableData Parse(ReadOnlyMemory<byte> data, SoundTableExtension extension)
    {
        return Parse(data.Span, extension);
    }

    public static SoundTableData Parse(ReadOnlySpan<byte> span, SoundTableExtension extension)
    {
        if (span.Length != SoundTableData.FixedFileSize)
            throw new InvalidDataException(
                $"Sound table parse error: buffer is {span.Length} bytes; " +
                $"expected exactly {SoundTableData.FixedFileSize} (0x{SoundTableData.FixedFileSize:X4}) bytes " +
                $"(= {SoundTableData.EntryCount} records × {SoundTableData.EntryStride} bytes + " +
                $"{SoundTableData.TrailerSize} bytes unread trailer). " +
                "spec: Docs/RE/formats/sound_tables.md §File layout.");

        var entries = new SoundTableEntry[SoundTableData.EntryCount];
        for (var i = 0; i < SoundTableData.EntryCount; i++)
        {
            var entryBase = i * SoundTableData.EntryStride;
            entries[i] = DecodeEntry(span, entryBase);
        }

        return new SoundTableData
        {
            Extension = extension,
            Entries = entries
        };
    }


    private static SoundTableEntry DecodeEntry(ReadOnlySpan<byte> span, int entryBase)
    {
        var soundEntryId = BinaryPrimitives.ReadUInt32LittleEndian(
            span[(entryBase + OffSoundEntryId)..]);

        var todEnable = new HourSchedule24();
        span.Slice(entryBase + OffTodEnable, SoundTableData.HoursPerDay)
            .CopyTo(todEnable.AsSpan());

        var weight = BinaryPrimitives.ReadSingleLittleEndian(
            span[(entryBase + OffWeight)..]);

        var posX = BinaryPrimitives.ReadSingleLittleEndian(
            span[(entryBase + OffPosX)..]);

        var unlabeled24 = BinaryPrimitives.ReadUInt32LittleEndian(
            span[(entryBase + OffUnlabeled24)..]);

        var posZ = BinaryPrimitives.ReadSingleLittleEndian(
            span[(entryBase + OffPosZ)..]);

        var radius = BinaryPrimitives.ReadSingleLittleEndian(
            span[(entryBase + OffRadius)..]);


        return new SoundTableEntry
        {
            SoundEntryId = soundEntryId,
            HourSchedule = todEnable,
            Weight = weight,
            PosX = posX,
            Unlabeled24 = unlabeled24,
            PosZ = posZ,
            Radius = radius
        };
    }


    public static SoundTableExtension ExtensionFromPath(string vfsPath)
    {
        if (string.IsNullOrEmpty(vfsPath))
            throw new ArgumentException("VFS path must not be null or empty.", nameof(vfsPath));

        var normalised = vfsPath.Replace('\\', '/');
        if (normalised.Contains("data/effect/obj/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"The path '{vfsPath}' points to a 3D geometry shape file (data/effect/obj/*.eff), " +
                "which is NOT a sound table. Do not parse it with SoundTableParser. " +
                "spec: Docs/RE/formats/sound_tables.md §CRITICAL DISAMBIGUATION.",
                nameof(vfsPath));

        var ext = Path.GetExtension(vfsPath).ToLowerInvariant();
        return ext switch
        {
            ".bgm" => SoundTableExtension.Bgm,
            ".bge" => SoundTableExtension.Bge,
            ".eff" => SoundTableExtension.Eff,
            ".wlk" => SoundTableExtension.Wlk,
            ".run" => SoundTableExtension.Run,
            _ => throw new ArgumentException(
                $"The extension '{ext}' (from path '{vfsPath}') is not a known sound-table extension. " +
                "Known extensions: .bgm, .bge, .eff (map path only), .wlk, .run. " +
                "spec: Docs/RE/formats/sound_tables.md §Identification.",
                nameof(vfsPath))
        };
    }
}