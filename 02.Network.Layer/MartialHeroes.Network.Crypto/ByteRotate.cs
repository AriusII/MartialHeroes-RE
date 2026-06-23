using System.Runtime.CompilerServices;

namespace MartialHeroes.Network.Crypto;

internal static class ByteRotate
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Rol8(byte value, int bits)
    {
        return (bits & 7) == 0 ? value : (byte)((value << bits) | (value >> (8 - bits)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Ror8(byte value, int bits)
    {
        return (bits & 7) == 0 ? value : (byte)((value >> bits) | (value << (8 - bits)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Not8(byte value)
    {
        return (byte)~value;
    }
}