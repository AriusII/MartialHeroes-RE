using System.Runtime.CompilerServices;

namespace MartialHeroes.Network.Crypto;

/// <summary>
/// 8-bit circular rotation and one's-complement primitives used by the wire byte cipher.
/// These are ordinary bit operations (not magic constants); the rotation *amounts* the cipher
/// uses are cited at their call sites in <see cref="WireCipher"/>.
/// </summary>
internal static class ByteRotate
{
    /// <summary>
    /// Rotate-left an 8-bit value by <paramref name="bits"/> (0..7). A rotation that is a multiple of
    /// 8 (including 0) is the identity; guarded explicitly because the complementary shift
    /// <c>value &gt;&gt; (8 - bits)</c> would shift by 8 when <c>bits == 0</c>. The cipher only ever
    /// rotates by the nonzero constants 3/4 (<see cref="WireCipher"/>), so this guard never fires on
    /// the hot path — it is defensive correctness.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Rol8(byte value, int bits)
        => (bits & 7) == 0 ? value : (byte)((value << bits) | (value >> (8 - bits)));

    /// <summary>
    /// Rotate-right an 8-bit value by <paramref name="bits"/> (0..7). A rotation that is a multiple of
    /// 8 (including 0) is the identity; guarded as in <see cref="Rol8"/>. The cipher only ever rotates
    /// by the nonzero constants 1/3, so this guard never fires on the hot path.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Ror8(byte value, int bits)
        => (bits & 7) == 0 ? value : (byte)((value >> bits) | (value << (8 - bits)));

    /// <summary>8-bit one's-complement (bitwise NOT, masked to a byte).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Not8(byte value)
        => (byte)~value;
}