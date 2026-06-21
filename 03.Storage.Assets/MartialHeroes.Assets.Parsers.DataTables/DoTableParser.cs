using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

/// <summary>
///     Parsers for binary <c>.do</c> client data table files:
///     <c>textcommand.do</c>, <c>emoticon.do</c>, <c>msginfo.do</c>, <c>items_extra.do</c>.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/config_tables.md §3. .do files (detailed layouts)
///     Common structural pattern: no header; record count = file_size / record_stride.
///     ZERO rendering/engine dependencies.
/// </remarks>
public static class DoTableParser
{
    // =========================================================================
    // textcommand.do — Chat command definitions (stride: 52 bytes)
    // =========================================================================

    // Stride: 52 bytes. CONFIRMED (28 records).
    // spec: Docs/RE/formats/config_tables.md §3.1 textcommand.do — "stride: 52 bytes": CONFIRMED.
    private const int TextCommandStride = 52;

    // =========================================================================
    // emoticon.do — Emoticon picker-panel sprite/grid definitions (stride: 40 bytes)
    // =========================================================================

    // Stride: 40 bytes. CONFIRMED (840 bytes = 21 records × 40). EOF-driven (no count prefix).
    // spec: Docs/RE/formats/ui_manifests.md §2.9 emoticon.do — "stride: 40 bytes (0x28); 21 records; EOF-driven": CONFIRMED.
    private const int EmoticonStride = 40; // 0x28

    // =========================================================================
    // msginfo.do — In-game popup messages (stride: 128 bytes)
    // =========================================================================

    // Stride: 128 bytes. CONFIRMED (14 records in sample).
    // spec: Docs/RE/formats/config_tables.md §3.3 msginfo.do — "stride: 128 bytes": CONFIRMED.
    private const int MsgInfoStride = 128;

    // =========================================================================
    // items_extra.do — Item 3D attachment data (stride: 48 bytes)
    // =========================================================================

    // Stride: 48 bytes. CONFIRMED (90,866 + 16 sentinel records).
    // spec: Docs/RE/formats/config_tables.md §3.4 items_extra.do — "stride: 48 bytes": CONFIRMED.
    private const int ItemsExtraStride = 48;

    // Sentinel item ID (INT32_MAX = 2,147,483,647 = 0x7FFFFFFF).
    // spec: Docs/RE/formats/config_tables.md §3.4 — "sentinel ID = 0x7FFFFFFF": CONFIRMED.
    private const uint ItemsExtraSentinelId = 0x7FFFFFFF;

    private static Encoding? _cp949;

    /// <summary>
    ///     Parses <c>data/script/textcommand.do</c> — chat command definitions.
    ///     Record count = file_size / 52. Encoding: CP949.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/config_tables.md §3.1: CONFIRMED (all 28 records decoded).
    /// </remarks>
    public static TextCommandRecord[] ParseTextCommandDo(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureStride(span, TextCommandStride, "textcommand.do", "Docs/RE/formats/config_tables.md §3.1");
        var count = span.Length / TextCommandStride;
        var results = new TextCommandRecord[count];
        var cp949 = GetCp949();

        for (var i = 0; i < count; i++)
        {
            var offset = i * TextCommandStride;
            var rec = span.Slice(offset, TextCommandStride);

            // Command ID u32 @ +0. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §3.1 — "+0 u32 Command ID: CONFIRMED".
            var commandId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            // Command name char[36] CP949 @ +4. Null-terminated; debug 0xCC fill after NUL. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §3.1 — "+4 char[36] Command name CP949: CONFIRMED".
            var commandName = DecodeNullTerminated(cp949, rec.Slice(4, 36));

            // Alignment pad 4 bytes @ +40. Debug 0xCC fill. CONFIRMED (pad).
            // spec: Docs/RE/formats/config_tables.md §3.1 — "+40 4B padding: CONFIRMED".

            // Argument flag u8 @ +44. 0=no arg; 1=takes player-name arg. CONFIRMED (pattern).
            // spec: Docs/RE/formats/config_tables.md §3.1 — "+44 u8 Argument flag: CONFIRMED (value pattern)".
            var argFlag = rec[44];

            // Alignment pad 3 bytes @ +45. Debug 0xCC fill. CONFIRMED (pad).

            // Sub-command ID u32 @ +48. Non-zero for emote/action commands. CONFIRMED (value pattern).
            // spec: Docs/RE/formats/config_tables.md §3.1 — "+48 u32 Sub-command ID: CONFIRMED (value pattern)".
            var subCommandId = BinaryPrimitives.ReadUInt32LittleEndian(rec[48..]);

            results[i] = new TextCommandRecord
            {
                CommandId = commandId,
                CommandName = commandName,
                ArgumentFlag = argFlag,
                SubCommandId = subCommandId,
                Raw = data.Slice(offset, TextCommandStride)
            };
        }

        return results;
    }

    /// <summary>
    ///     Parses <c>data/script/emoticon.do</c> — emoticon picker-panel sprite/grid definitions.
    ///     Record count = file_size / 40 (EOF-driven; no count prefix).
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/ui_manifests.md §2.9: SAMPLE-VERIFIED + CODE-CONFIRMED (all 21 records).
    ///     <para>
    ///         Two in-memory ordered maps are built by the loader over the same node set:
    ///         Map A keyed by <c>id</c> (+0x00), Map B keyed by <c>index</c> (+0x08).
    ///         spec: Docs/RE/formats/ui_manifests.md §2.9.2 — "Map A by id (+0x00); Map B by index (+0x08)".
    ///     </para>
    ///     <para>
    ///         LOAD-BEARING caveat: <c>pageId</c> (+0x04) is a SINGLE BYTE (low byte only).
    ///         The three bytes at +0x05..+0x07 are uninitialized authoring-tool fill (0xCC).
    ///         Reading +0x04 as a u32 yields a nonsense value (e.g. 0xCCCCCC00); always read it as u8.
    ///         spec: Docs/RE/formats/ui_manifests.md §2.9.3 — "+0x04 width caveat (load-bearing): read pageId as u8 ONLY".
    ///     </para>
    /// </remarks>
    public static EmoticonRecord[] ParseEmoticonDo(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureStride(span, EmoticonStride, "emoticon.do", "Docs/RE/formats/ui_manifests.md §2.9");
        var count = span.Length / EmoticonStride;
        var results = new EmoticonRecord[count];

        for (var i = 0; i < count; i++)
        {
            var offset = i * EmoticonStride;
            var rec = span.Slice(offset, EmoticonStride);

            // id u32 @ +0x00. Primary map key (Map A). Sequential 1..21. CODE-CONFIRMED + SAMPLE-VERIFIED.
            // spec: Docs/RE/formats/ui_manifests.md §2.9.3 — "+0x00 u32 id (Map A key)": CODE+SAMPLE.
            var emoteId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            // pageId u8 @ +0x04 — LOW BYTE ONLY. Values: 0 (records 0–17), 1 (records 18–20); valid range 0..2.
            // +0x05..+0x07 are authoring-tool 0xCC fill — MUST NOT be read as part of pageId.
            // spec: Docs/RE/formats/ui_manifests.md §2.9.3 — "+0x04 u8 pageId (low byte only); +0x05..+0x07 0xCC padding": CODE+SAMPLE.
            var categoryFlag = rec[4]; // pageId — read as single byte

            // +0x05..+0x07: 3-byte 0xCC authoring pad — consumed implicitly (skip to +0x08).
            // spec: Docs/RE/formats/ui_manifests.md §2.9.3 — "+0x05 3 — padding (0xCC fill)".

            // index u32 @ +0x08. Secondary map key (Map B) + click-match key. Sequential 0..20. CODE+SAMPLE.
            // spec: Docs/RE/formats/ui_manifests.md §2.9.3 — "+0x08 u32 index (Map B key; action id)": CODE+SAMPLE.
            var secondaryKey = BinaryPrimitives.ReadUInt32LittleEndian(rec[8..]);

            // emoteCode u32 @ +0x0C. Chat/output token dispatched to the main window on click.
            // Also the reverse-lookup key (Map A scan for emoteCode == incoming value).
            // Observed: 0 on page-0 records, then 10/11/12 on the last records. CODE+SAMPLE.
            // spec: Docs/RE/formats/ui_manifests.md §2.9.3 — "+0x0C u32 emoteCode": CODE+SAMPLE.
            var actionLink = BinaryPrimitives.ReadUInt32LittleEndian(rec[12..]);

            // dstX i32 @ +0x10. Picker-panel destination X of all four widgets. Observed: 10, 160. CODE+SAMPLE.
            // spec: Docs/RE/formats/ui_manifests.md §2.9.3 — "+0x10 i32 dstX": CODE+SAMPLE.
            var dstX = BinaryPrimitives.ReadInt32LittleEndian(rec[16..]);

            // dstY i32 @ +0x14. Picker-panel destination Y base. Observed: 20, 75, 130, 185, 240, 295, 350, 405. CODE+SAMPLE.
            // spec: Docs/RE/formats/ui_manifests.md §2.9.3 — "+0x14 i32 dstY": CODE+SAMPLE.
            var dstY = BinaryPrimitives.ReadInt32LittleEndian(rec[20..]);

            // glyphSrcX i32 @ +0x18. Atlas src X of the 23×23 emoticon glyph on emoticon.dds. CODE+SAMPLE.
            // spec: Docs/RE/formats/ui_manifests.md §2.9.3 — "+0x18 i32 glyphSrcX": CODE+SAMPLE.
            var glyphSrcX = BinaryPrimitives.ReadInt32LittleEndian(rec[24..]);

            // glyphSrcY i32 @ +0x1C. Atlas src Y of the 23×23 emoticon glyph. CODE+SAMPLE.
            // spec: Docs/RE/formats/ui_manifests.md §2.9.3 — "+0x1C i32 glyphSrcY": CODE+SAMPLE.
            var glyphSrcY = BinaryPrimitives.ReadInt32LittleEndian(rec[28..]);

            // labelSrcX i32 @ +0x20. Atlas src X of the 87×13 name-strip sprite. CODE+SAMPLE.
            // spec: Docs/RE/formats/ui_manifests.md §2.9.3 — "+0x20 i32 labelSrcX": CODE+SAMPLE.
            var labelSrcX = BinaryPrimitives.ReadInt32LittleEndian(rec[32..]);

            // labelSrcY i32 @ +0x24. Atlas src Y of the 87×13 name-strip sprite. CODE+SAMPLE.
            // spec: Docs/RE/formats/ui_manifests.md §2.9.3 — "+0x24 i32 labelSrcY": CODE+SAMPLE.
            var labelSrcY = BinaryPrimitives.ReadInt32LittleEndian(rec[36..]);

            results[i] = new EmoticonRecord
            {
                EmoteId = emoteId,
                CategoryFlag = categoryFlag,
                SecondaryKey = secondaryKey,
                ActionLink = actionLink,
                DstX = dstX,
                DstY = dstY,
                GlyphSrcX = glyphSrcX,
                GlyphSrcY = glyphSrcY,
                LabelSrcX = labelSrcX,
                LabelSrcY = labelSrcY,
                Raw = data.Slice(offset, EmoticonStride)
            };
        }

        return results;
    }

    /// <summary>
    ///     Parses <c>data/script/msginfo.do</c> — in-game popup message strings.
    ///     Record count = file_size / 128. Encoding: CP949.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/config_tables.md §3.3: CONFIRMED (all 14 sample records decoded, CP949 confirmed).
    /// </remarks>
    public static MsgInfoRecord[] ParseMsgInfoDo(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureStride(span, MsgInfoStride, "msginfo.do", "Docs/RE/formats/config_tables.md §3.3");
        var count = span.Length / MsgInfoStride;
        var results = new MsgInfoRecord[count];
        var cp949 = GetCp949();

        for (var i = 0; i < count; i++)
        {
            var offset = i * MsgInfoStride;
            var rec = span.Slice(offset, MsgInfoStride);

            // Message ID u32 @ +0. CONFIRMED. Non-sequential.
            // spec: Docs/RE/formats/config_tables.md §3.3 — "+0 u32 Message ID: CONFIRMED".
            var msgId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            // Dialog flag u32 @ +4. 0 for most; 1 for confirmation dialog. CONFIRMED (value pattern).
            // spec: Docs/RE/formats/config_tables.md §3.3 — "+4 u32 Dialog flag: CONFIRMED (pattern)".
            var dialogFlag = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..]);

            // Text line 1 char[60] CP949 @ +8. Null-terminated, zero-padded. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §3.3 — "+8 char[60] Text line 1 CP949: CONFIRMED".
            var textLine1 = DecodeNullTerminated(cp949, rec.Slice(8, 60));

            // Text line 2 char[60] CP949 @ +68. Null-terminated, zero-padded. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §3.3 — "+68 char[60] Text line 2 CP949: CONFIRMED".
            var textLine2 = DecodeNullTerminated(cp949, rec.Slice(68, 60));

            results[i] = new MsgInfoRecord
            {
                MessageId = msgId,
                DialogFlag = dialogFlag,
                TextLine1 = textLine1,
                TextLine2 = textLine2,
                Raw = data.Slice(offset, MsgInfoStride)
            };
        }

        return results;
    }

    /// <summary>
    ///     Parses <c>data/item/items_extra.do</c> — item 3D attachment and scale data.
    ///     Record count = file_size / 48. Sentinel records (item_id = 0x7FFFFFFF) are included as-is.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/config_tables.md §3.4: CONFIRMED (loader code confirmed anim_scale, attach positions,
    ///     rotations).
    ///     Sentinel records (16 mid-file records at ID=0x7FFFFFFF) are included but flagged.
    /// </remarks>
    public static ItemsExtraRecord[] ParseItemsExtraDo(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureStride(span, ItemsExtraStride, "items_extra.do", "Docs/RE/formats/config_tables.md §3.4");
        var count = span.Length / ItemsExtraStride;
        var results = new ItemsExtraRecord[count];

        for (var i = 0; i < count; i++)
        {
            var offset = i * ItemsExtraStride;
            var rec = span.Slice(offset, ItemsExtraStride);

            // Item ID u32 @ +0. Top byte encodes category. 0x7FFFFFFF = sentinel. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §3.4 — "+0 u32 Item ID (sentinel=0x7FFFFFFF): CONFIRMED".
            var itemId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            // Animation speed scale f32 @ +4. 1.0 = normal. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §3.4 — "+4 f32 Animation speed scale: CONFIRMED".
            var animScale = BinaryPrimitives.ReadSingleLittleEndian(rec[4..]);

            // Attachment field A i32 @ +8. Range 0..3. CONFIRMED (range); name UNVERIFIED.
            // spec: Docs/RE/formats/config_tables.md §3.4 — "+8 i32 Attachment field A (range 0..3): CONFIRMED".
            var attachFieldA = BinaryPrimitives.ReadInt32LittleEndian(rec[8..]);

            // Attachment field B i32 @ +12. Range 8..48. CONFIRMED (range); name UNVERIFIED.
            // spec: Docs/RE/formats/config_tables.md §3.4 — "+12 i32 Attachment field B (range 8..48): CONFIRMED".
            var attachFieldB = BinaryPrimitives.ReadInt32LittleEndian(rec[12..]);

            // Weapon bone attachment XYZ i32×3 @ +16..+24. Local space. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §3.4 — "+16 i32 Attach X; +20 Y; +24 Z: CONFIRMED".
            var attachX = BinaryPrimitives.ReadInt32LittleEndian(rec[16..]);
            var attachY = BinaryPrimitives.ReadInt32LittleEndian(rec[20..]);
            var attachZ = BinaryPrimitives.ReadInt32LittleEndian(rec[24..]);

            // Rotation XYZ i32×3 @ +28..+36. Degrees; ×π/180 at usage. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §3.4 — "+28 i32 RotX; +32 RotY; +36 RotZ (degrees): CONFIRMED".
            var rotX = BinaryPrimitives.ReadInt32LittleEndian(rec[28..]);
            var rotY = BinaryPrimitives.ReadInt32LittleEndian(rec[32..]);
            var rotZ = BinaryPrimitives.ReadInt32LittleEndian(rec[36..]);

            // Fourth rotation / secondary anim param i32 @ +40. Dominant 180; range -185..+300. CONFIRMED (range); name UNVERIFIED.
            // spec: Docs/RE/formats/config_tables.md §3.4 — "+40 i32 fourth rot component or secondary anim param: CONFIRMED (range)".
            var field40 = BinaryPrimitives.ReadInt32LittleEndian(rec[40..]);

            // Rarity tier u32 @ +44. Values {0,1,2,3,4,5}. CONFIRMED (range); semantic INFERRED.
            // spec: Docs/RE/formats/config_tables.md §3.4 — "+44 u32 Rarity tier (values 0..5): CONFIRMED (range); INFERRED".
            var rarityTier = BinaryPrimitives.ReadUInt32LittleEndian(rec[44..]);

            results[i] = new ItemsExtraRecord
            {
                ItemId = itemId,
                IsSentinel = itemId == ItemsExtraSentinelId,
                AnimScale = animScale,
                AttachFieldA = attachFieldA,
                AttachFieldB = attachFieldB,
                AttachX = attachX,
                AttachY = attachY,
                AttachZ = attachZ,
                RotXDeg = rotX,
                RotYDeg = rotY,
                RotZDeg = rotZ,
                Field40 = field40,
                RarityTier = rarityTier,
                Raw = data.Slice(offset, ItemsExtraStride)
            };
        }

        return results;
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private static void EnsureStride(ReadOnlySpan<byte> span, int stride, string fileName, string specRef)
    {
        if (span.Length % stride != 0)
            throw new InvalidDataException(
                $"{fileName} parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {stride}. spec: {specRef}.");
    }

    private static Encoding GetCp949()
    {
        if (_cp949 is not null)
            return _cp949;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return _cp949 = Encoding.GetEncoding(949);
    }

    private static string DecodeNullTerminated(Encoding enc, ReadOnlySpan<byte> buf)
    {
        var end = buf.IndexOf((byte)0);
        if (end < 0) end = buf.Length;
        if (end == 0) return string.Empty;
        return enc.GetString(buf[..end]);
    }
}