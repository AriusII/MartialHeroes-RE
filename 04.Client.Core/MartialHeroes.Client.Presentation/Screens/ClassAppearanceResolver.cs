// Screens/ClassAppearanceResolver.cs
//
// ONE shared appearance resolver for BOTH 3D front-end screens (character-select and
// character-create). It owns:
//   * the appearance/skeleton selector formula (ModelClassId, §3.3.7 / §3.5.2),
//   * the model_class_id -> skeleton edge (IdB -> g{n}.bnd, login_flow.md §3.2.1 / skinning.md §8(e)),
//   * the §3.7.5 confirmed starter-mesh table (the spec-grounded fallback body per class), AND
//   * the FULL §3.3.7 per-part appearance resolution math (the 64-bit catalogue key + slot-14 gid
//     reduction + the {3,4,6,2,11} worn-item gid reduction + the g{gid}.skn load path).
//
// ───────────────────────────────────────────────────────────────────────────────────────────────
// Pure, engine-free, headless resolver. Every formula/constant below is verified against the cited
// spec text. For the PLAYER (PC) path the appearance key is the PURE formula
//   appearance_key = 5*(class + 4*variant) - 24   (no categoryBase term; §3.5.2 / §3.3.7)
// and the per-part overlay uses the 64-bit catalogue key + per-slot gid reduction (implemented below).
//
// The per-class BODY resolves through the §3.5.3 appearance catalogue (keyed by (slot=3,
// model_class_id)) via the §3.7.5 starter variant {1,2,1,1} -> IdB {1,26,11,16}; this file owns the
// pure key inputs (StarterAppearanceVariant / StarterBodyModelClassId) + the gid -> .skn path
// (BodySknPathForMeshGid). The caller (layer 05) does the IO-bearing catalogue lookup with the actual
// per-slot descriptor bytes; this layer never reads a descriptor or invents an equipment id.
//
// FLAG (spec-justified, recovered-but-NOT-wired) — RIGID WEAPON ATTACH. §3.3.7: the slot-14 HAND-WEAPON
// worn-item id resolves to a STATIC item-skin attached to the HAND BONE (NOT a g{gid}.skn deform skin),
// dual-weapon aware. The shared SkinnedCharacterBuilder (§3.3.6 factory) has NO rigid-attach entry
// point, so the rigid weapon attach is recovered-but-not-wired (a follow-up wave). Do NOT fabricate it;
// it needs a new builder entry point + the hand-bone id. See the FLAG on DeformSkinPathForGid.
// ───────────────────────────────────────────────────────────────────────────────────────────────
//
// spec: Docs/RE/specs/frontend_scenes.md §3.3.7 — per-part appearance resolution math (the new edge):
//         appearance_key = 5*(class + 4*variant) - 24
//         key64 = gid + 1e9 * (slot + 100 * appearance_key)  for overlay slots {3,4,6,2,11,14}
//         slot 14: gid = 1000*(d + 10*(a + 10*(b + 10*(partId/1_000_000))))   d=+0x22, a=+0x2C, b=+0x34
//         slots {3,4,6,2,11}: gid = 10000*(partId/10000) + partId%100, partId = worn-item id (+0x58 table)
//         skin load: data/char/skin/g{gid}.skn (deform); weapon = SEPARATE rigid hand-bone attach.
// spec: Docs/RE/specs/frontend_scenes.md §3.3.1 — per-slot placement; scales 70 (lineup) / 81 (create).
// spec: Docs/RE/specs/frontend_scenes.md §3.3.6 — shared actor factory (list slots + create + player).
// spec: Docs/RE/specs/frontend_scenes.md §3.7.5 (CORRECTED 2026-06-22, binary wins) — the per-class
//         BODY is resolved through the §3.5.3 appearance catalogue keyed by (slot=3, model_class_id),
//         NOT a hard-coded class->skn table. The four starter classes carry variants {1,2,1,1} for
//         classes {1,2,3,4} -> model_class_id (IdB) {1,26,11,16} -> four DISTINCT body mesh gids:
//         class 1 Musa  (variant 1, IdB 1)  -> g202110001  (skin.txt col0=0,col2=3,col1=1)
//         class 2 Salsu (variant 2, IdB 26) -> g202220001  (skin.txt col0=0,col2=3,col1=26)
//         class 3 Dosa  (variant 1, IdB 11) -> g202130001  (skin.txt col0=0,col2=3,col1=11)
//         class 4 Monk  (variant 1, IdB 16) -> g202140001  (skin.txt col0=0,col2=3,col1=16)
//         The PRIOR table (g203110001 / g209110001 / g206110001) read the col2={4,6,11} class-1-family
//         OUTFIT rows (all col1==1 / Musa) — the wrong-key bug that made every slot a class-1 body
//         (slot-2 Dosa = a flat Musa slab). spec: frontend_scenes.md §3.7.5 "Port bug diagnosis".
// spec: Docs/RE/specs/skinning.md §3.5.2 — model_class_id = 5*(class + 4*variant) - 24, in {1,11,16,26};
//       variant == 3 -> 0 == invisible-actor sentinel.
// spec: Docs/RE/specs/login_flow.md §3.2.1 — IdB skeleton edge {1->g1, 26->g2, 11->g3, 16->g4}.
// spec: Docs/RE/specs/skinning.md §8(e) — the skin's id_b == model_class_id is the verbatim pose-pool key.

namespace MartialHeroes.Client.Presentation.Screens;

/// <summary>
///     Resolves character appearance for the 3D front-end screens. Exposes the §3.3.7 per-part resolution
///     math (the appearance key, the per-slot gid reduction, the 64-bit catalogue key, the
///     <c>g{gid}.skn</c> load path), the model_class_id -&gt; skeleton edge, and the §3.7.5 starter-mesh
///     body resolution shared by select and create so a class shows the identical body in both. All members
///     are pure, engine-free, headless functions of their explicit inputs — the IO-bearing catalogue lookup
///     and the actual per-slot descriptor bytes belong to the caller (layer 05). The rigid slot-14 weapon
///     attach is a spec-justified FLAG (recovered-but-not-wired) — see the file header.
///     spec: Docs/RE/specs/frontend_scenes.md §3.3.7 / §3.3.1 / §3.3.6 / §3.7.5; skinning.md §3.5.2 / §8(e);
///     login_flow.md §3.2.1.
/// </summary>
public static class ClassAppearanceResolver
{
    // The §3.3.7 64-bit key radices, as code immediates from the spec formula:
    //   key64 = gid + 1_000_000_000 * (slot + 100 * appearance_key).
    private const long Slot14BodySlot = 14; // body / face / visible-base overlay slot. spec: §3.3.7
    private const long CatalogueKeyGidRadix = 1_000_000_000L; // spec: §3.3.7 (1e9 * (slot + 100*key))

    private const long CatalogueKeySlotRadix = 100L; // spec: §3.3.7 (slot + 100 * appearance_key)

    // §3.3.7 overlay slot set the factory iterates — order as the spec lists them. The body/face is
    // slot 14 (reduced from the appearance bytes); {3,4,6,2,11} are worn-item overlays; 14 also carries
    // the hand-weapon for the rigid path (handled separately). spec: frontend_scenes.md §3.3.7.
    public static readonly int[] OverlaySlots = [3, 4, 6, 2, 11, 14]; // spec: §3.3.7

    /// <summary>
    ///     The appearance/skeleton selector for an (internalClass, appearanceVariant) pair:
    ///     <c>appearance_key = 5 * (class + 4 * variant) - 24</c>. For the four starter classes at
    ///     variant 0 this yields the appearance-slot identity in {1, 11, 16, 26}. <c>variant == 3</c>
    ///     yields <c>0</c>, the reserved invisible-actor sentinel (no mesh). For the PC path this formula
    ///     is PURE — there is NO categoryBase term (§3.3.7 corrects the old "categoryBase[] pending" note).
    ///     spec: Docs/RE/specs/frontend_scenes.md §3.3.7 / Docs/RE/specs/skinning.md §3.5.2.
    /// </summary>
    public static int ModelClassId(int internalClass, int appearanceVariant)
    {
        // variant == 3 is the reserved INVISIBLE-ACTOR sentinel: it resolves to 0 (no mesh), NOT the raw
        // formula output. This is an explicit spec rule, not a natural formula result. spec:
        // Docs/RE/specs/skinning.md §3.5.2 (variant == 3 -> 0, invisible sentinel).
        if (appearanceVariant == 3) return 0;

        return 5 * (internalClass + 4 * appearanceVariant) - 24;
        // spec: frontend_scenes.md §3.3.7 / skinning.md §3.5.2 (model_class_id = 5*(class + 4*variant) - 24)
    }

    /// <summary>
    ///     Maps a <see cref="ModelClassId" /> (the skin's <c>id_b</c>) to its deform skeleton
    ///     <c>data/char/bind/g{n}.bnd</c> via the data-driven edge {1-&gt;g1, 26-&gt;g2, 11-&gt;g3, 16-&gt;g4}.
    ///     This is the verbatim pose-pool key (the skin's <c>id_b</c> selects the rig, §8(e)). Returns
    ///     <c>null</c> for an unmapped id (incl. the <c>0</c> invisible sentinel) — caller logs + skips.
    ///     spec: Docs/RE/specs/login_flow.md §3.2.1 / Docs/RE/specs/skinning.md §8(e).
    /// </summary>
    public static string? SkeletonBndForModelClassId(int modelClassId)
    {
        return modelClassId switch
        {
            1 => "data/char/bind/g1.bnd", // spec: login_flow.md §3.2.1  {1->g1}
            26 => "data/char/bind/g2.bnd", // spec: login_flow.md §3.2.1  {26->g2}
            11 => "data/char/bind/g3.bnd", // spec: login_flow.md §3.2.1  {11->g3}
            16 => "data/char/bind/g4.bnd", // spec: login_flow.md §3.2.1  {16->g4}
            _ => null // 0 = invisible sentinel / unmapped -> caller skips
        };
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // §3.3.7 per-part appearance resolution math (the recovered edge). Pure functions — no IO, no
    // Godot types — so they are testable and ready to drive the overlay build once the raw descriptor
    // is plumbed into the select scene (see file-header host-API gap).
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Reduces the slot-14 body / face / visible-base gid from the appearance bytes:
    ///     <c>gid = 1000 * (d + 10 * (a + 10 * (b + 10 * (partId / 1_000_000))))</c>, where
    ///     <paramref name="d" /> = descriptor +0x22, and <paramref name="a" /> / <paramref name="b" /> are the
    ///     two appearance bytes (descriptor +0x2C / +0x34). The face/visible-base folds into these digits.
    ///     CONFLICT (debugger-pending): gid digit-label order (class vs variant) per frontend_scenes.md
    ///     §3.3.7 — byte sources pinned, label order unverified. The build-part routine labels the two
    ///     bytes OPPOSITE to the key formula; this implementation uses the documented byte SOURCES (+0x2C
    ///     as <c>a</c>, +0x34 as <c>b</c>) — which digit is "class" vs "variant" is debugger-pending.
    ///     spec: Docs/RE/specs/frontend_scenes.md §3.3.7 (slot-14 gid reduction).
    /// </summary>
    public static long ResolveBodyGidSlot14(int d, int a, int b, int partId)
    {
        // CONFLICT (debugger-pending): gid digit-label order (class vs variant) per frontend_scenes.md
        // §3.3.7 — byte sources pinned, label order unverified.
        return 1000L * (d + 10L * (a + 10L * (b + 10L * (partId / 1_000_000L)))); // spec: §3.3.7 slot-14
    }

    /// <summary>
    ///     Reduces a worn-item overlay gid for slots {3, 4, 6, 2, 11}:
    ///     <c>gid = 10000 * (partId / 10000) + partId % 100</c>, where <paramref name="partId" /> is the
    ///     worn-item id taken from the descriptor equipment table (descriptor +0x58, 20 entries x 16 bytes,
    ///     each entry's leading dword = a worn-item id).
    ///     spec: Docs/RE/specs/frontend_scenes.md §3.3.7 (other slots {3,4,6,2,11}).
    /// </summary>
    public static long ResolveWornItemGid(int partId)
    {
        return 10000L * (partId / 10000) + partId % 100;
        // spec: §3.3.7 worn-item gid reduction
    }

    /// <summary>
    ///     Composes the §3.3.7 64-bit per-part catalogue key for a resolved part:
    ///     <c>key64 = gid + 1_000_000_000 * (slot + 100 * appearance_key)</c>. The catalogue is keyed by
    ///     this value to obtain the part's skin handle. Pure arithmetic; the catalogue lookup itself lives
    ///     with the appearance catalogue (not in this layer).
    ///     spec: Docs/RE/specs/frontend_scenes.md §3.3.7 (per-part overlay build).
    /// </summary>
    public static long ComposeCatalogueKey64(int slot, int appearanceKey, long gid)
    {
        return gid + CatalogueKeyGidRadix * (slot + CatalogueKeySlotRadix * appearanceKey);
        // spec: §3.3.7
    }

    /// <summary>
    ///     Resolves the per-part gid for a §3.3.7 overlay slot. Slot 14 uses the appearance-byte reduction
    ///     (<see cref="ResolveBodyGidSlot14" />); slots {3,4,6,2,11} use the worn-item reduction
    ///     (<see cref="ResolveWornItemGid" />). <paramref name="d" />/<paramref name="a" />/<paramref name="b" />
    ///     are only consulted for slot 14.
    ///     spec: Docs/RE/specs/frontend_scenes.md §3.3.7.
    /// </summary>
    public static long ResolvePartGid(int slot, int partId, int d, int a, int b)
    {
        return slot == Slot14BodySlot
            ? ResolveBodyGidSlot14(d, a, b, partId) // spec: §3.3.7 slot-14 body reduction
            : ResolveWornItemGid(partId);
        // spec: §3.3.7 worn-item reduction
    }

    /// <summary>
    ///     The deform-skin VFS path for a resolved per-part gid: <c>data/char/skin/g{gid}.skn</c> (the
    ///     inverse-bind-baked skin per <c>specs/skinning.md</c>). This is the DEFORM overlay path used by
    ///     slots {3, 4, 6, 2, 11} and slot 14 (body).
    ///     FLAG (recovered-but-NOT-wired) — RIGID WEAPON ATTACH. §3.3.7: the slot-14 HAND-WEAPON worn-item
    ///     id resolves to a STATIC item-skin attached to the HAND BONE (NOT a <c>g{gid}.skn</c> deform
    ///     skin), dual-weapon aware. The shared <c>SkinnedCharacterBuilder</c> (§3.3.6 factory) has no
    ///     rigid-attach entry point, so the rigid weapon attach is recovered-but-not-wired. Do NOT fabricate
    ///     it; it needs a new builder entry point + the hand-bone id (a follow-up wave, out of this lane's
    ///     file set). spec: Docs/RE/specs/frontend_scenes.md §3.3.7 (weapons = separate rigid hand path).
    /// </summary>
    public static string DeformSkinPathForGid(long gid)
    {
        return $"data/char/skin/g{gid}.skn";
        // spec: frontend_scenes.md §3.3.7 (deform overlay load path)
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // §3.7.5 per-class BODY resolution (CORRECTED 2026-06-22). The body is NOT a hard-coded class->skn
    // table; it is the §3.5.3 appearance-catalogue body row keyed by (slot=3, model_class_id), found
    // via SkinTxtCatalog.GetBodyMeshGid (the catalogue layer owns the table). This file owns only the
    // pure inputs to that key: the per-class starter variant (so the model_class_id / IdB is correct)
    // and the gid -> .skn path formatting. The caller does the IO-bearing catalogue lookup.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     The §3.7.5 starter appearance variant for internal class 1..4, as stamped by the client
    ///     (<c>SelectWindow_WriteSlotRecord</c>): variant <c>{1, 2, 1, 1}</c> for classes
    ///     <c>{1, 2, 3, 4}</c>. With <see cref="ModelClassId" /> this yields the four DISTINCT starter
    ///     IdBs <c>{1, 26, 11, 16}</c> — the corrected per-class body key. Returns <c>0</c> for an
    ///     unknown class (caller treats as no body). NOTE: the host currently leaves the descriptor
    ///     <c>variant</c> (+0x2C) at 0 (host-API gap), so this spec-documented starter variant is used
    ///     for the select lineup until the raw descriptor is plumbed in — it is NOT an invented value.
    ///     spec: Docs/RE/specs/frontend_scenes.md §3.7.5 (starter variants {1,2,1,1}).
    /// </summary>
    public static int StarterAppearanceVariant(int internalClass)
    {
        return internalClass switch
        {
            1 => 1, // Musa  -> model_class_id 1
            2 => 2, // Salsu -> model_class_id 26
            3 => 1, // Dosa  -> model_class_id 11
            4 => 1, // Monk  -> model_class_id 16
            _ => 0 // unknown class -> caller logs + skips
        };
    }

    /// <summary>
    ///     The body <c>model_class_id</c> (IdB) for internal class 1..4 at its §3.7.5 starter variant —
    ///     the catalogue body key. Composes <see cref="StarterAppearanceVariant" /> with
    ///     <see cref="ModelClassId" />: <c>{1→1, 2→26, 3→11, 4→16}</c>. Returns <c>0</c> for an unknown
    ///     class. This is the value handed to <c>SkinTxtCatalog.GetBodyMeshGid</c> (slot 3 implied).
    ///     spec: Docs/RE/specs/frontend_scenes.md §3.7.5 / skinning.md §3.5.2 / §3.5.3.
    /// </summary>
    public static int StarterBodyModelClassId(int internalClass)
    {
        var variant = StarterAppearanceVariant(internalClass);
        return variant == 0 ? 0 : ModelClassId(internalClass, variant);
    }

    /// <summary>
    ///     The body <c>.skn</c> VFS path for a resolved body mesh gid: <c>data/char/skin/g{gid}.skn</c>.
    ///     The gid comes from <c>SkinTxtCatalog.GetBodyMeshGid(model_class_id)</c> (§3.5.3). Distinct per
    ///     class: <c>{1→g202110001, 26→g202220001, 11→g202130001, 16→g202140001}</c>.
    ///     spec: Docs/RE/specs/frontend_scenes.md §3.7.5 / skinning.md §3.5.3.
    /// </summary>
    public static string BodySknPathForMeshGid(int meshGid)
    {
        return $"data/char/skin/g{meshGid}.skn";
        // spec: frontend_scenes.md §3.7.5 (body .skn load path)
    }
}