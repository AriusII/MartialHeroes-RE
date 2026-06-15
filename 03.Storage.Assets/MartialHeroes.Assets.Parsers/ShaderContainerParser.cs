using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>.vsh</c> (vertex shader) and <c>.psh</c> (pixel shader) Direct3D 9
/// assembly source text files.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/shaders.md
/// ZERO rendering/engine dependencies. This parser does NOT compile or validate the shader.
/// It exposes the raw source text only.
/// spec: Docs/RE/formats/shaders.md §Format Overview — "plain-text D3D9 assembly; no proprietary container": VERIFIED.
/// </remarks>
public static class ShaderContainerParser
{
    // Version line token: "vs" (vertex shader) or "ps" (pixel shader).
    // spec: Docs/RE/formats/shaders.md §Version Declaration Line — shader-type token: VERIFIED (all 4 samples).
    private const string VertexToken = "vs";
    private const string PixelToken = "ps";

    /// <summary>
    /// Parses a <c>.vsh</c> or <c>.psh</c> shader source file.
    /// </summary>
    /// <param name="data">Raw file bytes from VFS (7-bit ASCII content, CRLF line endings).</param>
    /// <returns>Decoded shader source with type and text.</returns>
    /// <exception cref="InvalidDataException">Thrown when the version line is absent or unrecognized.</exception>
    /// <remarks>
    /// spec: Docs/RE/formats/shaders.md §Identification — "No magic bytes. File begins with version-declaration text line.": VERIFIED.
    /// spec: Docs/RE/formats/shaders.md §Identification — "Encoding: 7-bit ASCII": VERIFIED.
    /// spec: Docs/RE/formats/shaders.md §Identification — "Line endings: Windows CRLF": VERIFIED.
    /// </remarks>
    public static ShaderSource Parse(ReadOnlyMemory<byte> data)
    {
        // Decode as 7-bit ASCII.
        // spec: Docs/RE/formats/shaders.md — "No bytes above 0x7E observed": VERIFIED.
        string text = Encoding.ASCII.GetString(data.Span);

        // The first line is the version declaration.
        // spec: Docs/RE/formats/shaders.md §Version Declaration Line — first line: VERIFIED.
        ReadOnlySpan<char> remaining = text.AsSpan();
        int lineEnd = FindLineEnd(remaining);
        if (lineEnd < 0)
            lineEnd = remaining.Length; // Single-line file — treat whole text as the first line.

        ReadOnlySpan<char> firstLine = remaining[..lineEnd].TrimEnd('\r');

        ShaderType shaderType;
        if (firstLine.StartsWith(VertexToken.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            // spec: Docs/RE/formats/shaders.md §Version Declaration Line — shader-type token "vs": VERIFIED.
            shaderType = ShaderType.Vertex;
        }
        else if (firstLine.StartsWith(PixelToken.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            // spec: Docs/RE/formats/shaders.md §Version Declaration Line — shader-type token "ps": VERIFIED.
            shaderType = ShaderType.Pixel;
        }
        else
        {
            throw new InvalidDataException(
                $"Shader parse error: version line '{firstLine.ToString()}' does not begin with 'vs' or 'ps'. " +
                "spec: Docs/RE/formats/shaders.md §Version Declaration Line.");
        }

        return new ShaderSource
        {
            ShaderType = shaderType,
            SourceText = text,
            RawBytes = data,
        };
    }

    /// <inheritdoc cref="Parse(ReadOnlyMemory{byte})"/>
    public static ShaderSource Parse(ReadOnlySpan<byte> span) =>
        // Explicitly target the ReadOnlyMemory<byte> overload to avoid infinite recursion:
        // byte[].ToArray() converts to both ReadOnlyMemory<byte> and ReadOnlySpan<byte>,
        // and the compiler would prefer the span overload (infinite loop) without the cast.
        Parse(new ReadOnlyMemory<byte>(span.ToArray()));

    private static int FindLineEnd(ReadOnlySpan<char> text)
    {
        int lf = text.IndexOf('\n');
        return lf < 0 ? -1 : lf + 1; // include the LF in position
    }
}