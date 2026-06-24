using Godot;
using MartialHeroes.Assets.Parsers.Mesh;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Client.Application.Engine;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Presentation.Helpers;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class VisualActor : CharacterBody3D
{
    private const float WalkGlideSpeed = 5.0f;
    private const float RunGlideSpeed = 10.0f;

    private const float SkinnedAvatarScale = 5.0f;


    private CellCollisionManager? _cellCollisionManager;

    private Vector3 _currPosition;

    private bool _hasSnapshot;
    private bool _hasTarget;

    private bool _isDead;
    private bool _isRunning;

    private Vector3 _moveTarget;


    private MeshInstance3D? _placeholderMesh;

    private Vector3 _prevPosition;

    private Node3D? _skinnedAvatar;

    private SkinnedCharacterNode? _skinnedNode;

    private RealClientAssets? _combatAssets;
    private int _combatAppearanceKey;
    private int _combatSkinClass;
    private AnimationClip? _attackClipCache;
    private bool _attackClipResolved;


    private double _tickDurationSec = 1.0 / GameEngineLoop.DefaultTickRateHz;

    private double _timeSinceSnapshot;

    public bool IsLocalPlayer { get; set; }

    public ActorKey ActorKey { get; set; }

    public string ActorName { get; set; } = string.Empty;

    public void SetCollisionManager(CellCollisionManager mgr)
    {
        _cellCollisionManager = mgr;
    }

    public void AttachSkinnedAvatar(Node3D skinnedRoot)
    {
        if (_skinnedAvatar is not null && IsInstanceValid(_skinnedAvatar))
        {
            RemoveChild(_skinnedAvatar);
            _skinnedAvatar.QueueFree();
            _skinnedAvatar = null;
        }

        if (_placeholderMesh is not null && IsInstanceValid(_placeholderMesh))
        {
            RemoveChild(_placeholderMesh);
            _placeholderMesh.QueueFree();
            _placeholderMesh = null;
        }

        skinnedRoot.Name = "SkinnedAvatar";
        skinnedRoot.Scale = Vector3.One * SkinnedAvatarScale;
        AddChild(skinnedRoot);
        _skinnedAvatar = skinnedRoot;
        _skinnedNode = FindSkinnedNode(skinnedRoot);

        _isDead = false;
    }

    public void SetCombatClipSource(RealClientAssets assets, int appearanceKey, int skinClass)
    {
        _combatAssets = assets;
        _combatAppearanceKey = appearanceKey;
        _combatSkinClass = skinClass;
        _attackClipResolved = false;
        _attackClipCache = null;
    }

    public void PlayAttackMotion()
    {
        if (_skinnedNode is null || !IsInstanceValid(_skinnedNode))
            return;

        var clip = ResolveAttackClip();
        if (clip is null)
        {
            GD.Print($"[VisualActor] PlayAttackMotion (actor '{ActorName}'): no attack clip resolved " +
                     "(combat motion-kind→column not pinned in spec, or clip absent) — staying idle. " +
                     "spec: skinning.md §10.5 / formats/animation.md §actormotion (attack column DBG-pending).");
            return;
        }

        _skinnedNode.PlayActionClip(clip);
    }

    private AnimationClip? ResolveAttackClip()
    {
        if (_attackClipResolved) return _attackClipCache;
        _attackClipResolved = true;

        if (_combatAssets is null) return null;

        var registry = CharVisualRegistry.GetOrBuild(_combatAssets);
        if (registry is null) return null;

        var entry = registry.GetByMotionKey(_combatAppearanceKey)
                    ?? registry.ActorMotion.GetBySkinClass(_combatSkinClass);
        if (entry is null) return null;

        var motId = AttackMotionId(entry);
        if (motId <= 0) return null;

        var motPath = registry.ResolveMotPath(motId);
        if (motPath is null || !_combatAssets.Contains(motPath)) return null;

        try
        {
            var data = _combatAssets.GetRaw(motPath);
            if (data.IsEmpty) return null;
            _attackClipCache = AnimationParser.Parse(data);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[VisualActor] attack .mot load failed '{motPath}': {ex.Message}");
            _attackClipCache = null;
        }

        return _attackClipCache;
    }

    private static int AttackMotionId(MartialHeroes.Assets.Parsers.Character.Models.ActormotionEntry entry)
    {
        return 0;
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

    public bool TryBuildBodyAvatar(RealClientAssets assets, ushort serverClass)
    {
        if (assets is null)
        {
            GD.Print("[VisualActor] Body avatar: VFS handle unavailable — no skinned avatar attached. " +
                     "spec: skinning.md §8(e).");
            return false;
        }

        var avatar = PlayerAvatarResolver.TryBuild(assets, serverClass, []);
        if (avatar is null)
        {
            GD.Print($"[VisualActor] Body avatar: class={serverClass} did not resolve a skinned avatar " +
                     $"(actor '{ActorName}') — rendering nothing. spec: skinning.md §8(e).");
            return false;
        }

        AttachSkinnedAvatar(avatar);
        GD.Print($"[VisualActor] Body avatar attached (class={serverClass}, skinned+idle, body-only — " +
                 $"ActorSpawnedEvent carries no EquipGids, so equip overlay is deferred). " +
                 "spec: skinning.md §8(e) / §10.");
        return true;
    }

    public void PlayDeathMotion()
    {
        if (_isDead) return;

        if (_skinnedAvatar is null || !IsInstanceValid(_skinnedAvatar))
        {
            GD.Print($"[VisualActor] PlayDeathMotion: no skinned avatar attached (actor '{ActorName}') — " +
                     "no death pose applied. spec: 5-10_combat_death.yaml.");
            return;
        }

        _isDead = true;

        _skinnedAvatar.RotationDegrees = new Vector3(-90f, _skinnedAvatar.RotationDegrees.Y, 0f);

        GD.Print($"[VisualActor] PlayDeathMotion (actor '{ActorName}'): death clip id pending — visual cue " +
                 "only (avatar laid flat; idle still ticking). spec: 5-10_combat_death.yaml (death clip/" +
                 "effect ids capture-pending); skinning.md §8(e)/§10 (only the standing idle is recovered).");
    }


    public override void _Ready()
    {
        var col = new CollisionShape3D();
        var shape = new CapsuleShape3D();
        shape.Radius = 0.4f;
        shape.Height = 1.8f;
        col.Shape = shape;
        AddChild(col);

        var label = new Label3D();
        label.Text = ActorName;
        label.Position = new Vector3(0, 1.2f, 0);
        label.FontSize = 18;
        AddChild(label);
    }

    public override void _Process(double delta)
    {
        if (_hasSnapshot)
        {
            _timeSinceSnapshot += delta;
            var alpha = _tickDurationSec > 0.0
                ? _timeSinceSnapshot / _tickDurationSec
                : 1.0;

            var t = (float)Math.Clamp(alpha, 0.0, 1.2);
            var interpolated = _prevPosition.Lerp(_currPosition, t);

            if (IsLocalPlayer && _cellCollisionManager is not null &&
                _cellCollisionManager.TryMoveSlide(GlobalPosition, interpolated, out var resolved))
                GlobalPosition = resolved;
            else
                GlobalPosition = interpolated;
        }
        else if (_hasTarget)
        {
            LegacyGlide(delta);
        }
    }


    public void ApplySnapshot(in ActorSnapshot snapshot, double tickDurationSec)
    {
        _prevPosition = _hasSnapshot ? _currPosition : ConvertPosition(snapshot.Position);

        _currPosition = ConvertPosition(snapshot.MoveTarget);

        _tickDurationSec = tickDurationSec > 0.0 ? tickDurationSec : 1.0 / GameEngineLoop.DefaultTickRateHz;
        _timeSinceSnapshot = 0.0;
        _hasSnapshot = true;
        _hasTarget = false;
    }


    public void SetMoveTarget(Vector3 target, bool running)
    {
        _moveTarget = target;
        _isRunning = running;
        _hasTarget = true;
    }


    private static Vector3 ConvertPosition(Vector3Fixed pos)
    {
        var (fx, fy, fz) = pos.ToVector3Float();
        var (gx, gy, gz) = WorldCoordinates.ToGodot(fx, fy, fz);
        return new Vector3(gx, gy, gz);
    }

    private void LegacyGlide(double delta)
    {
        var speed = _isRunning ? RunGlideSpeed : WalkGlideSpeed;
        var current = GlobalPosition;
        var direction = _moveTarget - current;
        var distance = direction.Length();

        if (distance < 0.05f)
        {
            GlobalPosition = _moveTarget;
            Velocity = Vector3.Zero;
            _hasTarget = false;
            return;
        }

        var step = (float)(speed * delta);
        var desired = step >= distance ? _moveTarget : current + direction.Normalized() * step;

        if (IsLocalPlayer && _cellCollisionManager is not null &&
            _cellCollisionManager.TryMoveSlide(current, desired, out var resolved))
        {
            GlobalPosition = resolved;
            Velocity = Vector3.Zero;
        }
        else
        {
            Velocity = direction.Normalized() * speed;
            MoveAndSlide();
        }
    }
}