using Godot;
using MartialHeroes.Assets.Parsers.Core.Models;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Client.Presentation.Helpers;
using MartialHeroes.Client.Presentation.World;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Explorer.Viewer;

public sealed partial class ViewerSkinnedNode : Node3D
{
    public const int DefaultHandBoneId = 0;

    private ArrayMesh? _arrayMesh;
    private int _baseId;
    private Bone[] _bones = [];
    private AnimationClip? _clip;
    private float _clipDuration;

    private int[] _cornerVertex = [];
    private Vec3[] _deformedNrm = [];
    private Vec3[] _deformedPos = [];
    private bool[] _hasChild = [];
    private bool _hasClip;
    private int[] _idToIndex = [];
    private bool _loop = true;
    private Material? _material;
    private MeshInstance3D? _meshInstance;
    private float[] _nodeScale = [];

    private AnimationTrack?[] _noTracks = [];
    private Vector3[] _outNrm = [];
    private Vector3[] _outPos = [];
    private int[] _parentIndex = [];

    private SkinningMath.VertexInfluences[] _perVertex = [];

    private bool _ready;
    private StandardMaterial3D? _skelMat;
    private ImmediateMesh? _skelMesh;

    private MeshInstance3D? _skelOverlay;
    private float _speed = 1f;
    private ImageTexture? _storedAlbedo;
    private Array _surfaceArrays = [];

    private float _time;
    private AnimationTrack?[] _trackByBoneIndex = [];
    private Vector2[] _uvs = [];
    private SkinningMath.BoneTransform[] _world = [];

    public static bool AnimAsDelta { get; set; }

    public int FrameCount { get; private set; }

    public int BoneCount => _bones.Length;

    public int CurrentFrame => FrameCount <= 0
        ? 0
        : Math.Clamp((int)MathF.Round(_time / SkinningMath.MotSecondsPerFrame), 0, FrameCount - 1);

    public int TrackCount
    {
        get
        {
            var count = 0;
            foreach (var t in _trackByBoneIndex)
                if (t is not null)
                    count++;
            return count;
        }
    }

    public int TotalKeyframeCount
    {
        get
        {
            if (_clip is null) return 0;
            var total = 0;
            foreach (var tr in _clip.Tracks) total += tr.Keyframes.Length;
            return total;
        }
    }

    public int[] AnimatedBoneIds
    {
        get
        {
            if (_clip is null) return [];
            var seen = new HashSet<int>(_clip.Tracks.Length);
            foreach (var tr in _clip.Tracks) seen.Add(tr.BoneId);
            return [.. seen];
        }
    }

    public bool IsPlaying { get; private set; }

    public void Play()
    {
        if (!_ready || !_hasClip) return;
        IsPlaying = true;
    }

    public void Pause()
    {
        IsPlaying = false;
    }

    public void Stop()
    {
        IsPlaying = false;
        _time = 0f;
        DeformAndUpload(0f, false);
    }

    public void SeekFrame(int frame)
    {
        IsPlaying = false;
        var maxFrame = Math.Max(FrameCount - 1, 0);
        _time = Math.Clamp(frame, 0, maxFrame) * SkinningMath.MotSecondsPerFrame;
        DeformAndUpload(_time, false);
    }

    public void SetSpeed(float speed)
    {
        _speed = speed;
    }

    public void SetLoop(bool loop)
    {
        _loop = loop;
    }

    public void SetAlbedoEnabled(bool on)
    {
        if (_material is StandardMaterial3D std)
            std.AlbedoTexture = on ? _storedAlbedo : null;
    }

    public void SetSkeletonVisible(bool on)
    {
        if (_skelOverlay is null) return;
        _skelOverlay.Visible = on;
        if (on) RebuildSkeletonOverlay();
    }

    public void Setup(
        SkinnedMesh mesh,
        Skeleton skeleton,
        AnimationClip? clip,
        ImageTexture? albedo)
    {
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

        _storedAlbedo = albedo;
        _clip = clip;
        _trackByBoneIndex = BindClipTracks(clip, idToIndex, boneCount, baseId);
        if (clip is not null && clip.FrameCount > 0)
        {
            FrameCount = (int)clip.FrameCount;
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

        _arrayMesh = new ArrayMesh();
        _surfaceArrays = new Array();
        _surfaceArrays.Resize((int)Mesh.ArrayType.Max);
        _meshInstance = new MeshInstance3D { Name = "LbsMesh", Mesh = _arrayMesh };
        AddChild(_meshInstance);

        _skelMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.1f, 1f, 0.2f),
            VertexColorUseAsAlbedo = false
        };
        _skelMesh = new ImmediateMesh();
        _skelOverlay = new MeshInstance3D { Name = "SkeletonOverlay", Mesh = _skelMesh, Visible = false };
        AddChild(_skelOverlay);

        DeformAndUpload(0f, true);
        _meshInstance.SetSurfaceOverrideMaterial(0, _material);

        _ready = true;
    }

    public void PlayClip(AnimationClip? clip)
    {
        if (!_ready) return;

        if (clip is null || clip.FrameCount == 0)
        {
            RevertToRest();
            return;
        }

        var bound = BindClipTracks(clip, _idToIndex, _bones.Length, _baseId);
        var anyBound = false;
        foreach (var t in bound)
            if (t is not null)
            {
                anyBound = true;
                break;
            }

        if (!anyBound)
        {
            GD.Print($"[Viewer] Clip '{clip.Name}' bound 0 tracks to this {_bones.Length}-bone rig " +
                     "— reverting to rest.");
            RevertToRest();
            return;
        }

        _clip = clip;
        _trackByBoneIndex = bound;
        FrameCount = (int)clip.FrameCount;
        _clipDuration = clip.FrameCount * SkinningMath.MotSecondsPerFrame;
        _hasClip = _clipDuration > 0f;
        _time = 0f;
        IsPlaying = _hasClip;
    }

    private void RevertToRest()
    {
        _clip = null;
        _trackByBoneIndex = _noTracks;
        FrameCount = 0;
        _clipDuration = 0f;
        _hasClip = false;
        IsPlaying = false;
        _time = 0f;
        DeformAndUpload(0f, true);
    }

    public int ResolveHandBoneIndex(int boneId = DefaultHandBoneId)
    {
        if (!_ready || _bones.Length == 0) return -1;
        var bid = boneId & 0xFF;
        var idx = bid < _idToIndex.Length ? _idToIndex[bid] : -1;
        if (idx < 0)
        {
            var off = boneId - _baseId;
            idx = off >= 0 && off < _bones.Length ? off : -1;
        }

        return idx >= 0 && idx < _bones.Length ? idx : -1;
    }

    public bool TryGetBoneGodotTransform(int boneIndex, out Transform3D xform)
    {
        xform = Transform3D.Identity;
        if (!_ready || boneIndex < 0 || boneIndex >= _world.Length) return false;

        var bw = _world[boneIndex];
        var (gx, gy, gz) = WorldCoordinates.SkinToGodot(bw.Trans.X, bw.Trans.Y, bw.Trans.Z);
        var (qx, qy, qz, qw) = WorldCoordinates.SkinQuatToGodot(bw.Quat.X, bw.Quat.Y, bw.Quat.Z, bw.Quat.W);
        var rot = new Quaternion(qx, qy, qz, qw).Normalized();
        xform = new Transform3D(new Basis(rot), new Vector3(gx, gy, gz));
        return true;
    }

    public Aabb GetMeshAabb()
    {
        return _arrayMesh?.GetAabb() ?? new Aabb();
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

        return d;
    }

    private static bool IsFinite(Vector3 v)
    {
        return !(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z)
                 || float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z));
    }

    public override void _Process(double delta)
    {
        if (!_ready || _arrayMesh is null || !_hasClip || !IsPlaying) return;

        _time += (float)delta * _speed;
        if (_clipDuration > 0f && _time >= _clipDuration)
        {
            if (_loop)
            {
                _time %= _clipDuration;
            }
            else
            {
                _time = Math.Max(FrameCount - 1, 0) * SkinningMath.MotSecondsPerFrame;
                IsPlaying = false;
            }
        }

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

        _surfaceArrays[(int)Mesh.ArrayType.Vertex] = _outPos;
        _surfaceArrays[(int)Mesh.ArrayType.Normal] = _outNrm;
        _surfaceArrays[(int)Mesh.ArrayType.TexUV] = _uvs;

        _arrayMesh.ClearSurfaces();
        _arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, _surfaceArrays);
        _arrayMesh.SurfaceSetMaterial(0, _material);

        if (_skelOverlay is not null && _skelOverlay.Visible)
            RebuildSkeletonOverlay();
    }

    private void RebuildSkeletonOverlay()
    {
        if (_skelMesh is null || _skelMat is null || _world.Length == 0) return;

        _skelMesh.ClearSurfaces();

        var lineCount = 0;
        for (var i = 0; i < _world.Length; i++)
        {
            var p = _parentIndex[i];
            if (p >= 0 && p < _world.Length) lineCount++;
        }

        if (lineCount == 0) return;

        _skelMesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _skelMat);
        for (var i = 0; i < _world.Length; i++)
        {
            var p = _parentIndex[i];
            if (p < 0 || p >= _world.Length) continue;

            var c = _world[i].Trans;
            var pr = _world[p].Trans;
            var (cx, cy, cz) = WorldCoordinates.SkinToGodot(c.X, c.Y, c.Z);
            var (px, py, pz) = WorldCoordinates.SkinToGodot(pr.X, pr.Y, pr.Z);
            _skelMesh.SurfaceAddVertex(new Vector3(cx, cy, cz));
            _skelMesh.SurfaceAddVertex(new Vector3(px, py, pz));
        }

        _skelMesh.SurfaceEnd();
    }

    private void ComputeWorldPoses(float t, bool restPose)
    {
        var tracks = restPose ? _noTracks : _trackByBoneIndex;
        SkinningMath.ComputeAnimatedWorld(
            _bones, _parentIndex, tracks, t, true, _world, AnimAsDelta, _hasChild, _nodeScale);
    }

    private AnimationTrack?[] BindClipTracks(
        AnimationClip? clip,
        int[] idToIndex,
        int boneCount,
        int baseId)
    {
        var bound = new AnimationTrack?[boneCount];
        if (clip is null || clip.FrameCount == 0) return bound;

        foreach (var tr in clip.Tracks)
        {
            var bid = tr.BoneId & 0xFF;
            var bIdx = bid is >= 0 and < 256 ? idToIndex[bid] : -1;
            if (bIdx >= 0 && bIdx < boneCount)
                bound[bIdx] = tr;
        }

        return bound;
    }

    public sealed class SkinDiagnostics
    {
        public bool AabbFinite;
        public float MaxRestDeviation;
        public Vector3 RestAabbPos;
        public Vector3 RestAabbSize;
    }
}

public sealed partial class RigidBoneAttachment : Node3D
{
    private int _boneIndex = -1;
    private ViewerSkinnedNode? _source;

    public void Bind(ViewerSkinnedNode source, int boneIndex)
    {
        _source = source;
        _boneIndex = boneIndex;
        UpdateFromBone();
    }

    public override void _Process(double delta)
    {
        UpdateFromBone();
    }

    private void UpdateFromBone()
    {
        if (_source is null || _boneIndex < 0) return;
        if (_source.TryGetBoneGodotTransform(_boneIndex, out var xform))
            Transform = xform;
    }
}