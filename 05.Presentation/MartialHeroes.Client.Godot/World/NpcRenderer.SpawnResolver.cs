// World/NpcRenderer.SpawnResolver.cs
//
// Partial class — spawn-record → skin/skeleton resolution, the mob→actormotion→skin chain.
// See NpcRenderer.cs for the full file description and all spec cites.
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive.
// spec: Docs/RE/formats/mesh.md §.skn header — id_b; §actormotion.txt. CONFIRMED.
// spec: Docs/RE/specs/skinning.md §8(e)/§10 — idle = motion_ids_a[1] = column 16 (record +0x44);
//       column 15 / motion_ids_a[0] (record +0x40) is statically DEAD.

using Godot;
using MartialHeroes.Assets.Parsers.Mesh;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Presentation.World;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class NpcRenderer
{
    // -------------------------------------------------------------------------
    // Model resolution — mob_id → skin chain
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Resolves a model for the given mob_id via the confirmed chain and builds a SKINNED +
    ///     idle-animated CPU-LBS node (the same pipeline the player uses), falling back to a static
    ///     rest-pose node when any piece is missing:
    ///     actormotion.txt col1==mob_id → col2=skin_class (and col16 = default idle .mot id)
    ///     skinlist.txt scan → first .skn whose parsed IdB == skin_class
    ///     data/char/bind/g{skin_class}.bnd → skeleton (.bnd)
    ///     data/char/mot/g{idle}.mot → idle clip (.mot)
    ///     CharacterTextureResolver → albedo texture
    ///     SkinnedCharacterBuilder.Build (skinned when bnd present; static otherwise)
    ///     Returns null when even the .skn cannot be resolved; never throws.
    ///     spec: MISSION — "resolve each actor's skin/bind/idle-mot via the existing actormotion chain,
    ///     build the skinned node, fall back to the static path when any piece is missing."
    ///     spec: Docs/RE/specs/skinning.md §8(e)/§10 — col2=SkinClassId; idle = motion_ids_a[1] = col16
    ///     (record +0x44), NOT col15/motion_ids_a[0] (record +0x40, statically dead).
    ///     spec: Docs/RE/specs/skinning.md §8(d) — mob trio g2048.bnd + g219000630.skn + g182006900.mot.
    /// </summary>
    private Node3D? TryBuildFromMobId(RealClientAssets assets, ushort mobId)
    {
        if (_actorMotionLookup is null || _skinClassToSknPath is null)
            return null;

        // Step 1: actormotion.txt lookup: mob_id → skin_class + idle motion id.
        // spec: ActormotionParser — col1=actor_class_id, col2=skin_class_id, col15=idle motion.
        if (!_actorMotionLookup.TryGetValue(mobId, out var entry))
            return null; // mob_id not in actormotion.txt — silently skip

        var skinClass = entry.SkinClassId;

        // Step 2: skinlist.txt scan → .skn path.
        // spec: MISSION — "body .skn = the entry in skinlist.txt whose parsed .skn IdB == skin_class."
        if (!_skinClassToSknPath.TryGetValue(skinClass, out var sknPath))
            return null; // no .skn with matching IdB — silently skip

        // Idle motion id = motion_ids_a[1] = column 16 (in-memory record +0x44 = direction array A
        // element 1) — the DEFAULT IDLE slot. NOT motion_ids_a[0] / column 15 (record +0x40), which is
        // STATICALLY DEAD (zero read-sites) — selecting it (the off-by-one) plays the file-source idle
        // ref, frequently a fixed-stand snapshot, producing a frozen-looking mob (the liveDelta=0
        // diagnostic on the 120-frame mob trio). The first CONSUMED element of motion_ids_a is element 1.
        // Fall back to element 0 only if the array has no element 1 (defensive; the array is 9-wide).
        // spec: Docs/RE/specs/skinning.md §8(e) item 2 / §10 / §10.3.1 — stand/default idle = column 16
        //       (record +0x44, array A element 1); record +0x40 (col15, element 0) is statically DEAD.
        // spec: Docs/RE/formats/actormotion.md §The two 9-element sub-arrays — motion_ids_a a[1]=default idle.
        var idleMotId = entry.MotionIds.Length > 1 ? entry.MotionIds[1]
            : entry.MotionIds.Length > 0 ? entry.MotionIds[0]
            : 0;

        return TryBuildActorNode(assets, sknPath, skinClass, idleMotId,
            $"mob_id={mobId} skin_class={skinClass}");
    }

    /// <summary>
    ///     Fallback path: probe <c>data/char/skin/g{mobId}.skn</c> directly (NPCs not in actormotion.txt).
    ///     Builds skinned if a <c>.bnd</c> can be derived from the parsed mesh's IdB; static otherwise.
    ///     Returns null (silent skip) when the probe path does not exist in the VFS.
    /// </summary>
    private Node3D? TryBuildDirectSkinProbe(RealClientAssets assets, ushort mobId)
    {
        var sknPath = $"data/char/skin/g{mobId}.skn";
        if (!assets.Contains(sknPath))
            return null;
        // No actormotion row → no skin_class hint, no idle id; the builder derives the .bnd from
        // the parsed mesh IdB and renders a static rest pose (skeleton may still resolve).
        return TryBuildActorNode(assets, sknPath, -1, 0,
            $"direct-probe mob_id={mobId}");
    }

    /// <summary>
    ///     Parses the .skn, resolves its texture, loads the .bnd skeleton and idle .mot clip (when
    ///     available), and builds a skinned CPU-LBS node via <see cref="SkinnedCharacterBuilder" />.
    ///     Registers the resulting skinned node with the ~10 Hz stagger scheduler. When the skeleton
    ///     is missing, the builder degrades to a static rest pose (counted as a static fallback).
    /// </summary>
    /// <param name="skinClass">SkinClassId from actormotion (the .bnd g-id), or -1 to derive from mesh IdB.</param>
    /// <param name="idleMotId">Idle .mot id_a (col15), or 0 for none.</param>
    private Node3D? TryBuildActorNode(
        RealClientAssets assets, string sknPath, int skinClass, int idleMotId, string debugLabel)
    {
        SkinnedMesh mesh;
        try
        {
            var sknData = assets.GetRaw(sknPath);
            if (sknData.IsEmpty)
            {
                GD.PrintErr($"[NpcRenderer] .skn empty in VFS: {sknPath} ({debugLabel})");
                return null;
            }

            mesh = SknParser.Parse(sknData);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] SknParser.Parse failed '{sknPath}' ({debugLabel}): {ex.Message}");
            return null;
        }

        // Resolve texture — CharacterTextureResolver handles skin.txt + derivation fallback.
        // spec: World/CharacterTextureResolver.cs — Resolve(assets, mesh). CONFIRMED.
        ImageTexture? albedo = null;
        try
        {
            albedo = CharacterTextureResolver.Resolve(assets, mesh);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] Texture resolve failed for '{sknPath}': {ex.Message}");
            // Albedo stays null — neutral material will be applied by builder.
        }

        // Resolve the .bnd skeleton. The skin_class IS the .bnd g-id (== mesh IdB); fall back to
        // the mesh's own IdB when no actormotion row gave a skin_class.
        // spec: Docs/RE/formats/mesh.md §.bnd — actor_id == skin .skn id_b; path g{id}.bnd. CONFIRMED.
        var bndId = skinClass > 0 ? skinClass : (int)mesh.IdB;
        var skeleton = TryLoadSkeleton(assets, bndId, debugLabel);

        // Resolve the idle .mot clip. Only when we have a skeleton to drive it.
        // spec: Docs/RE/formats/animation.md §col15 — idle motion id → data/char/mot/g{id}.mot.
        var clip = skeleton is not null && idleMotId > 0
            ? TryLoadAnimation(assets, idleMotId, debugLabel)
            : null;

        try
        {
            // Skinned when a skeleton resolved; static rest pose otherwise. Mobs are externally
            // driven (the ~10 Hz scheduler pumps them) with a randomized clip start phase so the
            // town does not animate in lockstep.
            // spec: MISSION — skinned mob path, ~10 Hz staggered, randomized clip phase.
            var startPhase = clip is not null ? NextPhase(clip.FrameCount * SkinningMath.MotSecondsPerFrame) : 0f;
            var root = SkinnedCharacterBuilder.Build(
                mesh, skeleton, clip, albedo,
                skeleton is not null,
                startPhase,
                out var lbs,
                debugLabel);

            if (lbs is not null)
            {
                // Skinned: register with the stagger scheduler in a round-robin bucket.
                var bucket = _skinnedActors.Count % SkinTickGroups;
                _skinnedActors.Add(new SkinnedActor(lbs, bucket));
                _totalSkinned++;
            }
            else
            {
                _totalStaticFallback++;
            }

            return root;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] SkinnedCharacterBuilder.Build failed '{sknPath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     Loads the <c>.bnd</c> skeleton at <c>data/char/bind/g{bndId}.bnd</c>, or returns null
    ///     (→ static rest pose) when absent / unparseable. Never throws.
    ///     spec: Docs/RE/formats/mesh.md §.bnd — path g{id}.bnd; id == skin id_b == skin_class. CONFIRMED.
    /// </summary>
    private static Skeleton? TryLoadSkeleton(RealClientAssets assets, int bndId, string debugLabel)
    {
        if (bndId <= 0) return null;
        var bndPath = $"data/char/bind/g{bndId}.bnd";
        if (!assets.Contains(bndPath)) return null;
        try
        {
            var data = assets.GetRaw(bndPath);
            if (data.IsEmpty) return null;
            return BndParser.Parse(data);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] .bnd load failed '{bndPath}' ({debugLabel}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     Loads the idle <c>.mot</c> clip at <c>data/char/mot/g{idleMotId}.mot</c>, or returns null
    ///     (→ rest pose, no animation) when absent / unparseable. Never throws.
    ///     spec: Docs/RE/formats/animation.md §col15 — idle motion id → data/char/mot/g{id}.mot. CONFIRMED.
    /// </summary>
    private static AnimationClip? TryLoadAnimation(RealClientAssets assets, int idleMotId, string debugLabel)
    {
        var motPath = $"data/char/mot/g{idleMotId}.mot";
        if (!assets.Contains(motPath)) return null;
        try
        {
            var data = assets.GetRaw(motPath);
            if (data.IsEmpty) return null;
            return AnimationParser.Parse(data);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] .mot load failed '{motPath}' ({debugLabel}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     Deterministic pseudo-random clip start phase in [0, duration). Uses a cheap xorshift so it
    ///     allocates nothing and stays reproducible across runs (no System.Random per actor).
    ///     spec: MISSION — "randomize each actor's clip start phase so the town doesn't move in lockstep."
    /// </summary>
    private float NextPhase(float durationSeconds)
    {
        if (durationSeconds <= 0f) return 0f;
        // xorshift32
        var x = _phaseRng;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _phaseRng = x;
        var u = (x & 0xFFFFFF) / (float)0x1000000; // [0,1)
        return u * durationSeconds;
    }
}