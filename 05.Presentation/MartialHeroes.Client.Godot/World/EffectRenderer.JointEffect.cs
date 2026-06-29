using Godot;
using MartialHeroes.Assets.Parsers.Effects;
using MartialHeroes.Assets.Parsers.Effects.Models;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Client.Infrastructure.Catalog;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class EffectRenderer
{
    private readonly Dictionary<ActorKey, List<JointBoundEffectNode>> _jointBound = new();

    private JointEffectCatalogue? _jointCatalogue;
    private bool _jointCatalogueAttempted;
    private bool _jointMode1Noted;

    private JointEffectCatalogue? EnsureJointCatalogue()
    {
        if (_jointCatalogueAttempted) return _jointCatalogue;
        _jointCatalogueAttempted = true;

        if (_assets is null) return null;

        var raw = _assets.GetRaw(ItemJointEffectCatalogueParser.VfsPath);
        if (raw.IsEmpty)
        {
            GD.Print($"[EffectRenderer] itemjointeff.txt absent ({ItemJointEffectCatalogueParser.VfsPath}) — " +
                     "joint bone-bound effects disabled. spec: Docs/RE/formats/effects.md §F.4.");
            return null;
        }

        try
        {
            _jointCatalogue = new JointEffectCatalogue(ItemJointEffectCatalogueParser.Parse(raw));
            GD.Print($"[EffectRenderer] itemjointeff.txt loaded: {_jointCatalogue.Count} entries / " +
                     $"{_jointCatalogue.KeyCount} map keys. spec: Docs/RE/formats/effects.md §F.4 " +
                     "(player itemjointeff: effect_id at a joint bone, scale, rot_source).");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EffectRenderer] itemjointeff.txt parse failed: {ex.Message} — joint effects disabled.");
            _jointCatalogue = null;
        }

        return _jointCatalogue;
    }

    public void SpawnJointEffects(ActorKey actor, Node3D actorNode)
    {
        ArgumentNullException.ThrowIfNull(actorNode);
        SpawnJointEffects(actor, actorNode, actor.RawId);
    }

    public void SpawnJointEffects(ActorKey actor, Node3D actorNode, uint appearanceMapKey)
    {
        ArgumentNullException.ThrowIfNull(actorNode);

        ClearJointEffects(actor);

        var catalogue = EnsureJointCatalogue();
        if (catalogue is null) return;

        var skinned = FindSkinnedNode(actorNode);
        if (skinned is null)
        {
            GD.Print($"[EffectRenderer] SpawnJointEffects: actor={actor.RawId} carries no SkinnedCharacterNode — " +
                     "no bone-bound effects. spec: Docs/RE/formats/effects.md §F.4.");
            return;
        }

        var entries = catalogue.GetAll(appearanceMapKey);
        if (entries.Count == 0) return;

        var bound = new List<JointBoundEffectNode>(entries.Count);
        foreach (var entry in entries)
        {
            if (entry.EffectId == 0) continue;

            var node = BuildJointBoundEffect(entry, skinned, actorNode);
            if (node is null) continue;

            AddChild(node);
            bound.Add(node);
        }

        if (bound.Count == 0) return;

        _jointBound[actor] = bound;
        GD.Print($"[EffectRenderer] SpawnJointEffects: actor={actor.RawId} mapKey={appearanceMapKey} → " +
                 $"{bound.Count} bone-bound effect(s). spec: Docs/RE/formats/effects.md §F.4 " +
                 "(itemjointeff effect_id bound to joint bone; bone world transform per frame).");
    }

    public void ClearJointEffects(ActorKey actor)
    {
        if (!_jointBound.Remove(actor, out var list)) return;
        foreach (var node in list)
            if (IsInstanceValid(node))
                node.QueueFree();
    }

    private JointBoundEffectNode? BuildJointBoundEffect(
        JointEffectEntry entry, SkinnedCharacterNode skinned, Node3D facing)
    {
        if (entry.BoneNameMode == 1 && !_jointMode1Noted)
        {
            _jointMode1Noted = true;
            GD.Print($"[EffectRenderer] SpawnJointEffects: bone_name_mode=1 (AnimCatalog weapon-slot 902..905, " +
                     "visual-class mod 40) — slot mapping unavailable here; falling back to explicit bone_id. " +
                     "spec: Docs/RE/formats/effects.md §F.4 (AnimCatalog slot resolve DBG-pending).");
        }

        var boneId = entry.BoneId;
        var effScale = ResolveBaseScale(entry.EffectId) * (entry.Scale != 0f ? entry.Scale : 1f);

        var subEffects = TryLoadXeff(entry.EffectId);
        SubEffectDesc[] seArr = subEffects ?? [];

        var meshInstances = new MeshInstance3D?[seArr.Length];
        var simNodes = new GpuParticleSimNode?[seArr.Length];
        var allTextures = new ImageTexture?[seArr.Length][];

        for (var i = 0; i < seArr.Length; i++)
        {
            var se = seArr[i];
            allTextures[i] = LoadSubEffectTextures(se) ?? System.Array.Empty<ImageTexture?>();

            if (se.ResourceId >= XeffResourceParticleThreshold)
            {
                simNodes[i] = TryBuildParticleSimNode(se.ResourceId, Vector3.Zero);
            }
            else
            {
                meshInstances[i] = BuildSubEffectMesh(se, Vector3.Zero, allTextures[i], 0, effScale);
            }
        }

        var node = new JointBoundEffectNode(skinned, boneId, entry.RotSource, facing, this,
            seArr, meshInstances, simNodes, allTextures, effScale)
        {
            Name = $"JointEff_{entry.EffectId}_b{boneId}"
        };

        for (var i = 0; i < seArr.Length; i++)
        {
            if (meshInstances[i] is { } mi)
            {
                mi.Position = Vector3.Zero;
                node.AddChild(mi);
            }

            if (simNodes[i] is { } sim)
                node.AddChild(sim);
        }

        if (seArr.Length == 0)
            GD.Print($"[EffectRenderer] SpawnJointEffects: effectId={entry.EffectId} .xeff unavailable — " +
                     "bone-bound node carries no visual (no-placeholder doctrine).");

        return node;
    }

    private static SkinnedCharacterNode? FindSkinnedNode(Node node)
    {
        if (node is SkinnedCharacterNode skinned) return skinned;
        foreach (var child in node.GetChildren())
        {
            var found = FindSkinnedNode(child);
            if (found is not null) return found;
        }

        return null;
    }


    internal sealed partial class JointBoundEffectNode : Node3D
    {
        private readonly Node3D _facing;
        private readonly int _boneId;
        private readonly byte _rotSource;
        private readonly SkinnedCharacterNode _skinned;
        private readonly EffectRenderer _renderer;
        private readonly SubEffectDesc[] _subEffects;
        private readonly MeshInstance3D?[] _meshInstances;
        private readonly GpuParticleSimNode?[] _simNodes;
        private readonly ImageTexture?[][] _textures;
        private readonly float _effScale;
        private double _elapsedMs;

        internal JointBoundEffectNode(
            SkinnedCharacterNode skinned, int boneId, byte rotSource, Node3D facing,
            EffectRenderer renderer,
            SubEffectDesc[] subEffects,
            MeshInstance3D?[] meshInstances,
            GpuParticleSimNode?[] simNodes,
            ImageTexture?[][] textures,
            float effScale)
        {
            _skinned = skinned;
            _boneId = boneId;
            _rotSource = rotSource;
            _facing = facing;
            _renderer = renderer;
            _subEffects = subEffects;
            _meshInstances = meshInstances;
            _simNodes = simNodes;
            _textures = textures;
            _effScale = effScale;
        }

        public override void _Process(double delta)
        {
            if (!IsInstanceValid(_skinned) || !_skinned.TryGetBoneGlobalTransform(_boneId, out var boneWorld))
            {
                Visible = false;
                return;
            }

            Visible = true;

            var basis = _rotSource == 2 && IsInstanceValid(_facing)
                ? _facing.GlobalBasis.Orthonormalized()
                : boneWorld.Basis.Orthonormalized();

            GlobalTransform = new Transform3D(basis, boneWorld.Origin);

            _elapsedMs += delta * 1000.0;
            var origin = boneWorld.Origin;
            var boneQ = boneWorld.Basis.GetRotationQuaternion();

            for (var i = 0; i < _subEffects.Length; i++)
            {
                var se = _subEffects[i];
                if (se.ResourceId >= XeffResourceParticleThreshold)
                {
                    var sim = i < _simNodes.Length ? _simNodes[i] : null;
                    if (sim is not null && IsInstanceValid(sim))
                        sim.Tick(delta);
                }
                else
                {
                    var mi = i < _meshInstances.Length ? _meshInstances[i] : null;
                    if (mi is not null && IsInstanceValid(mi))
                    {
                        var texRow = i < _textures.Length ? _textures[i] : null;
                        _renderer.RebuildSubEffectMesh(mi, se, origin, _elapsedMs, texRow, _effScale, boneQ);
                    }
                }
            }
        }
    }
}
