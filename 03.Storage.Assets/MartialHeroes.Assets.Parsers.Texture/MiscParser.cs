using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.Texture.Models;

namespace MartialHeroes.Assets.Parsers.Texture;

public static class MiscParser
{

    private const int MobInfoRecordStride = 28;


    private const int TolHeaderSize = 16;


    private const int DescriptorStride = 68;

    private const int DisplayNameOffset = 8;
    private const int DisplayNameLength = 30;

    private const int KeyboardShortcutOffset = 38;
    private const int KeyboardShortcutLength = 3;

    private const int ReservedOffset = 41;
    private const int ReservedLength = 27;

    public static MobInfoRecord[] ParseMobInfoMi(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length < 4)
            throw new InvalidDataException(
                $"mobinfo.mi parse error: buffer too short for 4-byte header (got {span.Length}). " +
                "spec: Docs/RE/formats/mi.md §Container layout.");

        var count = BinaryPrimitives.ReadUInt32LittleEndian(span[..]);
        var expectedSize = 4 + (long)count * MobInfoRecordStride;
        if (span.Length < expectedSize)
            throw new InvalidDataException(
                $"mobinfo.mi parse error: expected {expectedSize} bytes, got {span.Length}. " +
                "spec: Docs/RE/formats/mi.md §Container layout.");

        var results = new MobInfoRecord[(int)count];
        for (var i = 0; i < (int)count; i++)
        {
            var offset = 4 + i * MobInfoRecordStride;
            var rec = span.Slice(offset, MobInfoRecordStride);

            results[i] = new MobInfoRecord
            {
                EntryId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]),
                CaptionMsgId = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..]),
                DescriptionMsgId =
                    BinaryPrimitives.ReadUInt32LittleEndian(rec[8..]),
                SmallParam = BinaryPrimitives.ReadUInt32LittleEndian(rec[12..]),
                PackedCodeA =
                    BinaryPrimitives.ReadUInt32LittleEndian(rec[16..]),
                PackedCodeB =
                    BinaryPrimitives.ReadUInt32LittleEndian(rec[20..]),
                AuxField = BinaryPrimitives.ReadUInt32LittleEndian(rec[24..])
            };
        }

        return results;
    }

    public static TolMapData ParseTol(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length < TolHeaderSize)
            throw new InvalidDataException(
                $".tol parse error: buffer too short for 16-byte header (got {span.Length}). " +
                "spec: Docs/RE/formats/misc_data.md §3.");

        var worldOriginX = BinaryPrimitives.ReadUInt32LittleEndian(span[..]);
        var worldOriginY = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        var widthTiles = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
        var heightTiles = BinaryPrimitives.ReadUInt32LittleEndian(span[12..]);

        var tileBytes = (long)widthTiles * heightTiles;
        var expectedSize = TolHeaderSize + tileBytes;
        if (span.Length < expectedSize)
            throw new InvalidDataException(
                $".tol parse error: expected {expectedSize} bytes ({widthTiles}×{heightTiles} grid), " +
                $"got {span.Length}. spec: Docs/RE/formats/misc_data.md §3.");

        var tileGrid = data.Slice(TolHeaderSize, (int)tileBytes);

        return new TolMapData
        {
            WorldOriginX = worldOriginX,
            WorldOriginY = worldOriginY,
            WidthTiles = widthTiles,
            HeightTiles = heightTiles,
            TileGrid = tileGrid
        };
    }

    public static DescriptorRecord[] ParseDescriptSc(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length % DescriptorStride != 0)
            throw new InvalidDataException(
                $"discript.sc parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {DescriptorStride}. spec: Docs/RE/formats/misc_data.md §5.");

        var count = span.Length / DescriptorStride;
        var results = new DescriptorRecord[count];

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);

        for (var i = 0; i < count; i++)
        {
            var offset = i * DescriptorStride;
            var rec = span.Slice(offset, DescriptorStride);

            var descriptorId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            var category = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..]);

            var nameBytes = rec.Slice(DisplayNameOffset, DisplayNameLength);
            var nameEnd = nameBytes.IndexOf((byte)0);
            var displayName = nameEnd < 0
                ? cp949.GetString(nameBytes)
                : nameEnd == 0
                    ? string.Empty
                    : cp949.GetString(nameBytes[..nameEnd]);

            var shortcutBytes = rec.Slice(KeyboardShortcutOffset, KeyboardShortcutLength);
            var scEnd = shortcutBytes.IndexOf((byte)0);
            var keyboardShortcut = scEnd < 0
                ? Encoding.ASCII.GetString(shortcutBytes)
                : scEnd == 0
                    ? string.Empty
                    : Encoding.ASCII.GetString(shortcutBytes[..scEnd]);

            var reserved = data.Slice(offset + ReservedOffset, ReservedLength);

            results[i] = new DescriptorRecord
            {
                DescriptorId = descriptorId,
                Category = category,
                DisplayName = displayName,
                KeyboardShortcut = keyboardShortcut,
                Reserved = reserved
            };
        }

        return results;
    }
}