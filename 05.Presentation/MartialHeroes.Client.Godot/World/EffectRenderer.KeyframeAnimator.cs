using Godot;
using MartialHeroes.Assets.Parsers.Core.Models;
using MartialHeroes.Assets.Parsers.Effects.Models;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class EffectRenderer
{
    private void TickXeffEffect(LiveEffect live, SubEffectDesc[] subEffects, double deltaMs = 0)
    {
        if (!IsInstanceValid(live.Anchor)) return;

        var anchorPos = live.AmbientAnchorOwned
            ? live.Anchor.GlobalPosition
            : live.Anchor.GlobalPosition + new Vector3(0f, live.YOffset, 0f);
        var elapsedMs = live.ElapsedMs;

        if (HasLocalPlayer)
        {
            var cdx = LocalPlayerGodotPos.X - anchorPos.X;
            var cdz = LocalPlayerGodotPos.Z - anchorPos.Z;
            var cr = CullRadius;
            if (cdx * cdx + cdz * cdz > cr * cr)
                return;
        }

        for (var i = 0; i < subEffects.Length; i++)
        {
            var se = subEffects[i];

            if (se.ResourceId >= XeffResourceParticleThreshold)
            {
                var sim = live.SimNodes?[i];
                if (sim is not null && IsInstanceValid(sim))
                {
                    sim.GlobalPosition = anchorPos;
                    sim.Tick(deltaMs / 1000.0);
                }

                continue;
            }

            var existing = live.MeshInstances?[i];
            if (existing is null) continue;

            var texRow = live.Textures?[i];
            RebuildSubEffectMesh(existing, se, anchorPos, elapsedMs, texRow, live.EffectiveScale);
        }

        if (!live.AmbientAnchorOwned)
        {
            var anyLoop = false;
            uint maxTotal = 0;
            for (var i = 0; i < subEffects.Length; i++)
            {
                if (subEffects[i].AnimLoop != 0) anyLoop = true;
                if (subEffects[i].TotalTime > maxTotal) maxTotal = subEffects[i].TotalTime;
            }

            if (!anyLoop && maxTotal > 0 && elapsedMs > maxTotal)
                live.Active = false;
        }
    }


    private void RebuildSubEffectMesh(
        MeshInstance3D mi,
        SubEffectDesc se,
        Vector3 origin,
        double elapsedMs,
        ImageTexture?[]? textures,
        float scale)
    {
        if (se.Keyframes.Length == 0) return;

        var texCount = se.TexCount;
        if (texCount == 0) return;

        var stride = se.AnimStride > 0 ? se.AnimStride : 1u;

        var phase = se.TotalTime > 0
            ? elapsedMs % se.TotalTime
            : elapsedMs % (stride * texCount);

        var frameIdx = (int)(phase / stride);
        var frac = (float)(phase % stride / stride);

        var kfCount = se.Keyframes.Length;

        int activeKf;
        int nextKf;
        if (se.AnimLoop != 0)
        {
            activeKf = frameIdx % (int)texCount;
            nextKf = (activeKf + 1) % (int)texCount;
        }
        else
        {
            activeKf = Math.Min(frameIdx, (int)texCount - 1);
            nextKf = Math.Min(frameIdx + 1, (int)texCount - 1);
        }

        var kA = se.Keyframes[Math.Min(activeKf, kfCount - 1)];
        var kB = se.Keyframes[Math.Min(nextKf, kfCount - 1)];

        var vx = kA.VelocityX + (kB.VelocityX - kA.VelocityX) * frac;
        var vy = kA.VelocityY + (kB.VelocityY - kA.VelocityY) * frac;
        var vz = kA.VelocityZ + (kB.VelocityZ - kA.VelocityZ) * frac;

        var sx = (kA.SizeX + (kB.SizeX - kA.SizeX) * frac) * scale;
        var sy = (kA.SizeY + (kB.SizeY - kA.SizeY) * frac) * scale;
        var sz = (kA.SizeZ + (kB.SizeZ - kA.SizeZ) * frac) * scale;

        var alpha = SampleCurveAt(se.Opacity, activeKf, nextKf, frac);
        var diffR = SampleCurveAt(se.DiffuseR, activeKf, nextKf, frac);
        var diffG = SampleCurveAt(se.DiffuseG, activeKf, nextKf, frac);
        var diffB = SampleCurveAt(se.DiffuseB, activeKf, nextKf, frac);

        var kfQ = SlerpKeyframeQuat(kA.Rotation, kB.Rotation, frac);

        var vGodot = new Vector3(vx, vy, -vz);
        var displace = kfQ * vGodot * scale;
        var particlePos = origin + displace;

        var uOff = se.ScrollU ? (float)(elapsedMs % UvScrollPeriodMs / UvScrollPeriodMs) : 0f;
        var vOff = se.ScrollV ? (float)(elapsedMs % UvScrollPeriodMs / UvScrollPeriodMs) : 0f;

        var tint = new Color(diffR, diffG, diffB, alpha);

        var preRot90Y = new Quaternion(Vector3.Up, (float)(Math.PI * 0.5));

        ArrayMesh? mesh;
        var meshOrient = Quaternion.Identity;
        bool billboardMat;

        switch (se.EmitterType)
        {
            case EmitterBillboard:
                mesh = BuildBillboardQuad(sx, sy, tint, uOff, vOff, false);
                billboardMat = true;
                break;

            case EmitterMesh:
                mesh = BuildMeshParticle(se, sx, sy, sz, tint);
                if (mesh is not null)
                {
                    meshOrient = (preRot90Y * kfQ).Normalized();
                    billboardMat = false;
                }
                else
                {
                    mesh = BuildBillboardQuad(sx, sy, tint, uOff, vOff, true);
                    billboardMat = true;
                }

                break;

            case EmitterDirectional:
                mesh = BuildMeshParticle(se, sx, sy, sz, tint);
                if (mesh is not null)
                {
                    meshOrient = kfQ;
                    billboardMat = false;
                }
                else
                {
                    mesh = BuildBillboardQuad(sx, sy, tint, uOff, vOff, false);
                    billboardMat = true;
                }

                break;

            default:
                mesh = BuildBillboardQuad(sx, sy, tint, uOff, vOff, false);
                billboardMat = true;
                break;
        }

        if (mesh is null) return;

        var blend = se.BlendModeKind == XeffBlendMode.Alpha
            ? BaseMaterial3D.BlendModeEnum.Mix
            : BaseMaterial3D.BlendModeEnum.Add;

        mi.Mesh = mesh;
        mi.GlobalPosition = particlePos;
        mi.Quaternion = billboardMat ? Quaternion.Identity : meshOrient;

        if (textures is { Length: > 0 })
        {
            var texIdx = Math.Min(activeKf, textures.Length - 1);
            if (textures[texIdx] is { } tex)
            {
                var mat = BuildEffectMaterial(tex, tint, blend, billboardMat);
                mi.SetSurfaceOverrideMaterial(0, mat);
            }
        }
        else
        {
            var mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = tint,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = blend,
                BillboardMode = billboardMat
                    ? BaseMaterial3D.BillboardModeEnum.Enabled
                    : BaseMaterial3D.BillboardModeEnum.Disabled
            };
            mi.SetSurfaceOverrideMaterial(0, mat);
        }
    }


    private static Quaternion SlerpKeyframeQuat(Quat legacyA, Quat legacyB, float frac)
    {
        var qa = new Quaternion(-legacyA.X, -legacyA.Y, legacyA.Z, legacyA.W);
        var qb = new Quaternion(-legacyB.X, -legacyB.Y, legacyB.Z, legacyB.W);

        qa = qa.LengthSquared() > 0.0001f ? qa.Normalized() : Quaternion.Identity;
        qb = qb.LengthSquared() > 0.0001f ? qb.Normalized() : Quaternion.Identity;

        return qa.Slerp(qb, frac).Normalized();
    }


    private static float SampleCurveAt(float[] keys, int a, int b, float frac)
    {
        if (keys.Length == 0) return 1f;
        a = Math.Min(a, keys.Length - 1);
        b = Math.Min(b, keys.Length - 1);
        return keys[a] + (keys[b] - keys[a]) * frac;
    }
}
