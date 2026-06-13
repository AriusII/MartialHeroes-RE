using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Assets.Parsers.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  Per-class stance skill table — data/script/<class><stance>.do
//  spec: Docs/RE/formats/ui_manifests.md §2.7
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// An inline-array value type holding 76 raw bytes from record offsets +0x28..+0x73.
/// These bytes are not yet decoded per spec — they are preserved verbatim so future
/// spec passes can light up individual fields without rewriting the parser.
/// (The spec text says "72 bytes" but 116 − 40 = 76; both are noted in <see cref="DoStanceRecord.TailByteCount"/>.)
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x28..+0x73 unmapped (UNKNOWN)".
/// The inline array avoids allocating a <c>byte[]</c> per record.
/// </remarks>
[InlineArray(DoStanceRecord.TailByteCount)]
public struct DoStanceTail72
{
    // Single private field required by [InlineArray].
    private byte _e0;

    /// <summary>
    /// Returns a writable span over all 72 tail bytes (used by the parser).
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x28 72 unmapped bytes": UNKNOWN.
    /// </summary>
    public Span<byte> AsSpan() =>
        MemoryMarshal.CreateSpan(ref _e0, DoStanceRecord.TailByteCount);

    /// <summary>
    /// Returns a read-only span over all 72 tail bytes (used by callers).
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x28 72 unmapped bytes": UNKNOWN.
    /// </summary>
    public ReadOnlySpan<byte> AsReadOnlySpan() =>
        MemoryMarshal.CreateReadOnlySpan(ref _e0, DoStanceRecord.TailByteCount);
}

/// <summary>
/// One 116-byte record from a per-class stance <c>.do</c> file
/// (<c>data/script/musajung.do</c> and 11 siblings).
///
/// The known fields drive per-skill icon coordinate lookup in the skill window and hotbar.
/// The unmapped tail (+0x28..+0x73) is preserved as a 72-byte inline array.
/// All-zero records (where <see cref="InstanceKey"/> == 0 and <see cref="SlotIndex"/> == 0
/// and the entire body is zero) are skipped by the parser.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/ui_manifests.md §2.7 Record layout (116 bytes / 0x74):
/// <code>
/// +0x00  4  u32  instanceKey      CODE-CONFIRMED + SAMPLE-VERIFIED
/// +0x04  4  u32  groupSubIndex    SAMPLE-VERIFIED
/// +0x08  4  u32  slotIndex        CODE-CONFIRMED
/// +0x0C  4  u32  classStanceRef   CODE-CONFIRMED (1001/1002/1003); PLAUSIBLE (other 9)
/// +0x10  4  u32  groupId          SAMPLE-VERIFIED
/// +0x14  2  u16  (secondary X variant — field name UNKNOWN)  SAMPLE-VERIFIED (value pattern)
/// +0x18  2  i16  iconSrcX         CODE-CONFIRMED + SAMPLE-VERIFIED
/// +0x1C  2  i16  iconSrcY         CODE-CONFIRMED + SAMPLE-VERIFIED
/// +0x20  2  u16  secondarySpriteX SAMPLE-VERIFIED (value pattern); field name UNKNOWN
/// +0x24  2  u16  secondarySpriteY SAMPLE-VERIFIED (value pattern); field name UNKNOWN
/// +0x28 72   —   (unmapped tail)  UNKNOWN
/// </code>
/// </remarks>
public sealed class DoStanceRecord
{
    // ─── layout constants ────────────────────────────────────────────────────

    /// <summary>
    /// Fixed record stride in bytes: 116 (0x74).
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "fixed 116-byte records": SAMPLE-VERIFIED.
    /// </summary>
    public const int Stride = 116; // 0x74

    /// <summary>
    /// Number of unmapped bytes at the tail of each record (+0x28..+0x73).
    /// Computed as Stride (116) − TailOffset (0x28 = 40) = 76.
    /// Note: the spec text says "72 bytes" at +0x28, but 116 − 40 = 76; the 76-byte value
    /// is what fits a 116-byte record and is used here.
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x28 72 bytes unmapped": UNKNOWN.
    /// </summary>
    public const int TailByteCount = 76; // Stride(116) − TailOffset(0x28=40) = 76

    // ─── confirmed/verified fields ───────────────────────────────────────────

    /// <summary>
    /// Large sequential skill-instance identifier; primary map key.
    /// Example (musajung.do record 0): 131101011.
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x00 u32 instanceKey": CODE-CONFIRMED + SAMPLE-VERIFIED.
    /// </summary>
    public required uint InstanceKey { get; init; }

    /// <summary>
    /// Sub-row within a skill family or stance step; small integer (0, 1, 2, …).
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x04 u32 groupSubIndex": SAMPLE-VERIFIED.
    /// </summary>
    public required uint GroupSubIndex { get; init; }

    /// <summary>
    /// Sequential slot number (0, 1, 2, …); secondary map key.
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x08 u32 slotIndex": CODE-CONFIRMED.
    /// </summary>
    public required uint SlotIndex { get; init; }

    /// <summary>
    /// Class-stance discriminator. Confirmed values: 1001 = musajung, 1002 = musasa,
    /// 1003 = musama. Values for the other 9 files are PLAUSIBLE but UNVERIFIED.
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x0C u32 classStanceRef":
    ///   CODE-CONFIRMED (1001/1002/1003); PLAUSIBLE (other 9).
    /// </summary>
    public required uint ClassStanceRef { get; init; }

    /// <summary>
    /// Skill-family / page group (observed: 19, 185, …).
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x10 u32 groupId": SAMPLE-VERIFIED.
    /// </summary>
    public required uint GroupId { get; init; }

    /// <summary>
    /// Secondary X variant at +0x14; value pattern tracks iconSrcX but is not identical.
    /// Observed: 120, 201, 282 … Field name UNKNOWN.
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x14 u16 (secondary X variant)": SAMPLE-VERIFIED (pattern); UNKNOWN name.
    /// </summary>
    public required ushort SecondaryXVariant { get; init; }

    /// <summary>
    /// Icon atlas left edge in pixels on the per-(job,kind) 512×512 DDS sheet.
    /// The hotbar/skill-window reads this at runtime (map position +24 in the node).
    /// Observed: 0, 23, 46, 69, … (authored data, not a formula — may be non-multiples of 23).
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x18 i16 iconSrcX": CODE-CONFIRMED + SAMPLE-VERIFIED.
    /// </summary>
    public required short IconSrcX { get; init; }

    /// <summary>
    /// Icon atlas top edge in pixels on the per-(job,kind) 512×512 DDS sheet.
    /// The hotbar/skill-window reads this at runtime (map position +28 in the node).
    /// Observed: 0, 23, 46, 62, 69, 92, … (authored data).
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x1C i16 iconSrcY": CODE-CONFIRMED + SAMPLE-VERIFIED.
    /// </summary>
    public required short IconSrcY { get; init; }

    /// <summary>
    /// Secondary sprite left at +0x20. Observed: 0, 87, 174, 261 … Field name UNKNOWN.
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x20 u16 secondarySpriteX": SAMPLE-VERIFIED (pattern); UNKNOWN name.
    /// </summary>
    public required ushort SecondarySpriteX { get; init; }

    /// <summary>
    /// Secondary sprite top at +0x24. Observed: 23, 36, 85, 98, 147, 160 … Field name UNKNOWN.
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x24 u16 secondarySpriteY": SAMPLE-VERIFIED (pattern); UNKNOWN name.
    /// </summary>
    public required ushort SecondarySpriteY { get; init; }

    // ─── unmapped tail ───────────────────────────────────────────────────────

    /// <summary>
    /// Raw 72 bytes at record offsets +0x28..+0x73.
    /// Not decoded — preserved verbatim for future spec passes.
    /// Stored as a value-type inline array; no heap allocation per record.
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "+0x28 72 bytes unmapped": UNKNOWN.
    /// </summary>
    public required DoStanceTail72 Tail { get; init; }
}

/// <summary>
/// Decoded result of one per-class stance <c>.do</c> file.
/// Contains only the non-zero records (all-zero records are skipped by the loader).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/ui_manifests.md §2.7 — record array with no file header;
///   record_count = file_size / 116; non-zero remainder (incomplete tail) is ignored;
///   all-zero records are skipped.
/// </remarks>
public sealed class DoStanceTable
{
    // ─── layout constants ────────────────────────────────────────────────────

    /// <summary>
    /// Fixed record stride: 116 bytes.
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "fixed 116-byte records": SAMPLE-VERIFIED.
    /// </summary>
    public const int RecordStride = DoStanceRecord.Stride; // 116

    // ─── decoded payload ─────────────────────────────────────────────────────

    /// <summary>
    /// All non-zero records decoded from the file, in file order.
    /// All-zero records (entire 116 bytes = 0x00) are excluded.
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "skip all-zero records".
    /// </summary>
    public required IReadOnlyList<DoStanceRecord> Records { get; init; }

    /// <summary>
    /// Total records read from file (including skipped all-zero ones).
    /// Equals <c>file_size / 116</c> (integer division).
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "record_count = file_size / 116": SAMPLE-VERIFIED.
    /// </summary>
    public required int TotalRecordCount { get; init; }

    /// <summary>
    /// Number of trailing bytes that did not form a complete record
    /// (must be in 0..115; observed as 40 in musama.do).
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "non-zero remainder ignored": SAMPLE-VERIFIED (musama.do 40 bytes).
    /// </summary>
    public required int TrailingByteCount { get; init; }

    /// <summary>Lookup by <see cref="DoStanceRecord.InstanceKey"/>.</summary>
    private readonly Dictionary<uint, DoStanceRecord> _byInstanceKey;

    /// <summary>Lookup by <see cref="DoStanceRecord.SlotIndex"/>.</summary>
    private readonly Dictionary<uint, DoStanceRecord> _bySlotIndex;

    /// <summary>Initialises lookup indices.</summary>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public DoStanceTable(
        IReadOnlyList<DoStanceRecord> records,
        int totalRecordCount,
        int trailingByteCount)
    {
        Records = records;
        TotalRecordCount = totalRecordCount;
        TrailingByteCount = trailingByteCount;

        _byInstanceKey = new Dictionary<uint, DoStanceRecord>(records.Count);
        _bySlotIndex = new Dictionary<uint, DoStanceRecord>(records.Count);
        foreach (var r in records)
        {
            _byInstanceKey[r.InstanceKey] = r;
            _bySlotIndex[r.SlotIndex] = r;
        }
    }

    /// <summary>
    /// Looks up a record by its <see cref="DoStanceRecord.InstanceKey"/>.
    /// Returns <see langword="null"/> if not found.
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "Map A keyed by instanceKey (+0x00)": CODE-CONFIRMED.
    /// </summary>
    public DoStanceRecord? GetByInstanceKey(uint instanceKey) =>
        _byInstanceKey.TryGetValue(instanceKey, out var r) ? r : null;

    /// <summary>
    /// Looks up a record by its <see cref="DoStanceRecord.SlotIndex"/>.
    /// Returns <see langword="null"/> if not found.
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "Map B keyed by slotIndex (+0x08)": CODE-CONFIRMED.
    /// </summary>
    public DoStanceRecord? GetBySlotIndex(uint slotIndex) =>
        _bySlotIndex.TryGetValue(slotIndex, out var r) ? r : null;
}