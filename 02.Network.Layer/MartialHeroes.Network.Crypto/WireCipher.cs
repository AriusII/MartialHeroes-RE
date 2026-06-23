using static MartialHeroes.Network.Crypto.ByteRotate;

namespace MartialHeroes.Network.Crypto;

public static class WireCipher
{
    private const int Rounds = 3;

    private const int ForwardRol = 3;
    private const int ForwardRor = 1;

    private const byte ForwardWhitenAdd = 0x48;

    private const int BackwardRol = 4;
    private const int BackwardRor = 3;

    private const byte BackwardWhitenXor = 0x13;

    public static void EncryptInPlace(Span<byte> payload)
    {
        if (payload.IsEmpty) return;

        for (var round = 0; round < Rounds; round++)
        {
            EncryptForwardSweep(payload);
            EncryptBackwardSweep(payload);
        }
    }

    public static void DecryptInPlace(Span<byte> payload)
    {
        if (payload.IsEmpty) return;

        for (var round = 0; round < Rounds; round++)
        {
            DecryptBackwardSweep(payload);
            DecryptForwardSweep(payload);
        }
    }


    private static void EncryptForwardSweep(Span<byte> payload)
    {
        byte acc = 0;
        var p = (byte)payload.Length;

        for (var i = 0; i < payload.Length; i++)
        {
            var t = Rol8(payload[i], ForwardRol);
            t = (byte)(t + p);
            t = (byte)(t ^ acc);
            acc = t;
            payload[i] = (byte)(ForwardWhitenAdd + Not8(Ror8(t, ForwardRor)));
            p = (byte)(p - 1);
        }
    }


    private static void EncryptBackwardSweep(Span<byte> payload)
    {
        byte acc = 0;
        var p = (byte)payload.Length;

        for (var i = payload.Length - 1; i >= 0; i--)
        {
            var t = Rol8(payload[i], BackwardRol);
            t = (byte)(t + p);
            t = (byte)(t ^ acc);
            acc = t;
            payload[i] = Ror8((byte)(t ^ BackwardWhitenXor), BackwardRor);
            p = (byte)(p - 1);
        }
    }


    private static void DecryptForwardSweep(Span<byte> payload)
    {
        byte accPrev = 0;
        var p = (byte)payload.Length;

        for (var i = 0; i < payload.Length; i++)
        {
            var rored = Not8((byte)(payload[i] - ForwardWhitenAdd));
            var t = Rol8(rored, ForwardRor);
            var input = (byte)((t ^ accPrev) - p);
            payload[i] = Ror8(input, ForwardRol);
            accPrev = t;
            p = (byte)(p - 1);
        }
    }


    private static void DecryptBackwardSweep(Span<byte> payload)
    {
        byte accPrev = 0;
        var p = (byte)payload.Length;

        for (var i = payload.Length - 1; i >= 0; i--)
        {
            var t = (byte)(Rol8(payload[i], BackwardRor) ^
                           BackwardWhitenXor);
            var input = (byte)((t ^ accPrev) - p);
            payload[i] = Ror8(input, BackwardRol);
            accPrev = t;
            p = (byte)(p - 1);
        }
    }
}