using System.Text;
using MartialHeroes.Assets.Parsers.Texture.Models;

namespace MartialHeroes.Assets.Parsers.Texture;

/// <summary>
///     Parser for <c>data/item/texturelist.txt</c> — the item icon texture manifest.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/ui_manifests.md §10 data/item/texturelist.txt: CODE-CONFIRMED.
///     File structure (CODE-CONFIRMED):
///     <code>
/// &lt;filename1&gt;\r\n
/// &lt;filename2&gt;\r\n
/// …
/// </code>
///     No block keywords, no braces, no '#' comment convention, no column headers.
///     Each line is a single filename; the leading decimal digits of the filename are the tex_id.
///     The full VFS path is resolved as <c>data/item/texture/&lt;filename&gt;</c>.
///     Parsing algorithm per §10.3 (CODE-CONFIRMED):
///     1. Read one line.
///     2. Locate the last '.' in the line (extension delimiter).
///     3. Split into name (before '.') and ext ('.' + remainder).
///     4. Parse leading decimal digits of name as integer → tex_id.
///     5. Construct full path = "data/item/texture/" + name + ext.
///     6. Register entry keyed by tex_id.
///     Encoding: CP949 (EUC-KR superset), line endings CRLF.
///     spec: Docs/RE/formats/ui_manifests.md §Identification — "CP949; CRLF": PARSER-CONFIRMED.
///     ZERO rendering/engine dependencies.
/// </remarks>
public static class TextureListParser
{
    // VFS path prefix for all item icon DDS files.
    // spec: Docs/RE/formats/ui_manifests.md §10.3 step 5 — "data/item/texture/ + name + ext": CODE-CONFIRMED.
    private const string VfsPathPrefix = "data/item/texture/";

    // Register CP949 once per AppDomain.
    // spec: Docs/RE/formats/ui_manifests.md §Identification — "CP949 for all string fields": PARSER-CONFIRMED.
    static TextureListParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <inheritdoc cref="Parse(ReadOnlySpan{byte})" />
    public static TextureListManifest Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    /// <inheritdoc cref="Parse(ReadOnlySpan{byte},string)" />
    public static TextureListManifest Parse(ReadOnlyMemory<byte> data, string vfsPathPrefix)
    {
        return Parse(data.Span, vfsPathPrefix);
    }

    /// <summary>
    ///     Parses the raw CP949 bytes of a <c>texturelist.txt</c> file.
    /// </summary>
    /// <param name="span">Raw bytes of <c>data/item/texturelist.txt</c> from the VFS.</param>
    /// <returns>A <see cref="TextureListManifest" /> with all entries.</returns>
    /// <remarks>
    ///     spec: Docs/RE/formats/ui_manifests.md §10.2 — "plain newline-delimited list, no block keywords": CODE-CONFIRMED.
    ///     spec: Docs/RE/formats/ui_manifests.md §10.3 — per-line parsing rules: CODE-CONFIRMED.
    /// </remarks>
    public static TextureListManifest Parse(ReadOnlySpan<byte> span)
    {
        // Decode from CP949 before line splitting — this is a text manifest.
        // spec: Docs/RE/formats/ui_manifests.md §Identification — "CP949": PARSER-CONFIRMED.
        var text = Encoding.GetEncoding(949).GetString(span);
        return ParseText(text, VfsPathPrefix);
    }

    /// <summary>
    ///     Parses a texture-list file whose filenames resolve under <paramref name="vfsPathPrefix" />.
    ///     Use this for the character buckets such as <c>data/char/tex512512list.txt</c>.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/texture.md §List files and target directories — character bucket
    ///     list files prepend their own target directory and key entries by leading decimal texture id.
    /// </remarks>
    public static TextureListManifest Parse(ReadOnlySpan<byte> span, string vfsPathPrefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(vfsPathPrefix);
        var text = Encoding.GetEncoding(949).GetString(span);
        return ParseText(text, vfsPathPrefix);
    }

    /// <summary>Overload accepting pre-decoded text (primarily for unit testing).</summary>
    public static TextureListManifest ParseText(string text)
    {
        return ParseText(text, VfsPathPrefix);
    }

    /// <summary>Overload accepting pre-decoded text and a target VFS prefix.</summary>
    public static TextureListManifest ParseText(string text, string vfsPathPrefix)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(vfsPathPrefix);

        // Approximate initial capacity — each line ~19 bytes, so entries ≈ text.Length / 19.
        // We allocate slightly generously to avoid resizing.
        var entries = new List<TextureListEntry>(Math.Max(16, text.Length / 20));

        foreach (var rawLine in text.Split('\n'))
        {
            // Trim CRLF and any leading/trailing whitespace.
            // spec: Docs/RE/formats/ui_manifests.md §Identification — "CRLF line endings": PARSER-CONFIRMED.
            var line = rawLine.Trim();

            // Skip empty lines. No '#' comment convention in this file.
            // spec: Docs/RE/formats/ui_manifests.md §10.2 — "no '#' comment convention observed": CODE-CONFIRMED.
            if (line.Length == 0)
                continue;

            // Step 2: locate the last '.' in the line (extension delimiter).
            // spec: Docs/RE/formats/ui_manifests.md §10.3 step 2 — "locate the last '.' in the line": CODE-CONFIRMED.
            var dotPos = line.LastIndexOf('.');
            if (dotPos < 0)
                // No extension delimiter — cannot form a valid path; skip line.
                continue;

            // Step 3: split into name (before '.') and ext ('.' + remainder).
            // spec: Docs/RE/formats/ui_manifests.md §10.3 step 3 — "split into name and ext": CODE-CONFIRMED.
            var nameSpan = line.AsSpan(0, dotPos);
            var ext = line[dotPos..]; // includes the '.'

            // Step 4: parse leading decimal digits of name as integer → tex_id.
            // spec: Docs/RE/formats/ui_manifests.md §10.3 step 4 — "tex_id = atol(name)": CODE-CONFIRMED.
            // atol() stops at the first non-digit character; we mimic that here.
            var texId = ParseLeadingDigits(nameSpan);

            // Step 5: construct full VFS path = "data/item/texture/" + filename.
            // spec: Docs/RE/formats/ui_manifests.md §10.3 step 5 — "data/item/texture/ + name + ext": CODE-CONFIRMED.
            var vfsPath = vfsPathPrefix + line;

            entries.Add(new TextureListEntry(texId, vfsPath));
        }

        return new TextureListManifest(entries);
    }

    // ─── helper ───────────────────────────────────────────────────────────────

    /// <summary>
    ///     Parses the leading ASCII decimal digits of a character span as a non-negative integer.
    ///     Mirrors the C runtime <c>atol()</c> behaviour: stops at the first non-digit character.
    ///     Returns 0 when <paramref name="name" /> contains no leading digits (matches atol("abc")).
    ///     spec: Docs/RE/formats/ui_manifests.md §10.3 step 4 — "tex_id = atol(name)": CODE-CONFIRMED.
    /// </summary>
    private static int ParseLeadingDigits(ReadOnlySpan<char> name)
    {
        var value = 0;
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c < '0' || c > '9') break;
            value = value * 10 + (c - '0');
        }

        return value;
    }
}