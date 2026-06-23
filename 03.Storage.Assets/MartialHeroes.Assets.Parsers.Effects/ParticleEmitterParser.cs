using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.Effects.Models;

namespace MartialHeroes.Assets.Parsers.Effects;

public static class ParticleEmitterParser
{
    private const int EntryHeaderSize = 28;

    private const int SubRecordStride = 52;

    private const int TextureNameSize = 64;

    private const int SubRecLifeBonusOffset = 0;
    private const int SubRecLifetimeOffset = 2;
    private const int SubRecSpawnDelayOffset = 4;
    private const int SubRecSizeInitOffset = 6;
    private const int SubRecColorROffset = 8;
    private const int SubRecColorGOffset = 9;
    private const int SubRecColorBOffset = 10;
    private const int SubRecColorAOffset = 11;
    private const int SubRecSpawnPosXOffset = 12;
    private const int SubRecSpawnPosYOffset = 16;
    private const int SubRecSpawnPosZOffset = 20;
    private const int SubRecSizeRateOffset = 24;
    private const int SubRecColorRRateOffset = 28;
    private const int SubRecColorGRateOffset = 30;
    private const int SubRecColorBRateOffset = 32;
    private const int SubRecColorARateOffset = 34;
    private const int SubRecVelocityXOffset = 36;
    private const int SubRecVelocityYOffset = 40;
    private const int SubRecVelocityZOffset = 44;
    private const int SubRecVelocityDampOffset = 48;

    private const int HdrEntryIdOffset = 0;
    private const int HdrNumFramesOffset = 4;
    private const int HdrSpriteSizeXOffset = 8;
    private const int HdrSpriteSizeYOffset = 12;
    private const int HdrMaxParticlesOffset = 16;
    private const int HdrTexHandleSlotOffset = 20;
    private const int HdrSubrecArrayPtrOffset = 24;

    public static ParticleEmitterTable Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span, data);
    }

    public static ParticleEmitterTable Parse(ReadOnlySpan<byte> data)
    {
        return Parse(data, ReadOnlyMemory<byte>.Empty);
    }

    private static ParticleEmitterTable Parse(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);

        var entries = new List<ParticleEmitterEntry>();
        var offset = 0;

        while (offset + EntryHeaderSize <= span.Length)
        {
            var hdr = span.Slice(offset, EntryHeaderSize);

            var entryId = BinaryPrimitives.ReadUInt32LittleEndian(hdr[..]);

            var numFrames = BinaryPrimitives.ReadUInt32LittleEndian(hdr[HdrNumFramesOffset..]);

            if (numFrames == 0)
                break;

            var spriteSizeX = BinaryPrimitives.ReadSingleLittleEndian(hdr[HdrSpriteSizeXOffset..]);
            var spriteSizeY = BinaryPrimitives.ReadSingleLittleEndian(hdr[HdrSpriteSizeYOffset..]);
            var maxParticles = BinaryPrimitives.ReadUInt32LittleEndian(hdr[HdrMaxParticlesOffset..]);
            var rawTexHandleSlot = BinaryPrimitives.ReadUInt32LittleEndian(hdr[HdrTexHandleSlotOffset..]);
            var rawSubrecArrayPtr = BinaryPrimitives.ReadUInt32LittleEndian(hdr[HdrSubrecArrayPtrOffset..]);

            offset += EntryHeaderSize;

            var subRecordBlockSize = (long)numFrames * SubRecordStride;
            var totalEntryRemaining = subRecordBlockSize + TextureNameSize;
            if (offset + totalEntryRemaining > span.Length)
                throw new InvalidDataException(
                    $"particleEmitter.eff parse error: entry id={entryId} at offset {offset - EntryHeaderSize} " +
                    $"declares num_frames={numFrames}, requiring {totalEntryRemaining} more bytes " +
                    $"(sub-records + texture name), but only {span.Length - offset} bytes remain. " +
                    "spec: Docs/RE/formats/effects.md §E.2.");

            var subRecords = new ParticleSubRecord[numFrames];
            for (uint f = 0; f < numFrames; f++)
            {
                var srOffset = offset + (int)f * SubRecordStride;
                var sr = span.Slice(srOffset, SubRecordStride);


                var lifeBonus = BinaryPrimitives.ReadUInt16LittleEndian(sr[..]);
                var lifetime = BinaryPrimitives.ReadUInt16LittleEndian(sr[SubRecLifetimeOffset..]);
                var spawnDelay = BinaryPrimitives.ReadUInt16LittleEndian(sr[SubRecSpawnDelayOffset..]);
                var sizeInit = BinaryPrimitives.ReadUInt16LittleEndian(sr[SubRecSizeInitOffset..]);

                var colorR = sr[SubRecColorROffset];
                var colorG = sr[SubRecColorGOffset];
                var colorB = sr[SubRecColorBOffset];
                var colorA = sr[SubRecColorAOffset];

                var spawnPosX = BinaryPrimitives.ReadSingleLittleEndian(sr[SubRecSpawnPosXOffset..]);
                var spawnPosY = BinaryPrimitives.ReadSingleLittleEndian(sr[SubRecSpawnPosYOffset..]);
                var spawnPosZ = BinaryPrimitives.ReadSingleLittleEndian(sr[SubRecSpawnPosZOffset..]);
                var sizeRate = BinaryPrimitives.ReadSingleLittleEndian(sr[SubRecSizeRateOffset..]);

                var colorRRate = BinaryPrimitives.ReadInt16LittleEndian(sr[SubRecColorRRateOffset..]);
                var colorGRate = BinaryPrimitives.ReadInt16LittleEndian(sr[SubRecColorGRateOffset..]);
                var colorBRate = BinaryPrimitives.ReadInt16LittleEndian(sr[SubRecColorBRateOffset..]);
                var colorARate = BinaryPrimitives.ReadInt16LittleEndian(sr[SubRecColorARateOffset..]);

                var velocityX = BinaryPrimitives.ReadSingleLittleEndian(sr[SubRecVelocityXOffset..]);
                var velocityY = BinaryPrimitives.ReadSingleLittleEndian(sr[SubRecVelocityYOffset..]);
                var velocityZ = BinaryPrimitives.ReadSingleLittleEndian(sr[SubRecVelocityZOffset..]);
                var velocityDamp = BinaryPrimitives.ReadSingleLittleEndian(sr[SubRecVelocityDampOffset..]);

                subRecords[f] = new ParticleSubRecord
                {
                    LifeBonus = lifeBonus,
                    Lifetime = lifetime,
                    SpawnDelay = spawnDelay,
                    SizeInit = sizeInit,
                    ColorR = colorR,
                    ColorG = colorG,
                    ColorB = colorB,
                    ColorA = colorA,
                    SpawnPosX = spawnPosX,
                    SpawnPosY = spawnPosY,
                    SpawnPosZ = spawnPosZ,
                    SizeRate = sizeRate,
                    ColorRRate = colorRRate,
                    ColorGRate = colorGRate,
                    ColorBRate = colorBRate,
                    ColorARate = colorARate,
                    VelocityX = velocityX,
                    VelocityY = velocityY,
                    VelocityZ = velocityZ,
                    VelocityDamp = velocityDamp
                };
            }

            offset += (int)subRecordBlockSize;

            var nameSpan = span.Slice(offset, TextureNameSize);
            var textureName = ReadNulTerminatedCp949(nameSpan, cp949);
            offset += TextureNameSize;

            entries.Add(new ParticleEmitterEntry
            {
                EntryId = entryId,
                NumFrames = numFrames,
                SpriteSizeX = spriteSizeX,
                SpriteSizeY = spriteSizeY,
                MaxParticles = maxParticles,
                RawTexHandleSlot = rawTexHandleSlot,
                RawSubrecordArrayPtr = rawSubrecArrayPtr,
                SubRecords = subRecords,
                TextureName = textureName
            });
        }

        return new ParticleEmitterTable(entries.ToArray());
    }


    private static string ReadNulTerminatedCp949(ReadOnlySpan<byte> nameSpan, Encoding cp949)
    {
        var len = nameSpan.IndexOf((byte)0);
        if (len < 0)
            len = nameSpan.Length;
        if (len == 0)
            return string.Empty;
        return cp949.GetString(nameSpan[..len]);
    }
}