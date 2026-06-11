using System.Buffers;
using System.Buffers.Binary;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;

namespace MartialHeroes.Network.Transport.Pipelines;

/// <summary>
/// Pure, socket-free framing logic.  Consumes a <see cref="System.IO.Pipelines.PipeReader"/>
/// and dispatches complete, length-prefixed frames to an <see cref="IFrameSink"/>.
/// </summary>
/// <remarks>
/// Framing spec: Docs/RE/opcodes.md — Wire frame header.
/// <list type="bullet">
///   <item>Header is 8 bytes, little-endian.</item>
///   <item>Bytes +0..+1: u16 <c>size</c> = total frame length including the 8-byte header.</item>
///   <item>Bytes +2..+3: unused (upper half of a physical u32; always zero on the wire).</item>
///   <item>Bytes +4..+5: u16 <c>major</c> opcode.</item>
///   <item>Bytes +6..+7: u16 <c>minor</c> opcode.</item>
///   <item>Bytes +8..: payload, (size - 8) bytes.</item>
/// </list>
/// A frame whose <c>size</c> field is less than <see cref="FramingConstants.HeaderSize"/> or
/// greater than <see cref="FramingConstants.MaxFrameSize"/> is treated as a framing violation
/// and the loop returns <see cref="FrameLoopResult.FramingError"/>.
/// </remarks>
internal static class FrameSplitter
{
    /// <summary>
    /// Outcome of a single <see cref="RunAsync"/> invocation.
    /// </summary>
    internal enum FrameLoopResult
    {
        /// <summary>The pipe was completed cleanly (remote EOF or local cancel).</summary>
        Completed,

        /// <summary>A malformed or oversized frame was detected.</summary>
        FramingError,
    }

    /// <summary>
    /// Reads frames from <paramref name="reader"/> until the pipe completes, is cancelled, or a
    /// framing error occurs.  For each complete frame, calls
    /// <see cref="IFrameSink.OnFrame"/> with the raw complete-frame memory (header + payload)
    /// as a contiguous <see cref="ReadOnlySpan{T}"/> rented from a pooled bounce buffer when the
    /// sequence is non-contiguous, or a direct span when it is contiguous.
    /// </summary>
    /// <remarks>
    /// The <paramref name="sink"/> receives the full frame bytes starting at offset 0 (i.e. the
    /// header is included). The packed opcode <c>(major &lt;&lt; 16 | minor)</c> is pre-computed here
    /// so the sink does not re-parse the header.  The span passed to <see cref="IFrameSink.OnFrame"/>
    /// covers <b>payload only</b> (header stripped), consistent with the <see cref="IFrameSink"/>
    /// contract.
    /// </remarks>
    internal static async ValueTask<FrameLoopResult> RunAsync(
        System.IO.Pipelines.PipeReader reader,
        SessionId sessionId,
        IFrameSink sink,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            System.IO.Pipelines.ReadResult result;
            try
            {
                result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is a clean exit.
                return FrameLoopResult.Completed;
            }

            ReadOnlySequence<byte> buffer = result.Buffer;
            SequencePosition consumed = buffer.Start;
            SequencePosition examined = buffer.End;
            bool framingError = false;

            // Process as many complete frames as are currently buffered.
            while (TryParseFrameLength(buffer, out int frameLength))
            {
                if (frameLength < FramingConstants.HeaderSize
                    || frameLength > FramingConstants.MaxFrameSize)
                {
                    // spec: Docs/RE/opcodes.md — size field is u16 and must include the
                    // 8-byte header; any value outside [HeaderSize, MaxFrameSize] is illegal.
                    framingError = true;
                    break;
                }

                if (buffer.Length < frameLength)
                {
                    // We know how large the frame will be but don't have all the bytes yet.
                    // Leave `examined` at buffer.End so the next ReadAsync waits for more data;
                    // do NOT advance `consumed` past the incomplete frame.
                    break;
                }

                // We have a complete frame in the buffer.
                ReadOnlySequence<byte> frameSeq = buffer.Slice(buffer.Start, frameLength);

                // Parse opcode from header.
                // spec: Docs/RE/opcodes.md — major at +4 (u16 LE), minor at +6 (u16 LE).
                uint packedOpcode = ReadPackedOpcode(frameSeq);

                // Dispatch payload (header stripped) to sink.
                // IFrameSink contract: span is only valid during the call.
                DispatchFrame(frameSeq, sessionId, packedOpcode, sink);

                // Advance past the frame we just consumed.
                consumed = frameSeq.End;
                examined = consumed;
                buffer = buffer.Slice(consumed);
            }

            reader.AdvanceTo(consumed, examined);

            if (framingError)
            {
                return FrameLoopResult.FramingError;
            }

            if (result.IsCompleted || result.IsCanceled)
            {
                return FrameLoopResult.Completed;
            }
        }
    }

    /// <summary>
    /// Attempts to read the u16 total-frame-size field from the front of <paramref name="buffer"/>.
    /// Returns <see langword="false"/> (and leaves <paramref name="frameLength"/> at 0) when fewer
    /// than <see cref="FramingConstants.HeaderSize"/> bytes are available — the prefix is split
    /// across reads.
    /// spec: Docs/RE/opcodes.md — "Offset +0, Size u16 (LE), total frame size."
    /// </summary>
    private static bool TryParseFrameLength(ReadOnlySequence<byte> buffer, out int frameLength)
    {
        if (buffer.Length < FramingConstants.HeaderSize)
        {
            frameLength = 0;
            return false;
        }

        // Use SequenceReader for zero-alloc prefix parsing across segment boundaries.
        var seqReader = new SequenceReader<byte>(buffer);
        // spec: Docs/RE/opcodes.md — SizeFieldOffset = 0, u16, little-endian.
        if (!seqReader.TryReadLittleEndian(out short rawSize))
        {
            frameLength = 0;
            return false;
        }

        // Cast via ushort to avoid sign extension; the wire value is an unsigned 16-bit quantity.
        frameLength = (ushort)rawSize;
        return true;
    }

    /// <summary>
    /// Reads major/minor opcodes from a <see cref="ReadOnlySequence{T}"/> that is guaranteed to
    /// contain at least <see cref="FramingConstants.HeaderSize"/> bytes.
    /// spec: Docs/RE/opcodes.md — major at +4 (u16 LE), minor at +6 (u16 LE).
    /// Packed opcode = (major &lt;&lt; 16) | minor.
    /// </summary>
    private static uint ReadPackedOpcode(ReadOnlySequence<byte> frameSeq)
    {
        var seqReader = new SequenceReader<byte>(frameSeq);
        // Skip to +4 (past size u16 at +0 and unused u16 at +2).
        seqReader.Advance(FramingConstants.MajorOpcodeOffset); // advance 4 bytes

        seqReader.TryReadLittleEndian(out short rawMajor);
        seqReader.TryReadLittleEndian(out short rawMinor);

        ushort major = (ushort)rawMajor;
        ushort minor = (ushort)rawMinor;
        return ((uint)major << 16) | minor;
    }

    /// <summary>
    /// Dispatches the payload portion of <paramref name="frameSeq"/> to <paramref name="sink"/>.
    /// Avoids a heap allocation when the sequence is already contiguous; rents a pooled buffer
    /// only when the frame straddles two or more pipe segments.
    /// </summary>
    private static void DispatchFrame(
        ReadOnlySequence<byte> frameSeq,
        SessionId sessionId,
        uint packedOpcode,
        IFrameSink sink)
    {
        // Payload starts after the 8-byte header.
        // spec: Docs/RE/opcodes.md — "Bytes +8..: payload".
        ReadOnlySequence<byte> payloadSeq = frameSeq.Slice(FramingConstants.HeaderSize);

        if (payloadSeq.IsSingleSegment)
        {
            // Zero-copy fast path: the payload is already contiguous in the pipe buffer.
            sink.OnFrame(sessionId, packedOpcode, payloadSeq.FirstSpan);
            return;
        }

        // Slow path: the payload spans multiple pipe segments — copy into a pooled buffer,
        // call the sink, then return the buffer immediately (span is only valid during the call).
        int payloadLength = (int)payloadSeq.Length;
        byte[] rented = ArrayPool<byte>.Shared.Rent(payloadLength);
        try
        {
            payloadSeq.CopyTo(rented);
            sink.OnFrame(sessionId, packedOpcode, rented.AsSpan(0, payloadLength));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
