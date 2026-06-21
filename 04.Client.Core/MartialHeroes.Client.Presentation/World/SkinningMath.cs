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

using MartialHeroes.Assets.Parsers.Core.Models;
using MartialHeroes.Assets.Parsers.Mesh.Models;

namespace MartialHeroes.Client.Presentation.World;

/// <summary>
///     Pure, engine-free quaternion/vector skinning math reproducing the legacy CPU LBS path.
///     All vectors and quaternions are in the native left-handed render space; the caller applies
///     the single handedness conversion to the final output.
///     spec: Docs/RE/specs/skinning.md (linear-blend skinning, inverse-bind bake, pose composition,
///     quaternion / handedness conventions).
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
        var x = a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y;
        var y = a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X;
        var z = a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W;
        var w = a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z;
        return new Quat(x, y, z, w);
    }

    /// <summary>Unit-quaternion conjugate / inverse (−x,−y,−z,w). spec: skinning.md §4, §7.</summary>
    public static Quat Conjugate(in Quat q)
    {
        return new Quat(-q.X, -q.Y, -q.Z, q.W);
    }

    /// <summary>
    ///     Active rotation of a vector by a unit quaternion: v' = q ⊗ v ⊗ q⁻¹.
    ///     spec: Docs/RE/specs/skinning.md §7 — "active rotation v' = q ⊗ v ⊗ q⁻¹ (unit-quat form)".
    /// </summary>
    public static Vec3 Rotate(in Quat q, in Vec3 v)
    {
        // Standard expansion of q ⊗ (0,v) ⊗ q⁻¹ for a unit quaternion.
        float x = q.X, y = q.Y, z = q.Z, w = q.W;
        // t = 2 * cross(q.xyz, v)
        var tx = 2f * (y * v.Z - z * v.Y);
        var ty = 2f * (z * v.X - x * v.Z);
        var tz = 2f * (x * v.Y - y * v.X);
        // v' = v + w*t + cross(q.xyz, t)
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

    /// <summary>
    ///     Shortest-arc SLERP between two quaternions, with the sign flip and the degenerate
    ///     fallbacks described by the spec.
    ///     spec: Docs/RE/specs/skinning.md §6.1 / formats/animation.md §Rotation interpolation —
    ///     "if dot(a,b) &lt; 0 negate b; near-identical → normalized LERP; antipodal → perpendicular path."
    /// </summary>
    public static Quat Slerp(in Quat a, Quat b, float t)
    {
        var dot = Dot(a, b);
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

    /// <summary>
    ///     Resolves the per-bone parent index (array position) from the parent_id linkage, and the
    ///     base bone ID. Bones are addressed by <c>id − base_id</c>, not array position, so an
    ///     id→index map is built and the root is the bone with <c>self_id == parent_id == 0</c>.
    ///     spec: Docs/RE/specs/skinning.md §3.2 — "Bone lookup is by ID offset, bone_array[id − base_id]
    ///     … An unmatched parent_id is a fatal error in the legacy client." (We degrade to root.)
    ///     spec: Docs/RE/formats/mesh.md §Root bone sentinel.
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
        ResolveHierarchy(bones, out parentIndex, out idToIndex, out baseId, out _);
    }

    /// <summary>
    ///     As <see cref="ResolveHierarchy(Bone[], out int[], out int[], out int)" />, additionally emitting
    ///     a per-bone <paramref name="hasChild" /> flag (true when at least one other bone names this bone
    ///     as its parent). The interior-bone translation lock (§6.3) needs this flag.
    ///     spec: Docs/RE/specs/skinning.md §6.3 — interior bone = has parent AND grandparent AND ≥1 child.
    /// </summary>
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

        // Map self_id (low byte) → array index. First occurrence wins (ids are unique in practice).
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
            // Unmatched / self-referential parent → treat as root (safe degradation, no explosion).
            parentIndex[i] = pidx >= 0 && pidx != i ? pidx : -1;
        }

        // Per-bone has-child flag: a bone is a parent of at least one other bone.
        // spec: Docs/RE/specs/skinning.md §6.3 (interior-bone test needs ≥1 child).
        for (var i = 0; i < n; i++)
        {
            var p = parentIndex[i];
            if (p >= 0 && p < n) hasChild[p] = true;
        }
    }

    /// <summary>
    ///     Accumulates each bone's bind WORLD transform from the parent-relative .bnd locals,
    ///     walking parent→child (the array is parent-before-child by the on-disk format).
    ///     spec: Docs/RE/specs/skinning.md §3.1 —
    ///     root:     worldTrans = localTrans;                       worldQuat = localQuat
    ///     non-root: worldTrans = parentWorldQuat ⊗ localTrans + parentWorldTrans
    ///     worldQuat  = parentWorldQuat ⊗ localQuat       (parent on the LEFT)
    /// </summary>
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
                // Root, or parent not yet processed (degenerate ordering) → use local as world.
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

    /// <summary>
    ///     Groups .skn weight records by vertex, resolves each bone_index by ID (<c>id − base_id</c>),
    ///     drops weights below the skip threshold, and normalizes per-vertex weights to sum 1.0.
    ///     A vertex with zero usable weight is bound entirely to the root bone (index 0) — a safe
    ///     fallback; the legacy client asserts here, but we must not crash the renderer.
    ///     spec: Docs/RE/specs/skinning.md §5.1 / §5.2 — drop weight &lt; 0.01, normalize to sum 1.0.
    ///     spec: Docs/RE/formats/mesh.md §Bone addressing — bone_index is a bone ID resolved by id − base_id.
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

        foreach (var wr in weights)
        {
            if (wr.Weight < WeightSkipThreshold) continue;

            var vi = (int)wr.VertexIndex;
            if ((uint)vi >= (uint)vertexCount) continue;

            // bone_index is a bone ID; resolve to array index by id − base_id, then validate
            // against the id→index map (handles base_id != 0 and non-sequential ids).
            var bid = (int)(wr.BoneIndex & 0xFF);
            var bIdx = bid >= 0 && bid < 256 ? idToIndex[bid] : -1;
            if (bIdx < 0)
            {
                // Fall back to plain offset id − base_id if the map missed (defensive).
                var off = (int)wr.BoneIndex - baseId;
                bIdx = off >= 0 && off < boneCount ? off : -1;
            }

            if (bIdx < 0 || bIdx >= boneCount) continue;

            (raw[vi] ??= new List<(int, float)>()).Add((bIdx, wr.Weight));
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

    /// <summary>
    ///     Bakes the inverse-bind into every influence's <see cref="Influence.LocalPos" /> /
    ///     <see cref="Influence.LocalNormal" />, using each influence's bone bind WORLD transform and
    ///     the vertex's model-space rest position / normal.
    ///     spec: Docs/RE/specs/skinning.md §4 —
    ///     localPos    = bindWorldQuat⁻¹ ⊗ (restModelPos − bindWorldTrans)   (subtract then rotate)
    ///     localNormal = bindWorldQuat⁻¹ ⊗  restModelNormal                  (rotation only)
    /// </summary>
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

    // =========================================================================
    // .mot sampling + animated pose composition (per frame)
    // =========================================================================

    /// <summary>
    ///     Samples one track at clip time <paramref name="t" /> (seconds) into a local replacement
    ///     (translation, rotation) pose.
    ///     spec: Docs/RE/specs/skinning.md §6.1 / formats/animation.md §Timing —
    ///     n = floor(t·10); nNext = min(n+1, key_count−1); alpha = t − n/10  (raw seconds in [0,0.1]).
    ///     trans = lerp(key[n].t, key[nNext].t, alpha); rot = shortest-arc slerp(... , alpha).
    /// </summary>
    /// <param name="renormalizeAlpha">
    ///     When true, alpha is divided by the frame period (→ [0,1]) for smooth playback; when false
    ///     the raw-seconds alpha is used for bit-faithful (snappy) legacy playback. spec §8(c).
    /// </param>
    public static (Vec3 Trans, Quat Rot) SampleTrack(Keyframe[] keys, float t, bool renormalizeAlpha)
    {
        var keyCount = keys.Length;
        if (keyCount == 0) return (new Vec3(0, 0, 0), new Quat(0, 0, 0, 1));
        if (keyCount == 1) return (keys[0].Translation, keys[0].Rotation);

        var n = (int)MathF.Floor(t * MotFps);
        if (n < 0) n = 0;
        if (n > keyCount - 1) n = keyCount - 1;
        var nNext = n < keyCount - 1 ? n + 1 : n;

        var alpha = t - n * MotSecondsPerFrame; // raw seconds in [0, 0.1]
        if (renormalizeAlpha) alpha *= MotFps; // → [0,1]
        alpha = Math.Clamp(alpha, 0f, 1f);

        var trans = Lerp(keys[n].Translation, keys[nNext].Translation, alpha);
        var rot = Slerp(keys[n].Rotation, keys[nNext].Rotation, alpha);
        return (trans, rot);
    }

    /// <summary>
    ///     Builds the per-bone committed LOCAL animated pose for clip time <paramref name="t" />, then
    ///     walks the hierarchy to produce each bone's animated WORLD transform.
    ///     Composition (spec §6.4 / §6.6):
    ///     - A bone with NO track keeps its bind local pose (so unanimated bones reproduce the bind
    ///     world pose — no explosion at rest).
    ///     - A bone WITH a track takes the sampled rotation; child bones keep their bind-pose local
    ///     translation (rotate-only), only the root may take the sampled translation.
    ///     - World walk: worldQuat  = parentWorldQuat ⊗ localQuat   (parent on the LEFT)
    ///     worldTrans = parentWorldQuat ⊗ localTrans + parentWorldTrans
    ///     NOTE on the §6.6 "parentWorld ⊗ bindLocal ⊗ animLocal" form: there, animLocal is the pure
    ///     animation delta layered on top of bindLocal. Here we instead commit the FULL local pose
    ///     (bind local for untracked/translation, sampled rotation for tracked bones) and compose
    ///     parentWorld ⊗ localFull — which is the equivalent of §6.4 ("keyframes are local replacement
    ///     poses, not additive deltas") and reproduces the bind world pose exactly at rest.
    ///     spec: Docs/RE/specs/skinning.md §6.3, §6.4, §6.6.
    /// </summary>
    /// <param name="bones">Bones in on-disk order (for bind local trans/rot).</param>
    /// <param name="parentIndex">Per-bone parent array index (-1 root).</param>
    /// <param name="trackByBoneIndex">Per-bone-index track, or null if the bone is unanimated.</param>
    /// <param name="t">Clip time in seconds (already wrapped into [0, duration)).</param>
    /// <param name="renormalizeAlpha">Smooth vs. faithful interpolation alpha (spec §8(c)).</param>
    /// <param name="outWorld">Reused output buffer (length == bones.Length).</param>
    /// <param name="hasChild">
    ///     Per-bone has-child flag (from <see cref="ResolveHierarchy(Bone[], out int[], out int[], out int, out bool[])" />).
    ///     Required for the §6.3 interior-bone translation lock; pass null to fall back to root-only.
    /// </param>
    /// <param name="nodeScale">
    ///     Per-bone runtime node scale (+84 field). The rotated local animated translation is
    ///     multiplied by it before the parent-add (rotate → scale → translate; spec §6.6). Pass null
    ///     for a uniform 1.0 (no behaviour change). SPEC GAP: the +84 disk SOURCE is undecoded; 1.0 is
    ///     the safe interim. spec: Docs/RE/specs/skinning.md §6.6 / §3.4 (+84).
    /// </param>
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
            var localT = bones[i].Translation; // bind-pose local translation (default / child rotate-only)
            var localQ = bones[i].Rotation; // bind-pose local rotation (default for untracked)

            var track = trackByBoneIndex[i];
            if (track is not null && track.Keyframes.Length > 0)
            {
                var (sT, sR) = SampleTrack(track.Keyframes, t, renormalizeAlpha);

                // Rotation composition for a TRACKED bone.
                //   animAsDelta == true  (spec §6.5/§6.6, the CORRECT legacy form): the world
                //       rotation is parentWorld ⊗ bindLocal ⊗ animLocal — the sampled rotation is a
                //       RIGHT (post) multiply on top of the bind-local rotation, so the committed
                //       local pose is bindLocalQ ⊗ sR. DATA-PROVEN correct: composing the real idle
                //       keyframes, this keeps the animated frame-0 deform close to the authored rest
                //       pose (mean per-vertex displacement g1=0.47, g4=1.17, mob=0.02), as an idle's
                //       first frame should.
                //   animAsDelta == false (a literal "replacement" reading, localQ = sR): flings the
                //       whole mesh ~half the model extent off the authored pose (g1=1.97, g4=2.81,
                //       mob=3.55) — decisively wrong. Retained only as a toggle.
                // spec: Docs/RE/specs/skinning.md §6.5 — "applied as a right (post) multiply on top
                //       of the bind-local rotation in the world walk (§6.6)". §6.4's "replacement"
                //       describes the per-pass mixer accumulator, not this world walk.
                localQ = animAsDelta ? Mul(bones[i].Rotation, sR) : sR;

                // Translation lock (§6.3): lock the animated local translation to the bind-local
                // translation ONLY for an INTERIOR bone — one that has a parent AND a grandparent AND
                // at least one child. The ROOT, the root's DIRECT CHILDREN, and LEAF bones take the
                // sampled translation. (The earlier rule locked every non-root bone, freezing leaves
                // and root-children in translation — too broad.)
                // spec: Docs/RE/specs/skinning.md §6.3 (interior-bone-only bind-local trans lock).
                var parent = parentIndex[i];
                var hasParent = parent >= 0;
                var hasGrandparent = hasParent && parentIndex[parent] >= 0;
                var boneHasChild = hasChild is not null ? hasChild[i] : false;
                var interior = hasParent && hasGrandparent && boneHasChild;
                if (!interior)
                    localT = sT; // root / root-child / leaf → take the sampled translation
            }

            var p = parentIndex[i];
            if (p < 0 || p >= i)
            {
                outWorld[i] = new BoneTransform(localT, localQ);
            }
            else
            {
                var pw = outWorld[p];
                // World walk: rotate → scale → translate (§6.6). The rotated local animated
                // translation is scaled by this bone's per-node scale (+84, default 1.0) before the
                // parent-add. spec: Docs/RE/specs/skinning.md §6.6.
                var rotatedLocal = Rotate(pw.Quat, localT);
                var s = nodeScale is not null ? nodeScale[i] : 1.0f;
                if (s != 1.0f) rotatedLocal = Scale(rotatedLocal, s);
                var wt = Add(rotatedLocal, pw.Trans);
                var wq = Mul(pw.Quat, localQ);
                outWorld[i] = new BoneTransform(wt, wq);
            }
        }
    }

    // =========================================================================
    // LBS deform (per frame) — produces the deformed vertex/normal in native space
    // =========================================================================

    /// <summary>
    ///     Deforms one render vertex by the animated bone world transforms via linear blend skinning.
    ///     spec: Docs/RE/specs/skinning.md §0 / §5.3 —
    ///     pos    = Σ_i w_i · ( boneWorldQuat_i ⊗ (localPos_i · scale) + boneWorldTrans_i )
    ///     normal = Σ_i w_i · ( boneWorldQuat_i ⊗  localNormal_i )
    ///     Per-mesh `scale` (spec §5.3/§9, RESOLVED CAMPAIGN 9): the legacy per-mesh scale is a real,
    ///     generally non-unit skin-object field set at attach as `meshScale · nodeScale`. It multiplies
    ///     the bone-local POSITION before rotation (never the normal, never the rotation). Here the
    ///     deform runs at unit scale because the live consumers (CharSelectScene3D / RealWorldRenderer /
    ///     CharPreview3D) carry the per-actor scale on the returned root Node3D's transform instead —
    ///     exactly the equivalent the spec sanctions for an engine that lets its node transform size the
    ///     rig ("the importer must still apply this mesh scale to the … node transform"). The cancellation
    ///     invariant (§0) is unaffected: a uniform scalar commutes through the convex weighted sum.
    /// </summary>
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

    // =========================================================================
    // Bind pose — parent-relative .bnd locals → bind WORLD transforms (accumulated at load)
    // =========================================================================

    /// <summary>A bone's world-space transform: a translation plus a rotation quaternion.</summary>
    public readonly record struct BoneTransform(Vec3 Trans, Quat Quat);

    // =========================================================================
    // Influences — expand .skn weights into per-vertex normalized influence records
    // =========================================================================

    /// <summary>
    ///     One runtime influence: which bone (by array index), the baked bone-local rest position /
    ///     normal (filled by <see cref="BakeInverseBind" />), and the normalized weight.
    ///     spec: Docs/RE/specs/skinning.md §2.2 / §4.
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
}