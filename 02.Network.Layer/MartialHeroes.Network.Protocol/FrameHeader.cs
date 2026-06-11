// spec: Docs/RE/opcodes.md — "Wire frame header".
// CAPTURE-UNVERIFIED routing layouts, but the 8-byte header framing is a hard static fact.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol;

/// <summary>
/// The 8-byte, little-endian frame header that prefixes every frame on the primary game
/// connection. The <c>(major:minor)</c> pair IS the opcode — there is no separate opcode field.
/// </summary>
/// <remarks>
/// spec: Docs/RE/opcodes.md ("Wire frame header"). Layout (little-endian):
/// <list type="bullet">
///   <item>+0 u16 <c>size</c> — total frame size incl. this header. Physically a u32, but the I/O
///   loop frames on the low 16 bits only; bytes @+2 are unused/zero for framing.</item>
///   <item>+4 u16 <c>major</c> — opcode high part / message family.</item>
///   <item>+6 u16 <c>minor</c> — opcode low part / message id.</item>
/// </list>
/// Payload begins at frame +8. All packet-spec field offsets are payload-relative.
/// This struct mirrors only the low-16-bit framing view of <c>size</c>; bytes @+2..+3 are not
/// modelled because the transport ignores them. Read from a span via <see cref="Read"/>.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct FrameHeader
{
    /// <summary>The header size in bytes; payload starts here. spec: Docs/RE/opcodes.md (+8 payload).</summary>
    public const int Size = 8;

    /// <summary>Total frame size in bytes incl. this 8-byte header (low-16-bit framing view). spec: opcodes.md +0.</summary>
    public ushort FrameSize { get; }

    /// <summary>Reserved high 16 bits of the physical u32 size field; unused for framing. spec: opcodes.md +2.</summary>
    public ushort SizeHigh { get; }

    /// <summary>Opcode high part / message family. spec: opcodes.md +4.</summary>
    public ushort Major { get; }

    /// <summary>Opcode low part / message id. spec: opcodes.md +6.</summary>
    public ushort Minor { get; }

    public FrameHeader(ushort frameSize, ushort sizeHigh, ushort major, ushort minor)
    {
        FrameSize = frameSize;
        SizeHigh = sizeHigh;
        Major = major;
        Minor = minor;
    }

    /// <summary>The <c>(major:minor)</c> opcode this frame carries. spec: opcodes.md.</summary>
    public PacketOpcode Opcode => new(Major, Minor);

    /// <summary>Packed <c>(major &lt;&lt; 16) | minor</c> opcode, matching <see cref="Opcodes"/>. spec: opcodes.md.</summary>
    public uint PackedOpcode => ((uint)Major << 16) | Minor;

    /// <summary>The payload length implied by <see cref="FrameSize"/> (frame size minus the 8-byte header).</summary>
    public int PayloadLength => FrameSize - Size;

    /// <summary>
    /// Reads an 8-byte frame header from the start of <paramref name="frame"/> with no allocation.
    /// All fields are little-endian per spec. spec: Docs/RE/opcodes.md.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="frame"/> is shorter than 8 bytes.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FrameHeader Read(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < Size)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frame), frame.Length, $"A frame header requires at least {Size} bytes.");
        }

        // spec: Docs/RE/opcodes.md — little-endian; size@+0 (u16 framing view), high@+2,
        // major@+4, minor@+6.
        ushort frameSize = BinaryPrimitives.ReadUInt16LittleEndian(frame);
        ushort sizeHigh = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(2, 2));
        ushort major = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(4, 2));
        ushort minor = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(6, 2));
        return new FrameHeader(frameSize, sizeHigh, major, minor);
    }

    /// <summary>
    /// Returns the payload of <paramref name="frame"/> (everything after the 8-byte header) as a
    /// zero-copy slice. spec: Docs/RE/opcodes.md (payload begins at frame +8).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<byte> Payload(ReadOnlySpan<byte> frame) => frame.Slice(Size);
}