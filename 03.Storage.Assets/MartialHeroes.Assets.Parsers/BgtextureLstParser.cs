using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for the binary <c>bgtexture.lst</c> background-texture index.
/// This is the authoritative runtime form consumed by the terrain/effect loader;
/// the adjacent <c>bgtexture.txt</c> is a human-readable mirror only.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/bgtexture_lst.md — "the .lst BINARY is the file the loader consumes":
///   CONFIRMED.
/// <para>
/// File layout:
/// <list type="bullet">
///   <item>Offset 0: <c>record_count</c> u32 LE.</item>
///   <item>Offset 4: flat array of <c>record_count × 48</c> bytes.</item>
///   <item>Per record: <c>kind</c> u8 @ +0 + <c>rel_path</c> char[47] @ +1.</item>
/// </list>
/// spec: Docs/RE/formats/bgtexture_lst.md §Header layout + §Record / body layout: CONFIRMED.
/// </para>
/// <para>
/// Size formula (CONFIRMED on both shipped instances):
/// <c>file_size = 4 + record_count × 48</c>
/// spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — size formula: CONFIRMED.
/// </para>
/// <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class BgtextureLstParser
{
    // Header: u32LE record_count @ 0x00. CONFIRMED.
    // spec: Docs/RE/formats/bgtexture_lst.md §Header layout — record_count u32LE @ 0: CONFIRMED.
    private const int HeaderSize = 4;

    // Record stride: 48 bytes (1 kind byte + 47 relpath bytes). CONFIRMED.
    // spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — stride 48 bytes: CONFIRMED.
    private const int RecordStride = 48;

    // Relpath field width: 47 bytes (null-terminated, zero-padded). CONFIRMED.
    // spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — rel_path char[47] @ +1: CONFIRMED.
    private const int RelPathFieldLen = 47;

    static BgtextureLstParser()
    {
        // The relpath field is printable ASCII in all observed records.
        // Treat as CP949 for safety (all game text is CP949/EUC-KR).
        // spec: Docs/RE/formats/bgtexture_lst.md §Identification —
        //   "treat as CP949 / EUC-KR for safety": CONFIRMED.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Parses a <c>bgtexture.lst</c> file from raw bytes delivered by the VFS.
    /// </summary>
    /// <param name="data">Raw file bytes (from the VFS).  Must contain the complete file.</param>
    /// <returns>A <see cref="BgtextureLstCatalog"/> with all decoded records.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when the buffer is shorter than the 4-byte header, when it is too short to hold
    /// the declared record count, or when the body length is not an exact multiple of 48.
    /// </exception>
    /// <remarks>
    /// spec: Docs/RE/formats/bgtexture_lst.md §Header layout — record_count u32LE @ 0: CONFIRMED.
    /// spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — stride 48 bytes: CONFIRMED.
    /// </remarks>
    public static BgtextureLstCatalog Parse(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;

        // Validate: minimum 4-byte header.
        // spec: Docs/RE/formats/bgtexture_lst.md §Header layout — record_count u32LE @ 0: CONFIRMED.
        if (span.Length < HeaderSize)
            throw new InvalidDataException(
                $"bgtexture.lst parse error: buffer length {span.Length} is shorter than the " +
                $"4-byte header. spec: Docs/RE/formats/bgtexture_lst.md §Header layout.");

        // record_count u32LE @ 0x00. CONFIRMED.
        // spec: Docs/RE/formats/bgtexture_lst.md §Header layout — record_count u32LE @ 0: CONFIRMED.
        uint recordCount = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);

        // Validate size formula: file_size must equal 4 + record_count * 48. CONFIRMED.
        // spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — size formula: CONFIRMED.
        long expectedBodyLen = (long)recordCount * RecordStride;
        long expectedTotalLen = HeaderSize + expectedBodyLen;

        if (span.Length < expectedTotalLen)
            throw new InvalidDataException(
                $"bgtexture.lst parse error: record_count={recordCount} requires " +
                $"{expectedTotalLen} bytes (4 + {recordCount} × {RecordStride}) " +
                $"but buffer is only {span.Length} bytes. " +
                "spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — size formula: CONFIRMED.");

        int bodyLen = span.Length - HeaderSize;
        if (bodyLen % RecordStride != 0)
            throw new InvalidDataException(
                $"bgtexture.lst parse error: body length {bodyLen} is not an exact multiple " +
                $"of stride {RecordStride}. " +
                "spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — stride 48 bytes: CONFIRMED.");

        // Derive count from body if record_count field is consistent.
        int count = (int)recordCount;

        // relpath field decoded as CP949 (ASCII in all observed records).
        // spec: Docs/RE/formats/bgtexture_lst.md §Identification — "treat as CP949 for safety": CONFIRMED.
        var cp949 = Encoding.GetEncoding(949);
        var records = new BgtextureLstRecord[count];

        for (int i = 0; i < count; i++)
        {
            int recBase = HeaderSize + i * RecordStride;
            ReadOnlySpan<byte> rec = span.Slice(recBase, RecordStride);

            // kind u8 @ record +0. CONFIRMED (non-constant; 6 distinct values; render-mode tag).
            // spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — kind u8 @ +0:
            //   CONFIRMED (non-constant, 6 values). Render-mode selector, NOT a boolean animated flag.
            // spec: Docs/RE/formats/bgtexture_lst.md §Enumerations — "earlier boolean reading is retired".
            byte kind = rec[0];

            // rel_path char[47] @ record +1. Null-terminated, zero-padded. CONFIRMED.
            // spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — rel_path char[47] @ +1: CONFIRMED.
            ReadOnlySpan<byte> relPathSpan = rec.Slice(1, RelPathFieldLen);
            int nulPos = relPathSpan.IndexOf((byte)0);
            int pathLen = nulPos < 0 ? RelPathFieldLen : nulPos;
            string relPath = pathLen == 0 ? string.Empty : cp949.GetString(relPathSpan[..pathLen]);

            records[i] = new BgtextureLstRecord
            {
                Index = i,
                KindRaw = kind,
                RelPath = relPath,
            };
        }

        return new BgtextureLstCatalog(records);
    }
}