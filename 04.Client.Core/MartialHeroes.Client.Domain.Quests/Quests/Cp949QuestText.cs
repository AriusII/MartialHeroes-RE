using System.Text;

namespace MartialHeroes.Client.Domain.Quests.Quests;

internal static class Cp949QuestText
{
    private static readonly Encoding Cp949;

    static Cp949QuestText()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp949 = Encoding.GetEncoding(949);
    }

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        var nul = bytes.IndexOf((byte)0);
        var body = nul >= 0 ? bytes[..nul] : bytes;
        return body.IsEmpty ? string.Empty : Cp949.GetString(body);
    }
}
