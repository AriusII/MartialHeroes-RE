using System.Text;

namespace MartialHeroes.Client.Application.Hud;

/// <summary>
/// Decodes CP949 (code page 949 / EUC-KR) byte runs into managed strings at the producer boundary
/// before they enter a HUD event DTO.
/// </summary>
/// <remarks>
/// All legacy game text — chat lines, sender / target names — is CP949 with no BOM. The HUD DTOs carry
/// already-decoded managed strings (never raw bytes), so the demo/stub publishers and the future packet
/// handlers decode at the seam through this helper. The code-pages provider is registered once in the
/// static constructor, per the project-wide convention. NUL-terminated buffers (the legacy fixed
/// name/text fields) are trimmed at the first NUL.
/// spec: Docs/RE/specs/chat.md §0 (all chat text is CP949 / EUC-KR);
/// Docs/RE/formats/misc_data.md §6 (register CodePagesEncodingProvider, decode with code page 949).
/// </remarks>
public static class Cp949Text
{
    private static readonly Encoding Cp949;

    static Cp949Text()
    {
        // Register the code-pages provider once so Encoding.GetEncoding(949) resolves on .NET (it is
        // not a built-in encoding). spec: Docs/RE/formats/misc_data.md §6 (CodePagesEncodingProvider).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp949 = Encoding.GetEncoding(949); // spec: Docs/RE/formats/misc_data.md §6 (code page 949 / EUC-KR)
    }

    /// <summary>
    /// Decodes a CP949 byte run to a managed string, stopping at the first NUL (the legacy fixed-buffer
    /// terminator convention). An empty or all-NUL run yields <see cref="string.Empty"/>.
    /// </summary>
    public static string Decode(ReadOnlySpan<byte> cp949Bytes)
    {
        int nul = cp949Bytes.IndexOf((byte)0);
        ReadOnlySpan<byte> body = nul >= 0 ? cp949Bytes[..nul] : cp949Bytes;
        return body.IsEmpty ? string.Empty : Cp949.GetString(body);
    }
}