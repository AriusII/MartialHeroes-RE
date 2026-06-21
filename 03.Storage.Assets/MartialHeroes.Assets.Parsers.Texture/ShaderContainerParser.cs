using System.Text;
using MartialHeroes.Assets.Parsers.Texture.Models;

namespace MartialHeroes.Assets.Parsers.Texture;

/// <summary>
///     Parser for <c>.vsh</c> (vertex shader) and <c>.psh</c> (pixel shader) Direct3D 9
///     assembly source text files.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/shaders.md
///     ZERO rendering/engine dependencies. This parser does NOT compile or validate the shader.
///     It exposes the raw source text only.
///     spec: Docs/RE/formats/shaders.md §Format Overview — "plain-text D3D9 assembly; no proprietary container": VERIFIED.
/// </remarks>
public static class ShaderContainerParser
{
    // Version line token: "vs" (vertex shader) or "ps" (pixel shader).
    // spec: Docs/RE/formats/shaders.md §Version Declaration Line — shader-type token: VERIFIED (all 4 samples).
    private const string VertexToken = "vs";
    private const string PixelToken = "ps";

    /// <summary>
    ///     Parses a <c>.vsh</c> or <c>.psh</c> shader source file.
    /// </summary>
    /// <param name="data">Raw file bytes from VFS (7-bit ASCII content, CRLF line endings).</param>
    /// <returns>Decoded shader source with type and text.</returns>
    /// <exception cref="InvalidDataException">Thrown when the version line is absent or unrecognized.</exception>
    /// <remarks>
    ///     spec: Docs/RE/formats/shaders.md §Identification — "No magic bytes. File begins with version-declaration text
    ///     line.": VERIFIED.
    ///     spec: Docs/RE/formats/shaders.md §Identification — "Encoding: 7-bit ASCII": VERIFIED.
    ///     spec: Docs/RE/formats/shaders.md §Identification — "Line endings: Windows CRLF": VERIFIED.
    /// </remarks>
    public static ShaderSource Parse(ReadOnlyMemory<byte> data)
    {
        // Delegate to the span overload; RawBytes is stored as the caller-supplied Memory slice
        // (no extra copy for the Memory path).
        return ParseCore(data.Span, data);
    }

    /// <inheritdoc cref="Parse(ReadOnlyMemory{byte})" />
    public static ShaderSource Parse(ReadOnlySpan<byte> span)
    {
        // For the span path, RawBytes must own its buffer (caller span has no guaranteed lifetime).
        return ParseCore(span, new ReadOnlyMemory<byte>(span.ToArray()));
    }

    private static ShaderSource ParseCore(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> rawBytes)
    {
        // Decode as Latin-1 (ISO-8859-1) — a byte-preserving 8-bit encoding.
        // Shader CODE TOKENS are 7-bit ASCII (version line, mnemonics, registers, def literals).
        // However COMMENT TEXT (everything after ';') may contain CP949/EUC-KR bytes above 0x7E:
        // two verified cel pixel-shader samples open with a Korean comment line.
        // Using ASCII would silently replace bytes > 0x7F with '?' and mangle those comments.
        // Latin-1 maps bytes 0x00–0xFF to identical Unicode codepoints, so the ASCII code tokens
        // round-trip exactly and the CP949 comment bytes are preserved (as Latin-1 surrogates).
        // Since the assembler ignores everything after ';', this never affects correctness of the
        // shader code; it only matters for faithful text representation and debug display.
        // spec: Docs/RE/formats/shaders.md §Identification —
        //   "Shader code tokens are 7-bit ASCII. Comment text is NOT guaranteed ASCII: two
        //    verified cel pixel-shader samples open with a CP949/EUC-KR Korean comment line
        //    (bytes above 0x7E). Do not assert no bytes above 0x7E; a faithful reader must
        //    tolerate CP949 in comments." VERIFIED.
        var text = Encoding.Latin1.GetString(span);

        // The first line is the version declaration.
        // spec: Docs/RE/formats/shaders.md §Version Declaration Line — first line: VERIFIED.
        var remaining = text.AsSpan();
        var lineEnd = FindLineEnd(remaining);
        if (lineEnd < 0)
            lineEnd = remaining.Length; // Single-line file — treat whole text as the first line.

        var firstLine = remaining[..lineEnd].TrimEnd('\r');

        ShaderType shaderType;
        if (firstLine.StartsWith(VertexToken.AsSpan(), StringComparison.OrdinalIgnoreCase))
            // spec: Docs/RE/formats/shaders.md §Version Declaration Line — shader-type token "vs": VERIFIED.
            shaderType = ShaderType.Vertex;
        else if (firstLine.StartsWith(PixelToken.AsSpan(), StringComparison.OrdinalIgnoreCase))
            // spec: Docs/RE/formats/shaders.md §Version Declaration Line — shader-type token "ps": VERIFIED.
            shaderType = ShaderType.Pixel;
        else
            throw new InvalidDataException(
                $"Shader parse error: version line '{firstLine.ToString()}' does not begin with 'vs' or 'ps'. " +
                "spec: Docs/RE/formats/shaders.md §Version Declaration Line.");

        return new ShaderSource
        {
            ShaderType = shaderType,
            SourceText = text,
            RawBytes = rawBytes
        };
    }

    private static int FindLineEnd(ReadOnlySpan<char> text)
    {
        var lf = text.IndexOf('\n');
        return lf < 0 ? -1 : lf + 1; // include the LF in position
    }
}