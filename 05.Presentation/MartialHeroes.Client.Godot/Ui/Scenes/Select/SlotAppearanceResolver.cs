// Ui/Scenes/Select/SlotAppearanceResolver.cs
//
// HARD-REWRITE of the per-slot character APPEARANCE resolution for the char-select preview row.
//
// THE SLOT-2 BIND-POSE / NO-ANIMATION DEBT (root cause this file retires):
//   The previous per-slot path used model_class_id = 5*(InternalClass + 4*AppearanceVariant) - 24
//   (in {1,11,16,26}) AS the skeleton filename number (g{model_class_id}.bnd) AND as the idle row key.
//   That is wrong: model_class_id is the APPEARANCE key (§3.5.2), NOT a .bnd filename — there is NO
//   g11.bnd / g16.bnd, only g1..g4.bnd. So slot 2 (arius, Dosa: model_class_id=11) tried g11.bnd,
//   which is absent → the rig failed to load → the avatar fell back to the STATIC BIND POSE, and the
//   idle lookup GetBySkinClass(11) matched no actormotion row (col2 is the SkinClassId {1,2,3,4}, not
//   model_class_id) → no idle clip → no animation. (Slots 0/1, Musa: model_class_id=1, happened to
//   work only because model_class_id 1 == SkinClassId 1 by coincidence.)
//   GROUND TRUTH (§8(e), CLAUDE.md "Character skeleton", and the proven in-world chain
//   World/PlayerAvatarResolver): the skeleton selector is the SkinClassId ∈ {1,2,3,4}, which for the PC
//   path is the descriptor InternalClass VERBATIM (Musa/Salsu/Dosa/Monk = 1/2/3/4). The rig is
//   g{SkinClassId}.bnd and the idle is actormotion col2 == SkinClassId -> col16. PlayerAvatarResolver
//   uses serverClass==SkinClassId exactly this way. So slot 2 (Dosa, InternalClass 3) -> g3.bnd +
//   col2==3 idle, and it ANIMATES like slots 0/1.
//   CAVEAT (layer-04 starter-body debt, surfaced not fixed): the §3.7.5 STARTER bodies
//   (g202/203/209/206...) are class-1-FAMILY overlay parts and all parse to id_b == 1 — they are NOT
//   per-class bodies. So the parsed body id_b is NOT a usable rig key here (it would put every slot on
//   g1.bnd + the Musa idle, the §8(e) rig-substitution shatter). The descriptor InternalClass is the
//   correct SkinClassId; the model_class_id {1,11,16,26} appearance key is diagnostic only.
//
// THE FIX (this resolver) — key the rig AND the idle by the SkinClassId = descriptor InternalClass,
// exactly like the proven in-world chain (World/PlayerAvatarResolver):
//   Resolve EVERYTHING from the slot DESCRIPTOR, per class:
//     1. model_class_id = 5*(InternalClass + 4*AppearanceVariant) - 24 (ClassAppearanceResolver) — the
//        APPEARANCE key {1,11,16,26}, used for diagnostics / the §3.3.7 catalogue key. It is NOT a
//        skeleton filename: there is NO g11.bnd / g16.bnd — using model_class_id as the .bnd number was
//        the SLOT-2 BIND-POSE BUG (it resolved g11.bnd, which is absent → fell back to bind pose; and
//        GetBySkinClass(11) matched no actormotion row → no idle).
//     2. SkinClassId = InternalClass ∈ {1,2,3,4} (the .skn HEADER class = Musa/Salsu/Dosa/Monk). The PC
//        path uses InternalClass verbatim as the SkinClassId (PlayerAvatarResolver does the same with
//        serverClass). The §3.7.5 starter bodies are class-1-family parts (all id_b==1), so the descriptor
//        InternalClass — NOT the parsed body id_b — is the rig key.
//     3. skeleton = data/char/bind/g{SkinClassId}.bnd (§8(e) — g1..g4.bnd, the ONLY .bnd that exist).
//        This is the SAME rule PlayerAvatarResolver uses; it makes every occupied slot (incl. slot 2
//        Dosa, SkinClassId 3 → g3.bnd) resolve its real rig instead of a non-existent g{model_class_id}.bnd.
//     4. idle .mot = actormotion.txt row keyed by SkinClassId (col2 == skin_class) -> col16
//        (motion_ids_a[1], record +0x44; col15/+0x40 is statically DEAD — CYCLE 7) -> g{id}.mot. Keying
//        by SkinClassId (3) not model_class_id (11) is what makes slot 2 ANIMATE like slots 0/1.
//     5. equip overlays {3,4,6,2,11,14} from the now-surfaced EquipGids: weapon (slot 14) = RIGID
//        hand-bone attach via the kept SkinnedCharacterBuilder.BuildWithEquipment; non-weapon overlays
//        are RESOLVED (gid -> g{gid}.skn) but the shared-skeleton multi-surface deform path is not yet
//        in SkinnedCharacterNode, so any genuinely-absent gear .skn is LOGGED-and-SKIPPED (no
//        fabrication) and the missing gids are surfaced in the build log — the documented §3.3.7 gap.
//   Every slot (incl. slot 2) is then built through the KEPT SkinnedCharacterBuilder, which applies the
//   +90°-about-Z stand-up remap + feet-to-Y=0 recentre, so it deforms upright, grounded, AABB-sane.
//
// THIS FILE OWNS ONLY THE APPEARANCE -> ASSET -> BUILDER RESOLUTION. It is a passive renderer:
//   zero game logic, zero domain mutation, zero packet parsing. Which slot to build, the placement /
//   scale / facing, and the host SlotDescriptor shape all stay with the 3D lane (CharSelectScene3D);
//   this resolver EXPOSES a builder that lane calls and returns the recentred actor Node3D + a small
//   diagnostics struct so the AABB-sanity loop has a number to converge on.
//
// SKELETON / IDLE SELECTION (SkinClassId = descriptor InternalClass, §8(e)):
//   The deform skeleton and the idle clip are BOTH selected by the SkinClassId ∈ {1,2,3,4} = the
//   descriptor InternalClass used VERBATIM — g{SkinClassId}.bnd for the rig, actormotion col2 ==
//   SkinClassId -> col16 for the idle — exactly as the in-world chain (PlayerAvatarResolver) does. The
//   appearance key model_class_id ∈ {1,11,16,26} is computed for diagnostics + the §3.3.7 overlay
//   catalogue key only; it is NEVER used as a .bnd filename or as the idle row key (doing so resolved a
//   non-existent g{model_class_id}.bnd and an unmatched actormotion row — the slot-2 bind-pose /
//   no-animation bug). The parsed body id_b is NOT the key either (the §3.7.5 starter bodies are all
//   class-1-family parts, id_b==1); a SkinClassId-vs-parsed-id_b gap is logged as the layer-04 debt.
//
// NEVER GltfDocument.AppendFromBuffer (native crash) — SkinnedCharacterBuilder builds ArrayMesh.
// global::Godot.* qualification is used where a bare Godot type would collide with the sibling
// MartialHeroes.Client.Godot.* namespace.
//
// spec: Docs/RE/specs/skinning.md §3.5.2 (model_class_id appearance key) / §8(e) (rig = g{id_b}.bnd +
//       idle = actormotion col2 == id_b -> col16, the VERBATIM id_b pose-pool key) / §10 (idle col16).
// spec: Docs/RE/specs/frontend_scenes.md §3.3.7 (per-part overlay build) / §3.7.5 (starter body).
// spec: Docs/RE/specs/login_flow.md §3.2.1 (model_class_id -> g{n}.bnd edge; cross-check only — the
//       resolver keys the rig by the verbatim id_b, equal to g1..g4.bnd for the four players).
// spec: Docs/RE/formats/actormotion.md §motion_ids_a (a[1] = +0x44 = col16 = runtime stand idle).

using Godot;
using MartialHeroes.Assets.Parsers.Character;
using MartialHeroes.Assets.Parsers.Mesh;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Godot.World;
using MartialHeroes.Client.Presentation.Screens;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

/// <summary>
///     Per-slot character appearance resolver for the char-select preview row. Takes a slot's
///     appearance descriptor (internal class, appearance variant, faceA, equip gids) and builds a
///     ready-to-place actor <see cref="Node3D" /> through the kept <see cref="SkinnedCharacterBuilder" />,
///     resolving the skeleton + idle clip from the parsed body <c>.skn</c> header <c>id_b</c> (the
///     SkinClassId, the §8(e) verbatim pose-pool key) — NOT from <c>model_class_id</c> {1,11,16,26}
///     (which is the appearance key, not a <c>.bnd</c> filename) — so every occupied slot, including
///     slot 2 (Dosa), loads its real rig + idle and animates, deformed, upright and grounded.
///     <para>
///         Strictly passive: no game logic, no domain state. The 3D lane (CharSelectScene3D) decides
///         WHICH slot to build and WHERE to place/scale/face it; this resolver only translates an
///         appearance descriptor into the correct skinned + animated node and the asset chain behind it.
///     </para>
///     spec: Docs/RE/specs/skinning.md §3.5.2 / §8(e) / §10; frontend_scenes.md §3.3.7 / §3.7.5;
///     login_flow.md §3.2.1; actormotion.md §motion_ids_a.
/// </summary>
public static class SlotAppearanceResolver
{
    private const string ActormotionTablePath = "data/char/actormotion.txt";
    private const string SkinTxtTablePath = "data/char/skin.txt";

    /// <summary>
    ///     Resolves and builds one preview slot's actor from its appearance descriptor.
    ///     Returns a <see cref="SlotBuildResult" /> whose <see cref="SlotBuildResult.ActorRoot" /> is null
    ///     when the appearance does not resolve (unknown class, absent body .skn, invisible sentinel) — the
    ///     caller logs + skips; this resolver NEVER substitutes a wrong-class mesh and NEVER fabricates an
    ///     equip id. Never throws — every step is guarded and degrades (missing rig -> static rest;
    ///     missing idle -> bind pose).
    ///     <para>
    ///         All <see cref="Node3D" /> construction happens on the calling (main) thread, as required.
    ///     </para>
    ///     spec: Docs/RE/specs/skinning.md §3.5.2 / §8(e); frontend_scenes.md §3.3.7 / §3.7.5.
    /// </summary>
    /// <param name="assets">Open VFS handle. Must not be null (caller guards offline).</param>
    /// <param name="appearance">The slot's appearance descriptor.</param>
    /// <param name="debugLabel">Label threaded into the builder + diagnostics (e.g. "slot2").</param>
    public static SlotBuildResult BuildSlotActor(
        RealClientAssets assets,
        SlotAppearance appearance,
        string debugLabel)
    {
        // 1) model_class_id — the APPEARANCE key (NOT a .bnd filename, NOT the idle row key). Used here
        //    only as the invisible-sentinel gate + a diagnostic cross-check against the parsed id_b. The
        //    rig + idle are keyed by the parsed body id_b below (§8(e) verbatim pose-pool key).
        // spec: skinning.md §3.5.2 — model_class_id = 5*(class + 4*variant) - 24, in {1,11,16,26};
        //       variant 3 -> 0 = invisible-actor sentinel (no mesh).
        var modelClassId = ClassAppearanceResolver.ModelClassId(
            (int)appearance.InternalClass, (int)appearance.AppearanceVariant);

        if (modelClassId <= 0)
        {
            GD.Print(
                $"[SlotAppearanceResolver] {debugLabel}: model_class_id={modelClassId} (class={appearance.InternalClass}, " +
                $"variant={appearance.AppearanceVariant}) is the invisible/unmapped sentinel — no mesh (faithful). " +
                "spec: skinning.md §3.5.2.");
            return new SlotBuildResult(null, modelClassId, 0u, false, false);
        }

        // 2) body .skn — the CORRECTED per-class body, resolved through the §3.5.3 appearance catalogue
        //    keyed by (slot=3, body model_class_id). The body model_class_id is computed from the class's
        //    §3.7.5 STARTER variant {1,2,1,1} -> IdB {1,26,11,16} (the host leaves descriptor variant=0,
        //    so the documented starter variant is used — NOT invented). SkinTxtCatalog.GetBodyMeshGid
        //    returns four DISTINCT body gids {1->g202110001, 26->g202220001, 11->g202130001,
        //    16->g202140001}, retiring the prior wrong-key path (col2={4,6,11} class-1-family rows) that
        //    made slot-2 Dosa a flat Musa slab. A genuinely-absent body .skn is LOGGED + reported as a
        //    data gap (NO wrong-class fallback, NO fabricated geometry).
        //    spec: frontend_scenes.md §3.7.5 (per-class IdB body table) / skinning.md §3.5.3 (catalogue
        //          key (slot=3, model_class_id)) / §3.5.1 (body == slot 3).
        var bodyModelClassId = ClassAppearanceResolver.StarterBodyModelClassId((int)appearance.InternalClass);
        if (bodyModelClassId <= 0)
        {
            GD.PrintErr(
                $"[SlotAppearanceResolver] {debugLabel}: unknown class={appearance.InternalClass} — no starter body key " +
                "(NO wrong-class fallback). spec: frontend_scenes.md §3.7.5.");
            return new SlotBuildResult(null, modelClassId, 0u, false, false);
        }

        var bodyMeshGid = ResolveBodyMeshGid(assets, bodyModelClassId, debugLabel);
        if (bodyMeshGid is null)
        {
            GD.PrintErr(
                $"[SlotAppearanceResolver] {debugLabel}: DATA GAP — no skin.txt body row for (slot=3, model_class_id={bodyModelClassId}) " +
                $"class={appearance.InternalClass} — LOGGED + skipped (NO wrong-class fallback, NO fabricated geometry). " +
                "spec: frontend_scenes.md §3.7.5 / skinning.md §3.5.3.");
            return new SlotBuildResult(null, modelClassId, 0u, false, false);
        }

        var bodySknPath = ClassAppearanceResolver.BodySknPathForMeshGid(bodyMeshGid.Value);

        if (!assets.Contains(bodySknPath))
        {
            GD.PrintErr(
                $"[SlotAppearanceResolver] {debugLabel}: DATA GAP — body .skn ABSENT '{bodySknPath}' " +
                $"(class={appearance.InternalClass}, model_class_id={bodyModelClassId}, mesh_gid={bodyMeshGid}) — " +
                "LOGGED + skipped (NO wrong-class fallback, NO fabricated geometry). " +
                "spec: frontend_scenes.md §3.7.5 / skinning.md §3.5.3.");
            return new SlotBuildResult(null, modelClassId, 0u, false, false);
        }

        SkinnedMesh bodyMesh;
        try
        {
            var raw = assets.GetRaw(bodySknPath);
            if (raw.IsEmpty)
            {
                GD.PrintErr($"[SlotAppearanceResolver] {debugLabel}: body .skn empty '{bodySknPath}' — skipped.");
                return new SlotBuildResult(null, modelClassId, 0u, false, false);
            }

            bodyMesh = SknParser.Parse(raw);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SlotAppearanceResolver] {debugLabel}: body .skn parse failed '{bodySknPath}': {ex.Message}");
            return new SlotBuildResult(null, modelClassId, 0u, false, false);
        }

        // The deform skeleton + idle are keyed by the SkinClassId ∈ {1,2,3,4}, which for the PC path is
        // the descriptor InternalClass VERBATIM (Musa/Salsu/Dosa/Monk = 1/2/3/4) — exactly as the proven
        // in-world chain (World/PlayerAvatarResolver uses serverClass==SkinClassId directly). This is the
        // §8(e) skeleton selector: g{SkinClassId}.bnd + actormotion col2 == SkinClassId -> col16.
        //
        // NOTE: the §3.7.5 STARTER bodies (g202/203/209/206...) are class-1-family overlay parts and all
        // parse to id_b == 1 (they are NOT per-class bodies), so the parsed body id_b CANNOT be used as
        // the rig key here — it would put every slot on g1.bnd + the Musa idle (the §8(e)
        // clean-at-rest / shatter-on-play rig substitution). The descriptor InternalClass is the correct
        // SkinClassId. A parsed-id_b != SkinClassId gap is the layer-04 starter-body debt (the starter
        // table returns a class-1-family mesh for every class) — surfaced here, NOT papered over.
        // spec: skinning.md §8(e); CLAUDE.md "Character skeleton" (g{SkinClassId}.bnd, id_b == .skn header class).
        var skinClassId = (int)appearance.InternalClass;

        // Cross-check: when the appearance key is a mapped value {1,11,16,26} it must reduce to the same
        // SkinClassId as the descriptor InternalClass (a descriptor class/variant consistency check).
        var mappedSkinClass = SkinClassForModelClassId(modelClassId); // {1->1,26->2,11->3,16->4}, 0 = unmapped
        if (mappedSkinClass != 0 && mappedSkinClass != skinClassId)
            GD.Print(
                $"[SlotAppearanceResolver] {debugLabel}: NOTE model_class_id={modelClassId} reduces to SkinClassId={mappedSkinClass} " +
                $"but descriptor InternalClass={skinClassId} — keying rig + idle by InternalClass. spec: skinning.md §8(e)/§3.5.2.");

        if ((int)bodyMesh.IdB != skinClassId)
            GD.Print(
                $"[SlotAppearanceResolver] {debugLabel}: NOTE descriptor SkinClassId={skinClassId} but starter body id_b={bodyMesh.IdB} " +
                "(layer-04 §3.7.5 starter table returns a class-1-family mesh) — rig + idle keyed by the descriptor " +
                "SkinClassId (g{n}.bnd + col2==n idle), per §8(e). spec: skinning.md §8(e) / frontend_scenes.md §3.7.5.");

        // 3) skeleton — g{SkinClassId}.bnd (§8(e); SkinClassId ∈ {1,2,3,4} → g1..g4.bnd, the only .bnd
        //    that exist). NOT g{model_class_id}.bnd (there is no g11.bnd/g16.bnd — that was the slot-2
        //    bind-pose bug). Same rule as PlayerAvatarResolver. spec: skinning.md §8(e) step 1.
        var skeleton = TryLoadSkeleton(assets, skinClassId, debugLabel);

        // 4) idle .mot — actormotion col2 == SkinClassId -> col16 (motion_ids_a[1], record +0x44).
        //    Keyed by SkinClassId ∈ {1,2,3,4}, NOT model_class_id — this is what makes EVERY occupied slot
        //    (incl. slot 2 Dosa) animate. spec: skinning.md §8(e) step 2 / §10; actormotion.md §motion_ids_a.
        var idleClip = TryLoadIdleClip(assets, skinClassId, debugLabel);

        // 5) texture — by the body skin's IdA through the kept CharacterTextureResolver chain.
        ImageTexture? albedo = null;
        try
        {
            albedo = CharacterTextureResolver.Resolve(assets, bodyMesh.IdA);
        }
        catch (Exception ex)
        {
            GD.PrintErr(
                $"[SlotAppearanceResolver] {debugLabel}: texture resolve failed for IdA={bodyMesh.IdA}: {ex.Message}");
        }

        // 6) overlay parts {4,6,2,11} + weapon (slot 14). The body is slot 3 (built as the PRIMARY mesh
        //    above — there is NO separate base mesh, §3.5.1). The non-weapon overlays {4,6,2,11} are now
        //    multi-surface-deformed onto the SHARED skeleton (SkinnedCharacterNode.AttachDeformPart via
        //    BuildWithEquipment); the weapon (slot 14) is the rigid hand-bone attach. Source of the
        //    overlay gids: the host-surfaced EquipGids when present, ELSE the §3.6.1 per-class STARTER
        //    overlay ids (spec-documented values, NOT fabricated — the char-select zoom path injects
        //    exactly these). Any gear .skn genuinely ABSENT from the VFS is LOGGED-and-SKIPPED.
        //    spec: frontend_scenes.md §3.3.7 / skinning.md §3.5.1 / §3.6.1 / §3.6.2 / equipment_visuals.md §4/§5.
        var parts = ResolveOverlayParts(assets, appearance, skinClassId, debugLabel, out var missingGearSkns);

        // 7) build through the KEPT SkinnedCharacterBuilder (it applies the +90°-Z stand-up remap +
        //    feet-to-Y=0 recentre, so the actor stands upright + grounded + AABB-sane).
        Node3D actorRoot;
        try
        {
            if (parts.Count > 0)
                actorRoot = SkinnedCharacterBuilder.BuildWithEquipment(
                    bodyMesh, skeleton, idleClip, albedo,
                    false, 0f, parts, out _, debugLabel);
            else
                actorRoot = SkinnedCharacterBuilder.Build(bodyMesh, skeleton, idleClip, albedo);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SlotAppearanceResolver] {debugLabel}: SkinnedCharacterBuilder failed: {ex.Message}");
            return new SlotBuildResult(null, modelClassId, bodyMesh.IdB, skeleton is not null, idleClip is not null);
        }

        var missingReport = missingGearSkns.Count > 0
            ? $" MISSING gear .skn (logged+skipped, NOT fabricated): [{string.Join(", ", missingGearSkns)}]"
            : "";

        GD.Print(
            $"[SlotAppearanceResolver] {debugLabel}: built class={appearance.InternalClass} variant={appearance.AppearanceVariant} " +
            $"-> model_class_id={modelClassId} body='{bodySknPath}' (IdA={bodyMesh.IdA}, parsed-id_b={bodyMesh.IdB}) " +
            $"SkinClassId={skinClassId} rig={(skeleton is not null ? "g" + skinClassId + ".bnd" : "REST")} idle={(idleClip is not null ? "col16" : "BIND")} " +
            $"equip={parts.Count}.{missingReport} spec: skinning.md §3.5.2/§8(e); frontend_scenes.md §3.3.7/§3.7.5.");

        return new SlotBuildResult(actorRoot, modelClassId, bodyMesh.IdB, skeleton is not null, idleClip is not null);
    }

    // =========================================================================
    // Body mesh gid — §3.5.3 appearance-catalogue body lookup keyed by (slot=3, model_class_id).
    // Returns the col4 mesh gid for the base-category (col0==0) body row whose class key (col1) equals
    // the body model_class_id (IdB ∈ {1,11,16,26}), or null = DATA GAP (caller logs + skips). NEVER
    // returns a wrong-class gid. spec: skinning.md §3.5.3 / frontend_scenes.md §3.7.5.
    // =========================================================================

    private static int? ResolveBodyMeshGid(RealClientAssets assets, int bodyModelClassId, string debugLabel)
    {
        if (!assets.Contains(SkinTxtTablePath))
        {
            GD.PrintErr(
                $"[SlotAppearanceResolver] {debugLabel}: DATA GAP — '{SkinTxtTablePath}' absent; cannot resolve body for " +
                $"model_class_id={bodyModelClassId}. spec: skinning.md §3.5.3.");
            return null;
        }

        try
        {
            var catalogue = SkinTxtParser.Parse(assets.GetRaw(SkinTxtTablePath));
            // (slot=3, model_class_id) -> col4 body mesh gid. spec: skinning.md §3.5.3.
            return catalogue.GetBodyMeshGid(bodyModelClassId);
        }
        catch (Exception ex)
        {
            GD.PrintErr(
                $"[SlotAppearanceResolver] {debugLabel}: skin.txt body resolve failed (model_class_id={bodyModelClassId}): {ex.Message}");
            return null;
        }
    }

    // =========================================================================
    // Skeleton — g{SkinClassId}.bnd (§8(e); SkinClassId == descriptor InternalClass ∈ {1,2,3,4} →
    // g1..g4.bnd, the only .bnd that exist). NOT g{model_class_id}.bnd — there is no g11.bnd / g16.bnd.
    // Same rule as World/PlayerAvatarResolver.TryLoadSkeleton.
    // =========================================================================

    private static Skeleton? TryLoadSkeleton(RealClientAssets assets, int skinClassId, string debugLabel)
    {
        if (skinClassId <= 0)
        {
            GD.PrintErr(
                $"[SlotAppearanceResolver] {debugLabel}: invalid SkinClassId={skinClassId} — static rest pose. spec: skinning.md §8(e).");
            return null;
        }

        // g{SkinClassId}.bnd. spec: skinning.md §8(e) step 1 (for players the pose-pool lookup ==
        // loading g{SkinClassId}.bnd; each g{n}.bnd parses to actor_id n).
        var bndPath = $"data/char/bind/g{skinClassId}.bnd";

        if (!assets.Contains(bndPath))
        {
            GD.PrintErr(
                $"[SlotAppearanceResolver] {debugLabel}: .bnd absent '{bndPath}' (SkinClassId={skinClassId}) — rest pose.");
            return null;
        }

        try
        {
            var data = assets.GetRaw(bndPath);
            return data.IsEmpty ? null : BndParser.Parse(data);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SlotAppearanceResolver] {debugLabel}: BndParser failed '{bndPath}': {ex.Message}");
            return null;
        }
    }

    // =========================================================================
    // Idle clip — actormotion.txt row keyed by SkinClassId (col2 == skin_class) -> col16 -> g{id}.mot.
    // Keyed by SkinClassId ∈ {1,2,3,4}, NOT model_class_id {1,11,16,26} (col2 IS the SkinClassId, so
    // GetBySkinClass(model_class_id) would miss / mis-match — that was the slot-2 no-animation bug).
    // Same rule as World/PlayerAvatarResolver.ResolveIdleMotionId.
    // =========================================================================

    private static AnimationClip? TryLoadIdleClip(RealClientAssets assets, int skinClassId, string debugLabel)
    {
        if (!assets.Contains(ActormotionTablePath)) return null;

        try
        {
            // Key the actormotion row on SkinClassId (col2 == skin_class), first-occurrence-wins — the
            // SAME SkinClassId that selects the rig. spec: actormotion.md §Per-record layout (int_a @ 0x04);
            // skinning.md §8(e) (idle = actormotion col2 == SkinClassId -> col16).
            var catalogue = ActormotionParser.Parse(assets.GetRaw(ActormotionTablePath));
            var entry = catalogue.GetBySkinClass(skinClassId);
            if (entry is null)
            {
                GD.Print(
                    $"[SlotAppearanceResolver] {debugLabel}: no actormotion row with col2==SkinClassId={skinClassId} — bind pose (no idle). " +
                    "spec: skinning.md §8(e)/§10.");
                return null;
            }

            // Idle = motion_ids_a[1] = column 16 (record +0x44) — the runtime stand idle. col15 / a[0] /
            // +0x40 is statically DEAD (CYCLE 7 reversal). spec: actormotion.md §motion_ids_a; skinning.md §10.
            var idle = entry.IdleMotionId;
            if (idle <= 0)
            {
                GD.Print(
                    $"[SlotAppearanceResolver] {debugLabel}: actormotion row col2==SkinClassId={skinClassId} has empty col16 idle — bind pose. " +
                    "spec: skinning.md §10.");
                return null;
            }

            var motPath = $"data/char/mot/g{idle}.mot";
            if (!assets.Contains(motPath))
            {
                GD.PrintErr(
                    $"[SlotAppearanceResolver] {debugLabel}: idle .mot absent '{motPath}' (SkinClassId={skinClassId}, col16={idle}) — bind pose.");
                return null;
            }

            var motData = assets.GetRaw(motPath);
            return motData.IsEmpty ? null : AnimationParser.Parse(motData);
        }
        catch (Exception ex)
        {
            GD.PrintErr(
                $"[SlotAppearanceResolver] {debugLabel}: idle resolve failed (SkinClassId={skinClassId}): {ex.Message}");
            return null;
        }
    }

    // =========================================================================
    // model_class_id {1,11,16,26} -> SkinClassId {1,2,3,4} (diagnostic cross-check only). Mirrors the
    // {1->g1,26->g2,11->g3,16->g4} bnd edge: the g-number IS the SkinClassId. 0 = unmapped/invisible.
    // spec: skinning.md §8(e); login_flow.md §3.2.1.
    // =========================================================================

    private static int SkinClassForModelClassId(int modelClassId)
    {
        return modelClassId switch
        {
            1 => 1, // -> g1.bnd  (Musa)
            26 => 2, // -> g2.bnd (Salsu)
            11 => 3, // -> g3.bnd (Dosa)
            16 => 4, // -> g4.bnd (Monk)
            _ => 0 // invisible sentinel / unmapped
        };
    }

    // =========================================================================
    // Equip overlays {3,4,6,2,11,14} — §3.3.7 per-part resolution.
    // =========================================================================

    /// <summary>
    ///     Resolves the per-slot overlay parts to multi-surface-deform onto the shared skeleton plus the
    ///     rigid weapon. The non-weapon overlays {4,6,2,11} are returned as deform parts (the kept builder
    ///     calls <see cref="SkinnedCharacterNode.AttachDeformPart" />); the weapon (slot 14) is a rigid
    ///     hand-bone attach. The body is slot 3 — built as the PRIMARY mesh by the caller, NOT included
    ///     here (§3.5.1: there is no separate base mesh).
    ///     <para>
    ///         Overlay-gid SOURCE: the host-surfaced <see cref="SlotAppearance.EquipGids" /> when present
    ///         (resolved through §3.3.7), ELSE the §3.6.1 per-class STARTER overlay ids — spec-documented
    ///         values the char-select zoom path injects verbatim (NOT fabricated). Any gear <c>.skn</c>
    ///         genuinely ABSENT from the VFS is logged-and-skipped and its path appended to
    ///         <paramref name="missingGids" /> — never substituted, never invented.
    ///     </para>
    ///     spec: Docs/RE/specs/frontend_scenes.md §3.3.7 / §3.6.1 (starter overlay ids); skinning.md
    ///     §3.5.1 (body = slot 3, overlays share one skeleton) / §3.6.2; equipment_visuals.md §4 / §5.
    /// </summary>
    private static IReadOnlyList<SkinnedCharacterBuilder.EquipmentVisualPart> ResolveOverlayParts(
        RealClientAssets assets, SlotAppearance appearance, int skinClassId, string debugLabel,
        out List<string> missingGids)
    {
        var parts = new List<SkinnedCharacterBuilder.EquipmentVisualPart>();
        missingGids = [];

        var equip = appearance.EquipGids;
        var haveHostEquip = equip is not null && equip.Length > 0;

        // §3.3.7 slot-14 gid digits: d = faceA (+0x2E/+0x22), a = variant (+0x2C), b = class (+0x34).
        var dDigit = (int)appearance.FaceA;
        var aDigit = (int)appearance.AppearanceVariant;
        var bDigit = (int)appearance.InternalClass;

        // §3.6.1 per-class STARTER overlay ids for slots {3,4,6,2} (the char-select zoom path injects
        // these 4 per class). Slot 3 is the BODY (built as the primary mesh, NOT re-attached here). The
        // remaining {4,6,2} are the deform overlays. spec: frontend_scenes.md §3.6.1 (starter equip ids).
        var starter = StarterOverlayIds(skinClassId);

        for (var i = 0; i < ClassAppearanceResolver.OverlaySlots.Length; i++)
        {
            var slot = ClassAppearanceResolver.OverlaySlots[i];

            // BODY (slot 3) is the primary mesh — never an overlay part here. spec: skinning.md §3.5.1.
            if (slot == 3) continue;

            // Resolve this slot's part .skn path: prefer the host equip table; else the §3.6.1 starter.
            string? sknPath;
            long gid;
            if (haveHostEquip && i < equip!.Length && (int)equip[i] > 0)
            {
                gid = ClassAppearanceResolver.ResolvePartGid(slot, (int)equip[i], dDigit, aDigit, bDigit);
                sknPath = ClassAppearanceResolver.DeformSkinPathForGid(gid);
            }
            else if (starter is not null && StarterIdForSlot(starter.Value, slot) is { } starterGid)
            {
                gid = starterGid;
                sknPath = ClassAppearanceResolver.DeformSkinPathForGid(gid);
            }
            else
            {
                continue; // empty slot (no host id, no starter) — faithful skip, no node.
            }

            if (slot == 14)
            {
                // WEAPON (slot 14): a STATIC item-skin rigidly attached to the hand bone (NOT a deform
                // skin). The §3.6.1 starter set has no weapon, so this fires only with a host equip id.
                // spec: frontend_scenes.md §3.3.7 / equipment_visuals.md §5.
                if (!assets.Contains(sknPath))
                {
                    GD.Print(
                        $"[SlotAppearanceResolver] {debugLabel}: weapon .skn absent '{sknPath}' (gid={gid}) — slot 14 empty (logged+skipped, NOT fabricated).");
                    missingGids.Add(sknPath);
                    continue;
                }

                try
                {
                    var raw = assets.GetRaw(sknPath);
                    if (raw.IsEmpty) continue;
                    var weaponMesh = SknParser.Parse(raw);
                    var weaponTex = CharacterTextureResolver.Resolve(assets, weaponMesh.IdA);
                    parts.Add(new SkinnedCharacterBuilder.EquipmentVisualPart(
                        slot, weaponMesh, weaponTex,
                        true, false,
                        SkinnedCharacterNode.DefaultHandBoneId, 1.0f));
                    GD.Print(
                        $"[SlotAppearanceResolver] {debugLabel}: weapon overlay slot 14 -> g{gid}.skn (rigid hand attach). spec: §3.3.7.");
                }
                catch (Exception ex)
                {
                    GD.PrintErr(
                        $"[SlotAppearanceResolver] {debugLabel}: weapon slot 14 parse failed '{sknPath}': {ex.Message}");
                }

                continue;
            }

            // NON-WEAPON overlay {4,6,2,11}: a deform part on the SHARED skeleton (multi-surface). A
            // genuinely-absent .skn is LOGGED + skipped (NOT fabricated). spec: skinning.md §3.5.1 / §3.6.2.
            if (!assets.Contains(sknPath))
            {
                GD.Print(
                    $"[SlotAppearanceResolver] {debugLabel}: overlay slot {slot} .skn absent '{sknPath}' (gid={gid}) — logged+skipped (NOT fabricated).");
                missingGids.Add(sknPath);
                continue;
            }

            try
            {
                var raw = assets.GetRaw(sknPath);
                if (raw.IsEmpty) continue;
                var partMesh = SknParser.Parse(raw);

                // NOTE (NOT a skip): the §3.7.5/§3.5.1 overlay parts are class-FAMILY parts that can parse
                // to id_b == 1 (the class-1-family tag) even for classes 2/3/4 — the parsed id_b is NOT a
                // reliable rig key here (the descriptor SkinClassId is, §8(e)). The part is bound to THIS
                // node's shared rig by the base-relative id resolver (bone_array[id − base_id]) regardless,
                // and out-of-range weight ids degrade safely (SkinningMath skips/clamps). So a parsed-id_b
                // mismatch is SURFACED, not used to drop the part — exactly as the body does above. spec:
                // skinning.md §8(e) / §3.2 / frontend_scenes.md §3.7.5.
                if ((int)partMesh.IdB != skinClassId && partMesh.IdB != 0)
                    GD.Print(
                        $"[SlotAppearanceResolver] {debugLabel}: NOTE overlay slot {slot} '{sknPath}' parsed id_b={partMesh.IdB} " +
                        $"!= descriptor SkinClassId={skinClassId} (class-family part; rig bound by descriptor SkinClassId). spec: skinning.md §8(e).");

                var partTex = CharacterTextureResolver.Resolve(assets, partMesh.IdA);
                parts.Add(new SkinnedCharacterBuilder.EquipmentVisualPart(
                    slot, partMesh, partTex,
                    false, false,
                    SkinnedCharacterNode.DefaultHandBoneId, 1.0f));
                GD.Print(
                    $"[SlotAppearanceResolver] {debugLabel}: overlay slot {slot} -> g{gid}.skn (deform on shared rig, IdA={partMesh.IdA}). spec: §3.5.1/§3.6.2.");
            }
            catch (Exception ex)
            {
                GD.PrintErr(
                    $"[SlotAppearanceResolver] {debugLabel}: overlay slot {slot} parse failed '{sknPath}': {ex.Message}");
            }
        }

        return parts;
    }

    private static StarterOverlay? StarterOverlayIds(int skinClassId)
    {
        // spec: Docs/RE/specs/frontend_scenes.md §3.6.1 (zoom synthesised descriptor: 4 starter equip
        // ids per class for slots {3,4,6,2} = families 202/"b" / 203/"p" / 206/"s" / 209/"a").
        return skinClassId switch
        {
            1 => new StarterOverlay(202110003, 203110002, 206110002, 209110001), // Musa
            2 => new StarterOverlay(202220003, 203220002, 206220002, 209220001), // Salsu
            3 => new StarterOverlay(202130003, 203130002, 206130002, 209130001), // Dosa
            4 => new StarterOverlay(202140003, 203140002, 206140002, 209140001), // Monk
            _ => null
        };
    }

    private static long? StarterIdForSlot(StarterOverlay o, int slot)
    {
        return slot switch
        {
            4 => o.Slot4P, // family 203 ("p")
            6 => o.Slot6S, // family 206 ("s")
            2 => o.Slot2A, // family 209 ("a")
            _ => null // slot 11 (head) / 14 (weapon) have no §3.6.1 starter id
        };
    }

    /// <summary>
    ///     Immutable appearance descriptor for one preview slot — the subset of the §3.2 / §3.3.7
    ///     880-byte spawn descriptor the appearance chain needs. Engine-free (only primitives) so the
    ///     resolution is unit-testable independent of the Godot host's <c>SlotDescriptor</c> record.
    ///     <para>
    ///         <see cref="InternalClass" /> (descriptor +0x34, {1..4}) and <see cref="AppearanceVariant" />
    ///         (descriptor +0x2C) drive <c>model_class_id = 5*(class + 4*variant) - 24</c>; <see cref="FaceA" />
    ///         (descriptor +0x2E/+0x22) and <see cref="EquipGids" /> (descriptor +0x58 worn-item ids) drive
    ///         the §3.3.7 per-part overlay build.
    ///     </para>
    ///     spec: Docs/RE/specs/frontend_scenes.md §3.2 / §3.3.7; skinning.md §3.5.2.
    /// </summary>
    /// <param name="InternalClass">Internal class id (descriptor +0x34, {1,2,3,4}). Used VERBATIM — never offset to 0.</param>
    /// <param name="AppearanceVariant">Appearance variant (descriptor +0x2C). Variant 3 -> invisible sentinel.</param>
    /// <param name="FaceA">Face / slot-14 'd' byte (descriptor +0x2E / +0x22) — §3.3.7 slot-14 gid digit.</param>
    /// <param name="EquipGids">Worn-item ids (descriptor +0x58), or empty when the host could not surface them.</param>
    public readonly record struct SlotAppearance(
        uint InternalClass,
        uint AppearanceVariant,
        uint FaceA,
        uint[]? EquipGids);

    /// <summary>
    ///     The result of resolving + building a slot's appearance: the recentred actor root (or null when
    ///     the appearance could not be resolved — caller logs + skips, NO wrong-class fallback), plus the
    ///     resolved chain for the AABB-sanity loop and diagnostics.
    /// </summary>
    /// <param name="ActorRoot">The recentred skinned + animated actor, ready to parent under a placement wrapper.</param>
    /// <param name="ModelClassId">The descriptor-derived appearance key {1,11,16,26} (the skeleton selector).</param>
    /// <param name="ParsedIdB">The parsed body-.skn header id_b (for the mismatch diagnostic).</param>
    /// <param name="SkeletonResolved">True when the g{n}.bnd rig loaded; false = static rest pose.</param>
    /// <param name="IdleResolved">True when the col16 idle .mot loaded; false = bind pose, no animation.</param>
    public readonly record struct SlotBuildResult(
        Node3D? ActorRoot,
        int ModelClassId,
        uint ParsedIdB,
        bool SkeletonResolved,
        bool IdleResolved);

    // =========================================================================
    // §3.6.1 per-class STARTER overlay ids (slots 3/4/6/2) — the values the char-select ZOOM path
    // injects verbatim per class. Spec-documented (NOT fabricated). Slot 3 is the body (built as the
    // primary mesh, not re-attached); {4,6,2} are the deform overlays. spec: frontend_scenes.md §3.6.1.
    // =========================================================================

    /// <summary>One class's §3.6.1 starter overlay ids: slot 3 (body), 4 ("p"), 6 ("s"), 2 ("a").</summary>
    private readonly record struct StarterOverlay(int Slot3Body, int Slot4P, int Slot6S, int Slot2A);
}