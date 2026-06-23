using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Routing.Routing;

public static class TypedPacketView
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref readonly T As<T>(ReadOnlySpan<byte> payload, int minSize)
        where T : struct
    {
        if (payload.Length < minSize)
            throw new ArgumentOutOfRangeException(
                nameof(payload), payload.Length,
                $"Payload too small for {typeof(T).Name}: need {minSize} bytes.");

        return ref MemoryMarshal.AsRef<T>(payload);
    }

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