// World/EquipOverlayResolver.cs
//
// Pure, engine-free (NO `using Godot;`) implementation of the IN-WORLD equip-overlay part-mesh GID
// derivation — the per-part avatar recomposition model recovered in equipment_visuals.md (the
// "equipping does NOT swap the body" headline). This is the WORLD-scene equip-rebuild resolver,
// distinct from the char-select ClassAppearanceResolver (frontend_scenes §3.3.7) which owns the
// front-end lineup. The two share the same family of formulas but the in-world rebuild pins three
// CYCLE 11 constants the char-select path does not model: the skin-level THRESHOLD 1000 short-circuit
// (which selects the full vs reduced slot set), the non-weapon GID scale 10000, and the weapon
// base-1000 digit formula (with the +partId%1000 add-back the char-select reducer omits).
//
// Every formula/constant below cites equipment_visuals.md. This file owns only the deterministic GID
// math + the slot-set selection + the 64-bit catalogue key; the IO-bearing catalog lookup (the GID ->
// skin map, the 47-entry categoryBase per-row values loaded from the item/skin script) and the actual
// skeleton attach belong to the caller (layer 05). No descriptor is read here and no equipment id is
// invented.
//
// spec: Docs/RE/specs/equipment_visuals.md §1.1 (two rebuild paths; threshold-1000 slot set),
//       §3.1 (weapon base-1000 digit formula), §3.2 (non-weapon scale 10000 + 64-bit key),
//       §3.3 / §3.4 (skin-level threshold = 1000; non-weapon GID scale = 10000; categoryBase 47-entry;
//       hand bone-id stays 0 / debugger-pending), §5 / §5.1 (weapon attach + dual-wield node flags).

namespace MartialHeroes.Client.Presentation.World;

/// <summary>
///     Pure, engine-free resolver for the in-world equip-overlay part-mesh recomposition. Reproduces the
///     <c>equipment_visuals.md</c> GID-derivation model: the skin-level threshold-1000 short-circuit that
///     selects the full <c>{3,4,6,2,11,14}</c> vs reduced <c>{3}</c> slot set, the non-weapon scale-10000
///     GID formula, the weapon base-1000 digit formula, and the 64-bit catalogue key. The 47-entry
///     <c>categoryBase</c> array SHAPE is modelled (the per-row base terms are data-driven — loaded from
///     the item/skin script — so the caller supplies them); the weapon hand bone-id is built as <c>0</c>
///     (debugger-pending). spec: Docs/RE/specs/equipment_visuals.md §1.1 / §3 / §5.
/// </summary>
public static class EquipOverlayResolver
{
    /// <summary>
    ///     The animation/visual catalog base-skin-id threshold. Equip-overlay catalog resolution runs only
    ///     when an actor's <c>base_skin_id</c> (Visual +108) is <c>&lt;= 1000</c>; above 1000 it is skipped.
    ///     spec: Docs/RE/specs/equipment_visuals.md §3.4 (skin-level threshold = 1000, CODE-CONFIRMED).
    /// </summary>
    public const int SkinLevelThreshold = 1000; // spec: equipment_visuals.md §3.4

    /// <summary>
    ///     The integer scale used by the non-weapon GID formula (§3.2):
    ///     <c>gid = 10000 * (partId / 10000) + (partId % 100)</c>. A discrete constant in the
    ///     animation-catalog GID computation path, distinct from the weapon formula's base-1000 scale.
    ///     spec: Docs/RE/specs/equipment_visuals.md §3.4 (non-weapon GID scale = 10000, CODE-CONFIRMED).
    /// </summary>
    public const long NonWeaponGidScale = 10000L; // spec: equipment_visuals.md §3.4

    /// <summary>
    ///     The number of entries in the animation/visual catalog <c>categoryBase</c> array (the equip-overlay
    ///     category → base-term table). The array SHAPE is confirmed at 47 entries (each entry text-field
    ///     first); the per-row base terms are DATA-DRIVEN (loaded from the item/skin script at startup), so
    ///     they are supplied by the caller, never hard-coded here.
    ///     spec: Docs/RE/specs/equipment_visuals.md §3.4 (categoryBase — 47-entry array, CODE-CONFIRMED shape).
    /// </summary>
    public const int CategoryBaseEntryCount = 47; // spec: equipment_visuals.md §3.4

    /// <summary>
    ///     The weapon visual part-slot id (slot 14). Only this slot uses the bone-attach list and the
    ///     base-1000 weapon GID formula; the non-weapon parts {2,3,4,6,11} are skinned-deform under the
    ///     shared skeleton. spec: Docs/RE/specs/equipment_visuals.md §1.1 (slot 14 = weapon) / §4.
    /// </summary>
    public const int WeaponSlot = 14; // spec: equipment_visuals.md §1.1 (part slot 14 = weapon)

    /// <summary>
    ///     The weapon hand-attach bone-id. Built as <c>0</c> (the default root/first bone) and NO static
    ///     non-zero write to that id is reachable on the build path; the concrete hand bone-id is
    ///     DEBUGGER-PENDING (CYCLE 11 deferred). Do NOT guess a non-zero id.
    ///     spec: Docs/RE/specs/equipment_visuals.md (hand bone-id debugger-pending, CYCLE 11 deferred).
    /// </summary>
    public const int
        WeaponHandBoneId = 0; // spec: equipment_visuals.md (hand bone-id debugger-pending, CYCLE 11 deferred)

    // Dual-wield node-flag discriminator (§5.1, RECON#F4-corrected): for a two-piece weapon (skin bind
    // class == 3) the MAIN-hand node carries flag 2 (reads catalog columns 902/904), the OFF-hand node
    // carries flag 1 (reads 903/905). spec: Docs/RE/specs/equipment_visuals.md §5.1.

    /// <summary>Main-hand attach-node flag (selects the main-hand catalog columns 902/904). spec: §5.1.</summary>
    public const int NodeFlagMainHand = 2; // spec: equipment_visuals.md §5.1 (flag 2 = main-hand)

    /// <summary>Off-hand attach-node flag (selects the off-hand catalog columns 903/905). spec: §5.1.</summary>
    public const int NodeFlagOffHand = 1; // spec: equipment_visuals.md §5.1 (flag 1 = off-hand)

    /// <summary>The skin bind-class value that marks a two-piece / dual-wield weapon. spec: §5.1.</summary>
    public const int DualWieldBindClass = 3; // spec: equipment_visuals.md §5.1 (bind class == 3 = two-piece)

    // The 64-bit catalogue-key radices (§3.2): key64 = gid + 1e9 * (slot + 100 * base_term).
    private const long CatalogueKeyGidRadix = 1_000_000_000L; // spec: equipment_visuals.md §3.2
    private const long CatalogueKeySlotRadix = 100L; // spec: equipment_visuals.md §3.2

    // The full vs reduced local-player rebuild slot sets (§1.1 / §3.4). When base_skin_id <= 1000 the full
    // set {3,4,6,2,11,14} is rebound; when > 1000 only slot 3 is bound (reduced high-tier composition).
    private static readonly int[] FullSlotSet = [3, 4, 6, 2, 11, 14]; // spec: equipment_visuals.md §1.1 / §3.4

    private static readonly int[]
        ReducedSlotSet = [3]; // spec: equipment_visuals.md §3.4 (base_skin_id > 1000 -> slot 3 only)

    // The other-actor rebuild omits slot 14 (no weapon). spec: equipment_visuals.md §1.1.
    private static readonly int[] OtherActorSlotSet = [3, 4, 6, 2, 11]; // spec: equipment_visuals.md §1.1

    /// <summary>
    ///     True when the equip-overlay catalog resolution runs for <paramref name="baseSkinId" />
    ///     (<c>base_skin_id &lt;= 1000</c>). Above the threshold the GID-derivation block is skipped entirely.
    ///     spec: Docs/RE/specs/equipment_visuals.md §3.3 / §3.4 (skin-level threshold = 1000).
    /// </summary>
    public static bool RunsOverlayResolution(int baseSkinId)
    {
        return baseSkinId <= SkinLevelThreshold; // spec: equipment_visuals.md §3.4
    }

    /// <summary>
    ///     The local-player rebuild slot set, selected by the skin-level threshold: the full
    ///     <c>{3,4,6,2,11,14}</c> set when <c>base_skin_id &lt;= 1000</c>, else the reduced <c>{3}</c> set
    ///     (high-tier characters use a reduced composition).
    ///     spec: Docs/RE/specs/equipment_visuals.md §1.1 / §3.4 (full vs reduced rebuild).
    /// </summary>
    public static ReadOnlySpan<int> LocalPlayerRebuildSlots(int baseSkinId)
    {
        return baseSkinId <= SkinLevelThreshold ? FullSlotSet : ReducedSlotSet; // spec: equipment_visuals.md §3.4
    }

    /// <summary>
    ///     The other-actor rebuild slot set <c>{3,4,6,2,11}</c> (no slot 14 — other actors carry no weapon
    ///     overlay on this path). spec: Docs/RE/specs/equipment_visuals.md §1.1.
    /// </summary>
    public static ReadOnlySpan<int> OtherActorRebuildSlots()
    {
        return OtherActorSlotSet; // spec: equipment_visuals.md §1.1
    }

    /// <summary>
    ///     The weapon (slot 14) mesh GID — the base-1000 digit formula:
    ///     <c>weapon_gid = 1000 * (B + 10 * (C + 10 * (D + 10 * (partActorId / 1_000_000))))</c>, then
    ///     <c>gid = weapon_gid + (partActorId % 1000)</c>. The three appearance digits come from the visual:
    ///     <paramref name="b" /> = +0x96 (weapon-appearance byte), <paramref name="c" /> = +0xA8 (variant
    ///     i16), <paramref name="d" /> = +0xA0 (class/race byte). The high digits of the part-actor id
    ///     supply the most-significant term; its low three digits are added back after the base-1000 scale.
    ///     The digit→lane wiring is CODE-CONFIRMED; only the human naming of each byte is PLAUSIBLE.
    ///     spec: Docs/RE/specs/equipment_visuals.md §3.1 (weapon GID, base-1000 digit formula).
    /// </summary>
    public static long ResolveWeaponGid(int b, int c, int d, int partActorId)
    {
        var weaponGid = 1000L * (b + 10L * (c + 10L * (d + 10L * (partActorId / 1_000_000L)))); // spec: §3.1
        return weaponGid + partActorId % 1000L; // spec: equipment_visuals.md §3.1 (gid = weapon_gid + partId%1000)
    }

    /// <summary>
    ///     The non-weapon part (slots {2,3,4,6,11}) mesh GID — the scale-10000 formula:
    ///     <c>gid = 10000 * (partActorId / 10000) + (partActorId % 100)</c>. The result is combined with a
    ///     class/variant base term into the 64-bit catalogue key (<see cref="ComposeCatalogueKey64" />).
    ///     spec: Docs/RE/specs/equipment_visuals.md §3.2 / §3.4 (non-weapon GID, scale 10000).
    /// </summary>
    public static long ResolveNonWeaponGid(int partActorId)
    {
        return NonWeaponGidScale * (partActorId / 10000L) + partActorId % 100L; // spec: §3.2 / §3.4
    }

    /// <summary>
    ///     Resolves the per-part mesh GID for a rebuild slot: slot 14 (weapon) uses the base-1000 digit
    ///     formula (<see cref="ResolveWeaponGid" />); every other slot uses the scale-10000 non-weapon
    ///     formula (<see cref="ResolveNonWeaponGid" />). The appearance digits <paramref name="b" /> /
    ///     <paramref name="c" /> / <paramref name="d" /> are consulted only for the weapon slot.
    ///     spec: Docs/RE/specs/equipment_visuals.md §3 (part slot 14 = weapon formula; else general).
    /// </summary>
    public static long ResolvePartGid(int slot, int partActorId, int b, int c, int d)
    {
        return slot == WeaponSlot
            ? ResolveWeaponGid(b, c, d, partActorId) // spec: equipment_visuals.md §3.1
            : ResolveNonWeaponGid(partActorId); // spec: equipment_visuals.md §3.2
    }

    /// <summary>
    ///     The base term for a non-weapon part, derived from the same three appearance fields the weapon GID
    ///     uses: <c>base_term = 5 * (variantField + 4 * classField) - 24</c>. The result feeds the high
    ///     billions lane of the 64-bit catalogue key (§3.2).
    ///     spec: Docs/RE/specs/equipment_visuals.md §3.2 (base_term = 5*(variant + 4*class) - 24).
    /// </summary>
    public static long ResolveBaseTerm(int variantField, int classField)
    {
        return 5L * (variantField + 4L * classField) - 24L; // spec: equipment_visuals.md §3.2
    }

    /// <summary>
    ///     Composes the §3.2 64-bit part catalogue key:
    ///     <c>key64 = gid + 1_000_000_000 * (slot + 100 * baseTerm)</c>. The animation-catalog GID→skin map
    ///     is keyed by this value; a miss yields null mesh pointers. Pure arithmetic — the catalog lookup
    ///     lives with the catalog (not in this layer).
    ///     spec: Docs/RE/specs/equipment_visuals.md §3.2 (key64 packing).
    /// </summary>
    public static long ComposeCatalogueKey64(int slot, long baseTerm, long gid)
    {
        return gid + CatalogueKeyGidRadix *
            (slot + CatalogueKeySlotRadix * baseTerm); // spec: equipment_visuals.md §3.2
    }

    /// <summary>
    ///     The dual-wield attach-node flag for a resolved weapon skin bind class. A two-piece weapon
    ///     (<c>bindClass == 3</c>) builds a MAIN-hand node (flag 2, columns 902/904) and an OFF-hand node
    ///     (flag 1, columns 903/905); a single-piece weapon carries one node whose flag is the bind-class
    ///     value itself. This helper returns the MAIN/OFF flag for the two-piece case (and a single node's
    ///     bind-class flag otherwise). spec: Docs/RE/specs/equipment_visuals.md §5.1 (RECON#F4-corrected).
    /// </summary>
    public static int WeaponNodeFlag(int bindClass, bool offHand)
    {
        if (bindClass != DualWieldBindClass)
            return bindClass; // single-piece: node flag = the skin bind-class value. spec: §5.1.

        // Two-piece: off-hand carries flag 1, main-hand carries flag 2 (flag 2 selects the main-hand
        // columns). spec: Docs/RE/specs/equipment_visuals.md §5.1 (RECON#F4 corrected the prior inversion).
        return offHand ? NodeFlagOffHand : NodeFlagMainHand;
    }
}