// spec: Docs/RE/opcodes.md — compile-time (major:minor) -> handler dispatch.
//
// Reflection-free, zero-allocation router. Dispatch is an explicit `switch` on the packed opcode;
// the payload span is reinterpreted in place over the frame buffer via MemoryMarshal — no copies,
// no boxing, no Dictionary lookup, no Activator/Type.GetType.
//
// TODO: source-generate this switch.
//   Design: a Roslyn IIncrementalGenerator scans for structs carrying [PacketOpcode(major, minor)]
//   (see Opcodes/PacketOpcodeAttribute.cs), and for each emits a `case Opcodes.<Name>:` arm that
//   (a) validates `payload.Length >= Unsafe.SizeOf<T>()`, (b) reinterprets via
//   MemoryMarshal.AsRef<T>(payload), and (c) calls handler.Handle(in view). The generated partial
//   would replace the hand-written body below verbatim, keeping the same `IPacketHandler` seam.
//   Until that generator lands, this hand-written switch is the source of truth and stays in lock-
//   step with the [PacketOpcode]-tagged structs.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;
using MartialHeroes.Network.Protocol.Packets;

namespace MartialHeroes.Network.Protocol.Routing;

/// <summary>
/// Reflection-free opcode router. Reads the 8-byte frame header, then dispatches the payload to the
/// matching typed handler via a compile-time <c>switch</c>. spec: Docs/RE/opcodes.md.
/// </summary>
public static class PacketRouter
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
    /// Dispatches an already-parsed payload by its packed opcode. spec: Docs/RE/opcodes.md.
    /// </summary>
    public static bool Route(uint packedOpcode, ReadOnlySpan<byte> payload, IPacketHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        // Compile-time switch — no reflection, no dictionary. Each arm corresponds to a
        // [PacketOpcode]-tagged fixed-size struct. spec: Docs/RE/opcodes.md.
        switch (packedOpcode)
        {
            case Opcodes.Opcodes.SmsgCharDespawn:
                handler.Handle(in Reinterpret<SmsgCharDespawn>(payload, SmsgCharDespawn.WireSize));
                return true;

            case Opcodes.Opcodes.SmsgEnterGameAck:
                handler.Handle(in Reinterpret<SmsgEnterGameAck>(payload, SmsgEnterGameAck.WireSize));
                return true;

            case Opcodes.Opcodes.SmsgActorMovementUpdate:
                handler.Handle(in Reinterpret<SmsgActorMovementUpdate>(payload, SmsgActorMovementUpdate.WireSize));
                return true;

            case Opcodes.Opcodes.SmsgCharSpawn:
                handler.Handle(in Reinterpret<SmsgCharSpawn>(payload, SmsgCharSpawn.WireSize));
                return true;

            default:
                handler.OnUnhandled(packedOpcode, payload);
                return false;
        }
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