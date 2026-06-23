using Godot;
using MartialHeroes.Assets.Parsers.Effects.Models;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Client.Godot.World;

// EffectRenderer — partial: billboard / mesh-particle geometry builders + texture loading + teardown.
// spec: Docs/RE/specs/effects.md §17.2 — emitter type rendering; CONFIRMED.
public sealed partial class EffectRenderer
{
    // ─────────────────────────────────────────────────────────────────────────
    // Geometry builders — billboard / mesh emitters
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Builds an initial <see cref="MeshInstance3D" /> for a sub-effect.
    ///     Returns null for GPU-particle sub-effects or empty geometry.
    /// </summary>
    private MeshInstance3D? BuildSubEffectMesh(
        SubEffectDesc se,
        Vector3 origin,
        ImageTexture?[]? textures,
        double elapsedMs)
    {
        var mi = new MeshInstance3D { GlobalPosition = origin };
        RebuildSubEffectMesh(mi, se, origin, elapsedMs, textures);
        return mi;
    }

    /// <summary>
    ///     Builds a camera-facing billboard quad from the sampled size channels.
    ///     spec: Docs/RE/specs/effects.md §17.2 — billboard: four corners at ±0.5·size_x / ±0.5·size_y; CONFIRMED.
    ///     spec: Docs/RE/formats/effects.md §A.8 — size Vec3 drives billboard half-extents; HIGH.
    /// </summary>
    private static ArrayMesh BuildBillboardQuad(
        float sizeX, float sizeY,
        Color tint,
        float uOff, float vOff,
        bool preRotate90Y)
    {
        // spec: Docs/RE/specs/effects.md §8.2 step 9 emitter_type 0 —
        //   half_width = 0.5 × size_x; half_height = 0.5 × size_y; four corners; CONFIRMED.
        var hw = 0.5f * Math.Abs(sizeX);
        var hh = 0.5f * Math.Abs(sizeY);
        // Minimum visible size (aesthetic, not spec-dictated).
        hw = MathF.Max(hw, 0.05f);
        hh = MathF.Max(hh, 0.05f);

        // Build quad in local XY plane; BillboardMode in the material makes it camera-facing.
        // FIX 14b — the fixed +90° Y pre-rotation belongs to emitter_type 1 (MESH), not type 2.
        //   In practice we swap X↔Z to approximate the rotation without camera-basis math.
        // IDA: sub_4A5E0D v29==1 @0x4a62ae — Quat_SetYawRotationFromAngle((float*)a2, 1.5707964)
        //   (1.5707964 rad = +90° Y) is called on the mesh-particle (type 1) branch; the type-2
        //   branch @0x4a64be runs the oriented mesh loop with NO yaw pre-rotation.
        // spec: Docs/RE/specs/effects.md §17.2 emitter_type 1 (mesh-particle) —
        //   "extra fixed +90° Y rotation applied before the per-vertex transform"; CONFIRMED.
        var (aX, aY, bX, bY, cX, cY, dX, dY) = preRotate90Y
            ? (-hh, hw, hh, hw, hh, -hw, -hh, -hw) // rotated 90°
            : (-hw, hh, hw, hh, hw, -hh, -hw, -hh); // standard

        var arrays = new Array();
        arrays.Resize((int)Mesh.ArrayType.Max);

        arrays[(int)Mesh.ArrayType.Vertex] = new Vector3[]
        {
            new(aX, aY, 0f),
            new(bX, bY, 0f),
            new(cX, cY, 0f),
            new(dX, dY, 0f)
        };
        arrays[(int)Mesh.ArrayType.TexUV] = new Vector2[]
        {
            new(0f + uOff, 0f + vOff),
            new(1f + uOff, 0f + vOff),
            new(1f + uOff, 1f + vOff),
            new(0f + uOff, 1f + vOff)
        };
        // Per-vertex diffuse tint (R,G,B from the sampled colour curve; A from the alpha curve).
        // spec: Docs/RE/specs/effects.md §17.3 — vertex diffuse = sampled colour curve × alpha; CONFIRMED.
        arrays[(int)Mesh.ArrayType.Color] = new[]
        {
            tint,
            tint,
            tint,
            tint
        };
        // Two triangles (CCW for Godot right-handed).
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 0, 2, 3 };

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>
    ///     Builds a mesh-particle quad from the sampled velocity/size/alpha.
    ///     For the MVP, produces a simple oriented quad scaled by (sizeX, sizeY).
    ///     spec: Docs/RE/specs/effects.md §17.2 emitter_type else (mesh) — scale by size Vec3; CONFIRMED.
    /// </summary>
    private static ArrayMesh BuildMeshParticle(
        XeffKeyframe kA, XeffKeyframe kB, float frac,
        float sx, float sy, float sz,
        Color tint, float uOff, float vOff)
    {
        // Reuse billboard shape scaled by sx/sy; sz drives depth for future 3D mesh.
        // A real implementation would sample the .xobj mesh vertices here.
        // XOBJ-HOOK: load data/effect/xobj/<resource_id>.eff via XeffMiniParser or AssetPassthrough
        //   and transform each vertex by (sx, sy, sz) scale and the sampled orientation quaternion.
        //   spec: Docs/RE/formats/effects.md §A.11 — .xobj ASCII mesh format; CONFIRMED.
        //   spec: Docs/RE/specs/effects.md §17.2 — mesh: per-vertex scale by size Vec3; CONFIRMED.
        return BuildBillboardQuad(sx, sy, tint, uOff, vOff, false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Material helper
    // ─────────────────────────────────────────────────────────────────────────

    private static StandardMaterial3D BuildEffectMaterial(ImageTexture texture, Color tint)
    {
        // AlbedoColor carries the sampled per-keyframe diffuse tint (R,G,B) and alpha; the texture is
        // modulated by it. Previously hardcoded white, which dropped the .xeff diffuse colour curve.
        // spec: Docs/RE/specs/effects.md §17.3 — diffuse tint drives AlbedoColor (not white); CONFIRMED.
        // Blend mode: default alpha (SRCALPHA/INVSRCALPHA). Per-drawable blend byte is a SPEC GAP
        // (effects.md §14.8); additive is reserved for the additive override once recovered.
        // spec: Docs/RE/specs/rendering.md §3.3/§4.2 — default FX blend = SRCALPHA/INVSRCALPHA.
        // spec-gap: per-sub-effect blend-state field location in .xeff (effects.md §14.8).
        return new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoTexture = texture,
            AlbedoColor = tint,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Mix, // spec: rendering.md §4.2 — default alpha
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Texture loading
    // ─────────────────────────────────────────────────────────────────────────

    private ImageTexture?[]? LoadSubEffectTextures(SubEffectDesc se)
    {
        if (_assets is null || se.TextureNames.Length == 0)
            return null;

        var result = new ImageTexture?[se.TextureNames.Length];
        for (var t = 0; t < se.TextureNames.Length; t++)
        {
            var name = se.TextureNames[t];
            if (string.IsNullOrEmpty(name)) continue;

            // Texture resolution: data/effect/texture/<name>.tga
            // spec: Docs/RE/formats/effects.md §A.4.1 — full path: data/effect/texture/<name>.tga; CONFIRMED.
            var vfsPath = $"data/effect/texture/{name}.tga";
            result[t] = _assets.LoadTexture(vfsPath);

            if (result[t] is null)
                // Some textures may use .dds extension instead; try that as well.
                result[t] = _assets.LoadTexture($"data/effect/texture/{name}.dds");
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Teardown helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void TeardownLiveEffect(LiveEffect live)
    {
        // Tear down real .xeff mesh instances.
        if (live.MeshInstances is not null)
            foreach (var mi in live.MeshInstances)
                if (mi is not null && IsInstanceValid(mi))
                    mi.QueueFree();

        // Tear down GpuParticles3D nodes (kept null in practice; retained for forward compat).
        if (live.GpuParticles is not null)
            foreach (var gpu in live.GpuParticles)
                if (gpu is not null && IsInstanceValid(gpu))
                {
                    gpu.Emitting = false;
                    var timer = GetTree().CreateTimer(1.1);
                    timer.Timeout += () =>
                    {
                        if (IsInstanceValid(gpu)) gpu.QueueFree();
                    };
                }

        // Tear down GPU-particle simulation nodes (GpuParticleSimNode).
        // spec: Docs/RE/formats/effects.md §E.2.2 — per-particle Euler integration nodes: CODE-CONFIRMED.
        if (live.SimNodes is not null)
            foreach (var sim in live.SimNodes)
                if (sim is not null && IsInstanceValid(sim))
                    sim.QueueFree();
    }
}