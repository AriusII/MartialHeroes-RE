// spec: Docs/RE/opcodes.md — zero-copy typed reinterpretation of a decrypted payload span.
//
// Reflection-free, zero-allocation. This is the reusable seam that reinterprets a payload's leading
// bytes as a Pack=1 wire struct in place via MemoryMarshal — no copies, no boxing, no Dictionary.
// It formalizes the `MemoryMarshal.AsRef<T>(payload)` idiom that consumers (e.g. Client.Application's
// OnUnhandled fan-out for the additional S2C packets) already perform by hand, so every packet struct
// in this project can be decoded the same way without touching the IPacketHandler dispatch seam.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Routing;

/// <summary>
/// Zero-copy helpers that reinterpret a decrypted payload span as a <c>Pack=1</c> wire struct in place.
/// Reflection-free and allocation-free. spec: Docs/RE/opcodes.md.
/// </summary>
/// <remarks>
/// Complements <see cref="PacketRouter"/>: the router owns the compile-time opcode→handler switch for
/// the core <see cref="IPacketHandler"/> seam; this type lets any consumer reinterpret one of the
/// project's packet structs from a raw payload without that seam, additively. Both share the same
/// <c>MemoryMarshal.AsRef</c> mechanism — no reflection, no per-packet allocation.
/// </remarks>
public static class TypedPacketView
{
    /// <summary>
    /// Reinterprets the leading bytes of <paramref name="payload"/> as a read-only reference to
    /// <typeparamref name="T"/> in place — no copy, no allocation. <paramref name="minSize"/> is the
    /// struct's wire size (or fixed-header size for variable-length packets).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If the payload is shorter than <paramref name="minSize"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref readonly T As<T>(ReadOnlySpan<byte> payload, int minSize)
        where T : struct
    {
        if (payload.Length < minSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload), payload.Length,
                $"Payload too small for {typeof(T).Name}: need {minSize} bytes.");
        }

        return ref MemoryMarshal.AsRef<T>(payload);
    }

    /// <summary>
    /// Tries to reinterpret the leading bytes of <paramref name="payload"/> as <typeparamref name="T"/>
    /// without throwing. Returns <see langword="false"/> (and leaves <paramref name="view"/> at the
    /// default) when the payload is shorter than <paramref name="minSize"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryAs<T>(ReadOnlySpan<byte> payload, int minSize, out T view)
        where T : struct
    {
        if (payload.Length < minSize)
        {
            view = default;
            return false;
        }

        view = MemoryMarshal.AsRef<T>(payload);
        return true;
    }
}
