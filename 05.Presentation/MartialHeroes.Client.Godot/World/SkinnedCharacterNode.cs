
using Godot;
using MartialHeroes.Assets.Parsers.Core.Models;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Client.Presentation.Helpers;
using MartialHeroes.Client.Presentation.World;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class SkinnedCharacterNode : Node3D
{

    public enum VisualState
    {
        Standing = 0,

        VisualState1 = 1,
        VisualState2 = 2,
        VisualState3 = 3,
        VisualState4 = 4,
        VisualState5 = 5
    }

    private const bool RenormalizeAlpha = true;

    public const int DefaultHandBoneId = 0;

    private readonly List<DeformPart> _overlayParts = new();

    private readonly List<WeaponAttachment> _weapons = new();

    private ArrayMesh? _arrayMesh;
    private int _baseId;

    private Bone[] _bones = [];

    private float _clipDuration;

    private int[] _cornerVertex = [];
    private Vec3[] _deformedNrm = [];
    private Vec3[] _deformedPos = [];

    private bool _externalDrive;
    private bool[] _hasChild = [];
    private bool _hasClip;

    private AnimationClip? _idleClip;

    private int[] _idToIndex = [];

    private Material? _material;
    private MeshInstance3D? _meshInstance;
    private float[] _nodeScale = [];

    private AnimationTrack?[] _noTracks = [];
    private Vector3[] _outNrm = [];
    private Vector3[] _outPos = [];
    private int[] _parentIndex = [];
    private SkinningMath.VertexInfluences[] _perVertex = [];
    private bool _ready;
    private float _time;
    private AnimationTrack?[] _trackByBoneIndex = [];
    private Vector2[] _uvs = [];

    private SkinningMath.BoneTransform[] _world = [];

    internal static bool AnimAsDelta { get; set; } = true;

    public bool IsIdlePlaying { get; private set; }

    public void Setup(
        SkinnedMesh mesh,
        Skeleton skeleton,
        AnimationClip? clip,
        ImageTexture? albedo,
        bool externalDrive = false,
        float startPhaseSeconds = 0f)
    {
        _externalDrive = externalDrive;
        _bones = skeleton.Bones;
        var boneCount = _bones.Length;

        SkinningMath.ResolveHierarchy(_bones, out _parentIndex, out var idToIndex, out var baseId, out _hasChild);
        _idToIndex = idToIndex;
        _baseId = baseId;
        var bindWorld = SkinningMath.AccumulateBindWorld(_bones, _parentIndex);

        _nodeScale = new float[boneCount];
        for (var i = 0; i < boneCount; i++) _nodeScale[i] = 1.0f;

        var vertexCount = mesh.Positions.Length;

        _perVertex = SkinningMath.BuildInfluences(mesh.Weights, vertexCount, idToIndex, baseId, boneCount);
        SkinningMath.BakeInverseBind(_perVertex, mesh.Positions, mesh.Normals, bindWorld);

        _idleClip = clip;

        _trackByBoneIndex = new AnimationTrack?[boneCount];
        if (clip is not null && clip.FrameCount > 0)
        {
            var boundTracks = 0;
            var skippedTracks = 0;
            foreach (var tr in clip.Tracks)
            {
                var bid = tr.BoneId & 0xFF;
                var bIdx = bid >= 0 && bid < 256 ? idToIndex[bid] : -1;

                if (bIdx >= 0 && bIdx < boneCount)
                {
                    _trackByBoneIndex[bIdx] = tr;
                    boundTracks++;
                }
                else
                {
                    skippedTracks++;
                }
            }

            if (skippedTracks > 0)
                GD.PrintErr($"[Skinning] '{mesh.Name}': SKIPPED {skippedTracks} clip track(s) whose " +
                            $"bone_id is not a bone of this {boneCount}-bone rig (base_id={baseId}); " +
                            $"bound {boundTracks}. spec: skinning.md §8(e) item 4 — skip, do not clamp.");

            _clipDuration = clip.FrameCount * SkinningMath.MotSecondsPerFrame;
            _hasClip = _clipDuration > 0f;
        }

        var faceCount = (int)mesh.FaceCount;
        var cornerCount = faceCount * 3;
        _cornerVertex = new int[cornerCount];
        _uvs = new Vector2[cornerCount];
        var corners = mesh.Corners;
        for (var f = 0; f < faceCount; f++)
        {
            var cBase = f * 3;
            int[] order = [cBase + 0, cBase + 2, cBase + 1];
            for (var j = 0; j < 3; j++)
            {
                var corner = corners[order[j]];
                var vi = corner.VertexIndex;
                if (vi >= (uint)vertexCount) vi = 0;
                _cornerVertex[cBase + j] = (int)vi;
                _uvs[cBase + j] = new Vector2(corner.UvU, corner.UvV);
            }
        }

        _world = new SkinningMath.BoneTransform[boneCount];
        _noTracks = new AnimationTrack?[boneCount];
        _deformedPos = new Vec3[vertexCount];
        _deformedNrm = new Vec3[vertexCount];
        _outPos = new Vector3[cornerCount];
        _outNrm = new Vector3[cornerCount];

        if (CelShadeMaterialFactory.CelEnabled)
            try
            {
                _material = CelShadeMaterialFactory.Build(albedo);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[Skinning] CelShadeMaterialFactory.Build failed for '{mesh.Name}': {ex.Message} " +
                            "— falling back to StandardMaterial3D.");
                _material = null;
            }

        if (_material is null)
        {
            var stdMat = new StandardMaterial3D
            {
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
            };
            if (albedo is not null)
                stdMat.AlbedoTexture = albedo;
            else
                stdMat.AlbedoColor = new Color(0.85f, 0.75f, 0.65f);
            _material = stdMat;
        }

        _arrayMesh = new ArrayMesh();
        _meshInstance = new MeshInstance3D { Name = "LbsMesh", Mesh = _arrayMesh };
        AddChild(_meshInstance);
        DeformAndUpload(0f, true);
        if (_material is not null)
            _meshInstance.SetSurfaceOverrideMaterial(0, _material);

        if (_hasClip && _clipDuration > 0f && startPhaseSeconds != 0f)
            _time = startPhaseSeconds % _clipDuration;

        var frameCount = clip?.FrameCount ?? 0u;
        GD.Print($"[Skinning] Setup '{mesh.Name}': hasClip={_hasClip} clipFrameCount={frameCount} " +
                 $"clipDuration={_clipDuration:F2}s externalDrive={externalDrive}.");

        _ready = true;

        PlayStandingIdle();
    }

    private AnimationClip? SelectVisualStateClip(VisualState state)
    {
        return state switch
        {
            VisualState.Standing => _idleClip,

            VisualState.VisualState1 => _idleClip,
            VisualState.VisualState2 => _idleClip,
            VisualState.VisualState3 => _idleClip,
            VisualState.VisualState4 => _idleClip,
            VisualState.VisualState5 => _idleClip,

            _ => _idleClip
        };
    }

    public void PlayStandingIdle()
    {
        PlayVisualState(VisualState.Standing);
    }

    public void PlayVisualState(VisualState state)
    {
        var selected = SelectVisualStateClip(state);

        if (selected is null || !_hasClip || _clipDuration <= 0f)
            return;

        IsIdlePlaying = true;
        GD.Print($"[Skinning] Idle playback engaged (state={state}, looping, " +
                 $"duration={_clipDuration:F2}s). spec: skinning.md §10.5 (advance real dt + loop).");
    }

    public Aabb GetMeshAabb()
    {
        return _arrayMesh?.GetAabb() ?? new Aabb();
    }

    public Aabb GetDisplayedFrame0Aabb()
    {
        if (_arrayMesh is null) return new Aabb();
        if (!_hasClip)
            return _arrayMesh.GetAabb();

        DeformAndUpload(0f, false);
        var animated = _arrayMesh.GetAabb();
        DeformAndUpload(0f, true);
        return animated;
    }

    public override void _Process(double delta)
    {
        if (_externalDrive) return;
        Advance((float)delta);
    }

    public void Tick(float dtSeconds)
    {
        if (!_externalDrive) return;
        Advance(dtSeconds);
    }

    private void Advance(float dtSeconds)
    {
        if (!_ready || !_hasClip || _arrayMesh is null) return;

        _time += dtSeconds;
        if (_clipDuration > 0f && _time >= _clipDuration)
            _time %= _clipDuration;

        DeformAndUpload(_time, false);
    }

    private void DeformAndUpload(float t, bool restPose)
    {
        if (_arrayMesh is null || _meshInstance is null) return;

        ComputeWorldPoses(t, restPose);

        for (var v = 0; v < _deformedPos.Length; v++)
            (_deformedPos[v], _deformedNrm[v]) = SkinningMath.DeformVertex(_perVertex[v], _world);

        for (var c = 0; c < _cornerVertex.Length; c++)
        {
            var vi = _cornerVertex[c];
            var p = _deformedPos[vi];
            var n = _deformedNrm[vi];
            var (gx, gy, gz) = WorldCoordinates.SkinToGodot(p.X, p.Y, p.Z);
            var (nx, ny, nz) = WorldCoordinates.SkinToGodot(n.X, n.Y, n.Z);
            _outPos[c] = new Vector3(gx, gy, gz);
            _outNrm[c] = new Vector3(nx, ny, nz).Normalized();
        }

        var arrays = new Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _outPos;
        arrays[(int)Mesh.ArrayType.Normal] = _outNrm;
        arrays[(int)Mesh.ArrayType.TexUV] = _uvs;

        _arrayMesh!.ClearSurfaces();
        _arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _arrayMesh.SurfaceSetMaterial(0, _material);

        for (var pi = 0; pi < _overlayParts.Count; pi++)
        {
            var part = _overlayParts[pi];

            for (var v = 0; v < part.DeformedPos.Length; v++)
                (part.DeformedPos[v], part.DeformedNrm[v]) = SkinningMath.DeformVertex(part.PerVertex[v], _world);

            for (var c = 0; c < part.CornerVertex.Length; c++)
            {
                var vi = part.CornerVertex[c];
                var pp = part.DeformedPos[vi];
                var pn = part.DeformedNrm[vi];
                var (pgx, pgy, pgz) = WorldCoordinates.SkinToGodot(pp.X, pp.Y, pp.Z);
                var (pnx, pny, pnz) = WorldCoordinates.SkinToGodot(pn.X, pn.Y, pn.Z);
                part.OutPos[c] = new Vector3(pgx, pgy, pgz);
                part.OutNrm[c] = new Vector3(pnx, pny, pnz).Normalized();
            }

            var partArrays = new Array();
            partArrays.Resize((int)Mesh.ArrayType.Max);
            partArrays[(int)Mesh.ArrayType.Vertex] = part.OutPos;
            partArrays[(int)Mesh.ArrayType.Normal] = part.OutNrm;
            partArrays[(int)Mesh.ArrayType.TexUV] = part.Uvs;

            _arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, partArrays);
            _arrayMesh.SurfaceSetMaterial(pi + 1, part.Material);
        }

        UpdateWeaponAttachments();
    }


    public void AttachHandWeapon(
        SkinnedMesh weaponMesh,
        ImageTexture? albedo = null,
        int boneId = DefaultHandBoneId,
        float visualScale = 1.0f,
        bool offHand = false)
    {
        var bid = boneId & 0xFF;
        var boneIndex = bid >= 0 && bid < _idToIndex.Length ? _idToIndex[bid] : -1;
        if (boneIndex < 0)
        {
            var off = boneId - _baseId;
            boneIndex = off >= 0 && off < _bones.Length ? off : 0;
        }

        var (inst, _) = SkinnedCharacterBuilder.BuildStaticRigidMesh(weaponMesh, albedo,
            $"Weapon{(offHand ? "Off" : "Main")}_{weaponMesh.Name}");

        AddChild(inst);
        _weapons.Add(new WeaponAttachment(inst, boneIndex, visualScale, offHand));

        UpdateWeaponAttachments();

        GD.Print($"[Skinning] Weapon attached: '{weaponMesh.Name}' boneId={boneId} " +
                 $"(idx={boneIndex}) scale={visualScale:F2} offHand={offHand}. " +
                 "spec: equipment_visuals.md §5 (rigid single-bone follow).");
    }

    public void ClearWeapons()
    {
        foreach (var w in _weapons)
            if (IsInstanceValid(w.Node))
            {
                RemoveChild(w.Node);
                w.Node.QueueFree();
            }

        _weapons.Clear();
    }


    public void AttachDeformPart(SkinnedMesh partMesh, ImageTexture? albedo, string debugLabel)
    {
        if (!_ready)
        {
            GD.PrintErr($"[Skinning] AttachDeformPart '{debugLabel}': node not Setup — part skipped.");
            return;
        }

        var boneCount = _bones.Length;
        var vertexCount = partMesh.Positions.Length;

        var bindWorld = SkinningMath.AccumulateBindWorld(_bones, _parentIndex);
        var perVertex = SkinningMath.BuildInfluences(partMesh.Weights, vertexCount, _idToIndex, _baseId, boneCount);
        SkinningMath.BakeInverseBind(perVertex, partMesh.Positions, partMesh.Normals, bindWorld);

        var faceCount = (int)partMesh.FaceCount;
        var cornerCount = faceCount * 3;
        var cornerVertex = new int[cornerCount];
        var uvs = new Vector2[cornerCount];
        var corners = partMesh.Corners;
        for (var f = 0; f < faceCount; f++)
        {
            var cBase = f * 3;
            int[] order = [cBase + 0, cBase + 2, cBase + 1];
            for (var j = 0; j < 3; j++)
            {
                var corner = corners[order[j]];
                var vi = corner.VertexIndex;
                if (vi >= (uint)vertexCount) vi = 0;
                cornerVertex[cBase + j] = (int)vi;
                uvs[cBase + j] = new Vector2(corner.UvU, corner.UvV);
            }
        }

        Material partMat;
        try
        {
            partMat = CelShadeMaterialFactory.CelEnabled
                ? CelShadeMaterialFactory.Build(albedo)
                : BuildStandardPartMaterial(albedo);
        }
        catch
        {
            partMat = BuildStandardPartMaterial(albedo);
        }

        _overlayParts.Add(new DeformPart(
            perVertex,
            cornerVertex,
            uvs,
            new Vec3[vertexCount],
            new Vec3[vertexCount],
            new Vector3[cornerCount],
            new Vector3[cornerCount],
            partMat));

        DeformAndUpload(_time, !_hasClip);

        GD.Print($"[Skinning] Overlay deform part attached: '{partMesh.Name}' " +
                 $"({vertexCount}v, surface {_overlayParts.Count}) on the shared {boneCount}-bone rig. " +
                 "spec: skinning.md §3.5.1 / §3.6.2.");
    }

    public void ClearOverlayParts()
    {
        _overlayParts.Clear();
        if (_ready && _arrayMesh is not null) DeformAndUpload(_time, !_hasClip);
    }

    private static StandardMaterial3D BuildStandardPartMaterial(ImageTexture? albedo)
    {
        var std = new StandardMaterial3D
        {
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
        };
        if (albedo is not null) std.AlbedoTexture = albedo;
        else std.AlbedoColor = new Color(0.85f, 0.75f, 0.65f);
        return std;
    }

    private void UpdateWeaponAttachments()
    {
        if (_weapons.Count == 0 || _world.Length == 0) return;

        for (var i = 0; i < _weapons.Count; i++)
        {
            var w = _weapons[i];
            if (!IsInstanceValid(w.Node)) continue;
            if ((uint)w.BoneIndex >= (uint)_world.Length) continue;

            var bw = _world[w.BoneIndex];

            var (qx, qy, qz, qw) = WorldCoordinates.SkinQuatToGodot(bw.Quat.X, bw.Quat.Y, bw.Quat.Z, bw.Quat.W);
            var (tx, ty, tz) = WorldCoordinates.SkinToGodot(bw.Trans.X, bw.Trans.Y, bw.Trans.Z);

            var basis = new Basis(new Quaternion(qx, qy, qz, qw).Normalized());
            if (w.VisualScale != 1.0f) basis = basis.Scaled(Vector3.One * w.VisualScale);
            w.Node.Transform = new Transform3D(basis, new Vector3(tx, ty, tz) * w.VisualScale);
        }
    }

    private void ComputeWorldPoses(float t, bool restPose)
    {
        var tracks = restPose ? _noTracks : _trackByBoneIndex;
        SkinningMath.ComputeAnimatedWorld(
            _bones, _parentIndex, tracks, t, RenormalizeAlpha, _world, AnimAsDelta,
            _hasChild, _nodeScale);
    }


    public SkinDiagnostics BuildDiagnostics(SkinnedMesh mesh)
    {
        var d = new SkinDiagnostics();

        ComputeWorldPoses(0f, true);
        var maxDev = 0f;
        for (var v = 0; v < _deformedPos.Length; v++)
        {
            var (p, _) = SkinningMath.DeformVertex(_perVertex[v], _world);
            var r = mesh.Positions[v];
            float dx = p.X - r.X, dy = p.Y - r.Y, dz = p.Z - r.Z;
            var dev = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (dev > maxDev) maxDev = dev;
        }

        d.MaxRestDeviation = maxDev;

        DeformAndUpload(0f, true);
        if (_arrayMesh is not null)
        {
            var aabb = _arrayMesh.GetAabb();
            d.RestAabbPos = aabb.Position;
            d.RestAabbSize = aabb.Size;
            d.AabbFinite = IsFinite(aabb.Position) && IsFinite(aabb.Size);
        }

        if (_hasClip && _clipDuration > 0f && _deformedPos.Length > 0)
        {
            ComputeWorldPoses(0f, false);
            var vc = _deformedPos.Length;
            var p0 = new Vec3[vc];
            for (var v = 0; v < vc; v++)
                (p0[v], _) = SkinningMath.DeformVertex(_perVertex[v], _world);

            var sampleTimes = LivenessSampleTimes(_clipDuration);

            var bestDelta = 0f;
            var bestVi = 0;
            var bestT = 0f;
            Vector3 bestP0 = Vector3.Zero, bestP1 = Vector3.Zero;
            foreach (var t in sampleTimes)
            {
                ComputeWorldPoses(t, false);
                for (var v = 0; v < vc; v++)
                {
                    var (pt, _) = SkinningMath.DeformVertex(_perVertex[v], _world);
                    float dx = pt.X - p0[v].X, dy = pt.Y - p0[v].Y, dz = pt.Z - p0[v].Z;
                    var dev = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (dev > bestDelta)
                    {
                        bestDelta = dev;
                        bestVi = v;
                        bestT = t;
                        bestP0 = new Vector3(p0[v].X, p0[v].Y, p0[v].Z);
                        bestP1 = new Vector3(pt.X, pt.Y, pt.Z);
                    }
                }
            }

            d.LivenessVertex = bestVi;
            d.LivenessT0 = 0f;
            d.LivenessT1 = bestT;
            d.LivenessP0 = bestP0;
            d.LivenessP1 = bestP1;
            d.LivenessDelta = bestDelta;
        }

        DeformAndUpload(0f, true);
        _time = 0f;
        return d;
    }

    private static float[] LivenessSampleTimes(float clipDuration)
    {
        var last = MathF.Max(clipDuration - 0.01f, 0f);
        return
        [
            clipDuration * 0.25f,
            clipDuration * 0.5f,
            clipDuration * 0.75f,
            last
        ];
    }

    private static bool IsFinite(Vector3 v)
    {
        return !(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z)
                 || float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z));
    }

    private readonly record struct WeaponAttachment(
        MeshInstance3D Node,
        int BoneIndex,
        float VisualScale,
        bool OffHand);

    private sealed record DeformPart(
        SkinningMath.VertexInfluences[] PerVertex,
        int[] CornerVertex,
        Vector2[] Uvs,
        Vec3[] DeformedPos,
        Vec3[] DeformedNrm,
        Vector3[] OutPos,
        Vector3[] OutNrm,
        Material Material);

    public sealed class SkinDiagnostics
    {
        public bool AabbFinite;
        public float LivenessDelta;
        public Vector3 LivenessP0;
        public Vector3 LivenessP1;
        public float LivenessT0;
        public float LivenessT1;
        public int LivenessVertex = -1;
        public float MaxRestDeviation;
        public Vector3 RestAabbPos;
        public Vector3 RestAabbSize;
    }
}