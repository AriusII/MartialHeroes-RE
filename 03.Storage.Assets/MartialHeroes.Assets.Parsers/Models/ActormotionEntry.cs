namespace MartialHeroes.Assets.Parsers.Models;

/// <summary>
/// One fully parsed record from <c>data/char/actormotion.txt</c>.
/// Represents the 136-byte (0x88) in-memory record as described in the spec.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/actormotion.md §Per-record layout
/// <para>
/// The text file is whitespace-delimited (tab-separated), CP949, with one leading integer
/// giving the record count, followed by one record per line (33 whitespace-delimited columns,
/// indices 0..32).
/// </para>
/// <para>
/// Asset-chain role:
/// <list type="bullet">
/// <item><description>
///   A <c>mob_id</c> (u16 @ offset 0 in a <c>mob*.arr</c> spawn record) is looked up against
///   <see cref="Col1RawOffset"/> (the intra-category offset column, which equals the mob_id for
///   mob entries).
/// </description></item>
/// <item><description>
///   <see cref="IntA"/> carries the associated animation / motion base-id for the actor.
/// </description></item>
/// <item><description>
///   The two 9-element directional arrays expose per-direction animation indices.
/// </description></item>
/// </list>
/// </para>
/// <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public sealed class ActormotionEntry
{
    // ----------------------------------------------------------------
    // Computed lookup key — record offset 0x00
    // spec: Docs/RE/formats/actormotion.md §Computed lookup key
    // ----------------------------------------------------------------

    /// <summary>
    /// The computed motion-map insertion key.
    /// <c>= col1 + base_table[(uint8)(col0 + 1)]</c>.
    /// When no <c>base_table</c> is supplied at parse time the raw <see cref="Col1RawOffset"/>
    /// value is used as-is (base contribution is 0).
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md §Computed lookup key — motion_key @ 0x00.</remarks>
    public uint MotionKey { get; init; }

    // ----------------------------------------------------------------
    // Key-input columns (consumed but also preserved for diagnostics)
    // spec: Docs/RE/formats/actormotion.md §Per-record layout — key-input columns
    // ----------------------------------------------------------------

    /// <summary>
    /// Raw value of text col0 (category / direction selector).
    /// Used as <c>(uint8)(col0 + 1)</c> to index the external base table.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — col0 role.</remarks>
    public int Col0Category { get; init; }

    /// <summary>
    /// Raw value of text col1 (intra-category offset).
    /// Equals <c>mob_id</c> from a spawn record for mob/NPC entries.
    /// Added to the base-table lookup to form <see cref="MotionKey"/>.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — col1 role.</remarks>
    public int Col1RawOffset { get; init; }

    // ----------------------------------------------------------------
    // Stored fields — record offset 0x04
    // spec: Docs/RE/formats/actormotion.md §Per-record layout
    // ----------------------------------------------------------------

    /// <summary>
    /// Integer field at record offset 0x04 (text col2).
    /// Likely the base motion / animation id — meaning MED (unconfirmed).
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — int_a @ 0x04, col2.</remarks>
    public int IntA { get; init; }

    /// <summary>
    /// Numerator of the per-frame X rate, stored at record offset 0x08 (text col3).
    /// <c>RateX = 15.0f × RateSrcX / DivisorX</c>.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — rate_src_x @ 0x08, col3.</remarks>
    public float RateSrcX { get; init; }

    /// <summary>
    /// Numerator of the per-frame Y rate, stored at record offset 0x0C (text col5).
    /// <c>RateY = 15.0f × RateSrcY / DivisorY</c>.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — rate_src_y @ 0x0C, col5.</remarks>
    public float RateSrcY { get; init; }

    /// <summary>Integer field at record offset 0x10 (text col6). Meaning MED.</summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — int_b @ 0x10, col6.</remarks>
    public int IntB { get; init; }

    /// <summary>Float field at record offset 0x14 (text col7). Meaning MED.</summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — float_c @ 0x14, col7.</remarks>
    public float FloatC { get; init; }

    /// <summary>Float field at record offset 0x18 (text col8). Meaning MED.</summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — float_d @ 0x18, col8.</remarks>
    public float FloatD { get; init; }

    /// <summary>Float field at record offset 0x1C (text col9). Meaning MED.</summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — float_e @ 0x1C, col9.</remarks>
    public float FloatE { get; init; }

    /// <summary>Float field at record offset 0x20 (text col10). Meaning MED.</summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — float_f @ 0x20, col10.</remarks>
    public float FloatF { get; init; }

    /// <summary>Float field at record offset 0x24 (text col11). Meaning MED.</summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — float_g @ 0x24, col11.</remarks>
    public float FloatG { get; init; }

    /// <summary>
    /// Frame / loop count divisor for the X rate, at record offset 0x28 (text col4).
    /// Forced to 1 when the source column parses as 0 (divide-by-zero guard).
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — divisor_x @ 0x28, col4; forced to 1 if 0.</remarks>
    public int DivisorX { get; init; }

    /// <summary>
    /// Frame / loop count divisor for the Y rate, at record offset 0x2C (text col14 — paired,
    /// placed after float_i in the column stream).
    /// Forced to 1 when the source column parses as 0 (divide-by-zero guard).
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/actormotion.md — divisor_y @ 0x2C, (paired); forced to 1 if 0.
    /// Observed at text column index 14 (0-based), directly before the two directional sub-arrays.
    /// </remarks>
    public int DivisorY { get; init; }

    /// <summary>
    /// Computed per-frame X rate at record offset 0x30.
    /// <c>= 15.0f × RateSrcX / DivisorX</c> (15 fps base, CONFIRMED).
    /// Physical meaning (movement displacement vs. animation advance) is MED.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — rate_x @ 0x30, 15 fps base CONFIRMED.</remarks>
    public float RateX { get; init; }

    /// <summary>
    /// Computed per-frame Y rate at record offset 0x34.
    /// <c>= 15.0f × RateSrcY / DivisorY</c> (15 fps base, CONFIRMED).
    /// Physical meaning is MED.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — rate_y @ 0x34, 15 fps base CONFIRMED.</remarks>
    public float RateY { get; init; }

    /// <summary>Float field at record offset 0x38 (text col12). Meaning MED.</summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — float_h @ 0x38, col12.</remarks>
    public float FloatH { get; init; }

    /// <summary>Float field at record offset 0x3C (text col13). Meaning MED.</summary>
    /// <remarks>spec: Docs/RE/formats/actormotion.md — float_i @ 0x3C, col13.</remarks>
    public float FloatI { get; init; }

    /// <summary>
    /// Per-direction primary motion / animation index array at record offset 0x40.
    /// 9 elements: 8 compass directions + 1 neutral/centre slot (3×3 directional grid).
    /// Sourced from text columns 15..23 (0-based).
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/actormotion.md — dir_array_1 @ 0x40, 9 i32 elements.
    /// Direction-slot ordering (which index is which compass direction) is LOW confidence —
    /// treat as opaque until runtime confirmation.
    /// </remarks>
    public int[] DirArray1 { get; init; } = [];

    /// <summary>
    /// Per-direction secondary / transition motion index array at record offset 0x64.
    /// 9 elements: same directional grid as <see cref="DirArray1"/>.
    /// Sourced from text columns 24..32 (0-based).
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/actormotion.md — dir_array_2 @ 0x64, 9 i32 elements.
    /// Which array is "primary" vs "secondary/transition" is LOW confidence.
    /// </remarks>
    public int[] DirArray2 { get; init; } = [];

    // ----------------------------------------------------------------
    // Convenience helpers (asset-chain wiring)
    // ----------------------------------------------------------------

    /// <summary>
    /// Convenience: the virtual path of the skeleton file for this actor,
    /// derived from <c>IntA</c> which is the likely skin/skeleton class id.
    /// Format: <c>data/char/bind/g{IntA}.bnd</c>.
    /// </summary>
    /// <remarks>
    /// The exact semantics of IntA (base motion id vs. skeleton class id) are MED.
    /// Callers should validate the path exists in the VFS before use.
    /// </remarks>
    public string BndVfsPath => $"data/char/bind/g{IntA}.bnd";

    // ----------------------------------------------------------------
    // Compatibility shims — preserve pre-refactor API surface
    // These map the new spec-faithful field names to the names used by
    // the Godot presentation layer (layer 05 / NpcRenderer.cs).
    // ----------------------------------------------------------------

    /// <summary>
    /// Actor-class identifier — the mob_id from a spawn record.
    /// Alias for <see cref="Col1RawOffset"/> (text col1).
    /// </summary>
    /// <remarks>
    /// Compatibility alias. Callers in layer 05 use <c>ActorClassId</c> to key the actormotion
    /// lookup; equals <see cref="Col1RawOffset"/> = text column 1.
    /// spec: Docs/RE/formats/actormotion.md — col1 = intra-category offset = mob_id for mob entries.
    /// </remarks>
    public int ActorClassId => Col1RawOffset;

    /// <summary>
    /// Skin-class identifier — equals the g-id of the skeleton file
    /// <c>data/char/bind/g{SkinClassId}.bnd</c>.
    /// Alias for <see cref="IntA"/> (text col2, record offset 0x04).
    /// </summary>
    /// <remarks>
    /// Compatibility alias. The skin/skeleton class is stored in the first non-key column.
    /// spec: Docs/RE/formats/actormotion.md — int_a @ 0x04, col2.
    /// The precise semantics (skeleton class id vs. base motion id) are MED confidence;
    /// observed usage confirms it is the skeleton g-id for mob entries.
    /// </remarks>
    public int SkinClassId => IntA;

    /// <summary>
    /// Motion IDs sourced from the first 7 elements of <see cref="DirArray1"/> (text cols 15..21),
    /// plus two trailing zeros for compatibility with the 7-element array expected by callers.
    /// </summary>
    /// <remarks>
    /// Compatibility alias. The original parser captured cols 15..21 as MotionIds[0..6].
    /// In the full spec layout these are dir_array_1[0..6]; the last two array slots (7, 8)
    /// are unused (typically 0) for most mob entries.
    /// spec: Docs/RE/formats/actormotion.md — dir_array_1 @ 0x40, 9 i32 elements.
    /// </remarks>
    public int[] MotionIds => DirArray1.Length >= 7
        ? DirArray1[..7]
        : DirArray1;
}