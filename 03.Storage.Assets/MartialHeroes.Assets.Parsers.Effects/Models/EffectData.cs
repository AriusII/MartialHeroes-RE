using System.Runtime.InteropServices;
using MartialHeroes.Assets.Parsers.Core.Models;

namespace MartialHeroes.Assets.Parsers.Effects.Models;

public enum XeffBlendMode
{
    Additive = 0,

    Alpha = 1,

    Opaque = 3
}

public sealed class XeffSubEffect
{
    public required uint EmitterType { get; init; }

    public required uint ResourceId { get; init; }

    public required uint AnimFlag { get; init; }

    public required uint BlendMode { get; init; }

    public required uint ElementDword2 { get; init; }


    public required uint EntryCount { get; init; }

    public required string[] TextureNames { get; init; }


    public required float[] Opacity { get; init; }

    public required float[] DiffuseR { get; init; }

    public required float[] DiffuseG { get; init; }

    public required float[] DiffuseB { get; init; }


    public required byte AnimLoop { get; init; }

    public required uint AnimStride { get; init; }

    public required uint AnimBaseTime { get; init; }


    public required XeffKeyframe[] Keyframes { get; init; }

    public XeffBlendMode BlendModeKind => BlendMode switch
    {
        1 => XeffBlendMode.Alpha,
        3 => XeffBlendMode.Opaque,
        _ => XeffBlendMode.Additive
    };
}

public readonly struct XeffKeyframe
{
    public XeffKeyframe(
        uint kfIndex,
        float velocityX,
        float velocityY,
        float velocityZ,
        float sizeX,
        float sizeY,
        float sizeZ,
        float rotXDeg,
        float rotYDeg,
        float rotZDeg)
    {
        KfIndex = kfIndex;
        VelocityX = velocityX;
        VelocityY = velocityY;
        VelocityZ = velocityZ;
        SizeX = sizeX;
        SizeY = sizeY;
        SizeZ = sizeZ;
        RotXDeg = rotXDeg;
        RotYDeg = rotYDeg;
        RotZDeg = rotZDeg;
        Rotation = ComputeQuat(rotXDeg, rotYDeg, rotZDeg);
    }

    public uint KfIndex { get; }

    public float VelocityX { get; }

    public float VelocityY { get; }

    public float VelocityZ { get; }

    public float SizeX { get; }

    public float SizeY { get; }

    public float SizeZ { get; }

    public float RotXDeg { get; }

    public float RotYDeg { get; }

    public float RotZDeg { get; }

    public Quat Rotation { get; }

    public Vec3 Velocity => new(VelocityX, VelocityY, VelocityZ);

    public Vec3 Size => new(SizeX, SizeY, SizeZ);

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

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ParticleSubRecord
{
    public readonly ushort LifeBonus;

    public readonly ushort SpawnDelay;

    public readonly ushort Lifetime;

    public readonly ushort SizeInit;

    public readonly byte ColorR;

    public readonly byte ColorG;

    public readonly byte ColorB;

    public readonly byte ColorA;

    public readonly float SpawnPosX;

    public readonly float SpawnPosY;

    public readonly float SpawnPosZ;

    public readonly float SizeRate;

    public readonly short ColorBRate;

    public readonly short ColorGRate;

    public readonly short ColorRRate;

    public readonly short ColorARate;

    public readonly float VelocityX;

    public readonly float VelocityY;

    public readonly float VelocityZ;

    public readonly float VelocityDamp;
}

public sealed class ParticleEmitterEntry
{
    public required uint EntryId { get; init; }

    public required uint NumFrames { get; init; }

    public required float SpriteSizeX { get; init; }

    public required float SpriteSizeY { get; init; }

    public required uint BlendAdditiveFlag { get; init; }

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