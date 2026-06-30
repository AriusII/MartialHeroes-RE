using MartialHeroes.Assets.Parsers.Core.Models;
using MartialHeroes.Assets.Parsers.Mesh.Models;

namespace MartialHeroes.Client.Presentation.World;

public static class SkinningMath
{
    public const float MotFps = 10.0f;
    public const float MotSecondsPerFrame = 1.0f / MotFps;

    public const float WeightSkipThreshold = 0.01f;


    public static Quat Mul(in Quat a, in Quat b)
    {
        var x = a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y;
        var y = a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X;
        var z = a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W;
        var w = a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z;
        return new Quat(x, y, z, w);
    }

    public static Quat Conjugate(in Quat q)
    {
        return new Quat(-q.X, -q.Y, -q.Z, q.W);
    }

    public static Vec3 Rotate(in Quat q, in Vec3 v)
    {
        float x = q.X, y = q.Y, z = q.Z, w = q.W;
        var tx = 2f * (y * v.Z - z * v.Y);
        var ty = 2f * (z * v.X - x * v.Z);
        var tz = 2f * (x * v.Y - y * v.X);
        var rx = v.X + w * tx + (y * tz - z * ty);
        var ry = v.Y + w * ty + (z * tx - x * tz);
        var rz = v.Z + w * tz + (x * ty - y * tx);
        return new Vec3(rx, ry, rz);
    }

    public static Vec3 Add(in Vec3 a, in Vec3 b)
    {
        return new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    }

    public static Vec3 Sub(in Vec3 a, in Vec3 b)
    {
        return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    }

    public static Vec3 Scale(in Vec3 a, float s)
    {
        return new Vec3(a.X * s, a.Y * s, a.Z * s);
    }

    public static float Dot(in Quat a, in Quat b)
    {
        return a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
    }

    public static Quat Normalize(in Quat q)
    {
        var n = MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
        if (n < 1e-12f) return new Quat(0f, 0f, 0f, 1f);
        var inv = 1f / n;
        return new Quat(q.X * inv, q.Y * inv, q.Z * inv, q.W * inv);
    }

    public static Vec3 Lerp(in Vec3 a, in Vec3 b, float t)
    {
        return new Vec3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
    }

    public static Quat Slerp(in Quat a, Quat b, float t)
    {
        var dot = Dot(a, b);
        if (dot < 0f)
        {
            b = new Quat(-b.X, -b.Y, -b.Z, -b.W);
            dot = -dot;
        }

        if (dot > 0.9995f)
        {
            var lerp = new Quat(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t,
                a.Z + (b.Z - a.Z) * t,
                a.W + (b.W - a.W) * t);
            return Normalize(lerp);
        }

        var theta0 = MathF.Acos(Math.Clamp(dot, -1f, 1f));
        var theta = theta0 * t;
        var sinTheta0 = MathF.Sin(theta0);
        var s0 = MathF.Cos(theta) - dot * MathF.Sin(theta) / sinTheta0;
        var s1 = MathF.Sin(theta) / sinTheta0;
        return new Quat(
            a.X * s0 + b.X * s1,
            a.Y * s0 + b.Y * s1,
            a.Z * s0 + b.Z * s1,
            a.W * s0 + b.W * s1);
    }

    public static void ResolveHierarchy(
        Bone[] bones,
        out int[] parentIndex,
        out int[] idToIndex,
        out int baseId,
        out bool[] hasChild)
    {
        var n = bones.Length;
        parentIndex = new int[n];
        idToIndex = new int[256];
        hasChild = new bool[n];
        for (var i = 0; i < 256; i++) idToIndex[i] = -1;

        baseId = n > 0 ? (int)(bones[0].SelfId & 0xFF) : 0;

        for (var i = 0; i < n; i++)
        {
            var sid = (int)(bones[i].SelfId & 0xFF);
            if (idToIndex[sid] < 0) idToIndex[sid] = i;
        }

        for (var i = 0; i < n; i++)
        {
            if (bones[i].IsRoot)
            {
                parentIndex[i] = -1;
                continue;
            }

            var pid = (int)(bones[i].ParentId & 0xFF);
            var pidx = idToIndex[pid];
            parentIndex[i] = pidx >= 0 && pidx != i ? pidx : -1;
        }

        for (var i = 0; i < n; i++)
        {
            var p = parentIndex[i];
            if (p >= 0 && p < n) hasChild[p] = true;
        }
    }

    public static BoneTransform[] AccumulateBindWorld(Bone[] bones, int[] parentIndex)
    {
        var n = bones.Length;
        var world = new BoneTransform[n];

        for (var i = 0; i < n; i++)
        {
            var localT = bones[i].Translation;
            var localQ = bones[i].Rotation;
            var p = parentIndex[i];

            if (p < 0 || p >= i)
            {
                world[i] = new BoneTransform(localT, localQ);
            }
            else
            {
                var pw = world[p];
                var t = Add(Rotate(pw.Quat, localT), pw.Trans);
                var q = Mul(pw.Quat, localQ);
                world[i] = new BoneTransform(t, q);
            }
        }

        return world;
    }

    public static VertexInfluences[] BuildInfluences(
        SknWeight[] weights,
        int vertexCount,
        int[] idToIndex,
        int baseId,
        int boneCount)
    {
        var raw = new List<(int Bone, float W)>[vertexCount];

        foreach (var wr in weights)
        {
            if (wr.Weight < WeightSkipThreshold) continue;

            var vi = (int)wr.VertexIndex;
            if ((uint)vi >= (uint)vertexCount) continue;

            var bid = (int)(wr.BoneIndex & 0xFF);
            var bIdx = bid is >= 0 and < 256 ? idToIndex[bid] : -1;
            if (bIdx < 0)
            {
                var off = (int)wr.BoneIndex - baseId;
                bIdx = off >= 0 && off < boneCount ? off : -1;
            }

            if (bIdx < 0 || bIdx >= boneCount) continue;

            (raw[vi] ??= []).Add((bIdx, wr.Weight));
        }

        var result = new VertexInfluences[vertexCount];
        for (var v = 0; v < vertexCount; v++)
        {
            var list = raw[v];
            var vi = new VertexInfluences();

            if (list is null || list.Count == 0)
            {
                vi.Items = [new Influence { BoneIndex = 0, Weight = 1f }];
                result[v] = vi;
                continue;
            }

            var total = 0f;
            for (var k = 0; k < list.Count; k++) total += list[k].W;
            if (total < 1e-8f)
            {
                vi.Items = [new Influence { BoneIndex = 0, Weight = 1f }];
                result[v] = vi;
                continue;
            }

            var inv = 1f / total;
            var items = new Influence[list.Count];
            for (var k = 0; k < list.Count; k++)
                items[k] = new Influence { BoneIndex = list[k].Bone, Weight = list[k].W * inv };

            vi.Items = items;
            result[v] = vi;
        }

        return result;
    }

    public static void BakeInverseBind(
        VertexInfluences[] perVertex,
        Vec3[] restPos,
        Vec3[] restNrm,
        BoneTransform[] bindWorld)
    {
        for (var v = 0; v < perVertex.Length; v++)
        {
            var p = restPos[v];
            var nrm = v < restNrm.Length ? restNrm[v] : new Vec3(0f, 1f, 0f);

            var items = perVertex[v].Items;
            for (var k = 0; k < items.Length; k++)
            {
                var bw = bindWorld[items[k].BoneIndex];
                var invQ = Conjugate(bw.Quat);
                items[k].LocalPos = Rotate(invQ, Sub(p, bw.Trans));
                items[k].LocalNormal = Rotate(invQ, nrm);
            }
        }
    }


    public static (Vec3 Trans, Quat Rot) SampleTrack(Keyframe[] keys, float t, bool renormalizeAlpha)
    {
        var keyCount = keys.Length;
        if (keyCount == 0) return (new Vec3(0, 0, 0), new Quat(0, 0, 0, 1));
        if (keyCount == 1) return (keys[0].Translation, keys[0].Rotation);

        var n = (int)MathF.Floor(t * MotFps);
        if (n < 0) n = 0;
        if (n > keyCount - 1) n = keyCount - 1;
        var nNext = n < keyCount - 1 ? n + 1 : 0;

        var alpha = t - n * MotSecondsPerFrame;
        if (renormalizeAlpha) alpha *= MotFps;
        alpha = Math.Clamp(alpha, 0f, 1f);

        var trans = Lerp(keys[n].Translation, keys[nNext].Translation, alpha);
        var rot = Slerp(keys[n].Rotation, keys[nNext].Rotation, alpha);
        return (trans, rot);
    }

    public static void ComputeAnimatedWorld(
        Bone[] bones,
        int[] parentIndex,
        AnimationTrack?[] trackByBoneIndex,
        float t,
        bool renormalizeAlpha,
        BoneTransform[] outWorld,
        bool animAsDelta = true,
        bool[]? hasChild = null,
        float[]? nodeScale = null)
    {
        var n = bones.Length;

        for (var i = 0; i < n; i++)
        {
            var localT = bones[i].Translation;
            var localQ = bones[i].Rotation;

            var track = trackByBoneIndex[i];
            if (track is not null && track.Keyframes.Length > 0)
            {
                var (sT, sR) = SampleTrack(track.Keyframes, t, renormalizeAlpha);

                localQ = animAsDelta ? Mul(bones[i].Rotation, sR) : sR;

                var parent = parentIndex[i];
                var hasParent = parent >= 0;
                var hasGrandparent = hasParent && parentIndex[parent] >= 0;
                var boneHasChild = hasChild is not null && hasChild[i];
                var interior = hasParent && hasGrandparent && boneHasChild;
                if (!interior)
                    localT = sT;
            }

            var p = parentIndex[i];
            if (p < 0 || p >= i)
            {
                outWorld[i] = new BoneTransform(localT, localQ);
            }
            else
            {
                var pw = outWorld[p];
                var rotatedLocal = Rotate(pw.Quat, localT);
                var s = nodeScale is not null ? nodeScale[i] : 1.0f;
                if (s != 1.0f) rotatedLocal = Scale(rotatedLocal, s);
                var wt = Add(rotatedLocal, pw.Trans);
                var wq = Mul(pw.Quat, localQ);
                outWorld[i] = new BoneTransform(wt, wq);
            }
        }
    }


    public static float AccumulateWeight(float accumWeight, float newWeight)
    {
        var sum = accumWeight + newWeight;
        if (MathF.Log(sum) < 0.001f) return newWeight / 0.001f;
        return newWeight / sum;
    }

    public static BoneTransform BlendBone(in BoneTransform from, in BoneTransform to, float progress)
    {
        var p = progress < 0f ? 0f : progress > 1f ? 1f : progress;
        var wOut = 2f * (1f - p);
        var wIn = 2f * p;
        if (wOut <= 0f) return to;
        if (wIn <= 0f) return from;
        var f = AccumulateWeight(wOut, wIn);
        var trans = Lerp(from.Trans, to.Trans, f);
        var rot = Slerp(from.Quat, to.Quat, f);
        return new BoneTransform(trans, rot);
    }

    public static void BlendPoses(BoneTransform[] from, BoneTransform[] to, float progress, BoneTransform[] outWorld)
    {
        var n = outWorld.Length;
        if (from.Length < n || to.Length < n) return;
        for (var i = 0; i < n; i++)
            outWorld[i] = BlendBone(from[i], to[i], progress);
    }


    public static (Vec3 Pos, Vec3 Nrm) DeformVertex(VertexInfluences vi, BoneTransform[] world)
    {
        float px = 0f, py = 0f, pz = 0f;
        float nx = 0f, ny = 0f, nz = 0f;

        var items = vi.Items;
        for (var k = 0; k < items.Length; k++)
        {
            var bw = world[items[k].BoneIndex];
            var w = items[k].Weight;

            var placedPos = Add(Rotate(bw.Quat, items[k].LocalPos), bw.Trans);
            var placedNrm = Rotate(bw.Quat, items[k].LocalNormal);

            px += placedPos.X * w;
            py += placedPos.Y * w;
            pz += placedPos.Z * w;
            nx += placedNrm.X * w;
            ny += placedNrm.Y * w;
            nz += placedNrm.Z * w;
        }

        return (new Vec3(px, py, pz), new Vec3(nx, ny, nz));
    }


    public readonly record struct BoneTransform(Vec3 Trans, Quat Quat);


    public struct Influence
    {
        public int BoneIndex;
        public float Weight;
        public Vec3 LocalPos;
        public Vec3 LocalNormal;
    }

    public sealed class VertexInfluences
    {
        public Influence[] Items = [];
    }
}