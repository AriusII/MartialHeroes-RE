using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="ShaderContainerParser"/>.
/// All fixtures are synthetic in-memory ASCII byte buffers.
/// spec: Docs/RE/formats/shaders.md
///
/// Key spec facts:
/// - .vsh and .psh are plain-text Direct3D 9 shader assembly source files.
///   No magic bytes; identified by the version-declaration line (first line of the file).
///   spec: shaders.md §Identification — "No magic bytes. File begins with version-declaration text line.": VERIFIED.
/// - Encoding: 7-bit ASCII. Line endings: Windows CRLF.
///   spec: shaders.md §Identification — "Encoding: 7-bit ASCII": VERIFIED.
/// - Version line token "vs" → Vertex shader; "ps" → Pixel shader.
///   spec: shaders.md §Version Declaration Line — shader-type token: VERIFIED (all 4 samples).
/// - .psh/.vsh are a separate shader path from D3DX texture loading (.dds/.png/.tga/.bmp).
///   Per spec, all texture formats pass through D3DX; shaders go through the D3D9 assembler.
///   spec: shaders.md §Load Path — "full byte buffer passed to D3D9 assembler": CONFIRMED.
/// </summary>
public sealed class ShaderContainerParserTests
{
    // ── Fixture helpers ───────────────────────────────────────────────────────

    private static ReadOnlyMemory<byte> ToBytes(string text) =>
        new(Encoding.ASCII.GetBytes(text));

    // ── Vertex shader tests ───────────────────────────────────────────────────

    [Fact]
    public void Parse_VertexShader_ReturnsVertex()
    {
        // "vs" on the first line identifies a vertex shader.
        // spec: shaders.md §Version Declaration Line — "vs": VERIFIED.
        string source = "vs_1_1\r\n; vertex shader\r\ndef c0, 1.0, 0.0, 0.0, 0.0\r\n";
        ShaderSource result = ShaderContainerParser.Parse(ToBytes(source));

        Assert.Equal(ShaderType.Vertex, result.ShaderType);
    }

    [Fact]
    public void Parse_VertexShader_FullVersionLine_ReturnsVertex()
    {
        // Various VS version tokens ("vs.1.1", "vs_2_0") all begin with "vs".
        // spec: shaders.md §Version Declaration Line — starts with "vs": VERIFIED.
        string source = "vs.1.0\r\n";
        ShaderSource result = ShaderContainerParser.Parse(ToBytes(source));
        Assert.Equal(ShaderType.Vertex, result.ShaderType);
    }

    [Fact]
    public void Parse_VertexShader_CaseInsensitive()
    {
        // The parser must be case-insensitive for the version token.
        string source = "VS_2_0\r\n; uppercase version\r\n";
        ShaderSource result = ShaderContainerParser.Parse(ToBytes(source));
        Assert.Equal(ShaderType.Vertex, result.ShaderType);
    }

    // ── Pixel shader tests ────────────────────────────────────────────────────

    [Fact]
    public void Parse_PixelShader_ReturnsPixel()
    {
        // "ps" on the first line identifies a pixel shader.
        // spec: shaders.md §Version Declaration Line — "ps": VERIFIED.
        string source = "ps_1_4\r\n; pixel shader\r\n";
        ShaderSource result = ShaderContainerParser.Parse(ToBytes(source));

        Assert.Equal(ShaderType.Pixel, result.ShaderType);
    }

    [Fact]
    public void Parse_PixelShader_DotVersion()
    {
        // "ps.1.1" also begins with "ps".
        string source = "ps.1.1\r\n";
        ShaderSource result = ShaderContainerParser.Parse(ToBytes(source));
        Assert.Equal(ShaderType.Pixel, result.ShaderType);
    }

    [Fact]
    public void Parse_PixelShader_CaseInsensitive()
    {
        string source = "PS_3_0\r\n; uppercase pixel shader\r\n";
        ShaderSource result = ShaderContainerParser.Parse(ToBytes(source));
        Assert.Equal(ShaderType.Pixel, result.ShaderType);
    }

    // ── Source text round-trip ────────────────────────────────────────────────

    [Fact]
    public void Parse_SourceText_RoundTrip()
    {
        // The full source text (including the version line) must round-trip.
        // spec: shaders.md §Identification — "Encoding: 7-bit ASCII": VERIFIED.
        string source = "vs_1_1\r\n; test shader\r\nmov r0, c0\r\n";
        ShaderSource result = ShaderContainerParser.Parse(ToBytes(source));

        Assert.Equal(source, result.SourceText);
    }

    [Fact]
    public void Parse_RawBytes_Preserved()
    {
        // RawBytes must be the original byte buffer.
        // spec: shaders.md §Load Path — "full byte buffer passed to D3D9 assembler": CONFIRMED.
        string source = "ps_1_1\r\n; raw bytes test\r\n";
        byte[] bytes = Encoding.ASCII.GetBytes(source);
        ShaderSource result = ShaderContainerParser.Parse(new ReadOnlyMemory<byte>(bytes));

        Assert.Equal(bytes, result.RawBytes.ToArray());
    }

    // ── Error handling tests ──────────────────────────────────────────────────

    [Fact]
    public void Parse_UnrecognizedVersionLine_Throws()
    {
        // A first line that does not begin with "vs" or "ps" must throw InvalidDataException.
        // spec: shaders.md §Version Declaration Line — "vs" or "ps" required: VERIFIED.
        string source = "unknown_1_0\r\n; not a recognised shader\r\n";
        Assert.Throws<InvalidDataException>(() =>
            ShaderContainerParser.Parse(ToBytes(source)));
    }

    [Fact]
    public void Parse_EmptySource_Throws()
    {
        // An empty file (no version line) must throw.
        Assert.Throws<InvalidDataException>(() =>
            ShaderContainerParser.Parse(ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void Parse_CommentLineFirst_Throws()
    {
        // A comment (';' first) in the very first line is not a version declaration — must throw.
        string source = "; this is a comment\r\nvs_1_0\r\n";
        Assert.Throws<InvalidDataException>(() =>
            ShaderContainerParser.Parse(ToBytes(source)));
    }

    // ── Span overload ─────────────────────────────────────────────────────────

    [Fact]
    public void Parse_SpanOverload_ReturnsCorrectType()
    {
        // The Span<byte> overload delegates to the ReadOnlyMemory overload.
        string source = "vs_1_0\r\n";
        byte[] bytes = Encoding.ASCII.GetBytes(source);
        ShaderSource result = ShaderContainerParser.Parse(bytes.AsSpan());

        Assert.Equal(ShaderType.Vertex, result.ShaderType);
    }

    // ── Single-line (no newline) shader source ────────────────────────────────

    [Fact]
    public void Parse_SingleLineSingleToken_ReturnsVertex()
    {
        // A single-line file with no newline must parse the whole text as the version line.
        string source = "vs_1_1";
        ShaderSource result = ShaderContainerParser.Parse(ToBytes(source));

        Assert.Equal(ShaderType.Vertex, result.ShaderType);
        Assert.Equal(source, result.SourceText);
    }

    // ── Shader path separation (spec note) ────────────────────────────────────

    [Fact]
    public void Parse_ShaderPath_IsDistinctFromTextureD3DX()
    {
        // .psh/.vsh go through the D3D9 assembler path, NOT through D3DX texture decode.
        // Per spec, all texture formats (.dds, .png, .tga, .bmp) pass through one D3DX path;
        // shaders are a separate path that passes the full raw byte buffer to D3D9.
        // spec: shaders.md §Load Path — "full byte buffer passed to D3D9 assembler": CONFIRMED.
        // spec: Docs/RE/formats/texture.md §Implementation guidance — D3DX path for textures.
        // This test verifies the ShaderSource model carries RawBytes for the assembler path.
        string source = "ps_1_4\r\ntex t0\r\nmov r0, t0\r\n";
        ShaderSource result = ShaderContainerParser.Parse(ToBytes(source));

        // RawBytes is the exact buffer that would be passed to D3D9 assembler.
        Assert.Equal(ShaderType.Pixel, result.ShaderType);
        Assert.NotEmpty(result.RawBytes.ToArray());
    }

    // ── CRLF vs LF line endings ───────────────────────────────────────────────

    [Fact]
    public void Parse_LfOnly_AlsoParses()
    {
        // The spec says CRLF is the expected encoding, but the parser should tolerate LF-only.
        // We do NOT assert any specific behaviour for non-CRLF files — only that it does not crash
        // and correctly identifies the shader type from the first line.
        string source = "vs_1_0\n; lf only\n";
        ShaderSource result = ShaderContainerParser.Parse(ToBytes(source));

        Assert.Equal(ShaderType.Vertex, result.ShaderType);
    }

    // ── Multi-instruction vertex shader ──────────────────────────────────────

    [Fact]
    public void Parse_MultiInstructionShader_AllTextPreserved()
    {
        // A realistic multi-instruction shader: full source must be in SourceText.
        // spec: shaders.md §Format Overview — "plain-text D3D9 assembly": VERIFIED.
        string source = "vs_1_1\r\n" +
                        "dcl_position v0\r\n" +
                        "dcl_texcoord v1\r\n" +
                        "m4x4 oPos, v0, c0\r\n" +
                        "mov oT0.xy, v1\r\n";
        ShaderSource result = ShaderContainerParser.Parse(ToBytes(source));

        Assert.Equal(ShaderType.Vertex, result.ShaderType);
        Assert.Contains("dcl_position", result.SourceText);
        Assert.Contains("m4x4 oPos", result.SourceText);
    }
}
