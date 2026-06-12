using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>data/script/msg.xdb</c> — the startup binary UI message catalogue.
/// Flat headerless array of 516-byte records: u32LE id + u8[512] CP949 NUL-terminated text.
/// Record count = file_size / 516 (must be exact multiple).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/misc_data.md §6 msg.xdb.
/// Verification status: CODE-CONFIRMED (loader routine, stride, lookup model);
/// SAMPLE-UNVERIFIED (record count, ID values, string content) — see spec §6 Known unknowns.
/// ZERO rendering/engine dependencies.
/// </remarks>
public static class MsgXdbParser
{
    // Record stride: 4 (id) + 512 (text buffer) = 516 bytes.
    // spec: Docs/RE/formats/misc_data.md §6 — "Total record size: 4 + 512 = 516 bytes": CODE-CONFIRMED.
    private const int RecordStride = 516;

    // id field: u32LE @ record+0x000. CODE-CONFIRMED.
    // spec: Docs/RE/formats/misc_data.md §6 Record layout — "id u32LE @ 0x000: CODE-CONFIRMED".
    private const int IdOffset = 0;

    // text field: u8[512] CP949 NUL-terminated @ record+0x004. CODE-CONFIRMED.
    // spec: Docs/RE/formats/misc_data.md §6 Record layout — "text u8[512] @ 0x004: CODE-CONFIRMED".
    private const int TextOffset = 4;
    private const int TextLength = 512; // maximum buffer including NUL

    // Register CP949 once per AppDomain.
    // spec: Docs/RE/formats/misc_data.md §6 Text encoding — "CP949 (code page 949)": CODE-CONFIRMED.
    static MsgXdbParser() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <inheritdoc cref="Parse(ReadOnlySpan{byte})"/>
    public static MsgXdbCatalog Parse(ReadOnlyMemory<byte> data) => Parse(data.Span);

    /// <summary>
    /// Parses all records from a <c>msg.xdb</c> file.
    /// The file size must be an exact multiple of 516; any remainder throws <see cref="InvalidDataException"/>.
    /// </summary>
    /// <param name="span">Raw bytes of <c>data/script/msg.xdb</c>.</param>
    /// <returns>A <see cref="MsgXdbCatalog"/> containing all (id, text) pairs.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when <paramref name="span"/> length is not a multiple of 516.
    /// </exception>
    /// <remarks>
    /// spec: Docs/RE/formats/misc_data.md §6 — "record_count = file_size / 516; any remainder
    /// implies a malformed file": CODE-CONFIRMED.
    /// </remarks>
    public static MsgXdbCatalog Parse(ReadOnlySpan<byte> span)
    {
        // spec: Docs/RE/formats/misc_data.md §6 — "file must be an exact multiple of 516": CODE-CONFIRMED.
        if (span.Length % RecordStride != 0)
            throw new InvalidDataException(
                $"msg.xdb parse error: buffer length {span.Length} is not an exact multiple of " +
                $"stride {RecordStride}. spec: Docs/RE/formats/misc_data.md §6.");

        // spec: Docs/RE/formats/misc_data.md §6 — "record_count = file_size / 516": CODE-CONFIRMED.
        int count = span.Length / RecordStride;

        // Allocate once for all records — no growing lists.
        var records = new MsgXdbRecord[count];

        // CP949 registered in static constructor.
        // spec: Docs/RE/formats/misc_data.md §6 Text encoding — "CP949 (code page 949)": CODE-CONFIRMED.
        Encoding cp949 = Encoding.GetEncoding(949);

        for (int i = 0; i < count; i++)
        {
            // Slice the 516-byte record without copying.
            ReadOnlySpan<byte> rec = span.Slice(i * RecordStride, RecordStride);

            // id u32LE @ record+0x000. CODE-CONFIRMED.
            // spec: Docs/RE/formats/misc_data.md §6 — "id u32LE @ 0x000: CODE-CONFIRMED".
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(IdOffset, 4));

            // text u8[512] CP949 NUL-terminated @ record+0x004. CODE-CONFIRMED.
            // spec: Docs/RE/formats/misc_data.md §6 — "text u8[512] CP949 NUL-terminated @ 0x004: CODE-CONFIRMED".
            // "The maximum usable string length before the NUL is 511 bytes."
            ReadOnlySpan<byte> textBuf = rec.Slice(TextOffset, TextLength);
            int nulPos = textBuf.IndexOf((byte)0);
            string text = nulPos switch
            {
                0 => string.Empty,
                < 0 => cp949.GetString(textBuf), // no NUL found — consume all 512 bytes
                _ => cp949.GetString(textBuf[..nulPos]), // NUL-terminated
            };

            records[i] = new MsgXdbRecord(id, text);
        }

        return new MsgXdbCatalog(records);
    }
}