namespace MartialHeroes.Assets.Parsers.Character.Models;

/// <summary>
///     One fully parsed record from <c>data/char/actormotion.txt</c>.
///     Represents the 136-byte (0x88) in-memory record as described in the spec.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/actormotion.md §Per-record layout
///     <para>
///         The text file is whitespace-delimited (tab-separated), CP949, with one leading integer
///         giving the record count, followed by one record per line (33 whitespace-delimited columns,
///         indices 0..32).
///     </para>
///     <para>
///         Asset-chain role:
///         <list type="bullet">
///             <item>
///                 <description>
///                     A <c>mob_id</c> (u16 @ offset 0 in a <c>mob*.arr</c> spawn record) is looked up against
///                     <see cref="Col1RawOffset" /> (the intra-category offset column, which equals the mob_id for
///                     mob entries).
///                 </description>
///             </item>
///             <item>
///                 <description>
///                     <see cref="IntA" /> (<c>skin_class</c> / SkinClassId, SAMPLE-VERIFIED) is the actor-to-skeleton key
///                     joining to <c>data/char/bind/g&lt;skin_class&gt;.bnd</c> via the bindlist membership test.
///                 </description>
///             </item>
///             <item>
///                 <description>
///                     <see cref="DirArray1" /> (<c>motion_ids_a</c>) holds action → <c>.mot</c> clip ids;
///                     <see cref="DirArray2" /> (<c>motion_ids_b</c>) holds action → SOUND/EFFECT event ids —
///                     NOT secondary motion. The "9-direction" reading is REFUTED (action/lifecycle-keyed by use-site).
///                 </description>
///             </item>
///         </list>
///     </para>
///     <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public sealed class ActormotionEntry
{
    // ----------------------------------------------------------------
    // Computed lookup key — record offset 0x00
    // spec: Docs/RE/formats/actormotion.md §Computed lookup key
    // ----------------------------------------------------------------

    /// <summary>
    ///     The computed motion-map insertion key.
    ///     <c>= col1 + base_table[(uint8)(col0 + 1)]</c>.
    ///     When no <c>base_table</c> is supplied at parse time the raw <see cref="Col1RawOffset" />
    ///     value is used as-is (base contribution is 0).
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md §Computed lookup key — motion_key @ 0x00.</remarks>
    public uint MotionKey { get; init; }

    // ----------------------------------------------------------------
    // Key-input columns (consumed but also preserved for diagnostics)
    // spec: Docs/RE/formats/actormotion.md §Per-record layout — key-input columns
    // ----------------------------------------------------------------

    /// <summary>
    ///     Raw value of text col0 (category / direction selector).
    ///     Used as <c>(uint8)(col0 + 1)</c> to index the external base table.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — col0 role.</remarks>
    public int Col0Category { get; init; }

    /// <summary>
    ///     Raw value of text col1 (intra-category offset).
    ///     Equals <c>mob_id</c> from a spawn record for mob/NPC entries.
    ///     Added to the base-table lookup to form <see cref="MotionKey" />.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — col1 role.</remarks>
    public int Col1RawOffset { get; init; }

    // ----------------------------------------------------------------
    // Stored fields — record offset 0x04
    // spec: Docs/RE/formats/actormotion.md §Per-record layout
    // ----------------------------------------------------------------

    /// <summary>
    ///     <c>skin_class</c> (SkinClassId) at record offset 0x04 (text col2). SAMPLE-VERIFIED.
    ///     The actor-to-skeleton key: joins to <c>data/char/bind/g&lt;skin_class&gt;.bnd</c> via the
    ///     bindlist membership test. A value of 0 is a null skeleton (login/camera/special actors).
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/actormotion.md §Per-record layout — int_a @ 0x04, col2 = skin_class: SAMPLE-VERIFIED.
    ///     spec: Docs/RE/formats/actormotion.md §Cross-references — "col2 = skin_class joins to data/char/bind/g&lt;skin_class
    ///     &gt;.bnd via bindlist".
    /// </remarks>
    public int IntA { get; init; }

    /// <summary>
    ///     Numerator of the per-frame X rate, stored at record offset 0x08 (text col3).
    ///     <c>RateX = 15.0f × RateSrcX / DivisorX</c>.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — rate_src_x @ 0x08, col3.</remarks>
    public float RateSrcX { get; init; }

    /// <summary>
    ///     Numerator of the per-frame Y rate, stored at record offset 0x0C (text col5).
    ///     <c>RateY = 15.0f × RateSrcY / DivisorY</c>.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — rate_src_y @ 0x0C, col5.</remarks>
    public float RateSrcY { get; init; }

    /// <summary>Integer field at record offset 0x10 (text col7). Meaning UNRESOLVED.</summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — int_b @ 0x10, col7.</remarks>
    public int IntB { get; init; }

    /// <summary>Float field at record offset 0x14 (text col8). Meaning UNRESOLVED.</summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — float_c @ 0x14, col8.</remarks>
    public float FloatC { get; init; }

    /// <summary>Float field at record offset 0x18 (text col9). Meaning UNRESOLVED.</summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — float_d @ 0x18, col9.</remarks>
    public float FloatD { get; init; }

    /// <summary>Float field at record offset 0x1C (text col10). Meaning UNRESOLVED.</summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — float_e @ 0x1C, col10.</remarks>
    public float FloatE { get; init; }

    /// <summary>Float field at record offset 0x20 (text col11). Meaning UNRESOLVED.</summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — float_f @ 0x20, col11.</remarks>
    public float FloatF { get; init; }

    /// <summary>Float field at record offset 0x24 (text col12). Meaning UNRESOLVED.</summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — float_g @ 0x24, col12.</remarks>
    public float FloatG { get; init; }

    /// <summary>
    ///     Frame / loop count divisor for the X rate, at record offset 0x28 (text col4).
    ///     Forced to 1 when the source column parses as 0 (divide-by-zero guard).
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — divisor_x @ 0x28, col4; forced to 1 if 0.</remarks>
    public int DivisorX { get; init; }

    /// <summary>
    ///     Frame / loop count divisor for the Y rate, at record offset 0x2C (text col6 — paired,
    ///     interleaved early in the column stream).
    ///     Forced to 1 when the source column parses as 0 (divide-by-zero guard).
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/actormotion.md — divisor_y @ 0x2C, col6; forced to 1 if 0.
    /// </remarks>
    public int DivisorY { get; init; }

    /// <summary>
    ///     Per-frame MOVEMENT SPEED (default locomotion) at record offset 0x30.
    ///     <c>= 15.0f × RateSrcX / DivisorX</c> (15 fps base, CONFIRMED).
    ///     This is the ground displacement per frame — NOT an animation playback rate.
    ///     (<c>rate_x</c> = default locomotion; <c>rate_y</c> = alternate / mounted locomotion.)
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/actormotion.md §Per-frame rate fields — rate_x @ 0x30: per-frame MOVEMENT SPEED, CONFIRMED.
    ///     spec: Docs/RE/formats/actormotion.md — "rate_x/rate_y = per-frame MOVEMENT SPEED, NOT animation advance".
    /// </remarks>
    public float RateX { get; init; }

    /// <summary>
    ///     Per-frame MOVEMENT SPEED (alternate / mounted locomotion) at record offset 0x34.
    ///     <c>= 15.0f × RateSrcY / DivisorY</c> (15 fps base, CONFIRMED).
    ///     This is the ground displacement per frame for the alt-locomotion path.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/actormotion.md §Per-frame rate fields — rate_y @ 0x34: per-frame MOVEMENT SPEED alt,
    ///     CONFIRMED.
    ///     spec: Docs/RE/formats/actormotion.md — "rate_x/rate_y = per-frame MOVEMENT SPEED, NOT animation advance".
    /// </remarks>
    public float RateY { get; init; }

    /// <summary>
    ///     Locomotion-dust FX descriptor / id at record offset 0x38 (text col13).
    ///     Passed as the FX descriptor to the footfall-dust particle spawn.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/actormotion.md §Per-record layout — float_h @ 0x38, col13:
    ///     locomotion-dust FX descriptor/id. HIGH layout / MED meaning.
    /// </remarks>
    public float FloatH { get; init; }

    /// <summary>
    ///     Locomotion-dust FX SCALE / magnitude at record offset 0x3C (text col14).
    ///     Multiplied by 1.0 (walk) or 0.18 (run) before being passed as the FX scale.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/actormotion.md §Per-record layout — float_i @ 0x3C, col14:
    ///     locomotion-dust FX scale/magnitude. HIGH layout / MED-HIGH meaning.
    /// </remarks>
    public float FloatI { get; init; }

    /// <summary>
    ///     <c>motion_ids_a</c> — action → <c>.mot</c> clip-id lookup at record offset 0x40.
    ///     9 elements; each slot is keyed by the actor's action/lifecycle state (idle, walk, run,
    ///     death, mount-idle, combat-idle) — NOT by direction.
    ///     <para>
    ///         Slot table: a[0]=file-source idle ref; a[1]=default idle; a[2]=walk; a[3]=run;
    ///         a[4]=death; a[5]=mount-follow idle; a[6]=combat-idle/alt; a[7..8]=no static consumer.
    ///     </para>
    ///     The "9 directions" reading is REFUTED — there is no direction-indexed access site.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/actormotion.md §The two 9-element sub-arrays — motion_ids_a @ 0x40:
    ///     action → .mot CLIP ids (animation layer); action/lifecycle-keyed, NOT direction-indexed.
    ///     "The 9-direction reading is REFUTED." HIGH layout / HIGH meaning (by use-site).
    /// </remarks>
    public int[] DirArray1 { get; init; } = [];

    /// <summary>
    ///     <c>motion_ids_b</c> — action → SOUND/EFFECT event-id lookup at record offset 0x64.
    ///     9 elements; feeds the sound/effect routers, NEVER the animation mixer.
    ///     NOT secondary motion — this is a binary-won spec reversal.
    ///     <para>
    ///         Slot table (loader indexing: b[0] = +0x64, element-0-unused convention):
    ///         b[0]=no consumer; b[1]=spawn sound (cat 11); b[2]=walk footstep SFX (cat 7);
    ///         b[3]=run footstep SFX (cat 8); b[4]=death effect/sound (+0x74); b[5..8]=no consumer.
    ///     </para>
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/actormotion.md §The two 9-element sub-arrays — motion_ids_b @ 0x64:
    ///     SOUND/EFFECT event ids — fed to the sound/effect routers, NEVER the animation mixer
    ///     (binary-won correction vs the old 'secondary motion' reading). HIGH layout / HIGH meaning (by use-site).
    ///     CYCLE 7 CORRECTED: death slot = b[4] @ +0x74 (NOT b[5]) — same element-0-unused convention
    ///     as the A-array. spec: Docs/RE/formats/actormotion.md §Enumerations — "b[4] at +0x74, not b[5]".
    /// </remarks>
    public int[] DirArray2 { get; init; } = [];

    // ----------------------------------------------------------------
    // Convenience helpers and aliases (asset-chain wiring)
    // ----------------------------------------------------------------

    /// <summary>
    ///     Alias for <see cref="DirArray1" />: the 9-element action → <c>.mot</c> clip-id array
    ///     (<c>motion_ids_a</c> @ record offset 0x40, cols 15..23).
    ///     Use this alias for clarity when the caller works with animation clip ids.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/actormotion.md §The two 9-element sub-arrays — motion_ids_a @ 0x40:
    ///     action → .mot CLIP ids. The "9-direction interpretation is REFUTED".
    /// </remarks>
    public int[] MotionClipIds => DirArray1; // spec: actormotion.md — motion_ids_a: action→.mot clip ids

    /// <summary>
    ///     The <c>.mot</c> clip-id the runtime uses for the default stand idle (motion_ids_a[1]).
    ///     This is record offset +0x44, text column 16 (a[1]).
    /// </summary>
    /// <remarks>
    ///     BINARY-WON (CYCLE 7, build 263bd994): the runtime stand idle reads a[1] (+0x44, col16),
    ///     NOT a[0] (+0x40, col15). The a[0] slot is file-loaded but has zero runtime read-sites
    ///     (dead at runtime). Both A-array and B-array share the element-0-unused convention:
    ///     element 0 is unused/padding, consumers start at element 1.
    ///     spec: Docs/RE/formats/actormotion.md §The two 9-element sub-arrays — a[1] @ +0x44 = col16 =
    ///     "default stand idle (the idle the runtime actually uses)". HIGH.
    ///     spec: Docs/RE/formats/actormotion.md — "KEY POINT: the runtime stand idle is COLUMN 16
    ///     (record +0x44, a[1]), NOT column 15."
    /// </remarks>
    public int IdleMotionId =>
        DirArray1.Length > 1
            ? DirArray1[1]
            : 0; // spec: actormotion.md — a[1] @ +0x44, col16 = runtime idle (BINARY-WON)

    /// <summary>
    ///     Alias for <see cref="DirArray2" />: the 9-element action → SOUND/EFFECT event-id array
    ///     (<c>motion_ids_b</c> @ record offset 0x64, cols 24..32).
    ///     Slots feed the sound/effect routers — NEVER the animation mixer.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/actormotion.md §The two 9-element sub-arrays — motion_ids_b @ 0x64:
    ///     SOUND/EFFECT event ids, NOT secondary motion. Binary-won correction.
    /// </remarks>
    public int[] SfxEventIds => DirArray2; // spec: actormotion.md — motion_ids_b: SFX/FX event ids, NOT motion

    /// <summary>
    ///     Convenience join: the VFS path of the skeleton file for this actor via the
    ///     <c>skin_class</c> direct player-path rule. Format: <c>data/char/bind/g{IntA}.bnd</c>.
    /// </summary>
    /// <remarks>
    ///     This convenience join is valid for the <b>player skin_class direct path only</b>.
    ///     Mobs resolve the skeleton through the actormotion/appearance-catalogue indirection,
    ///     NOT a literal <c>g{skin_class}.bnd</c>.
    ///     spec: Docs/RE/formats/actormotion.md §Cross-references — "There is NO computed g{N}.bnd
    ///     numeric rule; registration is by explicit list / IdB join". The mob skeleton is reached
    ///     INDIRECTLY via the catalogue — NOT as a literal g{skin_class}.bnd printf.
    /// </remarks>
    public string BndVfsPath =>
        $"data/char/bind/g{IntA}.bnd"; // spec: actormotion.md — player skin_class direct path only; mobs use catalogue indirection

    // ----------------------------------------------------------------
    // Compatibility shims — preserve pre-refactor API surface
    // These map the new spec-faithful field names to the names used by
    // the Godot presentation layer (layer 05 / NpcRenderer.cs).
    // ----------------------------------------------------------------

    /// <summary>
    ///     Actor-class identifier — the mob_id from a spawn record.
    ///     Alias for <see cref="Col1RawOffset" /> (text col1).
    /// </summary>
    /// <remarks>
    ///     Compatibility alias. Callers in layer 05 use <c>ActorClassId</c> to key the actormotion
    ///     lookup; equals <see cref="Col1RawOffset" /> = text column 1.
    ///     spec: Docs/RE/formats/actormotion.md — col1 = intra-category offset = mob_id for mob entries.
    /// </remarks>
    public int ActorClassId => Col1RawOffset;

    /// <summary>
    ///     Skin-class identifier (<c>skin_class</c>) — the actor-to-skeleton key.
    ///     Joins to <c>data/char/bind/g&lt;SkinClassId&gt;.bnd</c> via the bindlist membership test.
    ///     Alias for <see cref="IntA" /> (text col2, record offset 0x04). SAMPLE-VERIFIED.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/actormotion.md §Per-record layout — int_a @ 0x04, col2 = skin_class: SAMPLE-VERIFIED.
    ///     spec: Docs/RE/formats/actormotion.md §Cross-references — "col2 = skin_class; joins to data/char/bind/g&lt;
    ///     skin_class&gt;.bnd via bindlist."
    ///     There is NO computed g{N}.bnd numeric rule for mobs — mobs resolve via the catalogue/IdB join.
    ///     spec: Docs/RE/formats/actormotion.md §Cross-references — "NO computed g{N}.bnd numeric rule; registration by
    ///     explicit list / IdB join".
    /// </remarks>
    public int SkinClassId => IntA;

    /// <summary>
    ///     Motion IDs sourced from the first 7 elements of <see cref="DirArray1" /> (text cols 15..21),
    ///     plus two trailing zeros for compatibility with the 7-element array expected by callers.
    /// </summary>
    /// <remarks>
    ///     Compatibility alias. The original parser captured cols 15..21 as MotionIds[0..6].
    ///     In the full spec layout these are motion_ids_a[0..6]; the last two array slots (7, 8)
    ///     are unused (typically 0) for most mob entries.
    ///     spec: Docs/RE/formats/actormotion.md — motion_ids_a @ 0x40, 9 i32 elements.
    /// </remarks>
    public int[] MotionIds => DirArray1.Length >= 7
        ? DirArray1[..7]
        : DirArray1;
}