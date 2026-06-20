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
// CYCLE 6b D6 — §3.3.7 RESOLVER MATH IMPLEMENTED; OVERLAY RENDERING BLOCKED ON HOST-API PLUMBING.
//
// §3.3.7 RE-CORRECTS the old blocker. The prior header here claimed the gate was "categoryBase[]
// pending". §3.3.7 says verbatim that for the PLAYER (PC) path the appearance key is the PURE formula
//   appearance_key = 5*(class + 4*variant) - 24
// with NO categoryBase term — so "categoryBase[] pending" is the WRONG blocker for the player path.
// The real recovered edge is the per-part 64-bit catalogue key + the per-slot gid reduction, all of
// which is implemented below (ResolvePartGid / ResolvePartSknPath / ComposeCatalogueKey64).
//
// WHAT IS STILL BLOCKED (and why) — NOT a missing RE fact, a missing HOST-API edge (a FOLLOW-UP wave):
//   The §3.3.7 overlays + non-starter-variant body are driven by the server 880-byte spawn descriptor:
//     * class       = descriptor +0x34       (one appearance byte)
//     * variant     = descriptor +0x2C       (the other appearance byte)
//     * slot-14 'd'  = descriptor +0x22
//     * equipment table = descriptor +0x58 (20 entries x 16 bytes, leading dword = worn-item id)
//   None of those raw bytes are plumbed into CharSelectScene3D: the host (CharSelectWindow, a DIFFERENT
//   lane) populates CharSelectScene3D.SlotDescriptors with only (bool IsOccupied, uint SkinClassId).
//   The resolver math below is therefore READY but cannot be FED the equipment/appearance bytes without
//   widening the SlotDescriptors tuple — a host-API change that belongs to the other lane / a follow-up
//   wave. Inventing equipment ids or appearance bytes would manufacture a missing fact (forbidden), so
//   the select lineup KEEPS the §3.7.5 starter-mesh fallback (class -> base .skn at variant 0) until the
//   raw descriptor is plumbed in. SknPathForClass / SknCandidatesForClass preserve that working path.
//
// WEAPON (slot 14 hand-weapon) — RIGID ATTACH FLAGGED, NOT WIRED. §3.3.7: the hand-weapon worn-item id
//   resolves to a STATIC item-skin attached to the HAND BONE (NOT a g{gid}.skn deform skin), dual-weapon
//   aware. The shared SkinnedCharacterBuilder (§3.3.6 factory) has NO rigid-attach entry point (it
//   composes ONE deform mesh + skeleton + idle clip). The rigid weapon attach is recovered-but-not-wired;
//   see the FLAG on ResolvePartSknPath. Implementing it would require a new builder entry point and the
//   hand-bone id — out of this lane's file set; do NOT fabricate it.
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
// spec: Docs/RE/specs/frontend_scenes.md §3.7.5 — the four confirmed-present starter meshes (IdA=1):
//         class 1 (tag 3,  Bichimi / Dosa)      -> g202110001
//         class 2 (tag 4,  Monk)                -> g203110001
//         class 3 (tag 6,  Archer)              -> g209110001
//         class 4 (tag 11, Sorceress/Summoner)  -> g206110001
// spec: Docs/RE/specs/skinning.md §3.5.2 — model_class_id = 5*(class + 4*variant) - 24, in {1,11,16,26};
//       variant == 3 -> 0 == invisible-actor sentinel.
// spec: Docs/RE/specs/login_flow.md §3.2.1 — IdB skeleton edge {1->g1, 26->g2, 11->g3, 16->g4}.
// spec: Docs/RE/specs/skinning.md §8(e) — the skin's id_b == model_class_id is the verbatim pose-pool key.

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// Resolves character appearance for the 3D front-end screens. Exposes the §3.3.7 per-part resolution
/// math (the appearance key, the per-slot gid reduction, the 64-bit catalogue key, the
/// <c>g{gid}.skn</c> load path), the model_class_id -&gt; skeleton edge, and the §3.7.5 starter-mesh
/// fallback shared by select and create so a class shows the identical body in both.
///
/// <para>CYCLE 6b D6: the §3.3.7 resolver math is implemented and ready, but the equipment-overlay +
/// non-starter-variant lineup is BLOCKED on plumbing the raw 880-byte spawn descriptor (equip table
/// +0x58, appearance bytes +0x2C/+0x34, slot-14 +0x22) into <c>CharSelectScene3D.SlotDescriptors</c>
/// — a host-API change owned by another lane. Until then the lineup keeps the §3.7.5 starter fallback.
/// See the file header for the precise host-API gap and the rigid-weapon FLAG.</para>
///
/// spec: Docs/RE/specs/frontend_scenes.md §3.3.7 / §3.3.1 / §3.3.6 / §3.7.5; skinning.md §3.5.2 / §8(e);
///       login_flow.md §3.2.1.
/// </summary>
internal static class ClassAppearanceResolver
{
    // §3.3.7 overlay slot set the factory iterates — order as the spec lists them. The body/face is
    // slot 14 (reduced from the appearance bytes); {3,4,6,2,11} are worn-item overlays; 14 also carries
    // the hand-weapon for the rigid path (handled separately). spec: frontend_scenes.md §3.3.7.
    public static readonly int[] OverlaySlots = [3, 4, 6, 2, 11, 14]; // spec: §3.3.7

    // The §3.3.7 64-bit key radices, as code immediates from the spec formula:
    //   key64 = gid + 1_000_000_000 * (slot + 100 * appearance_key).
    private const long Slot14BodySlot = 14; // body / face / visible-base overlay slot. spec: §3.3.7
    private const long CatalogueKeyGidRadix = 1_000_000_000L; // spec: §3.3.7 (1e9 * (slot + 100*key))
    private const long CatalogueKeySlotRadix = 100L; // spec: §3.3.7 (slot + 100 * appearance_key)

    /// <summary>
    /// The appearance/skeleton selector for an (internalClass, appearanceVariant) pair:
    /// <c>appearance_key = 5 * (class + 4 * variant) - 24</c>. For the four starter classes at
    /// variant 0 this yields the appearance-slot identity in {1, 11, 16, 26}. <c>variant == 3</c>
    /// yields <c>0</c>, the reserved invisible-actor sentinel (no mesh). For the PC path this formula
    /// is PURE — there is NO categoryBase term (§3.3.7 corrects the old "categoryBase[] pending" note).
    /// spec: Docs/RE/specs/frontend_scenes.md §3.3.7 / Docs/RE/specs/skinning.md §3.5.2.
    /// </summary>
    public static int ModelClassId(int internalClass, int appearanceVariant) =>
        5 * (internalClass + 4 * appearanceVariant) - 24; // spec: frontend_scenes.md §3.3.7 / skinning.md §3.5.2

    /// <summary>
    /// Maps a <see cref="ModelClassId"/> (the skin's <c>id_b</c>) to its deform skeleton
    /// <c>data/char/bind/g{n}.bnd</c> via the data-driven edge {1-&gt;g1, 26-&gt;g2, 11-&gt;g3, 16-&gt;g4}.
    /// This is the verbatim pose-pool key (the skin's <c>id_b</c> selects the rig, §8(e)). Returns
    /// <c>null</c> for an unmapped id (incl. the <c>0</c> invisible sentinel) — caller logs + skips.
    /// spec: Docs/RE/specs/login_flow.md §3.2.1 / Docs/RE/specs/skinning.md §8(e).
    /// </summary>
    public static string? SkeletonBndForModelClassId(int modelClassId) => modelClassId switch
    {
        1 => "data/char/bind/g1.bnd", // spec: login_flow.md §3.2.1  {1->g1}
        26 => "data/char/bind/g2.bnd", // spec: login_flow.md §3.2.1  {26->g2}
        11 => "data/char/bind/g3.bnd", // spec: login_flow.md §3.2.1  {11->g3}
        16 => "data/char/bind/g4.bnd", // spec: login_flow.md §3.2.1  {16->g4}
        _ => null, // 0 = invisible sentinel / unmapped -> caller skips
    };

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // §3.3.7 per-part appearance resolution math (the recovered edge). Pure functions — no IO, no
    // Godot types — so they are testable and ready to drive the overlay build once the raw descriptor
    // is plumbed into the select scene (see file-header host-API gap).
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reduces the slot-14 body / face / visible-base gid from the appearance bytes:
    /// <c>gid = 1000 * (d + 10 * (a + 10 * (b + 10 * (partId / 1_000_000))))</c>, where
    /// <paramref name="d"/> = descriptor +0x22, and <paramref name="a"/> / <paramref name="b"/> are the
    /// two appearance bytes (descriptor +0x2C / +0x34). The face/visible-base folds into these digits.
    ///
    /// CONFLICT (debugger-pending): gid digit-label order (class vs variant) per frontend_scenes.md
    /// §3.3.7 — byte sources pinned, label order unverified. The build-part routine labels the two
    /// bytes OPPOSITE to the key formula; this implementation uses the documented byte SOURCES (+0x2C
    /// as <c>a</c>, +0x34 as <c>b</c>) — which digit is "class" vs "variant" is debugger-pending.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.3.7 (slot-14 gid reduction).
    /// </summary>
    public static long ResolveBodyGidSlot14(int d, int a, int b, int partId)
    {
        // CONFLICT (debugger-pending): gid digit-label order (class vs variant) per frontend_scenes.md
        // §3.3.7 — byte sources pinned, label order unverified.
        return 1000L * (d + 10L * (a + 10L * (b + 10L * (partId / 1_000_000L)))); // spec: §3.3.7 slot-14
    }

    /// <summary>
    /// Reduces a worn-item overlay gid for slots {3, 4, 6, 2, 11}:
    /// <c>gid = 10000 * (partId / 10000) + partId % 100</c>, where <paramref name="partId"/> is the
    /// worn-item id taken from the descriptor equipment table (descriptor +0x58, 20 entries x 16 bytes,
    /// each entry's leading dword = a worn-item id).
    /// spec: Docs/RE/specs/frontend_scenes.md §3.3.7 (other slots {3,4,6,2,11}).
    /// </summary>
    public static long ResolveWornItemGid(int partId) =>
        10000L * (partId / 10000) + partId % 100; // spec: §3.3.7 worn-item gid reduction

    /// <summary>
    /// Composes the §3.3.7 64-bit per-part catalogue key for a resolved part:
    /// <c>key64 = gid + 1_000_000_000 * (slot + 100 * appearance_key)</c>. The catalogue is keyed by
    /// this value to obtain the part's skin handle. Pure arithmetic; the catalogue lookup itself lives
    /// with the appearance catalogue (not in this layer).
    /// spec: Docs/RE/specs/frontend_scenes.md §3.3.7 (per-part overlay build).
    /// </summary>
    public static long ComposeCatalogueKey64(int slot, int appearanceKey, long gid) =>
        gid + CatalogueKeyGidRadix * (slot + CatalogueKeySlotRadix * appearanceKey); // spec: §3.3.7

    /// <summary>
    /// Resolves the per-part gid for a §3.3.7 overlay slot. Slot 14 uses the appearance-byte reduction
    /// (<see cref="ResolveBodyGidSlot14"/>); slots {3,4,6,2,11} use the worn-item reduction
    /// (<see cref="ResolveWornItemGid"/>). <paramref name="d"/>/<paramref name="a"/>/<paramref name="b"/>
    /// are only consulted for slot 14.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.3.7.
    /// </summary>
    public static long ResolvePartGid(int slot, int partId, int d, int a, int b) =>
        slot == Slot14BodySlot
            ? ResolveBodyGidSlot14(d, a, b, partId) // spec: §3.3.7 slot-14 body reduction
            : ResolveWornItemGid(partId); // spec: §3.3.7 worn-item reduction

    /// <summary>
    /// The deform-skin VFS path for a resolved per-part gid: <c>data/char/skin/g{gid}.skn</c> (the
    /// inverse-bind-baked skin per <c>specs/skinning.md</c>). This is the DEFORM overlay path used by
    /// slots {3, 4, 6, 2, 11} and slot 14 (body).
    ///
    /// FLAG (recovered-but-NOT-wired) — RIGID WEAPON ATTACH. §3.3.7: the slot-14 HAND-WEAPON worn-item
    /// id resolves to a STATIC item-skin attached to the HAND BONE (NOT a <c>g{gid}.skn</c> deform
    /// skin), dual-weapon aware. The shared <c>SkinnedCharacterBuilder</c> (§3.3.6 factory) has no
    /// rigid-attach entry point, so the rigid weapon attach is recovered-but-not-wired. Do NOT fabricate
    /// it; it needs a new builder entry point + the hand-bone id (a follow-up wave, out of this lane's
    /// file set). spec: Docs/RE/specs/frontend_scenes.md §3.3.7 (weapons = separate rigid hand path).
    /// </summary>
    public static string DeformSkinPathForGid(long gid) =>
        $"data/char/skin/g{gid}.skn"; // spec: frontend_scenes.md §3.3.7 (deform overlay load path)

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // §3.7.5 starter-mesh fallback (the spec-grounded path the select lineup uses until the raw
    // descriptor is plumbed in so the §3.3.7 overlay build above can run). KEEP working.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Base-skin <c>.skn</c> path for an (internalClass, appearanceVariant) appearance.
    ///
    /// Until the raw 880-byte spawn descriptor is plumbed into the select scene, the per-part §3.3.7
    /// overlay build cannot be FED its equipment/appearance bytes, so any appearance resolves to its
    /// class's §3.7.5 starter body (variant 0, IdA=1). This is the spec-grounded fallback — NOT an
    /// invented catalogue row. The <see cref="ModelClassId"/> selector and the full §3.3.7 math above
    /// are ready for the follow-up wave that widens <c>SlotDescriptors</c>.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.7.5 (starter set) / §3.3.7 (overlay build — host-API
    ///       plumbing pending; see file header).
    /// </summary>
    public static string? SknPathForAppearance(int internalClass, int appearanceVariant)
    {
        // appearance_key is computed (and surfaced for the rig via ModelClassId), but the per-part
        // catalogue select needs the equipment table + appearance bytes, none of which is plumbed into
        // the select scene yet. So resolve only the spec-grounded starter body for the class.
        // spec: frontend_scenes.md §3.3.7 (do NOT invent the equipment ids / appearance bytes).
        _ = appearanceVariant; // variant is reserved for the §3.3.7 overlay build (host-API pending)
        return StarterSknForClass(internalClass);
    }

    /// <summary>
    /// Returns the base-skin <c>.skn</c> path for class id 1..4, or <c>null</c> for any other id
    /// (the caller logs + skips — never substitutes a wrong-class mesh). Routes through the
    /// appearance path at variant 0 (the starter appearance). Each mesh carries its OWN id_b which
    /// then drives its rig (<c>g{id_b}.bnd</c>, see <see cref="SkeletonBndForModelClassId"/>) and
    /// idle clip (actormotion col15).
    /// spec: Docs/RE/specs/frontend_scenes.md §3.7.5.
    /// </summary>
    public static string? SknPathForClass(int classId) => SknPathForAppearance(classId, 0);

    /// <summary>
    /// Candidate list form (single confirmed mesh) for the select-screen path that probes
    /// <c>assets.Contains</c> over a candidate set. Returns an empty array for an unknown id.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.7.5.
    /// </summary>
    public static string[] SknCandidatesForClass(int classId)
    {
        string? path = SknPathForClass(classId);
        return path is null ? [] : [path];
    }

    /// <summary>
    /// The §3.7.5 confirmed-present starter mesh for internal class 1..4 (default appearance IdA=1),
    /// or <c>null</c> for an unknown id. Internal class 1..4 corresponds to select tags {3,4,6,11}.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.7.5.
    /// </summary>
    private static string? StarterSknForClass(int classId) => classId switch
    {
        1 => "data/char/skin/g202110001.skn", // §3.7.5 (tag 3)  Bichimi / Dosa starter mesh
        2 => "data/char/skin/g203110001.skn", // §3.7.5 (tag 4)  Monk starter mesh
        3 => "data/char/skin/g209110001.skn", // §3.7.5 (tag 6)  Archer starter mesh
        4 => "data/char/skin/g206110001.skn", // §3.7.5 (tag 11) Sorceress / Summoner starter mesh
        _ => null, // unknown class -> caller logs + skips (no fallback)
    };
}
