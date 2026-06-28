using System;
using System.Text;

namespace MartialHeroes.Explorer.Files.Services.Decoders;

public static class TextEncodings
{
    private static readonly object Gate = new();
    private static Encoding? _cp949;

    public static Encoding Cp949
    {
        get
        {
            if (_cp949 is not null) return _cp949;

            lock (Gate)
            {
                if (_cp949 is null)
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    _cp949 = Encoding.GetEncoding(949);
                }
            }

            return _cp949;
        }
    }

    public static double PrintableRatio(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0) return 1.0;

        var printable = 0;
        var sampled = Math.Min(bytes.Length, 4096);
        for (var i = 0; i < sampled; i++)
        {
            var b = bytes[i];
            if (b is 0x09 or 0x0A or 0x0D || (b >= 0x20 && b < 0x7F) || b >= 0x80)
                printable++;
        }

        return printable / (double)sampled;
    }
}