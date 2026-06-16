namespace MartialHeroes.Assets.Parsers.Models;

// spec: Docs/RE/formats/mi.md

/// <summary>
/// One widget/sub-element record from a <c>.mi</c> UI-panel descriptor file.
/// Fixed stride: 28 bytes = 7 × u32le.
/// </summary>
/// <remarks>
/// <para>
/// The container structure is HIGH confidence (exact factorisation: 4 + 21 × 28 = 592 bytes).
/// The field semantics are PLAUSIBLE working hypotheses derived from re-parsing all 21 sample
/// records — no dedicated loader was located in the static pass so meanings are not yet
/// parser-verified.
/// spec: Docs/RE/formats/mi.md §Record layout — 28 bytes per record (7 × u32): HIGH (stride/count); PLAUSIBLE (semantics).
/// </para>
/// <para>
/// The null/none sentinel for optional fields is <c>0xFFFFFFFF</c> — HIGH confidence that the
/// sentinel value is correct; which fields legitimately carry it is PLAUSIBLE.
/// Observed in fields 1, 2, 5, and 6; never in ordinal field 0.
/// spec: Docs/RE/formats/mi.md §Record layout — "None sentinel: 0xFFFFFFFF": HIGH (value); PLAUSIBLE (field set).
/// </para>
/// <para>
/// PLAUSIBLE field groupings derived from structural signals across 21 sample records:
/// <list type="bullet">
///   <item>Field 0 (WidgetId) is unambiguously a sequential per-record ordinal (strictly increasing, no gaps).</item>
///   <item>Fields 1+2 (FieldA0/FieldA1) co-vary as a ±1 caption-id couple: FieldA1 = FieldA0 − 1.</item>
///   <item>Fields 3+6 (FieldKind/FieldLink) co-vary as a kind/link couple: small repeated values.</item>
///   <item>Fields 4+5 (FieldB0/FieldB1) co-vary as a decimal-packed icon-id pair: FieldB1 = FieldB0 + 1.</item>
/// </list>
/// spec: Docs/RE/formats/mi.md §Record layout §Structural signals — PLAUSIBLE.
/// </para>
/// <para>
/// Do not implement business logic against these meanings until a live-debugger pass confirms
/// the loader's field read order.
/// spec: Docs/RE/formats/mi.md §Status — "loader: UNRESOLVED (static) — LIVE-DEBUGGER-PENDING".
/// </para>
/// </remarks>
public sealed class MiWidgetRecord
{
    // PLAUSIBLE field semantics (spec mi.md §Record layout — all meanings derived from 21-record
    // re-parse; loader not located in static pass; pending live-debugger confirmation).

    /// <summary>
    /// Field 0 (+0x00): PLAUSIBLE sequential per-record ordinal (strictly increasing, no gaps
    /// across all 21 sample records).  This is the structurally cleanest signal in the file.
    /// spec: Docs/RE/formats/mi.md §Record layout — field 0 u32 @ +0x00:
    ///   "structure SAMPLE-VERIFIED / meaning PLAUSIBLE".
    /// </summary>
    public required uint WidgetId { get; init; }

    /// <summary>
    /// Field 1 (+0x04): PLAUSIBLE caption / text id (primary of a ±1 couple with <see cref="FieldA1"/>),
    /// or none-sentinel <c>0xFFFFFFFF</c>.  Observed: FieldA1 = FieldA0 − 1 when both are set.
    /// spec: Docs/RE/formats/mi.md §Record layout — field 1 u32 @ +0x04: PLAUSIBLE.
    /// </summary>
    public required uint FieldA0 { get; init; }

    /// <summary>
    /// Field 2 (+0x08): PLAUSIBLE caption / text id (sibling of <see cref="FieldA0"/>), or
    /// none-sentinel <c>0xFFFFFFFF</c>.  Observed: FieldA1 = FieldA0 − 1 when both are set.
    /// spec: Docs/RE/formats/mi.md §Record layout — field 2 u32 @ +0x08: PLAUSIBLE.
    /// </summary>
    public required uint FieldA1 { get; init; }

    /// <summary>
    /// Field 3 (+0x0C): PLAUSIBLE small kind / link id (co-varies with <see cref="FieldLink"/>
    /// field 6); whether this field carries the "kind" or the "link" half of the couple is
    /// UNRESOLVED.  Observed to repeat across records (small integer values).
    /// spec: Docs/RE/formats/mi.md §Record layout — field 3 u32 @ +0x0C:
    ///   "PLAUSIBLE / kind-vs-link UNRESOLVED".
    /// </summary>
    public required uint FieldKind { get; init; }

    /// <summary>
    /// Field 4 (+0x10): PLAUSIBLE decimal-packed icon / sprite id (primary of a pair with
    /// <see cref="FieldB1"/>); confirmed NOT a pointer (values too small for a VAS address).
    /// Observed: FieldB1 = FieldB0 + 1.
    /// spec: Docs/RE/formats/mi.md §Record layout — field 4 u32 @ +0x10: PLAUSIBLE.
    /// </summary>
    public required uint FieldB0 { get; init; }

    /// <summary>
    /// Field 5 (+0x14): PLAUSIBLE decimal-packed icon / sprite id (sibling of <see cref="FieldB0"/>),
    /// or none-sentinel <c>0xFFFFFFFF</c>.  Observed: FieldB1 = FieldB0 + 1 when both are set.
    /// spec: Docs/RE/formats/mi.md §Record layout — field 5 u32 @ +0x14: PLAUSIBLE.
    /// </summary>
    public required uint FieldB1 { get; init; }

    /// <summary>
    /// Field 6 (+0x18): PLAUSIBLE small kind / link id (co-varies with <see cref="FieldKind"/>
    /// field 3), or none-sentinel <c>0xFFFFFFFF</c>.  Which half of the kind/link couple this
    /// field carries is UNRESOLVED.
    /// spec: Docs/RE/formats/mi.md §Record layout — field 6 u32 @ +0x18:
    ///   "PLAUSIBLE / kind-vs-link UNRESOLVED".
    /// </summary>
    public required uint FieldLink { get; init; }
}

/// <summary>
/// The decoded container from a <c>.mi</c> UI-panel descriptor file (<c>data/ui/mobinfo.mi</c>).
/// </summary>
/// <remarks>
/// <para>
/// Container layout: 4-byte record count header + flat array of 28-byte <see cref="MiWidgetRecord"/> records.
/// spec: Docs/RE/formats/mi.md §Container structure — "4 + recordCount × 28 = file size": HIGH.
/// </para>
/// <para>
/// The single observed instance is <c>data/ui/mobinfo.mi</c> (592 bytes, 21 records).
/// spec: Docs/RE/formats/mi.md §Identification — "Census: exactly one .mi file … 592 bytes".
/// </para>
/// <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public sealed class MiPanelData
{
    /// <summary>
    /// Record size in bytes.
    /// spec: Docs/RE/formats/mi.md §Record layout — "Record stride: 28 bytes": HIGH.
    /// </summary>
    public const int RecordStride = 28; // spec: Docs/RE/formats/mi.md §Record layout

    /// <summary>
    /// Number of u32 fields per record.
    /// spec: Docs/RE/formats/mi.md §Record layout — "7 × u32": HIGH.
    /// </summary>
    public const int FieldsPerRecord = 7; // spec: Docs/RE/formats/mi.md §Record layout

    /// <summary>
    /// Number of widget records in this panel.
    /// spec: Docs/RE/formats/mi.md §Header layout — "recordCount u32 @ 0x00": HIGH.
    /// </summary>
    public required uint RecordCount { get; init; }

    /// <summary>
    /// Decoded widget records. Length = <see cref="RecordCount"/>.
    /// spec: Docs/RE/formats/mi.md §Record layout — 28-byte records starting at 0x04.
    /// </summary>
    public required MiWidgetRecord[] Records { get; init; }
}