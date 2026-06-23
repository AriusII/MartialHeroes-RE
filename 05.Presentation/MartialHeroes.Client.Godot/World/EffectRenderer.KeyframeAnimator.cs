using Godot;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class EffectRenderer
{
    private void TickXeffEffect(LiveEffect live, SubEffectDesc[] subEffects, double deltaMs = 0)
    {
        if (!IsInstanceValid(live.Anchor)) return;

        var anchorPos = live.AmbientAnchorOwned
            ? live.Anchor.GlobalPosition
            : live.Anchor.GlobalPosition + new Vector3(0f, EmitterHeightOffset, 0f);
        var elapsedMs = live.ElapsedMs;

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
                else
                {
                    var gpu = live.GpuParticles?[i];
                    if (gpu is not null && IsInstanceValid(gpu))
                        gpu.GlobalPosition = anchorPos;
                }

                continue;
            }

            var existing = live.MeshInstances?[i];
            if (existing is null) continue;

            var texRow = live.Textures?[i];
            RebuildSubEffectMesh(existing, se, anchorPos, elapsedMs, texRow);
        }
    }


    private static void RebuildSubEffectMesh(
        MeshInstance3D mi,
        SubEffectDesc se,
        Vector3 origin,
        double elapsedMs,
        ImageTexture?[]? textures)
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
        var kfA = Math.Min(frameIdx, kfCount - 1);
        var kfB = Math.Min(frameIdx + 1, kfCount - 1);

        var kA = se.Keyframes[kfA];
        var kB = se.Keyframes[kfB];

        var vx = kA.VelocityX + (kB.VelocityX - kA.VelocityX) * frac;
        var vy = kA.VelocityY + (kB.VelocityY - kA.VelocityY) * frac;
        var vz = kA.VelocityZ + (kB.VelocityZ - kA.VelocityZ) * frac;

        var sx = kA.SizeX + (kB.SizeX - kA.SizeX) * frac;
        var sy = kA.SizeY + (kB.SizeY - kA.SizeY) * frac;
        var sz = kA.SizeZ + (kB.SizeZ - kA.SizeZ) * frac;

        var alpha = SampleCurveLinear(se.AlphaKeys, frameIdx, frac);

        var diffR = SampleCurveLinear(se.DiffuseR, frameIdx, frac);
        var diffG = SampleCurveLinear(se.DiffuseG, frameIdx, frac);
        var diffB = SampleCurveLinear(se.DiffuseB, frameIdx, frac);

        var displace = new Vector3(vx, vy, -vz);
        var particlePos = origin + displace;

        var spriteFrame = Math.Min(frameIdx, (int)texCount - 1);

        var uOff = se.ScrollU ? (float)(elapsedMs % UvScrollPeriodMs / UvScrollPeriodMs) : 0f;
        var vOff = se.ScrollV ? (float)(elapsedMs % UvScrollPeriodMs / UvScrollPeriodMs) : 0f;

        var tint = new Color(diffR, diffG, diffB, alpha);

        var mesh = se.EmitterType switch
        {
            EmitterBillboard => BuildBillboardQuad(sx, sy, tint, uOff, vOff, false),
            EmitterMesh => BuildBillboardQuad(sx, sy, tint, uOff, vOff, true),
            _ => BuildMeshParticle(kA, kB, frac, sx, sy, sz, tint, uOff, vOff)
        };

        if (mesh is null) return;

        mi.Mesh = mesh;
        mi.GlobalPosition = particlePos;

        if (textures is { Length: > 0 })
        {
            var texIdx = Math.Min(spriteFrame, textures.Length - 1);
            if (textures[texIdx] is { } tex)
            {
                var mat = BuildEffectMaterial(tex, tint);
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
                BlendMode = BaseMaterial3D.BlendModeEnum.Mix,
                BillboardMode = se.EmitterType < EmitterDirectional
                    ? BaseMaterial3D.BillboardModeEnum.Enabled
                    : BaseMaterial3D.BillboardModeEnum.Disabled
            };
            mi.SetSurfaceOverrideMaterial(0, mat);
        }
    }


    private static float SampleCurveLinear(float[] keys, int frameIdx, float frac)
    {
        if (keys.Length == 0) return 1f;
        var a = Math.Min(frameIdx, keys.Length - 1);
        var b = Math.Min(frameIdx + 1, keys.Length - 1);
        return keys[a] + (keys[b] - keys[a]) * frac;
    }
}