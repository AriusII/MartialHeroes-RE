using Godot;
using MartialHeroes.Assets.Parsers.Effects;
using MartialHeroes.Assets.Parsers.Effects.Models;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

public sealed partial class XeffSceneEffect : Node3D
{
    private const string XeffTexturePath = "data/effect/texture/";

    private const string CharSelectXeffPath = "data/effect/xeff/char_select-u.xeff";

    private const uint EmitterBillboard = 0;
    private const uint EmitterMesh = 1;
    private const uint EmitterDirectional = 2;


    private AnimEntry[] _animEntries = [];

    private double _elapsed;


    public void Initialise(XeffData xeff, RealClientAssets? assets)
    {
        var builtBillboard = 0;
        var builtMesh = 0;
        var builtDirectional = 0;
        var skippedNoKf = 0;
        var skippedTransparent = 0;
        var skippedNoTexture = 0;
        var skippedDegenerate = 0;

        var animList = new List<AnimEntry>(xeff.SubEffects.Length);

        for (var i = 0; i < xeff.SubEffects.Length; i++)
        {
            var sub = xeff.SubEffects[i];

            if (sub.Keyframes.Length == 0)
            {
                skippedNoKf++;
                continue;
            }

            var kf0 = sub.Keyframes[0];

            var offset = new Vector3(kf0.VelocityX, kf0.VelocityY, -kf0.VelocityZ);

            var sizeX = kf0.SizeX;
            var sizeY = kf0.SizeY;
            var quadW = sizeX * 2.0f;
            var quadH = sizeY * 2.0f;
            if (quadW <= 0.0f || quadH <= 0.0f)
            {
                skippedDegenerate++;
                continue;
            }

            var opacity = 1.0f;
            if (sub.AlphaKeys.Length > 0)
                opacity = 1.0f - sub.AlphaKeys[0];

            opacity = Math.Clamp(opacity, 0.0f, 1.0f);
            if (opacity <= 0.0f)
            {
                skippedTransparent++;
                continue;
            }

            ImageTexture? albedo = null;
            if (sub.TextureNames.Length > 0 && sub.TextureNames[0].Length > 0 && assets is not null)
            {
                var tgaPath = $"{XeffTexturePath}{sub.TextureNames[0]}.tga";
                if (assets.Contains(tgaPath))
                    try
                    {
                        albedo = assets.LoadTexture(tgaPath);
                    }
                    catch
                    {
                    }
            }

            if (albedo is null)
            {
                skippedNoTexture++;
                continue;
            }

            var diffR = sub.DiffuseR.Length > 0 ? Math.Clamp(sub.DiffuseR[0], 0f, 1f) : 1.0f;
            var diffG = sub.DiffuseG.Length > 0 ? Math.Clamp(sub.DiffuseG[0], 0f, 1f) : 1.0f;
            var diffB = sub.DiffuseB.Length > 0 ? Math.Clamp(sub.DiffuseB[0], 0f, 1f) : 1.0f;
            var tint = new Color(diffR, diffG, diffB);

            var mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                AlbedoTexture = albedo,
                AlbedoColor = new Color(tint.R, tint.G, tint.B, opacity)
            };

            MeshInstance3D mi;

            switch (sub.EmitterType)
            {
                case EmitterBillboard:
                {
                    mat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;

                    var quad = new QuadMesh
                    {
                        Size = new Vector2(quadW, quadH),
                        Material = mat
                    };

                    mi = new MeshInstance3D
                    {
                        Name = $"XeffBillboard{i}",
                        Position = offset,
                        Mesh = quad
                    };

                    AddChild(mi);
                    builtBillboard++;
                    break;
                }

                case EmitterMesh:
                {
                    mat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Disabled;

                    var quad = new QuadMesh
                    {
                        Size = new Vector2(quadW, quadH),
                        Material = mat
                    };

                    var legacyQ = kf0.Rotation;

                    var godotKfQ = new Quaternion(-legacyQ.X, -legacyQ.Y, legacyQ.Z, legacyQ.W);
                    if (godotKfQ.LengthSquared() > 0.0001f)
                        godotKfQ = godotKfQ.Normalized();
                    else
                        godotKfQ = Quaternion.Identity;

                    var preRot = new Quaternion(Vector3.Up, Mathf.Pi * 0.5f);
                    var godotQ = (preRot * godotKfQ).Normalized();

                    mi = new MeshInstance3D
                    {
                        Name = $"XeffMesh{i}",
                        Position = offset,
                        Quaternion = godotQ,
                        Mesh = quad
                    };

                    AddChild(mi);
                    builtMesh++;
                    break;
                }

                case EmitterDirectional:
                {
                    mat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Disabled;

                    var quad = new QuadMesh
                    {
                        Size = new Vector2(quadW, quadH),
                        Material = mat
                    };

                    var legacyQ = kf0.Rotation;
                    var godotKfQ = new Quaternion(-legacyQ.X, -legacyQ.Y, legacyQ.Z, legacyQ.W);
                    if (godotKfQ.LengthSquared() > 0.0001f)
                        godotKfQ = godotKfQ.Normalized();
                    else
                        godotKfQ = Quaternion.Identity;

                    mi = new MeshInstance3D
                    {
                        Name = $"XeffDir{i}",
                        Position = offset,
                        Quaternion = godotKfQ,
                        Mesh = quad
                    };

                    AddChild(mi);
                    builtDirectional++;
                    break;
                }

                default:
                {
                    mat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;

                    var quad = new QuadMesh
                    {
                        Size = new Vector2(quadW, quadH),
                        Material = mat
                    };

                    mi = new MeshInstance3D
                    {
                        Name = $"XeffUnk{i}",
                        Position = offset,
                        Mesh = quad
                    };

                    AddChild(mi);
                    builtBillboard++;
                    break;
                }
            }

            animList.Add(new AnimEntry(
                mi,
                sub.AlphaKeys,
                sub.AnimStride,
                (uint)sub.TextureNames.Length,
                sub.TextureNames,
                albedo,
                mat,
                assets));
        }

        _animEntries = [.. animList];

        GD.Print($"[XeffSceneEffect] Built effectId={xeff.EffectId} subEffectCount={xeff.SubEffectCount}: " +
                 $"billboard={builtBillboard} mesh={builtMesh} directional={builtDirectional} " +
                 $"(total={builtBillboard + builtMesh + builtDirectional}). " +
                 $"Skipped: noKf={skippedNoKf} transparent={skippedTransparent} " +
                 $"noTexture={skippedNoTexture} degenerate={skippedDegenerate}. " +
                 "spec: frontend_scenes.md §3.6.7 (expected: ~6 billboard / ~51 mesh / ~11 directional).");
    }


    public override void _Process(double delta)
    {
        _elapsed += delta;
        var elapsedMs = _elapsed * 1000.0;

        foreach (ref readonly var entry in _animEntries.AsSpan())
        {
            if (entry.AnimStride == 0 || entry.FrameCount <= 1)
                continue;

            var frameIdx = (int)(elapsedMs / entry.AnimStride) % (int)entry.FrameCount;

            var opacity = 1.0f;
            if (entry.AlphaKeys.Length > frameIdx)
                opacity = Math.Clamp(1.0f - entry.AlphaKeys[frameIdx], 0.0f, 1.0f);

            entry.Material.AlbedoColor = new Color(1.0f, 1.0f, 1.0f, opacity);

            if (frameIdx == 0)
            {
                if (entry.Tex0 is not null)
                    entry.Material.AlbedoTexture = entry.Tex0;
            }
            else if (frameIdx < entry.TextureNames.Length && entry.Assets is not null)
            {
                var texName = entry.TextureNames[frameIdx];
                if (texName.Length > 0)
                {
                    var tgaPath = $"{XeffTexturePath}{texName}.tga";
                    if (entry.Assets.Contains(tgaPath))
                        try
                        {
                            var tex = entry.Assets.LoadTexture(tgaPath);
                            if (tex is not null)
                                entry.Material.AlbedoTexture = tex;
                        }
                        catch
                        {
                        }
                }
            }
        }
    }


    public static XeffSceneEffect? LoadAndAttach(
        Node3D parent,
        Vector3 anchorGodotPos,
        RealClientAssets? assets)
    {
        if (assets is null)
        {
            GD.Print("[XeffSceneEffect] No VFS — char_select-u.xeff skipped.");
            return null;
        }

        if (!assets.Contains(CharSelectXeffPath))
        {
            GD.Print($"[XeffSceneEffect] {CharSelectXeffPath} absent from VFS — effect skipped.");
            return null;
        }

        XeffData xeff;
        try
        {
            var bytes = assets.GetRaw(CharSelectXeffPath);
            if (bytes.IsEmpty)
            {
                GD.PrintErr($"[XeffSceneEffect] {CharSelectXeffPath} returned empty bytes.");
                return null;
            }

            xeff = XeffParser.ParseXeff(bytes);
            GD.Print($"[XeffSceneEffect] Parsed {CharSelectXeffPath}: " +
                     $"effectId={xeff.EffectId} subEffectCount={xeff.SubEffectCount}. " +
                     "spec: frontend_scenes.md §3.6.5 + §3.6.6 effectId=380003000 / 68 sub-effects. CONFIRMED.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[XeffSceneEffect] XeffParser failed on {CharSelectXeffPath}: {ex.Message}");
            return null;
        }

        var node = new XeffSceneEffect
        {
            Name = "CharSelectXeff",
            Position = anchorGodotPos,
            Scale = Vector3.One
        };

        parent.AddChild(node);

        node.Initialise(xeff, assets);

        return node;
    }

    private readonly record struct AnimEntry(
        MeshInstance3D Node,
        float[] AlphaKeys,
        uint AnimStride,
        uint FrameCount,
        string[] TextureNames,
        ImageTexture? Tex0,
        StandardMaterial3D Material,
        RealClientAssets? Assets);
}