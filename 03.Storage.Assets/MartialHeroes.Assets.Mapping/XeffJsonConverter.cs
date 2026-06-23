using System.Text.Json;
using System.Text.Json.Serialization;
using MartialHeroes.Assets.Parsers.Effects.Models;

namespace MartialHeroes.Assets.Mapping;

public static class XeffJsonConverter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };


    public static void WriteJson(XeffData effect, Stream output)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(output);

        var root = MapToRoot(effect);
        JsonSerializer.Serialize(output, root, SerializerOptions);
    }

    public static byte[] WriteJsonBytes(XeffData effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        var root = MapToRoot(effect);
        return JsonSerializer.SerializeToUtf8Bytes(root, SerializerOptions);
    }


    private static XeffJsonRoot MapToRoot(XeffData effect)
    {
        return new XeffJsonRoot(
            effect.EffectId,
            effect.SubEffectCount,
            MapSubEffects(effect.SubEffects));
    }

    private static XeffJsonSubEffect[] MapSubEffects(XeffSubEffect[] subEffects)
    {
        var result = new XeffJsonSubEffect[subEffects.Length];
        for (var i = 0; i < subEffects.Length; i++)
            result[i] = MapSubEffect(subEffects[i]);
        return result;
    }

    private static XeffJsonSubEffect MapSubEffect(XeffSubEffect sub)
    {
        return new XeffJsonSubEffect(
            sub.EmitterType,
            sub.ResourceId,
            sub.AnimFlag,
            sub.EntryCount,
            sub.TextureNames,
            MapAlphaKeys(sub.AlphaKeys),
            sub.DiffuseR,
            sub.DiffuseG,
            sub.DiffuseB,
            sub.AnimLoop,
            sub.AnimStride,
            sub.AnimBaseTime,
            MapKeyframes(sub.Keyframes));
    }

    private static XeffJsonKeyframe[] MapKeyframes(XeffKeyframe[] kfs)
    {
        var result = new XeffJsonKeyframe[kfs.Length];
        for (var i = 0; i < kfs.Length; i++)
        {
            var kf = kfs[i];
            var vel = kf.Velocity;
            var sz = kf.Size;
            var rot = kf.Rotation;
            result[i] = new XeffJsonKeyframe(
                kf.KfIndex,
                [vel.X, vel.Y, vel.Z],
                [sz.X, sz.Y, sz.Z],
                [rot.X, rot.Y, rot.Z, rot.W],
                kf.RotXDeg,
                kf.RotYDeg,
                kf.RotZDeg);
        }

        return result;
    }

    private static XeffJsonAlpha[] MapAlphaKeys(float[] keys)
    {
        var result = new XeffJsonAlpha[keys.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            var fileValue = keys[i];
            result[i] = new XeffJsonAlpha(fileValue, 1f - fileValue);
        }

        return result;
    }


    private sealed record XeffJsonRoot(
        uint EffectId,
        uint SubEffectCount,
        XeffJsonSubEffect[] SubEffects);

    private sealed record XeffJsonSubEffect(
        uint EmitterType,
        uint ResourceId,
        uint AnimFlag,
        uint EntryCount,
        string[] TextureNames,
        XeffJsonAlpha[] AlphaKeys,
        float[] DiffuseR,
        float[] DiffuseG,
        float[] DiffuseB,
        byte AnimLoop,
        uint AnimStride,
        uint AnimBaseTime,
        XeffJsonKeyframe[] Keyframes);

    private sealed record XeffJsonAlpha(float FileValue, float Opacity);

    private sealed record XeffJsonKeyframe(
        uint KfIndex,
        float[] Velocity,
        float[] Size,
        float[] Rotation,
        float RotXDeg,
        float RotYDeg,
        float RotZDeg);
}