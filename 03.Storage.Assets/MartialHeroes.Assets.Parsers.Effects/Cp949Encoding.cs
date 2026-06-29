using System.Text;

namespace MartialHeroes.Assets.Parsers.Effects;

internal static class Cp949Encoding
{
    public static readonly Encoding Instance;

    static Cp949Encoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Instance = Encoding.GetEncoding(949);
    }
}