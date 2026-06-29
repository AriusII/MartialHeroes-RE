using System.Runtime.InteropServices;
using MartialHeroes.Assets.Parsers.Audio.Models;

namespace MartialHeroes.Assets.Parsers.Audio;

public static class SoundTableParser
{
    public static SoundTableData Parse(ReadOnlyMemory<byte> data, SoundTableExtension extension)
    {
        var entries = DecodeEntries(data.Span);

        return new SoundTableData
        {
            Extension = extension,
            Entries = entries,
            PresentFlags = data.Slice(SoundTableData.ReadSize, SoundTableData.TrailerSize)
        };
    }

    public static SoundTableData Parse(ReadOnlySpan<byte> span, SoundTableExtension extension)
    {
        var entries = DecodeEntries(span);

        return new SoundTableData
        {
            Extension = extension,
            Entries = entries
        };
    }


    private static SoundTableEntry[] DecodeEntries(ReadOnlySpan<byte> span)
    {
        if (span.Length != SoundTableData.FixedFileSize)
            throw new InvalidDataException(
                $"Sound table parse error: buffer is {span.Length} bytes; " +
                $"expected exactly {SoundTableData.FixedFileSize} (0x{SoundTableData.FixedFileSize:X4}) bytes " +
                $"(= {SoundTableData.EntryCount} records x {SoundTableData.EntryStride} bytes + " +
                $"{SoundTableData.TrailerSize} bytes unread trailer).");

        var records = MemoryMarshal.Cast<byte, SoundTableEntry>(span[..SoundTableData.ReadSize]);
        if (records.Length != SoundTableData.EntryCount)
            throw new InvalidDataException(
                $"Sound table parse error: record region cast yielded {records.Length} records; " +
                $"expected {SoundTableData.EntryCount}. " +
                $"sizeof(SoundTableEntry) must equal {SoundTableData.EntryStride}.");

        var entries = new SoundTableEntry[SoundTableData.EntryCount];
        records.CopyTo(entries);
        return entries;
    }


    public static SoundTableExtension ExtensionFromPath(string vfsPath)
    {
        if (string.IsNullOrEmpty(vfsPath))
            throw new ArgumentException("VFS path must not be null or empty.", nameof(vfsPath));

        var normalised = vfsPath.Replace('\\', '/');
        if (normalised.Contains("data/effect/obj/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"The path '{vfsPath}' points to a 3D geometry shape file (data/effect/obj/*.eff), " +
                "which is NOT a sound table. Do not parse it with SoundTableParser.",
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
                "Known extensions: .bgm, .bge, .eff (map path only), .wlk, .run.",
                nameof(vfsPath))
        };
    }
}