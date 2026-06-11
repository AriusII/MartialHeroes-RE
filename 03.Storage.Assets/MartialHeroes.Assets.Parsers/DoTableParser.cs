using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parsers for binary <c>.do</c> client data table files:
/// <c>textcommand.do</c>, <c>emoticon.do</c>, <c>msginfo.do</c>, <c>items_extra.do</c>.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §3. .do files (detailed layouts)
/// Common structural pattern: no header; record count = file_size / record_stride.
/// ZERO rendering/engine dependencies.
/// </remarks>
public static class DoTableParser
{
    // =========================================================================
    // textcommand.do — Chat command definitions (stride: 52 bytes)
    // =========================================================================

    // Stride: 52 bytes. CONFIRMED (28 records).
    // spec: Docs/RE/formats/config_tables.md §3.1 textcommand.do — "stride: 52 bytes": CONFIRMED.
    private const int TextCommandStride = 52;

    /// <summary>
    /// Parses <c>data/script/textcommand.do</c> — chat command definitions.
    /// Record count = file_size / 52. Encoding: CP949.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/config_tables.md §3.1: CONFIRMED (all 28 records decoded).
    /// </remarks>
    public static TextCommandRecord[] ParseTextCommandDo(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureStride(span, TextCommandStride, "textcommand.do", "Docs/RE/formats/config_tables.md §3.1");
        int count = span.Length / TextCommandStride;
        var results = new TextCommandRecord[count];
        var cp949 = GetCp949();

        for (int i = 0; i < count; i++)
        {
            int offset = i * TextCommandStride;
            ReadOnlySpan<byte> rec = span.Slice(offset, TextCommandStride);

            // Command ID u32 @ +0. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §3.1 — "+0 u32 Command ID: CONFIRMED".
            uint commandId = BinaryPrimitives.ReadUInt32LittleEndian(rec[0..]);

            // Command name char[36] CP949 @ +4. Null-terminated; debug 0xCC fill after NUL. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §3.1 — "+4 char[36] Command name CP949: CONFIRMED".
            string commandName = DecodeNullTerminated(cp949, rec.Slice(4, 36));

            // Alignment pad 4 bytes @ +40. Debug 0xCC fill. CONFIRMED (pad).
            // spec: Docs/RE/formats/config_tables.md §3.1 — "+40 4B padding: CONFIRMED".

            // Argument flag u8 @ +44. 0=no arg; 1=takes player-name arg. CONFIRMED (pattern).
            // spec: Docs/RE/formats/config_tables.md §3.1 — "+44 u8 Argument flag: CONFIRMED (value pattern)".
            byte argFlag = rec[44];

            // Alignment pad 3 bytes @ +45. Debug 0xCC fill. CONFIRMED (pad).

            // Sub-command ID u32 @ +48. Non-zero for emote/action commands. CONFIRMED (value pattern).
            // spec: Docs/RE/formats/config_tables.md §3.1 — "+48 u32 Sub-command ID: CONFIRMED (value pattern)".
            uint subCommandId = BinaryPrimitives.ReadUInt32LittleEndian(rec[48..]);

            results[i] = new TextCommandRecord
            {
                CommandId = commandId,
                CommandName = commandName,
                ArgumentFlag = argFlag,
                SubCommandId = subCommandId,
                Raw = data.Slice(offset, TextCommandStride),
            };
        }

        return results;
    }

    // =========================================================================
    // emoticon.do — Emoticon sprite definitions (stride: 40 bytes)
    // =========================================================================

    // Stride: 40 bytes. CONFIRMED (21 records).
    // spec: Docs/RE/formats/config_tables.md §3.2 emoticon.do — "stride: 40 bytes": CONFIRMED.
    private const int EmoticonStride = 40;

    /// <summary>
    /// Parses <c>data/script/emoticon.do</c> — emoticon sprite-sheet definitions.
    /// Record count = file_size / 40.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/config_tables.md §3.2: CONFIRMED (all 21 records).
    /// </remarks>
    public static EmoticonRecord[] ParseEmoticonDo(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureStride(span, EmoticonStride, "emoticon.do", "Docs/RE/formats/config_tables.md §3.2");
        int count = span.Length / EmoticonStride;
        var results = new EmoticonRecord[count];

        for (int i = 0; i < count; i++)
        {
            int offset = i * EmoticonStride;
            ReadOnlySpan<byte> rec = span.Slice(offset, EmoticonStride);

            // Emote ID u32 @ +0. CONFIRMED. Sequential 1..21.
            // spec: Docs/RE/formats/config_tables.md §3.2 — "+0 u32 Emote ID: CONFIRMED".
            uint emoteId = BinaryPrimitives.ReadUInt32LittleEndian(rec[0..]);

            // Category flag u8 @ +4. CONFIRMED (value pattern); semantic UNVERIFIED.
            // spec: Docs/RE/formats/config_tables.md §3.2 — "+4 u8 Category flag: CONFIRMED (pattern)".
            byte categoryFlag = rec[4];

            // Alignment pad 3 bytes @ +5.

            // Secondary key u32 @ +8. CONFIRMED. Sequential 0..20.
            // spec: Docs/RE/formats/config_tables.md §3.2 — "+8 u32 Secondary key: CONFIRMED".
            uint secondaryKey = BinaryPrimitives.ReadUInt32LittleEndian(rec[8..]);

            // Action link field u32 @ +12. CONFIRMED (value pattern); name UNVERIFIED.
            // spec: Docs/RE/formats/config_tables.md §3.2 — "+12 u32 Action link: CONFIRMED (pattern)".
            uint actionLink = BinaryPrimitives.ReadUInt32LittleEndian(rec[12..]);

            results[i] = new EmoticonRecord
            {
                EmoteId = emoteId,
                CategoryFlag = categoryFlag,
                SecondaryKey = secondaryKey,
                ActionLink = actionLink,
                Raw = data.Slice(offset, EmoticonStride),
            };
        }

        return results;
    }

    // =========================================================================
    // msginfo.do — In-game popup messages (stride: 128 bytes)
    // =========================================================================

    // Stride: 128 bytes. CONFIRMED (14 records in sample).
    // spec: Docs/RE/formats/config_tables.md §3.3 msginfo.do — "stride: 128 bytes": CONFIRMED.
    private const int MsgInfoStride = 128;

    /// <summary>
    /// Parses <c>data/script/msginfo.do</c> — in-game popup message strings.
    /// Record count = file_size / 128. Encoding: CP949.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/config_tables.md §3.3: CONFIRMED (all 14 sample records decoded, CP949 confirmed).
    /// </remarks>
    public static MsgInfoRecord[] ParseMsgInfoDo(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureStride(span, MsgInfoStride, "msginfo.do", "Docs/RE/formats/config_tables.md §3.3");
        int count = span.Length / MsgInfoStride;
        var results = new MsgInfoRecord[count];
        var cp949 = GetCp949();

        for (int i = 0; i < count; i++)
        {
            int offset = i * MsgInfoStride;
            ReadOnlySpan<byte> rec = span.Slice(offset, MsgInfoStride);

            // Message ID u32 @ +0. CONFIRMED. Non-sequential.
            // spec: Docs/RE/formats/config_tables.md §3.3 — "+0 u32 Message ID: CONFIRMED".
            uint msgId = BinaryPrimitives.ReadUInt32LittleEndian(rec[0..]);

            // Dialog flag u32 @ +4. 0 for most; 1 for confirmation dialog. CONFIRMED (value pattern).
            // spec: Docs/RE/formats/config_tables.md §3.3 — "+4 u32 Dialog flag: CONFIRMED (pattern)".
            uint dialogFlag = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..]);

            // Text line 1 char[60] CP949 @ +8. Null-terminated, zero-padded. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §3.3 — "+8 char[60] Text line 1 CP949: CONFIRMED".
            string textLine1 = DecodeNullTerminated(cp949, rec.Slice(8, 60));

            // Text line 2 char[60] CP949 @ +68. Null-terminated, zero-padded. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §3.3 — "+68 char[60] Text line 2 CP949: CONFIRMED".
            string textLine2 = DecodeNullTerminated(cp949, rec.Slice(68, 60));

            results[i] = new MsgInfoRecord
            {
                MessageId = msgId,
                DialogFlag = dialogFlag,
                TextLine1 = textLine1,
                TextLine2 = textLine2,
                Raw = data.Slice(offset, MsgInfoStride),
            };
        }

        return results;
    }

    // =========================================================================
    // items_extra.do — Item 3D attachment data (stride: 48 bytes)
    // =========================================================================

    // Stride: 48 bytes. CONFIRMED (90,866 + 16 sentinel records).
    // spec: Docs/RE/formats/config_tables.md §3.4 items_extra.do — "stride: 48 bytes": CONFIRMED.
    private const int ItemsExtraStride = 48;

    // Sentinel item ID (INT32_MAX = 2,147,483,647 = 0x7FFFFFFF).
    // spec: Docs/RE/formats/config_tables.md §3.4 — "sentinel ID = 0x7FFFFFFF": CONFIRMED.
    private const uint ItemsExtraSentinelId = 0x7FFFFFFF;

    /// <summary>
    /// Parses <c>data/item/items_extra.do</c> — item 3D attachment and scale data.
    /// Record count = file_size / 48. Sentinel records (item_id = 0x7FFFFFFF) are included as-is.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/config_tables.md §3.4: CONFIRMED (loader code confirmed anim_scale, attach positions, rotations).
    /// Sentinel records (16 mid-file records at ID=0x7FFFFFFF) are included but flagged.
    /// </remarks>
    public static ItemsExtraRecord[] ParseItemsExtraDo(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureStride(span, ItemsExtraStride, "items_extra.do", "Docs/RE/formats/config_tables.md §3.4");
        int count = span.Length / ItemsExtraStride;
        var results = new ItemsExtraRecord[count];

        for (int i = 0; i < count; i++)
        {
            int offset = i * ItemsExtraStride;
            ReadOnlySpan<byte> rec = span.Slice(offset, ItemsExtraStride);

            // Item ID u32 @ +0. Top byte encodes category. 0x7FFFFFFF = sentinel. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §3.4 — "+0 u32 Item ID (sentinel=0x7FFFFFFF): CONFIRMED".
            uint itemId = BinaryPrimitives.ReadUInt32LittleEndian(rec[0..]);

            // Animation speed scale f32 @ +4. 1.0 = normal. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §3.4 — "+4 f32 Animation speed scale: CONFIRMED".
            float animScale = BinaryPrimitives.ReadSingleLittleEndian(rec[4..]);

            // Attachment field A i32 @ +8. Range 0..3. CONFIRMED (range); name UNVERIFIED.
            // spec: Docs/RE/formats/config_tables.md §3.4 — "+8 i32 Attachment field A (range 0..3): CONFIRMED".
            int attachFieldA = BinaryPrimitives.ReadInt32LittleEndian(rec[8..]);

            // Attachment field B i32 @ +12. Range 8..48. CONFIRMED (range); name UNVERIFIED.
            // spec: Docs/RE/formats/config_tables.md §3.4 — "+12 i32 Attachment field B (range 8..48): CONFIRMED".
            int attachFieldB = BinaryPrimitives.ReadInt32LittleEndian(rec[12..]);

            // Weapon bone attachment XYZ i32×3 @ +16..+24. Local space. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §3.4 — "+16 i32 Attach X; +20 Y; +24 Z: CONFIRMED".
            int attachX = BinaryPrimitives.ReadInt32LittleEndian(rec[16..]);
            int attachY = BinaryPrimitives.ReadInt32LittleEndian(rec[20..]);
            int attachZ = BinaryPrimitives.ReadInt32LittleEndian(rec[24..]);

            // Rotation XYZ i32×3 @ +28..+36. Degrees; ×π/180 at usage. CONFIRMED.
            // spec: Docs/RE/formats/config_tables.md §3.4 — "+28 i32 RotX; +32 RotY; +36 RotZ (degrees): CONFIRMED".
            int rotX = BinaryPrimitives.ReadInt32LittleEndian(rec[28..]);
            int rotY = BinaryPrimitives.ReadInt32LittleEndian(rec[32..]);
            int rotZ = BinaryPrimitives.ReadInt32LittleEndian(rec[36..]);

            // Fourth rotation / secondary anim param i32 @ +40. Dominant 180; range -185..+300. CONFIRMED (range); name UNVERIFIED.
            // spec: Docs/RE/formats/config_tables.md §3.4 — "+40 i32 fourth rot component or secondary anim param: CONFIRMED (range)".
            int field40 = BinaryPrimitives.ReadInt32LittleEndian(rec[40..]);

            // Rarity tier u32 @ +44. Values {0,1,2,3,4,5}. CONFIRMED (range); semantic INFERRED.
            // spec: Docs/RE/formats/config_tables.md §3.4 — "+44 u32 Rarity tier (values 0..5): CONFIRMED (range); INFERRED".
            uint rarityTier = BinaryPrimitives.ReadUInt32LittleEndian(rec[44..]);

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
                Raw = data.Slice(offset, ItemsExtraStride),
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

    private static Encoding? _cp949;

    private static Encoding GetCp949()
    {
        if (_cp949 is not null)
            return _cp949;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return _cp949 = Encoding.GetEncoding(949);
    }

    private static string DecodeNullTerminated(Encoding enc, ReadOnlySpan<byte> buf)
    {
        int end = buf.IndexOf((byte)0);
        if (end < 0) end = buf.Length;
        if (end == 0) return string.Empty;
        return enc.GetString(buf[..end]);
    }
}
