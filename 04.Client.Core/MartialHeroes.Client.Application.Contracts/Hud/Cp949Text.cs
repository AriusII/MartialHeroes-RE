using System.Text;

namespace MartialHeroes.Client.Application.Contracts.Hud;

public static class Cp949Text
{
    private static readonly Encoding Cp949;

    static Cp949Text()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp949 = Encoding.GetEncoding(949);
    }

    public static string Decode(ReadOnlySpan<byte> cp949Bytes)
    {
        var nul = cp949Bytes.IndexOf((byte)0);
        var body = nul >= 0 ? cp949Bytes[..nul] : cp949Bytes;
        return body.IsEmpty ? string.Empty : Cp949.GetString(body);
    }
}