// World/PlayerAvatarResolver.cs
//
// Resolves and builds the IN-WORLD LOCAL PLAYER avatar as a skinned, idle-animated character node,
// reusing the EXACT same recovered chain NpcRenderer uses for mobs/NPCs and the same
// SkinnedCharacterBuilder / SkinnedCharacterNode pipeline. ZERO new skinning path; NO GltfDocument.
//
// The local player is a class, not a mob: the wire ServerClass IS the .skn header SkinClassId
// (id_b) ∈ {1,2,3,4} = Musa/Salsu(Jagaek)/Dosa/Monk. From the SkinClassId the full chain resolves
// through the shared data-driven CharVisualRegistry (no g{n}.bnd / g{id}.mot sprintf):
//   - body .skn  : data/char/skinlist.txt scan → first entry whose parsed IdB == SkinClassId
//   - skeleton   : BindPosePool.TryGetByIdB(id_b) — the bnd resolved by its parsed actor_id (the bnd is
//                  named in bindlist.txt with no derivable filename rule); for the four players id_b
//                  reduces to the pooled actor_id {1,2,3,4}
//   - idle .mot  : actormotion GetByMotionKey(appearanceKey) → motion_ids_a[1] (col16, record +0x44 =
//                  default idle) → MotlistRegistry.ResolvePath(id_b) (id_b → 'data/char/mot/'+filename).
//                  With the recovered CategoryBase the player appearance keys {1,11,16,26} ARE the
//                  motion_key for their rows (col0=0 → base 0)
//   - albedo     : CharacterTextureResolver (skin.txt: mesh.IdA → tex id → PNG)
//   - build      : SkinnedCharacterBuilder.Build (skinned when the .bnd resolves; static otherwise)
//
// The standing idle for the first human class (g101100001.mot) is STATIC DATA (0 animated
// tracks): a frozen STANDING HUMAN is FAITHFUL, not a defect — only mobs have genuinely animated
// idles. SkinnedCharacterBuilder/Setup auto-engages PlayStandingIdle, so playback is already on.
//
// The player avatar is built SELF-DRIVEN (externalDrive=false): SkinnedCharacterNode._Process
// advances it per frame independently. NpcRenderer's ~10 Hz
// staggered scheduler is for the town of mobs only and does not apply here.
//
// spec: Docs/RE/specs/skinning.md §8(e) — skeleton = BindPosePool[id_b] (verbatim pose-pool key); idle
//       clip = actormotion GetByMotionKey(appearanceKey) → motion_ids_a[1] (col16, +0x44 = default idle)
//       → MotlistRegistry[id_b]; skin .skn = skinlist entry whose id_b == SkinClassId. §10 / §10.2 — the
//       human stand idle is static data, render faithfully. col15/motion_ids_a[0] (+0x40) is the DEAD ref.
// spec: Docs/RE/formats/actormotion.md — motion_key = col1 + CategoryBase[(byte)(col0+1)]; col16 =
//       motion_ids_a[1] (+0x44) = default idle; col15 = motion_ids_a[0] (+0x40) = dead file-source ref.
// spec: CLAUDE.md "Recovered asset mappings" — skin (.skn IdA → skin.txt), skeleton (pose pool by id_b),
//       idle motion via actormotion.txt + motlist registry.

using System.Text;
using Godot;
using MartialHeroes.Assets.Parsers.Character;
using MartialHeroes.Assets.Parsers.Mesh;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Presentation.Screens;
using MartialHeroes.Client.Presentation.World;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
///     Builds the in-world local player avatar (skinned + idle-animated) from the player's class via the
///     recovered .skn → .bnd → idle .mot chain. Pure build-time helper: it never holds game state and
///     never throws (each step degrades to a simpler-but-visible result). Call on the Godot main thread.
///     spec: Docs/RE/specs/skinning.md §8(e) (idle-clip + skeleton resolution chain).
/// </summary>
internal static class PlayerAvatarResolver
{
    /// <summary>
    ///     Resolves the player's class → SkinClassId and builds the skinned avatar root node, or returns
    ///     <see langword="null" /> when the body .skn cannot be resolved (the caller keeps the placeholder).
    ///     <paramref name="serverClass" /> is the wire character class {1,2,3,4} = the .skn header
    ///     SkinClassId / id_b (Musa/Jagaek/Dosa/Seungnyeo). This identity (ServerClass == SkinClassId)
    ///     is the §8(e) skeleton-selection key used verbatim — no slot transform.
    ///     spec: Docs/RE/specs/skinning.md §8(e); CLAUDE.md "Recovered asset mappings".
    ///     spec: MartialHeroes.Shared.Kernel.Enums.CharacterClass — 1-based {1,2,3,4}.
    /// </summary>
    /// <param name="assets">Open VFS handle (shared with the rest of the world renderer).</param>
    /// <param name="serverClass">Wire character class == SkinClassId ∈ {1,2,3,4}.</param>
    public static Node3D? TryBuild(RealClientAssets assets, ushort serverClass)
    {
        // No-equip overload: delegate with an empty equipment list (body only). The GameLoop call-site
        // (owned by the world engineer) still compiles unchanged; once the core hands the local player's
        // resolved EquipmentParts to layer 05 (see the CORE-SURFACE GAP in the wave report), the call-site
        // switches to the parts-aware overload below.
        return TryBuild(assets, serverClass, []);
    }

    /// <summary>
    ///     Equip-aware overload: builds the local-player avatar and, when the equip-overlay resolution
    ///     RUNS for this character (<see cref="EquipOverlayResolver.RunsOverlayResolution" /> ⇒
    ///     <c>base_skin_id &lt;= 1000</c>), composes the resolved <paramref name="equipParts" /> onto the
    ///     shared skeleton via <see cref="SkinnedCharacterBuilder.BuildWithEquipment" /> using the
    ///     local-player rebuild slot set (<see cref="EquipOverlayResolver.LocalPlayerRebuildSlots" />,
    ///     the full <c>{3,4,6,2,11,14}</c> when ≤1000, else reduced <c>{3}</c>). The parts are already the
    ///     core's catalogue-resolved EquipmentParts (the per-part GID / key64 math ran in the layer-04
    ///     <c>ActorComposer</c>); a part whose mesh <c>.skn</c> does not load is skipped (null mesh → no
    ///     crash). The weapon (slot 14) attaches at the DEFERRED hand bone-id 0 (DBG-PENDING — flagged,
    ///     never fabricated).
    ///     spec: Docs/RE/specs/equipment_visuals.md §1.1 / §3.4 (slot-set + threshold) / §4 (deform parts) /
    ///     §5 (weapon hand-bone, bone-id 0 deferred). spec: Docs/RE/specs/skinning.md §8(e).
    /// </summary>
    /// <param name="assets">Open VFS handle (shared with the rest of the world renderer).</param>
    /// <param name="serverClass">Wire character class == SkinClassId ∈ {1,2,3,4} == base_skin_id key.</param>
    /// <param name="equipParts">
    ///     The core's catalogue-resolved equipment overlay parts for this actor (from
    ///     <c>AssembledActor.EquipmentParts</c>). Empty ⇒ body only (faithful when nothing is worn / the
    ///     core surface does not yet carry equip data — see the wave report's core-surface gap).
    /// </param>
    public static Node3D? TryBuild(
        RealClientAssets assets, ushort serverClass, IReadOnlyList<EquipmentPart> equipParts)
    {
        // The wire class is the SkinClassId used verbatim as the §8(e) skeleton-selection key.
        // spec: Docs/RE/specs/skinning.md §8(e) — id_b (SkinClassId) is the pose-pool key, no transform.
        int skinClass = serverClass;
        if (skinClass <= 0)
        {
            GD.Print($"[PlayerAvatar] serverClass={serverClass} not a valid SkinClassId — keeping placeholder. " +
                     "spec: skinning.md §8(e).");
            return null;
        }

        // ── Step 1: body .skn — skinlist.txt scan for the entry whose parsed IdB == SkinClassId ──
        // spec: skinning.md §8(e) — body .skn = the skinlist entry whose id_b == SkinClassId.
        var sknPath = ResolveBodySknPath(assets, skinClass);
        if (sknPath is null)
        {
            GD.Print($"[PlayerAvatar] No .skn with IdB=={skinClass} in skinlist.txt — keeping placeholder. " +
                     "spec: skinning.md §8(e).");
            return null;
        }

        SkinnedMesh mesh;
        try
        {
            var sknData = assets.GetRaw(sknPath);
            if (sknData.IsEmpty)
            {
                GD.PrintErr($"[PlayerAvatar] .skn empty in VFS: {sknPath}");
                return null;
            }

            mesh = SknParser.Parse(sknData);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PlayerAvatar] SknParser.Parse failed '{sknPath}': {ex.Message}");
            return null;
        }

        // ── Step 2: albedo texture (skin.txt: mesh.IdA → tex id → PNG) ──
        // spec: World/CharacterTextureResolver.cs — Resolve(assets, mesh). CONFIRMED.
        ImageTexture? albedo = null;
        try
        {
            albedo = CharacterTextureResolver.Resolve(assets, mesh);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PlayerAvatar] Texture resolve failed for '{sknPath}': {ex.Message} — neutral material.");
        }

        // ── Shared layer-05 catalogues (bind-pose pool by verbatim id_b, motlist by header id_b,
        //    actormotion with the recovered CategoryBase). spec: skinning.md §8(e); CharVisualRegistry. ──
        var registry = CharVisualRegistry.GetOrBuild(assets);

        // The appearance key for the in-world player: the §3.7.5 starter-variant model_class_id in
        // {1,11,16,26}. Its pose-pool key reduces to {1,2,3,4} (== skinClass for the four players).
        // spec: skinning.md §3.5.2 / §8(e); login_flow §3.2.1.
        var appearanceKey = ClassAppearanceResolver.StarterBodyModelClassId(skinClass);
        var poolKey = ClassAppearanceResolver.SkeletonIdBForModelClassId(appearanceKey);
        if (poolKey == 0) poolKey = skinClass; // fallback id_b

        // ── Step 3: skeleton — BindPosePool.TryGetByIdB(id_b) (verbatim §8(e) pose-pool key; the bnd is
        //    named in bindlist.txt with NO derivable g{n}.bnd filename rule). ──
        var skeleton = TryLoadSkeleton(registry, poolKey);

        // ── Step 4: idle .mot — actormotion GetByMotionKey(appearanceKey) → motion_ids_a[1] (col16),
        //    resolved through MotlistRegistry.ResolvePath(id_b), NOT g{id}.mot. ──
        // spec: skinning.md §8(e) item 2; formats/actormotion.md — col16 = default idle; MotList_LoadAndRegister.
        var idleMotId = skeleton is not null ? ResolveIdleMotionId(registry, appearanceKey, skinClass) : 0;
        var clip = skeleton is not null && idleMotId > 0
            ? TryLoadAnimation(assets, registry, idleMotId)
            : null;

        // ── Step 5: build via SkinnedCharacterBuilder (skinned when .bnd present; static otherwise) ──
        // SELF-DRIVEN (externalDrive=false): the node self-ticks per frame via _Process.
        // The town's ~10 Hz stagger scheduler (NpcRenderer) is mob-only.
        // Setup() auto-calls PlayStandingIdle, so the looping idle is engaged on build.
        // The pivot up-axis remap (UpAxisRemapDeg) is applied inside Build — IDENTICAL to NPCs, so the
        // player stands the same way every spawned actor does (see report: up-axis is §9 debugger-pending).
        // spec: skinning.md §8(b)/§7/§9 (single handedness conversion + importer-layer up-axis remap),
        //       §10.5 (engage the col15 idle, advance with real dt, loop at clip end).
        // spec: CLAUDE.md "Known Godot Pitfalls" — NEVER GltfDocument; build ArrayMesh directly.
        try
        {
            // ── Equip-overlay wiring (EquipOverlayResolver) ──
            // base_skin_id key = the SkinClassId (ServerClass). The overlay resolution RUNS only when
            // base_skin_id <= 1000 (always true for a class id 1..4); above the threshold only slot 3
            // would be bound. The local-player rebuild slot set is the resolver's full {3,4,6,2,11,14}
            // (≤1000) vs reduced {3} (>1000). We honour that slot gate before composing parts.
            // spec: Docs/RE/specs/equipment_visuals.md §1.1 / §3.4 (threshold 1000; full vs reduced).
            var baseSkinId = skinClass;
            var runsOverlay = EquipOverlayResolver.RunsOverlayResolution(baseSkinId);
            var rebuildSlots = EquipOverlayResolver.LocalPlayerRebuildSlots(baseSkinId);

            var visualParts =
                runsOverlay && equipParts.Count > 0
                    ? EquipmentPartResolver.Resolve(assets, equipParts, rebuildSlots,
                        $"local-player class={skinClass}")
                    : [];

            Node3D root;
            SkinnedCharacterNode? lbs;
            if (visualParts.Count > 0)
                // Compose, don't swap: body + resolved overlay parts under the ONE shared skeleton.
                // spec: Docs/RE/specs/equipment_visuals.md §1 / §4 / §5.
                root = SkinnedCharacterBuilder.BuildWithEquipment(
                    mesh, skeleton, clip, albedo,
                    false,
                    0f,
                    visualParts,
                    out lbs,
                    $"local-player class={skinClass}");
            else
                root = SkinnedCharacterBuilder.Build(
                    mesh, skeleton, clip, albedo,
                    false,
                    0f,
                    out lbs,
                    $"local-player class={skinClass}");

            GD.Print($"[PlayerAvatar] Built class={skinClass} from '{sknPath}' " +
                     $"(skeleton={skeleton is not null}, idleMot={idleMotId}, " +
                     $"skinned={lbs is not null}, idlePlaying={lbs?.IsIdlePlaying ?? false}, " +
                     $"runsOverlay={runsOverlay}, equipParts={equipParts.Count}->{visualParts.Count} rendered). " +
                     "spec: skinning.md §8(e) / equipment_visuals.md §3.4.");
            return root;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PlayerAvatar] SkinnedCharacterBuilder.Build failed '{sknPath}': {ex.Message}");
            return null;
        }
    }

    // -------------------------------------------------------------------------
    // Chain steps (mirror NpcRenderer's resolution exactly)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Scans <c>data/char/skinlist.txt</c> (CP949 bare filenames under <c>data/char/skin/</c>) for the
    ///     first <c>.skn</c> whose parsed header <c>IdB</c> == <paramref name="skinClass" />.
    ///     spec: skinning.md §8(e) — body .skn = the skinlist entry whose id_b == SkinClassId.
    ///     spec: Docs/RE/formats/mesh.md §.skn header — id_b at +4. CONFIRMED.
    /// </summary>
    private static string? ResolveBodySknPath(RealClientAssets assets, int skinClass)
    {
        const string listPath = "data/char/skinlist.txt";
        if (!assets.Contains(listPath)) return null;

        try
        {
            // spec: project brief — "ALL game text is CP949".
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var text = Encoding.GetEncoding(949).GetString(assets.GetRaw(listPath).Span);

            // skinlist.txt lines are bare filenames; the full path is "data/char/skin/" + filename.
            // spec: verified on real VFS (NpcRenderer.EnsureSkinClassMapLoaded) — bare filenames.
            const string skinDir = "data/char/skin/";
            foreach (var rawLine in text.Split('\n'))
            {
                var fname = rawLine.Trim('\r', '\n', ' ').ToLowerInvariant();
                if (fname.Length == 0 || !fname.EndsWith(".skn", StringComparison.Ordinal)) continue;

                var sknPath = skinDir + fname;
                if (!assets.Contains(sknPath)) continue;

                try
                {
                    var sknData = assets.GetRaw(sknPath);
                    if (sknData.IsEmpty) continue;
                    var mesh = SknParser.Parse(sknData);
                    if ((int)mesh.IdB == skinClass) return sknPath; // first match wins (§8(e))
                }
                catch
                {
                    // Skip unparseable entries; keep scanning.
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PlayerAvatar] skinlist.txt scan failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    ///     Resolves the skeleton through the bind-pose pool by the verbatim <c>id_b</c> (= the parsed
    ///     <c>.bnd</c> header actor_id), or null (→ static rest pose) when not registered. The bnd is named
    ///     in <c>bindlist.txt</c> with NO derivable <c>g{n}.bnd</c> filename rule.
    ///     spec: skinning.md §8(e) (verbatim pose-pool key); formats/bindlist.md.
    /// </summary>
    private static Skeleton? TryLoadSkeleton(CharVisualRegistry? registry, int idB)
    {
        if (registry is null)
        {
            GD.Print("[PlayerAvatar] No CharVisualRegistry (VFS registries absent) — static rest pose. spec: skinning.md §8(e).");
            return null;
        }

        var skeleton = registry.TryGetSkeletonByIdB(idB);
        if (skeleton is null)
            GD.Print($"[PlayerAvatar] No .bnd registered with parsed actor_id={idB} in bindlist.txt — static rest pose. " +
                     "spec: skinning.md §8(e) / formats/bindlist.md.");
        return skeleton;
    }

    /// <summary>
    ///     Resolves the idle motion id via <c>actormotion GetByMotionKey(appearanceKey)</c> →
    ///     <c>motion_ids_a[1]</c> (col16, record +0x44) — the DEFAULT idle the runtime uses. With the
    ///     recovered CategoryBase the player appearance keys {1,11,16,26} ARE the motion_key for their
    ///     rows (col0=0 → base 0). Defensive fallback: <c>GetBySkinClass(skinClass)</c> (legacy col2 path).
    ///     Returns 0 when no row matches or the slot is empty.
    ///     spec: skinning.md §8(e) item 2 / §10; formats/actormotion.md — col16 = default idle.
    /// </summary>
    private static int ResolveIdleMotionId(CharVisualRegistry? registry, int appearanceKey, int skinClass)
    {
        if (registry is null) return 0;

        var entry = registry.GetByMotionKey(appearanceKey)
                    ?? registry.ActorMotion.GetBySkinClass(skinClass);
        // col16 = motion_ids_a[1] (record +0x44); 0 = empty slot. spec: actormotion.md — col16 = default idle.
        return entry?.IdleMotionId ?? 0;
    }

    /// <summary>
    ///     Loads the idle <c>.mot</c> resolved through the motlist registry (id_b → path), NOT
    ///     <c>g{idleMotId}.mot</c>. Returns null (→ rest pose) when absent / unparseable. Never throws.
    ///     spec: skinning.md §8(e); MotList_LoadAndRegister (id_b registry); formats/animation.md (header id_b).
    /// </summary>
    private static AnimationClip? TryLoadAnimation(RealClientAssets assets, CharVisualRegistry? registry, int idleMotId)
    {
        var motPath = registry?.ResolveMotPath(idleMotId);
        if (motPath is null || !assets.Contains(motPath))
        {
            GD.Print($"[PlayerAvatar] idle .mot not registered for id_b={idleMotId} — rest pose. " +
                     "spec: MotList_LoadAndRegister (id_b registry).");
            return null;
        }

        try
        {
            var data = assets.GetRaw(motPath);
            if (data.IsEmpty) return null;
            return AnimationParser.Parse(data);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PlayerAvatar] .mot load failed '{motPath}': {ex.Message}");
            return null;
        }
    }
}

/// <summary>
///     Shared layer-05 helper that turns the core's catalogue-resolved
///     <see cref="EquipmentPart" />s (from <c>AssembledActor.EquipmentParts</c>) into renderable
///     <see cref="SkinnedCharacterBuilder.EquipmentVisualPart" />s for both the local player
///     (<see cref="PlayerAvatarResolver" />) and other actors (<see cref="NpcRenderer" />).
///     <para>
///         The deterministic equip math (GID derivation, the 64-bit catalogue key, the slot-set
///         selection) lives in the engine-free <see cref="EquipOverlayResolver" /> and ran upstream in
///         the layer-04 <c>ActorComposer</c>; this helper performs ONLY the layer-05 IO the resolver
///         delegates to its caller: load each resolved part's <c>.skn</c> mesh
///         (<c>data/char/skin/g{MeshGid}.skn</c>) and its albedo (<c>TextureId</c> via
///         <see cref="CharacterTextureResolver" />), gated by the resolver's rebuild slot set. A part
///         whose mesh does not load is skipped (null mesh → no crash). It holds NO game state and
///         decides NO rule — strictly the build-time translation of an Application-supplied part list.
///     </para>
///     spec: Docs/RE/specs/equipment_visuals.md §3 (GID/key64 upstream) / §4 (non-weapon deform parts) /
///     §5 / §5.1 (weapon hand-bone + dual-wield; bone-id 0 DEFERRED). spec: skinning.md §3.5.3
///     (mesh_gid → data/char/skin/g{MeshGid}.skn; tex_id → texture).
/// </summary>
internal static class EquipmentPartResolver
{
    /// <summary>
    ///     Resolves the renderable overlay parts for an actor: for each <paramref name="rebuildSlots" />
    ///     slot that the core supplied a part for, loads the part <c>.skn</c> + texture and emits an
    ///     <see cref="SkinnedCharacterBuilder.EquipmentVisualPart" />. Weapon parts (slot 14) carry the
    ///     DEFERRED hand bone-id 0 (<see cref="SkinnedCharacterNode.DefaultHandBoneId" /> — DBG-PENDING,
    ///     never fabricated) and the §5.1 off-hand flag; non-weapon parts are skinned-deform under the
    ///     shared skeleton (§4). Never throws — a part that fails to load is skipped.
    ///     spec: Docs/RE/specs/equipment_visuals.md §3.4 (rebuild slot set) / §4 / §5 / §5.1.
    /// </summary>
    /// <param name="assets">Open VFS handle (main thread).</param>
    /// <param name="coreParts">The core's catalogue-resolved parts (Slot/MeshGid/TextureId/weapon flags).</param>
    /// <param name="rebuildSlots">
    ///     The resolver-selected rebuild slot set (local-player full/reduced, or other-actor). Only parts
    ///     whose <see cref="EquipmentPart.Slot" /> is in this set are rendered — the resolver owns which
    ///     slots a given actor rebuilds. spec: equipment_visuals.md §1.1 / §3.4.
    /// </param>
    /// <param name="debugLabel">Label for diagnostics.</param>
    public static IReadOnlyList<SkinnedCharacterBuilder.EquipmentVisualPart> Resolve(
        RealClientAssets assets,
        IReadOnlyList<EquipmentPart> coreParts,
        ReadOnlySpan<int> rebuildSlots,
        string debugLabel)
    {
        var result = new List<SkinnedCharacterBuilder.EquipmentVisualPart>(coreParts.Count);

        foreach (var part in coreParts)
        {
            // The resolver owns the rebuild slot set; only render a part whose slot is in it.
            // spec: Docs/RE/specs/equipment_visuals.md §1.1 / §3.4.
            if (!SlotInSet(part.Slot, rebuildSlots)) continue;
            if (part.MeshGid <= 0) continue; // catalogue miss / empty slot → no node (faithful skip).

            // Load the part mesh: data/char/skin/g{MeshGid}.skn.
            // spec: Docs/RE/specs/skinning.md §3.5.3 (mesh_gid → data/char/skin/g{MeshGid}.skn).
            var sknPath = $"data/char/skin/g{part.MeshGid}.skn";
            if (!assets.Contains(sknPath))
            {
                GD.Print($"[EquipPart] slot {part.Slot} mesh '{sknPath}' absent — skipped ({debugLabel}). " +
                         "spec: equipment_visuals.md §3.2 (catalogue miss → null mesh, no crash).");
                continue;
            }

            SkinnedMesh partMesh;
            try
            {
                var raw = assets.GetRaw(sknPath);
                if (raw.IsEmpty) continue;
                partMesh = SknParser.Parse(raw);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[EquipPart] slot {part.Slot} SknParser.Parse failed '{sknPath}': {ex.Message}");
                continue;
            }

            // Albedo: tex_id via the skin chain (CharacterTextureResolver). Null → neutral material.
            // spec: Docs/RE/specs/skinning.md §3.5.3 (tex_id → texture).
            ImageTexture? albedo = null;
            try
            {
                albedo = part.TextureId > 0
                    ? CharacterTextureResolver.Resolve(assets, (uint)part.TextureId)
                    : CharacterTextureResolver.Resolve(assets, partMesh);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[EquipPart] slot {part.Slot} texture resolve failed '{sknPath}': {ex.Message}");
            }

            result.Add(new SkinnedCharacterBuilder.EquipmentVisualPart(
                part.Slot,
                partMesh,
                albedo,
                part.IsHandWeapon,
                part.IsOffHand,
                // Hand bone-id stays the DEFERRED default (0) — DBG-PENDING; never fabricated.
                // spec: Docs/RE/specs/equipment_visuals.md §5 / §3.4 (hand bone-id debugger-pending).
                SkinnedCharacterNode.DefaultHandBoneId,
                // Visual+100 scalar scale: 1.0 until the core surfaces the per-weapon grip scale.
                // spec: Docs/RE/specs/equipment_visuals.md §5 (Visual+100 scalar scale).
                1.0f));
        }

        return result;
    }

    private static bool SlotInSet(int slot, ReadOnlySpan<int> set)
    {
        foreach (var s in set)
            if (s == slot)
                return true;
        return false;
    }
}