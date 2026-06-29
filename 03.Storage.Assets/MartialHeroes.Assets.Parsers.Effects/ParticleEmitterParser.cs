using System.Buffers.Binary;
using System.Runtime.InteropServices;
using MartialHeroes.Assets.Parsers.Effects.Models;

namespace MartialHeroes.Assets.Parsers.Effects;

public static class ParticleEmitterParser
{
    private const int EntryHeaderSize = 28;

    private const int SubRecordStride = 52;

    private const int TextureNameSize = 64;

    private const int HdrNumFramesOffset = 4;
    private const int HdrSpriteSizeXOffset = 8;
    private const int HdrSpriteSizeYOffset = 12;
    private const int HdrBlendAdditiveFlagOffset = 16;
    private const int HdrTexHandleSlotOffset = 20;
    private const int HdrSubrecArrayPtrOffset = 24;

    public static ParticleEmitterTable Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static ParticleEmitterTable Parse(ReadOnlySpan<byte> span)
    {
        var entries = new List<ParticleEmitterEntry>();
        var offset = 0;

        while (offset + EntryHeaderSize <= span.Length)
        {
            var hdr = span.Slice(offset, EntryHeaderSize);

            var entryId = BinaryPrimitives.ReadUInt32LittleEndian(hdr);
            var numFrames = BinaryPrimitives.ReadUInt32LittleEndian(hdr[HdrNumFramesOffset..]);

            if (numFrames == 0)
                break;

            var spriteSizeX = BinaryPrimitives.ReadSingleLittleEndian(hdr[HdrSpriteSizeXOffset..]);
            var spriteSizeY = BinaryPrimitives.ReadSingleLittleEndian(hdr[HdrSpriteSizeYOffset..]);
            var blendAdditiveFlag = BinaryPrimitives.ReadUInt32LittleEndian(hdr[HdrBlendAdditiveFlagOffset..]);
            var rawTexHandleSlot = BinaryPrimitives.ReadUInt32LittleEndian(hdr[HdrTexHandleSlotOffset..]);
            var rawSubrecArrayPtr = BinaryPrimitives.ReadUInt32LittleEndian(hdr[HdrSubrecArrayPtrOffset..]);

            offset += EntryHeaderSize;

            var subRecordBlockSize = (long)numFrames * SubRecordStride;
            var totalEntryRemaining = subRecordBlockSize + TextureNameSize;
            if (offset + totalEntryRemaining > span.Length)
                throw new InvalidDataException(
                    $"particleEmitter.eff parse error: entry id={entryId} declares num_frames={numFrames}, " +
                    $"requiring {totalEntryRemaining} more bytes (sub-records + texture name) but only " +
                    $"{span.Length - offset} bytes remain.");

            var subRecordBlock = span.Slice(offset, (int)subRecordBlockSize);
            var rawSubRecords = MemoryMarshal.Cast<byte, ParticleSubRecord>(subRecordBlock);
            var subRecords = new ParticleSubRecord[numFrames];
            rawSubRecords.CopyTo(subRecords);

            offset += (int)subRecordBlockSize;

            var nameSpan = span.Slice(offset, TextureNameSize);
            var textureName = ReadNulTerminatedCp949(nameSpan);
            offset += TextureNameSize;

            entries.Add(new ParticleEmitterEntry
            {
                EntryId = entryId,
                NumFrames = numFrames,
                SpriteSizeX = spriteSizeX,
                SpriteSizeY = spriteSizeY,
                BlendAdditiveFlag = blendAdditiveFlag,
                RawTexHandleSlot = rawTexHandleSlot,
                RawSubrecordArrayPtr = rawSubrecArrayPtr,
                SubRecords = subRecords,
                TextureName = textureName
            });
        }

        return new ParticleEmitterTable(entries.ToArray());
    }

    private static string ReadNulTerminatedCp949(ReadOnlySpan<byte> nameSpan)
    {
        var len = nameSpan.IndexOf((byte)0);
        if (len < 0)
            len = nameSpan.Length;
        if (len == 0)
            return string.Empty;
        return Cp949Encoding.Instance.GetString(nameSpan[..len]);
    }
}