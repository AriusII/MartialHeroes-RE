using MartialHeroes.Assets.Parsers.Core.Models;

namespace MartialHeroes.Assets.Parsers.Effects.Models;


public sealed class XeffSubEffect
{

    public required uint EmitterType { get; init; }

    public required uint ResourceId { get; init; }

    public required uint AnimFlag { get; init; }

    public required uint FieldUnknownA { get; init; }

    public required uint ElementDword2 { get; init; }


    public required uint EntryCount { get; init; }

    public required string[] TextureNames { get; init; }


    public required float[] AlphaKeys { get; init; }

    public required float[] DiffuseR { get; init; }

    public required float[] DiffuseG { get; init; }

    public required float[] DiffuseB { get; init; }


    public required byte AnimLoop { get; init; }

    public required uint AnimStride { get; init; }

    public required uint AnimBaseTime { get; init; }


    public required XeffKeyframe[] Keyframes { get; init; }
}

public sealed class XeffKeyframe
{
    public required uint KfIndex { get; init; }

    public required float VelocityX { get; init; }

    public required float VelocityY { get; init; }

    public required float VelocityZ { get; init; }

    public required float SizeX { get; init; }

    public required float SizeY { get; init; }

    public required float SizeZ { get; init; }

    public required float RotXDeg { get; init; }

    public required float RotYDeg { get; init; }

    public required float RotZDeg { get; init; }

    public Vec3 Velocity => new(VelocityX, VelocityY, VelocityZ);

    public Vec3 Size => new(SizeX, SizeY, SizeZ);

    public Quat Rotation => ComputeQuat(RotXDeg, RotYDeg, RotZDeg);

    internal static Quat ComputeQuat(float xDeg, float yDeg, float zDeg)
    {
        const float DegToRad = MathF.PI / 180f;
        var cx = MathF.Cos(xDeg * DegToRad * 0.5f);
        var sx = MathF.Sin(xDeg * DegToRad * 0.5f);
        var cy = MathF.Cos(yDeg * DegToRad * 0.5f);
        var sy = MathF.Sin(yDeg * DegToRad * 0.5f);
        var cz = MathF.Cos(zDeg * DegToRad * 0.5f);
        var sz = MathF.Sin(zDeg * DegToRad * 0.5f);
        return new Quat(
            sx * cy * cz + cx * sy * sz,
            cx * sy * cz - sx * cy * sz,
            cx * cy * sz + sx * sy * cz,
            cx * cy * cz - sx * sy * sz
        );
    }
}

public sealed class XeffData
{
    public required uint EffectId { get; init; }

    public required uint SubEffectCount { get; init; }

    public required XeffSubEffect[] SubEffects { get; init; }
}


public readonly record struct EffVertex(
    float PosX,
    float PosY,
    float PosZ,
    float NormalX,
    float NormalY,
    float NormalZ,
    float TexU,
    float TexV);

public sealed class EffObjectShape
{
    public required ushort[] Indices { get; init; }

    public required EffVertex[] Vertices { get; init; }
}


public sealed class ParticleSubRecord
{

    public required ushort LifeBonus { get; init; }

    public required ushort Lifetime { get; init; }

    public required ushort SpawnDelay { get; init; }

    public required ushort SizeInit { get; init; }


    public required byte ColorR { get; init; }

    public required byte ColorG { get; init; }

    public required byte ColorB { get; init; }

    public required byte ColorA { get; init; }


    public required float SpawnPosX { get; init; }

    public required float SpawnPosY { get; init; }

    public required float SpawnPosZ { get; init; }

    public required float SizeRate { get; init; }


    public required short ColorRRate { get; init; }

    public required short ColorGRate { get; init; }

    public required short ColorBRate { get; init; }

    public required short ColorARate { get; init; }


    public required float VelocityX { get; init; }

    public required float VelocityY { get; init; }

    public required float VelocityZ { get; init; }

    public required float VelocityDamp { get; init; }
}

public sealed class ParticleEmitterEntry
{
    public required uint EntryId { get; init; }

    public required uint NumFrames { get; init; }

    public required float SpriteSizeX { get; init; }

    public required float SpriteSizeY { get; init; }

    public required uint MaxParticles { get; init; }

    public required uint RawTexHandleSlot { get; init; }

    public required uint RawSubrecordArrayPtr { get; init; }

    public required ParticleSubRecord[] SubRecords { get; init; }

    public required string TextureName { get; init; }
}

public sealed class ParticleEmitterTable
{
    private readonly Dictionary<uint, ParticleEmitterEntry> _byId;

    public ParticleEmitterTable(ParticleEmitterEntry[] entries)
    {
        Entries = entries;
        _byId = new Dictionary<uint, ParticleEmitterEntry>(entries.Length);
        foreach (var e in entries)
            _byId[e.EntryId] = e;
    }

    public ParticleEmitterEntry[] Entries { get; }

    public ParticleEmitterEntry? TryGetById(uint entryId)
    {
        return _byId.TryGetValue(entryId, out var e) ? e : null;
    }
}