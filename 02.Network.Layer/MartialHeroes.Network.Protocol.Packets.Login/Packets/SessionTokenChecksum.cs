using System.Security.Cryptography;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

public static class SessionTokenChecksum
{
    public const int SessionTokenLength = 33;

    private const int HexCharCount = 32;

    public static void WriteSelfChecksum(Span<byte> destination33)
    {
        if (destination33.Length != SessionTokenLength)
            throw new ArgumentException(
                $"destination33 must be exactly {SessionTokenLength} bytes (SessionToken field width).",
                nameof(destination33));

        destination33.Clear();

        var exePath = Environment.ProcessPath;

        if (exePath is null)
            return;

        byte[]? digest;
        try
        {
            var exeBytes = File.ReadAllBytes(exePath);
            digest = MD5.HashData(exeBytes);
        }
        catch
        {
            return;
        }

        WriteHexLower(digest, destination33);
    }

    public static bool TryWriteChecksumOf(string exePath, Span<byte> destination33)
    {
        if (destination33.Length != SessionTokenLength)
            throw new ArgumentException(
                $"destination33 must be exactly {SessionTokenLength} bytes (SessionToken field width).",
                nameof(destination33));

        destination33.Clear();

        if (string.IsNullOrWhiteSpace(exePath))
            return false;

        byte[] digest;
        try
        {
            var exeBytes = File.ReadAllBytes(exePath);
            digest = MD5.HashData(exeBytes);
        }
        catch
        {
            return false;
        }

        WriteHexLower(digest, destination33);
        return true;
    }

    private static void WriteHexLower(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var hexTable =
            "0123456789abcdef"u8;

        for (var i = 0; i < source.Length; i++)
        {
            var b = source[i];
            destination[i * 2] = hexTable[b >> 4];
            destination[i * 2 + 1] = hexTable[b & 0x0F];
        }

        _ = HexCharCount;
    }
}