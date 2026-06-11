using System.Runtime.CompilerServices;

namespace MartialHeroes.Network.Crypto;

/// <summary>
/// 8-bit circular rotation and one's-complement primitives used by the wire byte cipher.
/// These are ordinary bit operations (not magic constants); the rotation *amounts* the cipher
/// uses are cited at their call sites in <see cref="WireCipher"/>.
/// </summary>
internal static class ByteRotate
{
    /// <summary>Rotate-left an 8-bit value by <paramref name="bits"/> (0..7).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Rol8(byte value, int bits)
        => (byte)((value << bits) | (value >> (8 - bits)));

    /// <summary>Rotate-right an 8-bit value by <paramref name="bits"/> (0..7).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Ror8(byte value, int bits)
        => (byte)((value >> bits) | (value << (8 - bits)));

    /// <summary>8-bit one's-complement (bitwise NOT, masked to a byte).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Not8(byte value)
        => (byte)~value;
}
