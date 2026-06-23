using System.Security.Cryptography;

namespace MartialHeroes.Network.Crypto;

public interface IPaddingRandom
{
    void Fill(Span<byte> destination);
}

public sealed class CryptoPaddingRandom : IPaddingRandom
{
    public static readonly CryptoPaddingRandom Shared = new();

    public void Fill(Span<byte> destination)
    {
        RandomNumberGenerator.Fill(destination);
    }
}