namespace MartialHeroes.Assets.Parsers.Texture.Models;

/// <summary>
///     Shader type discriminator — vertex or pixel.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/shaders.md §Version Declaration Line — shader-type token: VERIFIED.
/// </remarks>
public enum ShaderType
{
    /// <summary>Vertex shader. Version line token: "vs". spec: Docs/RE/formats/shaders.md §Version Declaration Line.</summary>
    Vertex,

    /// <summary>Pixel shader. Version line token: "ps". spec: Docs/RE/formats/shaders.md §Version Declaration Line.</summary>
    Pixel
}

/// <summary>
///     Decoded result of a <c>.vsh</c> or <c>.psh</c> Direct3D 9 shader assembly source file.
///     The parser does not compile or validate the shader; it exposes the raw source text only.
///     Re-authoring for modern hardware is <c>Assets.Mapping</c>'s responsibility.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/shaders.md §Format Overview:
///     "plain-text Direct3D 9 shader assembly source. No proprietary container." VERIFIED.
///     spec: Docs/RE/formats/shaders.md §Identification:
///     "No magic bytes. File begins with version-declaration text line." VERIFIED.
///     Encoding: 7-bit ASCII throughout. Line endings: Windows CRLF. VERIFIED.
/// </remarks>
public sealed class ShaderSource
{
    /// <summary>
    ///     Shader type inferred from the first line of the file ("vs.x.x" or "ps.x.x").
    ///     spec: Docs/RE/formats/shaders.md §Version Declaration Line: VERIFIED (all 4 samples).
    /// </summary>
    public required ShaderType ShaderType { get; init; }

    /// <summary>
    ///     Full source text, decoded byte-preservingly (Latin-1 / ISO-8859-1).
    ///     Shader CODE TOKENS are 7-bit ASCII. COMMENT TEXT (everything after ';') may contain
    ///     CP949/EUC-KR bytes above 0x7E — two verified cel pixel-shader samples open with a Korean
    ///     comment line. Latin-1 preserves these bytes as-is so the code tokens round-trip exactly
    ///     and comments are not mangled.
    ///     spec: Docs/RE/formats/shaders.md §Identification —
    ///     "Shader code tokens are 7-bit ASCII. Comment text is NOT guaranteed ASCII: two verified
    ///     cel pixel-shader samples open with a CP949/EUC-KR Korean comment line (bytes above 0x7E).
    ///     Do not assert no bytes above 0x7E." VERIFIED.
    /// </summary>
    public required string SourceText { get; init; }

    /// <summary>
    ///     Raw source bytes as delivered from the VFS.
    ///     Retained for downstream use (e.g. passing the buffer to a D3D9 assembler).
    ///     spec: Docs/RE/formats/shaders.md §Load Path — "full byte buffer passed to D3D9 assembler": CONFIRMED.
    /// </summary>
    public required ReadOnlyMemory<byte> RawBytes { get; init; }
}