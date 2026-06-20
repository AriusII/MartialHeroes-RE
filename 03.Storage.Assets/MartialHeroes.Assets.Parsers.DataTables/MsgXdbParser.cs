using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

/// <summary>
///     Parser for <c>data/script/msg.xdb</c> — the client-wide UI caption string catalogue.
///     Flat, headerless array of fixed 516-byte records:
///     i32 LE caption_id @ +0x000, char[512] CP949 NUL-terminated text @ +0x004.
///     Record count = file_size / 516 (must be an exact multiple; any remainder → <see cref="InvalidDataException" />).
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/msg_xdb.md — "flat header-less array of fixed 516-byte records". CONFIRMED.
///     ZERO rendering / engine dependencies. Engine-free (no <c>using Godot</c>).
///     CP949 provider registered once in the static constructor.
/// </remarks>
public static class MsgXdbParser
{
    // Record stride: 4 (caption_id) + 512 (text buffer) = 516 bytes (0x204).
    // spec: Docs/RE/formats/msg_xdb.md §File layout — "Record stride: 516 bytes (0x204)". CONFIRMED.
    private const int RecordStride = 516;

    // caption_id field: i32 LE @ record+0x000.
    // spec: Docs/RE/formats/msg_xdb.md §Record layout — "caption_id i32 LE @ +0x000". CONFIRMED.
    private const int CaptionIdOffset = 0;

    // text field: char[512] CP949 NUL-terminated @ record+0x004.
    // spec: Docs/RE/formats/msg_xdb.md §Record layout — "text char[512] CP949 NUL-terminated @ +0x004". CONFIRMED.
    private const int TextOffset = 4;
    private const int TextLength = 512; // fixed buffer width including NUL

    // Register CP949 exactly once per AppDomain.
    // spec: Docs/RE/formats/msg_xdb.md §Status — "Encoding: CP949 (code page 949)". CONFIRMED.
    static MsgXdbParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <inheritdoc cref="Parse(ReadOnlySpan{byte})" />
    public static MsgXdbCatalog Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    /// <summary>
    ///     Parses all records from a <c>data/script/msg.xdb</c> payload.
    /// </summary>
    /// <param name="span">Raw bytes of <c>data/script/msg.xdb</c> as obtained from the VFS.</param>
    /// <returns>A <see cref="MsgXdbCatalog" /> providing id→text lookup for all caption records.</returns>
    /// <exception cref="InvalidDataException">
    ///     Thrown when <paramref name="span" /> length is not an exact multiple of 516.
    /// </exception>
    /// <remarks>
    ///     spec: Docs/RE/formats/msg_xdb.md §File layout —
    ///     "record_count = file_size / 516; no count prefix; record 0 begins at byte 0". CONFIRMED.
    ///     "Magic / signature: None — there is no file header; the first record begins at byte 0". CONFIRMED.
    /// </remarks>
    public static MsgXdbCatalog Parse(ReadOnlySpan<byte> span)
    {
        // Validate: file size must be an exact multiple of the 516-byte stride.
        // spec: Docs/RE/formats/msg_xdb.md §File layout — "record_count = file_size / 516
        //   (must be exact multiple; any remainder → malformed file)". CONFIRMED.
        if (span.Length % RecordStride != 0)
            throw new InvalidDataException(
                $"msg.xdb parse error: buffer length {span.Length} is not an exact multiple of " +
                $"the 516-byte record stride. " +
                $"spec: Docs/RE/formats/msg_xdb.md §File layout.");

        // spec: Docs/RE/formats/msg_xdb.md §File layout —
        //   "Observed record count: approximately 2,644 records". SAMPLE-VERIFIED.
        var count = span.Length / RecordStride;

        var records = new MsgXdbRecord[count];

        // CP949 decoder registered in static constructor.
        // spec: Docs/RE/formats/msg_xdb.md §Status — "Encoding: CP949 (code page 949)". CONFIRMED.
        var cp949 = Encoding.GetEncoding(949);

        for (var i = 0; i < count; i++)
        {
            // Slice the fixed 516-byte record without copying.
            var rec = span.Slice(i * RecordStride, RecordStride);

            // caption_id: i32 LE @ record+0x000.
            // spec: Docs/RE/formats/msg_xdb.md §Record layout — "caption_id i32 LE @ +0x000". CONFIRMED.
            var captionId = BinaryPrimitives.ReadInt32LittleEndian(rec.Slice(CaptionIdOffset, 4));

            // text: char[512] CP949 NUL-terminated @ record+0x004.
            // spec: Docs/RE/formats/msg_xdb.md §Record layout —
            //   "text char[512] CP949 NUL-terminated @ +0x004; decode up to first NUL or full 512 bytes". CONFIRMED.
            var textBuf = rec.Slice(TextOffset, TextLength);
            var nulPos = textBuf.IndexOf((byte)0);
            var text = nulPos switch
            {
                0 => string.Empty,
                < 0 => cp949.GetString(textBuf), // no NUL found — consume all 512 bytes
                _ => cp949.GetString(textBuf[..nulPos]) // NUL-terminated
            };

            records[i] = new MsgXdbRecord(captionId, text);
        }

        return new MsgXdbCatalog(records);
    }
}