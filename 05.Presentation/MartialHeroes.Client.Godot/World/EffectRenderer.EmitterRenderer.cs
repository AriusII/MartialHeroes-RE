using Godot;
using MartialHeroes.Assets.Parsers.Effects.Models;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class EffectRenderer
{
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

    private static ArrayMesh BuildBillboardQuad(
        float sizeX, float sizeY,
        Color tint,
        float uOff, float vOff,
        bool preRotate90Y)
    {
        var hw = 0.5f * Math.Abs(sizeX);
        var hh = 0.5f * Math.Abs(sizeY);
        hw = MathF.Max(hw, 0.05f);
        hh = MathF.Max(hh, 0.05f);

        var (aX, aY, bX, bY, cX, cY, dX, dY) = preRotate90Y
            ? (-hh, hw, hh, hw, hh, -hw, -hh, -hw)
            : (-hw, hh, hw, hh, hw, -hh, -hw, -hh);

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
        arrays[(int)Mesh.ArrayType.Color] = new[]
        {
            tint,
            tint,
            tint,
            tint
        };
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 0, 2, 3 };

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static ArrayMesh BuildMeshParticle(
        XeffKeyframe kA, XeffKeyframe kB, float frac,
        float sx, float sy, float sz,
        Color tint, float uOff, float vOff)
    {
        return BuildBillboardQuad(sx, sy, tint, uOff, vOff, false);
    }


    private static StandardMaterial3D BuildEffectMaterial(ImageTexture texture, Color tint)
    {
        return new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoTexture = texture,
            AlbedoColor = tint,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Mix,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
        };
    }


    private ImageTexture?[]? LoadSubEffectTextures(SubEffectDesc se)
    {
        if (_assets is null || se.TextureNames.Length == 0)
            return null;

        var result = new ImageTexture?[se.TextureNames.Length];
        for (var t = 0; t < se.TextureNames.Length; t++)
        {
            var name = se.TextureNames[t];
            if (string.IsNullOrEmpty(name)) continue;

            var vfsPath = $"data/effect/texture/{name}.tga";
            result[t] = _assets.LoadTexture(vfsPath);

            if (result[t] is null)
                result[t] = _assets.LoadTexture($"data/effect/texture/{name}.dds");
        }

        return result;
    }


    private void TeardownLiveEffect(LiveEffect live)
    {
        if (live.MeshInstances is not null)
            foreach (var mi in live.MeshInstances)
                if (mi is not null && IsInstanceValid(mi))
                    mi.QueueFree();

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

        if (live.SimNodes is not null)
            foreach (var sim in live.SimNodes)
                if (sim is not null && IsInstanceValid(sim))
                    sim.QueueFree();
    }
}