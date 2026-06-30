using Godot;
using MartialHeroes.Assets.Parsers.Character.Models;
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
    private const float MovingEpsilon = 0.02f;
    private const float FacingPlanarEpsilonSq = 0.0001f;

    private readonly Dictionary<int, AnimationClip?> _clipByMotId = new();

    private ActorBlobShadow? _blobShadow;
    private int _optionShadowMode = 1;
    private bool _optionShadowRead;


    private CellCollisionManager? _cellCollisionManager;
    private int _combatAppearanceKey;

    private RealClientAssets? _combatAssets;
    private int _combatSkinClass;

    private Vector3 _currPosition;

    private bool _hasSnapshot;
    private bool _hasTarget;

    private bool _isDead;
    private bool _isRunning;
    private bool _locomotionResolved;

    private Vector3 _moveTarget;


    private MeshInstance3D? _placeholderMesh;

    private Vector3 _prevPosition;
    private AnimationClip? _runClip;

    private Node3D? _skinnedAvatar;

    private SkinnedCharacterNode? _skinnedNode;
    private TerrainNode? _terrainNode;


    private double _tickDurationSec = 1.0 / GameEngineLoop.DefaultTickRateHz;

    private double _timeSinceSnapshot;
    private AnimationClip? _walkClip;

    public bool IsLocalPlayer { get; set; }

    public ActorKey ActorKey { get; set; }

    public string ActorName { get; set; } = string.Empty;

    public void SetCollisionManager(CellCollisionManager mgr)
    {
        _cellCollisionManager = mgr;
    }

    public void SetTerrainNode(TerrainNode terrainNode)
    {
        _terrainNode = terrainNode;
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
        _clipByMotId.Clear();
        _walkClip = null;
        _runClip = null;
        _locomotionResolved = false;

        EnsureBlobShadow();
    }

    private void EnsureBlobShadow()
    {
        EnsureOptionShadowMode();

        if (_blobShadow is null || !IsInstanceValid(_blobShadow))
        {
            _blobShadow = new ActorBlobShadow();
            AddChild(_blobShadow);
        }

        _blobShadow.Configure(_combatAssets);
        _blobShadow.SetFootprintHalfExtent(ResolveFootprintHalfExtent());
    }

    private float ResolveFootprintHalfExtent()
    {
        if (_skinnedNode is null || !IsInstanceValid(_skinnedNode))
            return 0f;

        var aabb = _skinnedNode.GetMeshAabb();
        var ext = MathF.Max(aabb.Size.X, aabb.Size.Z);
        return ext * 0.5f * SkinnedAvatarScale;
    }

    private void EnsureOptionShadowMode()
    {
        if (_optionShadowRead) return;
        _optionShadowRead = true;
        _optionShadowMode = ReadOptionShadowMode();

        GD.Print($"[VisualActor] OPTION_SHADOW resolved to {_optionShadowMode} (DoOption.ini [DO_OPTION], " +
                 "clamped [1,3], default 1). 3 = native shadows off, blob drawn for all actors; 1/2 = blob is the " +
                 "FAR fallback beyond DirectionalShadowMaxDistance only. " +
                 "spec: Docs/RE/structs/shadow_projector.md (mode_flag +312).");
    }

    private static int ReadOptionShadowMode()
    {
        var mode = 1;
        try
        {
            var dir = ClientPathResolver.ResolveClientDir();
            if (dir is null) return mode;

            var path = Path.Combine(dir, "DoOption.ini");
            if (!File.Exists(path)) return mode;

            var inSection = false;
            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (line.StartsWith('['))
                {
                    inSection = line.Equals("[DO_OPTION]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inSection) continue;

                var eq = line.IndexOf('=');
                if (eq < 0) continue;

                var k = line[..eq].Trim();
                if (!k.Equals("OPTION_SHADOW", StringComparison.OrdinalIgnoreCase)) continue;

                if (int.TryParse(line[(eq + 1)..].Trim(), out var v))
                    mode = Math.Clamp(v, 1, 3);

                return mode;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[VisualActor] DoOption.ini OPTION_SHADOW read failed: {ex.Message}");
        }

        return mode;
    }

    private void UpdateBlobShadow()
    {
        if (_blobShadow is null || !IsInstanceValid(_blobShadow)) return;

        var camera = GetViewport()?.GetCamera3D();
        var focus = camera is not null ? camera.GlobalPosition : GlobalPosition;
        var here = GlobalPosition;
        var dx = here.X - focus.X;
        var dz = here.Z - focus.Z;
        var planar = MathF.Sqrt(dx * dx + dz * dz);

        _blobShadow.UpdateState(planar, _optionShadowMode);
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
        return ResolveClipForMotion(static e => AttackMotionId(e));
    }

    private AnimationClip? ResolveClipForMotion(Func<ActormotionEntry, int> selector)
    {
        if (_combatAssets is null) return null;

        var registry = CharVisualRegistry.GetOrBuild(_combatAssets);
        if (registry is null) return null;

        var entry = registry.ActorMotion.GetBySkinClass(_combatSkinClass);
        if (entry is null) return null;

        var motId = selector(entry);
        if (motId <= 0) return null;

        return ResolveClipById(registry, motId);
    }

    private AnimationClip? ResolveClipById(CharVisualRegistry registry, int motId)
    {
        if (_clipByMotId.TryGetValue(motId, out var cached)) return cached;

        AnimationClip? clip = null;
        var motPath = registry.ResolveMotPath(motId);
        if (motPath is not null && _combatAssets!.Contains(motPath))
            try
            {
                var data = _combatAssets.GetRaw(motPath);
                if (!data.IsEmpty) clip = AnimationParser.Parse(data);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[VisualActor] .mot load failed '{motPath}': {ex.Message}");
                clip = null;
            }

        _clipByMotId[motId] = clip;
        return clip;
    }

    private static int AttackMotionId(ActormotionEntry entry)
    {
        return 0;
    }

    private void EnsureLocomotionClips()
    {
        if (_locomotionResolved) return;
        _locomotionResolved = true;

        _walkClip = ResolveClipForMotion(static e => e.WalkMotionId);
        _runClip = ResolveClipForMotion(static e => e.RunMotionId);
    }

    private void UpdateLocomotion(bool moving)
    {
        if (_skinnedNode is null || !IsInstanceValid(_skinnedNode)) return;
        if (_isDead) return;

        if (!moving)
        {
            if (_skinnedNode.IsLocomotionPlaying) _skinnedNode.StopLocomotion();
            return;
        }

        EnsureLocomotionClips();

        var clip = _isRunning ? _runClip ?? _walkClip : _walkClip ?? _runClip;
        if (clip is not null) _skinnedNode.SetLocomotionClip(clip);
        else if (_skinnedNode.IsLocomotionPlaying) _skinnedNode.StopLocomotion();
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
        SetCombatClipSource(assets, serverClass, serverClass);
        GD.Print($"[VisualActor] Body avatar attached (class={serverClass}, skinned+idle, body-only — " +
                 $"ActorSpawnedEvent carries no EquipGids, so equip overlay is deferred). " +
                 "spec: skinning.md §8(e) / §10.");
        return true;
    }

    public void PlayDeathMotion()
    {
        if (_isDead) return;

        if (_skinnedNode is null || !IsInstanceValid(_skinnedNode))
        {
            GD.Print($"[VisualActor] PlayDeathMotion: no skinned node attached (actor '{ActorName}') — " +
                     "no death pose applied. spec: 5-10_combat_death.yaml.");
            return;
        }

        _isDead = true;
        _skinnedNode.StopLocomotion();

        var clip = ResolveClipForMotion(static e => e.DeathMotionId);
        if (clip is null)
        {
            GD.Print($"[VisualActor] PlayDeathMotion (actor '{ActorName}'): DeathMotionId (a[4]/col19) did not " +
                     "resolve to a registered .mot — no death clip played (no fabrication). " +
                     "spec: formats/actormotion.md (a[4]=col19 death); 5-10_combat_death.yaml.");
            return;
        }

        _skinnedNode.PlayActionClip(clip);
        GD.Print($"[VisualActor] PlayDeathMotion (actor '{ActorName}'): death clip id_b={clip.IdB} played. " +
                 "spec: formats/actormotion.md (a[4]=col19 death, HIGH); 5-10_combat_death.yaml.");
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
        var moving = false;
        var faceDir = Vector3.Zero;

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

            faceDir = _currPosition - _prevPosition;
            moving = _prevPosition.DistanceTo(_currPosition) > MovingEpsilon;
        }
        else if (_hasTarget)
        {
            var before = GlobalPosition;
            LegacyGlide(delta);
            faceDir = GlobalPosition - before;
            moving = _hasTarget;
        }

        if (_hasSnapshot || _hasTarget)
            ApplyTerrainGroundY();

        if (moving)
            UpdateFacingYaw(faceDir);

        UpdateLocomotion(moving);

        UpdateBlobShadow();
    }

    private void ApplyTerrainGroundY()
    {
        if (_terrainNode is null)
            return;

        var pos = GlobalPosition;
        if (_terrainNode.TryGetGroundHeight(pos.X, -pos.Z, out var groundY))
        {
            pos.Y = groundY;
            GlobalPosition = pos;
        }
    }

    private void UpdateFacingYaw(Vector3 godotDelta)
    {
        var planarSq = godotDelta.X * godotDelta.X + godotDelta.Z * godotDelta.Z;
        if (planarSq < FacingPlanarEpsilonSq)
            return;

        var yaw = MathF.Atan2(godotDelta.X, godotDelta.Z);

        if (_skinnedAvatar is not null && IsInstanceValid(_skinnedAvatar))
            _skinnedAvatar.Rotation = new Vector3(0f, yaw, 0f);
        else
            Rotation = new Vector3(0f, yaw, 0f);
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