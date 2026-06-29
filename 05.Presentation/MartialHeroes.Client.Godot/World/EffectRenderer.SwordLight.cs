using Godot;
using MartialHeroes.Assets.Parsers.Effects;
using MartialHeroes.Assets.Parsers.Effects.Models;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Client.Infrastructure.Catalog;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class EffectRenderer
{
    private readonly Dictionary<ActorKey, SwordLightRibbonNode> _swordLight = new();

    private SwordLightCatalogue? _swordCatalogue;
    private bool _swordCatalogueAttempted;

    private SwordLightCatalogue? EnsureSwordCatalogue()
    {
        if (_swordCatalogueAttempted) return _swordCatalogue;
        _swordCatalogueAttempted = true;

        if (_assets is null) return null;

        var raw = _assets.GetRaw(SwordLightDescriptorParser.ItemVfsPath);
        if (raw.IsEmpty)
        {
            GD.Print($"[EffectRenderer] itemswordlight.txt absent ({SwordLightDescriptorParser.ItemVfsPath}) — " +
                     "sword-light ribbons disabled. spec: Docs/RE/formats/effects.md §F.5.");
            return null;
        }

        try
        {
            _swordCatalogue = new SwordLightCatalogue(
                SwordLightDescriptorParser.Parse(raw, "itemswordlight.txt"));
            GD.Print($"[EffectRenderer] itemswordlight.txt loaded: {_swordCatalogue.Count} entries. " +
                     "spec: Docs/RE/formats/effects.md §F.5 (column semantics RGB vs single-float UNCERTAIN — " +
                     "values read defensively).");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EffectRenderer] itemswordlight.txt parse failed: {ex.Message} — ribbons disabled.");
            _swordCatalogue = null;
        }

        return _swordCatalogue;
    }

    public void RefreshSwordLight(ActorKey actor, Node3D actorNode)
    {
        ArgumentNullException.ThrowIfNull(actorNode);
        RefreshSwordLight(actor, actorNode, actor.RawId, 1);
    }

    public void RefreshSwordLight(ActorKey actor, Node3D actorNode, uint weaponItemId, int handSelector)
    {
        ArgumentNullException.ThrowIfNull(actorNode);

        ClearSwordLight(actor);

        var catalogue = EnsureSwordCatalogue();
        if (catalogue is null) return;
        if (!catalogue.TryGet(weaponItemId, out var entry)) return;

        var skinned = FindSkinnedNode(actorNode);
        if (skinned is null) return;

        var color = ResolveSwordLightColor(entry);
        var handBone = ResolveHandBone(handSelector);
        var texture = ResolveSwordLightTexture(entry);

        var node = new SwordLightRibbonNode(skinned, handBone, color, texture)
        {
            Name = $"SwordLight_{entry.Key}_h{handSelector}"
        };
        AddChild(node);
        _swordLight[actor] = node;

        GD.Print($"[EffectRenderer] RefreshSwordLight: actor={actor.RawId} weapon={weaponItemId} hand={handSelector} " +
                 $"rgb=({color.R:F2},{color.G:F2},{color.B:F2}) tex='{entry.TextureName}'. " +
                 "spec: Docs/RE/formats/effects.md §F.5 (gated by attack state, hand-bone trail).");
    }

    public void ClearSwordLight(ActorKey actor)
    {
        if (!_swordLight.Remove(actor, out var node)) return;
        if (IsInstanceValid(node)) node.QueueFree();
    }

    private static Color ResolveSwordLightColor(SwordLightEntry entry)
    {
        var r = entry.R;
        var g = entry.G;
        var b = entry.B;

        if (r > 1f || g > 1f || b > 1f)
        {
            r /= 255f;
            g /= 255f;
            b /= 255f;
        }

        r = Math.Clamp(r, 0f, 1f);
        g = Math.Clamp(g, 0f, 1f);
        b = Math.Clamp(b, 0f, 1f);

        if (r <= 0f && g <= 0f && b <= 0f)
            return new Color(1f, 1f, 1f);

        return new Color(r, g, b);
    }

    private static int ResolveHandBone(int handSelector)
    {
        return SkinnedCharacterNode.DefaultHandBoneId;
    }

    private ImageTexture? ResolveSwordLightTexture(SwordLightEntry entry)
    {
        if (_assets is null || string.IsNullOrEmpty(entry.TextureName)) return null;

        var name = entry.TextureName;
        return _assets.LoadTexture($"{SwordLightCatalogue.TexturePrefix}{name}")
               ?? _assets.LoadTexture($"{SwordLightCatalogue.TexturePrefix}{name}.tga")
               ?? _assets.LoadTexture($"{SwordLightCatalogue.TexturePrefix}{name}.dds");
    }


    internal sealed partial class SwordLightRibbonNode : Node3D
    {
        private const int Cap = 242;
        private const float RibbonHalfWidth = 0.25f;
        private const int FadeFrames = 12;

        private readonly Color[] _col = new Color[2 * Cap];
        private readonly int _handBone;
        private readonly int[] _idx = new int[6 * (Cap - 1)];
        private readonly StandardMaterial3D _material;
        private readonly ArrayMesh _mesh = new();
        private readonly Vector3[] _pts = new Vector3[Cap];
        private readonly MeshInstance3D _ribbon = new();
        private readonly SkinnedCharacterNode _skinned;
        private readonly Array _surfaceArrays = new();
        private readonly Vector2[] _uv = new Vector2[2 * Cap];
        private readonly Vector3[] _vtx = new Vector3[2 * Cap];

        private int _count;
        private int _fadeCountdown;
        private int _head;

        internal SwordLightRibbonNode(
            SkinnedCharacterNode skinned, int handBone, Color tint, ImageTexture? texture)
        {
            _skinned = skinned;
            _handBone = handBone;

            for (var q = 0; q < Cap - 1; q++)
            {
                var b = q * 6;
                var v0 = 2 * q;
                var v1 = 2 * q + 1;
                var v2 = 2 * (q + 1);
                var v3 = 2 * (q + 1) + 1;
                _idx[b + 0] = v0;
                _idx[b + 1] = v2;
                _idx[b + 2] = v1;
                _idx[b + 3] = v1;
                _idx[b + 4] = v2;
                _idx[b + 5] = v3;
            }

            for (var i = 0; i < Cap; i++)
            {
                var frac = Cap > 1 ? i / (float)(Cap - 1) : 0f;
                _uv[2 * i] = new Vector2(frac, 0f);
                _uv[2 * i + 1] = new Vector2(frac, 1f);
                var faded = new Color(tint.R, tint.G, tint.B, tint.A * frac);
                _col[2 * i] = faded;
                _col[2 * i + 1] = faded;
            }

            _surfaceArrays.Resize((int)Mesh.ArrayType.Max);
            _surfaceArrays[(int)Mesh.ArrayType.Vertex] = _vtx;
            _surfaceArrays[(int)Mesh.ArrayType.TexUV] = _uv;
            _surfaceArrays[(int)Mesh.ArrayType.Color] = _col;
            _surfaceArrays[(int)Mesh.ArrayType.Index] = _idx;

            _material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                VertexColorUseAsAlbedo = true,
                AlbedoColor = new Color(1f, 1f, 1f),
                AlbedoTexture = texture,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
            };

            _ribbon.Name = "SwordLightRibbon";
            _ribbon.Mesh = _mesh;
        }

        public override void _Ready()
        {
            AddChild(_ribbon);
            RebuildMesh();
        }

        public override void _Process(double delta)
        {
            if (!IsInstanceValid(_skinned))
            {
                Visible = false;
                return;
            }

            var attacking = _skinned.IsActionPlaying;

            if (_skinned.TryGetBoneGlobalTransform(_handBone, out var hand))
            {
                if (attacking)
                {
                    Push(hand.Origin);
                    _fadeCountdown = FadeFrames;
                }
                else if (_fadeCountdown > 0)
                {
                    _fadeCountdown--;
                    DropOldest();
                }
                else if (_count > 0)
                {
                    _count = 0;
                }
            }

            Visible = _count >= 2;
            if (Visible) RebuildMesh();
        }

        private void Push(Vector3 p)
        {
            _pts[_head] = p;
            _head = (_head + 1) % Cap;
            if (_count < Cap) _count++;
        }

        private void DropOldest()
        {
            if (_count > 0) _count--;
        }

        private Vector3 RingPoint(int i)
        {
            var oldest = (_head - _count + 2 * Cap) % Cap;
            return _pts[(oldest + i) % Cap];
        }

        private void RebuildMesh()
        {
            var last = _count > 0 ? RingPoint(_count - 1) : Vector3.Zero;

            for (var j = 0; j < Cap; j++)
            {
                var p = j < _count ? RingPoint(j) : last;

                var prevIdx = j > 0 ? j - 1 < _count ? j - 1 : _count - 1 : 0;
                var nextIdx = j + 1 < _count ? j + 1 : _count > 0 ? _count - 1 : 0;
                var prev = _count > 0 ? RingPoint(Math.Clamp(prevIdx, 0, _count - 1)) : p;
                var next = _count > 0 ? RingPoint(Math.Clamp(nextIdx, 0, _count - 1)) : p;

                var tangent = next - prev;
                if (tangent.LengthSquared() < 1e-6f) tangent = Vector3.Forward;

                var axis = tangent.Cross(Vector3.Up);
                if (axis.LengthSquared() < 1e-6f) axis = Vector3.Right;
                axis = axis.Normalized() * RibbonHalfWidth;

                _vtx[2 * j] = p + axis;
                _vtx[2 * j + 1] = p - axis;
            }

            _mesh.ClearSurfaces();
            _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, _surfaceArrays);
            _mesh.SurfaceSetMaterial(0, _material);
        }
    }
}