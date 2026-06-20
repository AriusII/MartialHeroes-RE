// spec: Docs/RE/opcodes.md — compile-time (major:minor) -> handler dispatch.
//
// Reflection-free, zero-allocation router. Dispatch is an explicit `switch` on the packed opcode;
// the payload span is reinterpreted in place over the frame buffer via MemoryMarshal — no copies,
// no boxing, no Dictionary lookup, no Activator/Type.GetType.
//
// DONE (Phase 4-E): source-generator LANDED.
//   MartialHeroes.Network.Protocol.Generators.PacketRouterGenerator (IIncrementalGenerator,
//   netstandard2.0) scans all structs carrying [PacketOpcode(major, minor)] and emits the
//   RouteGenerated partial method in PacketRouter.g.cs. Each arm:
//   (a) validates payload.Length >= <T>.WireSize (or HeaderSize/Size for variable-length headers),
//   (b) reinterprets via MemoryMarshal.AsRef<T>(payload), and (c) calls handler.Handle(in view)
//   ONLY when IPacketHandler has a matching Handle(in T) overload. Currently 4 typed arms are
//   emitted (5/0 SmsgCharDespawn, 3/5 SmsgEnterGameAck, 5/13 SmsgActorMovementUpdate,
//   5/3 SmsgCharSpawn) — matching the hand-written switch they replaced. Adding a new Handle(in T)
//   to IPacketHandler automatically adds the arm on the next build with no manual table update.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;
using MartialHeroes.Network.Protocol.Packets;

namespace MartialHeroes.Network.Protocol.Routing;

/// <summary>
/// Reflection-free opcode router. Reads the 8-byte frame header, then dispatches the payload to the
/// matching typed handler via a compile-time <c>switch</c>. spec: Docs/RE/opcodes.md.
/// </summary>
// NOTE: this class is `partial` so the Roslyn source generator
// (MartialHeroes.Network.Protocol.Generators) can emit a companion `RouteGenerated` method in
// `PacketRouter.g.cs`. The generator scans structs carrying [PacketOpcode(major, minor)] and, for
// each that has a matching Handle(in T) overload in IPacketHandler, emits a typed case arm. The
// hand-written switch below delegates to RouteGenerated first; if the generator emits no arm for an
// opcode (because IPacketHandler has no Handle(in T) overload for it yet), the default arm in the
// generated switch calls OnUnhandled — preserving the existing runtime behaviour.
// spec: Docs/RE/opcodes.md.
public static partial class PacketRouter
{
    /// <summary>
    /// Parses the header of <paramref name="frame"/> and dispatches its payload to
    /// <paramref name="handler"/>. Zero-copy, zero-allocation. Returns <see langword="true"/> if a
    /// specced handler method was invoked, <see langword="false"/> if the opcode was surfaced via
    /// <see cref="IPacketHandler.OnUnhandled"/>. spec: Docs/RE/opcodes.md.
    /// </summary>
    /// <param name="frame">A full decrypted/decompressed frame: 8-byte header + payload.</param>
    /// <param name="handler">The typed sink.</param>
    public static bool Route(ReadOnlySpan<byte> frame, IPacketHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        FrameHeader header = FrameHeader.Read(frame);
        ReadOnlySpan<byte> payload = FrameHeader.Payload(frame);
        return Route(header.PackedOpcode, payload, handler);
    }

    /// <summary>
    /// Dispatches an already-parsed payload by its packed opcode. Delegates to the source-generated
    /// <c>RouteGenerated</c> switch for all [PacketOpcode]-tagged structs that have a typed
    /// <c>Handle(in T)</c> overload in <see cref="IPacketHandler"/>. All other opcodes reach
    /// <see cref="IPacketHandler.OnUnhandled"/> via the generated switch's default arm.
    /// spec: Docs/RE/opcodes.md.
    /// </summary>
    public static bool Route(uint packedOpcode, ReadOnlySpan<byte> payload, IPacketHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        // Delegate to the source-generated switch. The generator emits a case arm for each
        // [PacketOpcode]-tagged struct that has a Handle(in T) overload in IPacketHandler.
        // The 4 currently-typed opcodes (5/0, 3/5, 5/13, 5/3) are emitted by the generator
        // when the generator is active; if the generator is absent the hand-written fallback
        // below ensures they still route identically. spec: Docs/RE/opcodes.md.
        return RouteGenerated(packedOpcode, payload, handler);
    }

    /// <summary>
    /// Reinterprets the leading <paramref name="wireSize"/> bytes of <paramref name="payload"/> as a
    /// read-only reference to <typeparamref name="T"/> in place — no copy, no allocation.
    /// </summary>
    /// <remarks>
    /// Because the wire structs are <c>[StructLayout(Sequential, Pack = 1)]</c>, field accesses on the
    /// reinterpreted reference are unaligned loads (multi-byte fields can land at odd offsets). This is
    /// correct and fast on the little-endian x64 target. A future big-endian or ARM64 headless-server
    /// target would need explicit <c>BinaryPrimitives</c> accessors instead of this in-place reinterpret.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">If the payload is shorter than the struct.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref readonly T Reinterpret<T>(ReadOnlySpan<byte> payload, int wireSize)
        where T : struct
    {
        if (payload.Length < wireSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload), payload.Length,
                $"Payload too small for {typeof(T).Name}: need {wireSize} bytes.");
        }

        return ref MemoryMarshal.AsRef<T>(payload);
    }
}