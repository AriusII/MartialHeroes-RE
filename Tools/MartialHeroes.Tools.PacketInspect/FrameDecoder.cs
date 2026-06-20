// FrameDecoder — parses an 8-byte FrameHeader from a captured/pasted frame, identifies its opcode via
// the OpcodeCatalog, validates the declared frame size against the struct's wire size, and dumps the
// struct's primitive fields (offset + little-endian value). Buffer/nested fields are noted, not dumped.
//
// Index/structure only: it prints field offsets and decoded primitive values from the bytes the user
// supplied — it is a decoder for the user's own capture, never a copyrighted-asset exporter.

using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core;

namespace MartialHeroes.Tools.PacketInspect;

internal static class FrameDecoder
{
    public static void Decode(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < FrameHeader.Size)
        {
            Console.Error.WriteLine(
                $"decode: need at least {FrameHeader.Size} bytes for a frame header; got {frame.Length}.");
            return;
        }

        var h = FrameHeader.Read(frame);
        var payload = frame[FrameHeader.Size..];

        Console.WriteLine($"frame: {frame.Length} bytes supplied");
        Console.WriteLine(
            $"  header: size={h.FrameSize}  major={h.Major}  minor={h.Minor}  " +
            $"opcode={h.Major}/{h.Minor} (0x{h.PackedOpcode:X})");
        Console.WriteLine($"  payload: {payload.Length} bytes (declared {h.PayloadLength})");

        if (h.FrameSize != (uint)frame.Length)
            Console.WriteLine(
                $"  ! declared frame size {h.FrameSize} != supplied {frame.Length} " +
                "(truncated capture, coalesced frames, or wrong slice)");

        var entry = OpcodeCatalog.Find(h.PackedOpcode);
        if (entry is null)
        {
            Console.WriteLine(
                $"  opcode {h.Major}/{h.Minor}: UNKNOWN — no [PacketOpcode]-tagged struct " +
                "(untyped / keepalive / not yet recovered). Use `opcodes` to list known ones.");
            return;
        }

        Console.WriteLine(
            $"  struct: {entry.Name}  [{entry.Direction}]  {entry.SizeKind}={entry.WireSize?.ToString() ?? "variable"}");

        if (entry.WireSize is { } need && payload.Length < need)
        {
            Console.WriteLine($"  ! payload {payload.Length} < {entry.SizeKind} {need}: too short to decode fields.");
            return;
        }

        DumpFields(entry.StructType, payload);
    }

    // Dumps each public instance field at its Marshal offset; primitives are decoded LE, other field
    // types are noted with their size. Pack=1 sequential layout makes the offsets the wire offsets.
    private static void DumpFields(Type type, ReadOnlySpan<byte> payload)
    {
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        if (fields.Length == 0)
        {
            Console.WriteLine("  (no readable instance fields — likely a property-backed or opaque struct)");
            return;
        }

        Console.WriteLine("  fields:");
        foreach (var f in fields)
        {
            int off;
            try
            {
                off = (int)Marshal.OffsetOf(type, f.Name);
            }
            catch
            {
                Console.WriteLine($"    +?    {f.Name}: <offset unavailable>");
                continue;
            }

            var ft = f.FieldType;
            var readType = ft.IsEnum ? Enum.GetUnderlyingType(ft) : ft;
            var size = PrimitiveSize(readType);

            if (size == 0)
            {
                // Non-primitive: an [InlineArray] buffer or nested struct. Report its marshalled size.
                var bufSize = TryMarshalSize(ft);
                Console.WriteLine(
                    $"    +0x{off:X2}  {f.Name}: {ft.Name} [{(bufSize > 0 ? bufSize + " bytes" : "opaque")}]");
                continue;
            }

            if (off + size > payload.Length)
            {
                Console.WriteLine($"    +0x{off:X2}  {f.Name}: <past end of payload>");
                continue;
            }

            var value = ReadPrimitive(readType, payload, off);
            var shown = ft.IsEnum ? $"{Enum.ToObject(ft, value)} ({value})" : value.ToString() ?? "";
            var hex = readType == typeof(byte) ? $"  0x{(byte)value:X2}" : "";
            Console.WriteLine($"    +0x{off:X2}  {f.Name}: {ft.Name} = {shown}{hex}");
        }
    }

    private static int PrimitiveSize(Type t)
    {
        return t == typeof(byte) || t == typeof(sbyte) || t == typeof(bool) ? 1
            : t == typeof(ushort) || t == typeof(short) || t == typeof(char) ? 2
            : t == typeof(uint) || t == typeof(int) || t == typeof(float) ? 4
            : t == typeof(ulong) || t == typeof(long) || t == typeof(double) ? 8
            : 0;
    }

    private static object ReadPrimitive(Type t, ReadOnlySpan<byte> p, int off)
    {
        if (t == typeof(byte)) return p[off];
        if (t == typeof(sbyte)) return (sbyte)p[off];
        if (t == typeof(bool)) return p[off] != 0;
        if (t == typeof(ushort)) return BinaryPrimitives.ReadUInt16LittleEndian(p[off..]);
        if (t == typeof(short)) return BinaryPrimitives.ReadInt16LittleEndian(p[off..]);
        if (t == typeof(char)) return (char)BinaryPrimitives.ReadUInt16LittleEndian(p[off..]);
        if (t == typeof(uint)) return BinaryPrimitives.ReadUInt32LittleEndian(p[off..]);
        if (t == typeof(int)) return BinaryPrimitives.ReadInt32LittleEndian(p[off..]);
        if (t == typeof(float)) return BinaryPrimitives.ReadSingleLittleEndian(p[off..]);
        if (t == typeof(ulong)) return BinaryPrimitives.ReadUInt64LittleEndian(p[off..]);
        if (t == typeof(long)) return BinaryPrimitives.ReadInt64LittleEndian(p[off..]);
        if (t == typeof(double)) return BinaryPrimitives.ReadDoubleLittleEndian(p[off..]);
        return 0;
    }

    private static int TryMarshalSize(Type t)
    {
        try
        {
            return Marshal.SizeOf(t);
        }
        catch
        {
            return 0;
        }
    }
}