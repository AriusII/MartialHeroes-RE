
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core;

namespace MartialHeroes.Network.Protocol.Routing.Routing;

public static partial class PacketRouter
{
    public static bool Route(ReadOnlySpan<byte> frame, IPacketHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var header = FrameHeader.Read(frame);
        var payload = FrameHeader.Payload(frame);
        return Route(header.PackedOpcode, payload, handler);
    }

    public static bool Route(uint packedOpcode, ReadOnlySpan<byte> payload, IPacketHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return RouteGenerated(packedOpcode, payload, handler);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref readonly T Reinterpret<T>(ReadOnlySpan<byte> payload, int wireSize)
        where T : struct
    {
        if (payload.Length < wireSize)
            throw new ArgumentOutOfRangeException(
                nameof(payload), payload.Length,
                $"Payload too small for {typeof(T).Name}: need {wireSize} bytes.");

        return ref MemoryMarshal.AsRef<T>(payload);
    }
}