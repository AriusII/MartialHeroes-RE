// World/SkinningMath.cs
//
// Engine-free (NO `using Godot;`) implementation of the legacy Martial Heroes CPU
// linear-blend-skinning (LBS) pipeline, exactly as recovered in the clean-room spec.
//
// This is the math companion the Godot glue (SkinnedCharacterBuilder / SkinnedCharacterNode)
// drives. It operates only on the neutral parser types (Vec3, Quat, Bone, AnimationTrack,
// Keyframe) and plain float arrays, so the whole skinning convention can be reasoned about
// (and, if a layer-05 test project is ever added, unit-tested) without the Godot runtime.
//
// EVERYTHING here runs in the engine's NATIVE left-handed space. No axis negation or
// mirroring happens inside the skinning math — the single project handedness conversion
// (world Z-negate) is applied by the caller to the FINAL deformed position/normal only.
// spec: Docs/RE/specs/skinning.md §7 — "No axis negation or mirroring happens inside the
//       skinning math … Bone space and rest-mesh space are the same native left-handed space —
//       that is precisely why the inverse-bind and the forward transform cancel."
//
// The one load-bearing rule (spec §0):
//   vertexWorld(v) = Σ_i  w_i · ( boneWorldQuat_i ⊗ (localPos_i · scale) + boneWorldTrans_i )
// where localPos_i is baked once at load by the inverse-bind:
//   localPos_i = bindWorldQuat_i⁻¹ ⊗ ( restModelPos(v) − bindWorldTrans_i )
// and boneWorld*_i is rebuilt each frame from the animated hierarchy. At rest the two cancel
// exactly to the identity, reproducing the rest mesh — the property that stops the explosion.
//
// spec: Docs/RE/specs/skinning.md §0, §3, §4, §5, §6, §7.
// spec: Docs/RE/formats/mesh.md §.bnd bone array (parent-relative locals, XYZW quaternion).
// spec: Docs/RE/formats/animation.md §Track array, §Timing (10 fps, raw-seconds alpha).

using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// Pure, engine-free quaternion/vector skinning math reproducing the legacy CPU LBS path.
/// All vectors and quaternions are in the native left-handed render space; the caller applies
/// the single handedness conversion to the final output.
///
/// spec: Docs/RE/specs/skinning.md (linear-blend skinning, inverse-bind bake, pose composition,
///       quaternion / handedness conventions).
/// </summary>
public static class SkinningMath
{
    // Fixed .mot frame rate — clip duration (s) = frame_count × 0.1.
    // spec: Docs/RE/formats/animation.md §Timing — "Fixed frame rate: 10 fps." CONFIRMED.
    public const float MotFps = 10.0f;
    public const float MotSecondsPerFrame = 1.0f / MotFps;

    // Records with weight below this threshold are dropped at load.
    // spec: Docs/RE/formats/mesh.md §Weight record — "records where weight < 0.01 are skipped";
    //       SAMPLE-VERIFIED min observed weight 0.010 (kept).
    public const float WeightSkipThreshold = 0.01f;

    // =========================================================================
    // Quaternion / vector primitives (XYZW, Hamilton, active rotation)
    // =========================================================================
    //
    // spec: Docs/RE/specs/skinning.md §7 — XYZW order (scalar W last), Hamilton product,
    //       active rotation v' = q ⊗ v ⊗ q⁻¹, inverse = conjugate (−x,−y,−z,w).

    /// <summary>Hamilton product a ⊗ b (XYZW order). spec: skinning.md §7.</summary>
    public static Quat Mul(in Quat a, in Quat b)
    {
        // (a.w + a.x i + a.y j + a.z k)(b.w + b.x i + b.y j + b.z k), XYZW storage.
        float x = a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y;
        float y = a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X;
        float z = a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W;
        float w = a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z;
        return new Quat(x, y, z, w);
    }

    /// <summary>Unit-quaternion conjugate / inverse (−x,−y,−z,w). spec: skinning.md §4, §7.</summary>
    public static Quat Conjugate(in Quat q) => new(-q.X, -q.Y, -q.Z, q.W);

    /// <summary>
    /// Active rotation of a vector by a unit quaternion: v' = q ⊗ v ⊗ q⁻¹.
    /// spec: Docs/RE/specs/skinning.md §7 — "active rotation v' = q ⊗ v ⊗ q⁻¹ (unit-quat form)".
    /// </summary>
    public static Vec3 Rotate(in Quat q, in Vec3 v)
    {
        // Standard expansion of q ⊗ (0,v) ⊗ q⁻¹ for a unit quaternion.
        float x = q.X, y = q.Y, z = q.Z, w = q.W;
        // t = 2 * cross(q.xyz, v)
        float tx = 2f * (y * v.Z - z * v.Y);
        float ty = 2f * (z * v.X - x * v.Z);
        float tz = 2f * (x * v.Y - y * v.X);
        // v' = v + w*t + cross(q.xyz, t)
        float rx = v.X + w * tx + (y * tz - z * ty);
        float ry = v.Y + w * ty + (z * tx - x * tz);
        float rz = v.Z + w * tz + (x * ty - y * tx);
        return new Vec3(rx, ry, rz);
    }

    public static Vec3 Add(in Vec3 a, in Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 Sub(in Vec3 a, in Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 Scale(in Vec3 a, float s) => new(a.X * s, a.Y * s, a.Z * s);

    public static float Dot(in Quat a, in Quat b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

    public static Quat Normalize(in Quat q)
    {
        float n = MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
        if (n < 1e-12f) return new Quat(0f, 0f, 0f, 1f);
        float inv = 1f / n;
        return new Quat(q.X * inv, q.Y * inv, q.Z * inv, q.W * inv);
    }

    public static Vec3 Lerp(in Vec3 a, in Vec3 b, float t)
        => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    /// <summary>
    /// Shortest-arc SLERP between two quaternions, with the sign flip and the degenerate
    /// fallbacks described by the spec.
    /// spec: Docs/RE/specs/skinning.md §6.1 / formats/animation.md §Rotation interpolation —
    ///       "if dot(a,b) &lt; 0 negate b; near-identical → normalized LERP; antipodal → perpendicular path."
    /// </summary>
    public static Quat Slerp(in Quat a, Quat b, float t)
    {
        float dot = Dot(a, b);
        if (dot < 0f)
        {
            // Shortest arc: negate b and the dot.
            b = new Quat(-b.X, -b.Y, -b.Z, -b.W);
            dot = -dot;
        }

        // Near-identical quaternions: fall back to normalized LERP (avoids divide-by-~0).
        if (dot > 0.9995f)
        {
            var lerp = new Quat(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t,
                a.Z + (b.Z - a.Z) * t,
                a.W + (b.W - a.W) * t);
            return Normalize(lerp);
        }

        float theta0 = MathF.Acos(Math.Clamp(dot, -1f, 1f));
        float theta = theta0 * t;
        float sinTheta0 = MathF.Sin(theta0);
        float s0 = MathF.Cos(theta) - dot * MathF.Sin(theta) / sinTheta0;
        float s1 = MathF.Sin(theta) / sinTheta0;
        return new Quat(
            a.X * s0 + b.X * s1,
            a.Y * s0 + b.Y * s1,
            a.Z * s0 + b.Z * s1,
            a.W * s0 + b.W * s1);
    }

    // =========================================================================
    // Bind pose — parent-relative .bnd locals → bind WORLD transforms (accumulated at load)
    // =========================================================================

    /// <summary>A bone's world-space transform: a translation plus a rotation quaternion.</summary>
    public readonly record struct BoneTransform(Vec3 Trans, Quat Quat);

    /// <summary>
    /// Resolves the per-bone parent index (array position) from the parent_id linkage, and the
    /// base bone ID. Bones are addressed by <c>id − base_id</c>, not array position, so an
    /// id→index map is built and the root is the bone with <c>self_id == parent_id == 0</c>.
    ///
    /// spec: Docs/RE/specs/skinning.md §3.2 — "Bone lookup is by ID offset, bone_array[id − base_id]
    ///       … An unmatched parent_id is a fatal error in the legacy client." (We degrade to root.)
    /// spec: Docs/RE/formats/mesh.md §Root bone sentinel.
    /// </summary>
    /// <param name="bones">Bones in on-disk order.</param>
    /// <param name="parentIndex">Out: per-bone parent ARRAY index (-1 for root / unresolved).</param>
    /// <param name="idToIndex">Out: 256-entry map boneId(low byte) → array index (-1 if absent).</param>
    /// <param name="baseId">Out: the first bone's self_id low byte (the addressing base).</param>
    public static void ResolveHierarchy(
        Bone[] bones,
        out int[] parentIndex,
        out int[] idToIndex,
        out int baseId)
    {
        int n = bones.Length;
        parentIndex = new int[n];
        idToIndex = new int[256];
        for (int i = 0; i < 256; i++) idToIndex[i] = -1;

        baseId = n > 0 ? (int)(bones[0].SelfId & 0xFF) : 0;

        // Map self_id (low byte) → array index. First occurrence wins (ids are unique in practice).
        for (int i = 0; i < n; i++)
        {
            int sid = (int)(bones[i].SelfId & 0xFF);
            if (idToIndex[sid] < 0) idToIndex[sid] = i;
        }

        for (int i = 0; i < n; i++)
        {
            if (bones[i].IsRoot)
            {
                parentIndex[i] = -1;
                continue;
            }

            int pid = (int)(bones[i].ParentId & 0xFF);
            int pidx = idToIndex[pid];
            // Unmatched / self-referential parent → treat as root (safe degradation, no explosion).
            parentIndex[i] = (pidx >= 0 && pidx != i) ? pidx : -1;
        }
    }

    /// <summary>
    /// Accumulates each bone's bind WORLD transform from the parent-relative .bnd locals,
    /// walking parent→child (the array is parent-before-child by the on-disk format).
    ///
    /// spec: Docs/RE/specs/skinning.md §3.1 —
    ///   root:     worldTrans = localTrans;                       worldQuat = localQuat
    ///   non-root: worldTrans = parentWorldQuat ⊗ localTrans + parentWorldTrans
    ///             worldQuat  = parentWorldQuat ⊗ localQuat       (parent on the LEFT)
    /// </summary>
    public static BoneTransform[] AccumulateBindWorld(Bone[] bones, int[] parentIndex)
    {
        int n = bones.Length;
        var world = new BoneTransform[n];

        for (int i = 0; i < n; i++)
        {
            Vec3 localT = bones[i].Translation;
            Quat localQ = bones[i].Rotation;
            int p = parentIndex[i];

            if (p < 0 || p >= i)
            {
                // Root, or parent not yet processed (degenerate ordering) → use local as world.
                world[i] = new BoneTransform(localT, localQ);
            }
            else
            {
                BoneTransform pw = world[p];
                Vec3 t = Add(Rotate(pw.Quat, localT), pw.Trans);
                Quat q = Mul(pw.Quat, localQ);
                world[i] = new BoneTransform(t, q);
            }
        }

        return world;
    }

    // =========================================================================
    // Influences — expand .skn weights into per-vertex normalized influence records
    // =========================================================================

    /// <summary>
    /// One runtime influence: which bone (by array index), the baked bone-local rest position /
    /// normal (filled by <see cref="BakeInverseBind"/>), and the normalized weight.
    /// spec: Docs/RE/specs/skinning.md §2.2 / §4.
    /// </summary>
    public struct Influence
    {
        public int BoneIndex; // array index into the bone arrays (resolved id − base_id)
        public float Weight; // normalized so per-vertex influences sum to 1.0
        public Vec3 LocalPos; // bindWorldQuat⁻¹ ⊗ (restModelPos − bindWorldTrans)
        public Vec3 LocalNormal; // bindWorldQuat⁻¹ ⊗ restModelNormal
    }

    /// <summary>All influences for one render vertex.</summary>
    public sealed class VertexInfluences
    {
        public Influence[] Items = [];
    }

    /// <summary>
    /// Groups .skn weight records by vertex, resolves each bone_index by ID (<c>id − base_id</c>),
    /// drops weights below the skip threshold, and normalizes per-vertex weights to sum 1.0.
    /// A vertex with zero usable weight is bound entirely to the root bone (index 0) — a safe
    /// fallback; the legacy client asserts here, but we must not crash the renderer.
    ///
    /// spec: Docs/RE/specs/skinning.md §5.1 / §5.2 — drop weight &lt; 0.01, normalize to sum 1.0.
    /// spec: Docs/RE/formats/mesh.md §Bone addressing — bone_index is a bone ID resolved by id − base_id.
    /// </summary>
    public static VertexInfluences[] BuildInfluences(
        SknWeight[] weights,
        int vertexCount,
        int[] idToIndex,
        int baseId,
        int boneCount)
    {
        // Collect raw (boneIndex, weight) per vertex.
        var raw = new List<(int Bone, float W)>[vertexCount];

        foreach (SknWeight wr in weights)
        {
            if (wr.Weight < WeightSkipThreshold) continue;

            int vi = (int)wr.VertexIndex;
            if ((uint)vi >= (uint)vertexCount) continue;

            // bone_index is a bone ID; resolve to array index by id − base_id, then validate
            // against the id→index map (handles base_id != 0 and non-sequential ids).
            int bid = (int)(wr.BoneIndex & 0xFF);
            int bIdx = (bid >= 0 && bid < 256) ? idToIndex[bid] : -1;
            if (bIdx < 0)
            {
                // Fall back to plain offset id − base_id if the map missed (defensive).
                int off = (int)wr.BoneIndex - baseId;
                bIdx = (off >= 0 && off < boneCount) ? off : -1;
            }

            if (bIdx < 0 || bIdx >= boneCount) continue;

            (raw[vi] ??= new List<(int, float)>()).Add((bIdx, wr.Weight));
        }

        var result = new VertexInfluences[vertexCount];
        for (int v = 0; v < vertexCount; v++)
        {
            var list = raw[v];
            var vi = new VertexInfluences();

            if (list is null || list.Count == 0)
            {
                vi.Items = [new Influence { BoneIndex = 0, Weight = 1f }];
                result[v] = vi;
                continue;
            }

            float total = 0f;
            for (int k = 0; k < list.Count; k++) total += list[k].W;
            if (total < 1e-8f)
            {
                vi.Items = [new Influence { BoneIndex = 0, Weight = 1f }];
                result[v] = vi;
                continue;
            }

            float inv = 1f / total;
            var items = new Influence[list.Count];
            for (int k = 0; k < list.Count; k++)
                items[k] = new Influence { BoneIndex = list[k].Bone, Weight = list[k].W * inv };

            vi.Items = items;
            result[v] = vi;
        }

        return result;
    }

    /// <summary>
    /// Bakes the inverse-bind into every influence's <see cref="Influence.LocalPos"/> /
    /// <see cref="Influence.LocalNormal"/>, using each influence's bone bind WORLD transform and
    /// the vertex's model-space rest position / normal.
    ///
    /// spec: Docs/RE/specs/skinning.md §4 —
    ///   localPos    = bindWorldQuat⁻¹ ⊗ (restModelPos − bindWorldTrans)   (subtract then rotate)
    ///   localNormal = bindWorldQuat⁻¹ ⊗  restModelNormal                  (rotation only)
    /// </summary>
    public static void BakeInverseBind(
        VertexInfluences[] perVertex,
        Vec3[] restPos,
        Vec3[] restNrm,
        BoneTransform[] bindWorld)
    {
        for (int v = 0; v < perVertex.Length; v++)
        {
            Vec3 p = restPos[v];
            Vec3 nrm = v < restNrm.Length ? restNrm[v] : new Vec3(0f, 1f, 0f);

            Influence[] items = perVertex[v].Items;
            for (int k = 0; k < items.Length; k++)
            {
                BoneTransform bw = bindWorld[items[k].BoneIndex];
                Quat invQ = Conjugate(bw.Quat);
                items[k].LocalPos = Rotate(invQ, Sub(p, bw.Trans));
                items[k].LocalNormal = Rotate(invQ, nrm);
            }
        }
    }

    // =========================================================================
    // .mot sampling + animated pose composition (per frame)
    // =========================================================================

    /// <summary>
    /// Samples one track at clip time <paramref name="t"/> (seconds) into a local replacement
    /// (translation, rotation) pose.
    ///
    /// spec: Docs/RE/specs/skinning.md §6.1 / formats/animation.md §Timing —
    ///   n = floor(t·10); nNext = min(n+1, key_count−1); alpha = t − n/10  (raw seconds in [0,0.1]).
    ///   trans = lerp(key[n].t, key[nNext].t, alpha); rot = shortest-arc slerp(... , alpha).
    /// </summary>
    /// <param name="renormalizeAlpha">
    ///   When true, alpha is divided by the frame period (→ [0,1]) for smooth playback; when false
    ///   the raw-seconds alpha is used for bit-faithful (snappy) legacy playback. spec §8(c).
    /// </param>
    public static (Vec3 Trans, Quat Rot) SampleTrack(Keyframe[] keys, float t, bool renormalizeAlpha)
    {
        int keyCount = keys.Length;
        if (keyCount == 0) return (new Vec3(0, 0, 0), new Quat(0, 0, 0, 1));
        if (keyCount == 1) return (keys[0].Translation, keys[0].Rotation);

        int n = (int)MathF.Floor(t * MotFps);
        if (n < 0) n = 0;
        if (n > keyCount - 1) n = keyCount - 1;
        int nNext = n < keyCount - 1 ? n + 1 : n;

        float alpha = t - n * MotSecondsPerFrame; // raw seconds in [0, 0.1]
        if (renormalizeAlpha) alpha *= MotFps; // → [0,1]
        alpha = Math.Clamp(alpha, 0f, 1f);

        Vec3 trans = Lerp(keys[n].Translation, keys[nNext].Translation, alpha);
        Quat rot = Slerp(keys[n].Rotation, keys[nNext].Rotation, alpha);
        return (trans, rot);
    }

    /// <summary>
    /// Builds the per-bone committed LOCAL animated pose for clip time <paramref name="t"/>, then
    /// walks the hierarchy to produce each bone's animated WORLD transform.
    ///
    /// Composition (spec §6.4 / §6.6):
    ///   - A bone with NO track keeps its bind local pose (so unanimated bones reproduce the bind
    ///     world pose — no explosion at rest).
    ///   - A bone WITH a track takes the sampled rotation; child bones keep their bind-pose local
    ///     translation (rotate-only), only the root may take the sampled translation.
    ///   - World walk: worldQuat  = parentWorldQuat ⊗ localQuat   (parent on the LEFT)
    ///                 worldTrans = parentWorldQuat ⊗ localTrans + parentWorldTrans
    ///
    /// NOTE on the §6.6 "parentWorld ⊗ bindLocal ⊗ animLocal" form: there, animLocal is the pure
    /// animation delta layered on top of bindLocal. Here we instead commit the FULL local pose
    /// (bind local for untracked/translation, sampled rotation for tracked bones) and compose
    /// parentWorld ⊗ localFull — which is the equivalent of §6.4 ("keyframes are local replacement
    /// poses, not additive deltas") and reproduces the bind world pose exactly at rest.
    /// spec: Docs/RE/specs/skinning.md §6.3, §6.4, §6.6.
    /// </summary>
    /// <param name="bones">Bones in on-disk order (for bind local trans/rot).</param>
    /// <param name="parentIndex">Per-bone parent array index (-1 root).</param>
    /// <param name="trackByBoneIndex">Per-bone-index track, or null if the bone is unanimated.</param>
    /// <param name="t">Clip time in seconds (already wrapped into [0, duration)).</param>
    /// <param name="renormalizeAlpha">Smooth vs. faithful interpolation alpha (spec §8(c)).</param>
    /// <param name="outWorld">Reused output buffer (length == bones.Length).</param>
    public static void ComputeAnimatedWorld(
        Bone[] bones,
        int[] parentIndex,
        AnimationTrack?[] trackByBoneIndex,
        float t,
        bool renormalizeAlpha,
        BoneTransform[] outWorld,
        bool animAsDelta = true)
    {
        int n = bones.Length;

        for (int i = 0; i < n; i++)
        {
            Vec3 localT = bones[i].Translation; // bind-pose local translation (default / child rotate-only)
            Quat localQ = bones[i].Rotation; // bind-pose local rotation (default for untracked)

            AnimationTrack? track = trackByBoneIndex[i];
            if (track is not null && track.Keyframes.Length > 0)
            {
                (Vec3 sT, Quat sR) = SampleTrack(track.Keyframes, t, renormalizeAlpha);

                // Rotation composition for a TRACKED bone.
                //   animAsDelta == true  (spec §6.5/§6.6): the world rotation is
                //       parentWorld ⊗ bindLocal ⊗ animLocal — the sampled rotation is a RIGHT
                //       (post) multiply DELTA layered on top of the bind-local rotation. So the
                //       committed local pose is bindLocalQ ⊗ sR.
                //   animAsDelta == false (spec §6.4 literal): the sampled rotation REPLACES the
                //       bind-local rotation outright (localQ = sR).
                // spec: Docs/RE/specs/skinning.md §6.5 — "applied as a right (post) multiply on top
                //       of the bind-local rotation in the world walk (§6.6)".
                localQ = animAsDelta ? Mul(bones[i].Rotation, sR) : sR;

                // Translation: only the root translates freely; child bones hold the bind-pose
                // local translation (fixed bone length, rotate-only).
                // spec: Docs/RE/specs/skinning.md §6.3.
                if (parentIndex[i] < 0)
                    localT = sT;
            }

            int p = parentIndex[i];
            if (p < 0 || p >= i)
            {
                outWorld[i] = new BoneTransform(localT, localQ);
            }
            else
            {
                BoneTransform pw = outWorld[p];
                Vec3 wt = Add(Rotate(pw.Quat, localT), pw.Trans);
                Quat wq = Mul(pw.Quat, localQ);
                outWorld[i] = new BoneTransform(wt, wq);
            }
        }
    }

    // =========================================================================
    // LBS deform (per frame) — produces the deformed vertex/normal in native space
    // =========================================================================

    /// <summary>
    /// Deforms one render vertex by the animated bone world transforms via linear blend skinning.
    ///
    /// spec: Docs/RE/specs/skinning.md §0 / §5.3 —
    ///   pos    = Σ_i w_i · ( boneWorldQuat_i ⊗ (localPos_i)    + boneWorldTrans_i )
    ///   normal = Σ_i w_i · ( boneWorldQuat_i ⊗  localNormal_i )
    /// (scale assumed 1.0 for characters — spec §5.3 / §9 open item).
    /// </summary>
    public static (Vec3 Pos, Vec3 Nrm) DeformVertex(VertexInfluences vi, BoneTransform[] world)
    {
        float px = 0f, py = 0f, pz = 0f;
        float nx = 0f, ny = 0f, nz = 0f;

        Influence[] items = vi.Items;
        for (int k = 0; k < items.Length; k++)
        {
            BoneTransform bw = world[items[k].BoneIndex];
            float w = items[k].Weight;

            Vec3 placedPos = Add(Rotate(bw.Quat, items[k].LocalPos), bw.Trans);
            Vec3 placedNrm = Rotate(bw.Quat, items[k].LocalNormal);

            px += placedPos.X * w;
            py += placedPos.Y * w;
            pz += placedPos.Z * w;
            nx += placedNrm.X * w;
            ny += placedNrm.Y * w;
            nz += placedNrm.Z * w;
        }

        return (new Vec3(px, py, pz), new Vec3(nx, ny, nz));
    }
}