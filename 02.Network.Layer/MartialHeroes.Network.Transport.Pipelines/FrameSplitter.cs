using System.Buffers;
using System.IO.Pipelines;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;

namespace MartialHeroes.Network.Transport.Pipelines;

internal static class FrameSplitter
{
    internal static async ValueTask<FrameLoopResult> RunAsync(
        PipeReader reader,
        SessionId sessionId,
        IFrameSink sink,
        CancellationToken cancellationToken,
        InboundDecompressDelegate? decompress = null)
    {
        while (true)
        {
            ReadResult result;
            try
            {
                result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return FrameLoopResult.Completed;
            }

            var buffer = result.Buffer;
            var consumed = buffer.Start;
            var examined = buffer.End;
            var framingError = false;

            while (TryParseFrameLength(buffer, out var frameLength))
            {
                if (frameLength < FramingConstants.HeaderSize
                    || frameLength > FramingConstants.MaxFrameSize)
                {
                    framingError = true;
                    break;
                }

                if (buffer.Length < frameLength)
                    break;

                var frameSeq = buffer.Slice(buffer.Start, frameLength);

                var packedOpcode = ReadPackedOpcode(frameSeq);

                DispatchFrame(frameSeq, sessionId, packedOpcode, sink, decompress);

                consumed = frameSeq.End;
                examined = consumed;
                buffer = buffer.Slice(consumed);
            }

            reader.AdvanceTo(consumed, examined);

            if (framingError) return FrameLoopResult.FramingError;

            if (result.IsCompleted || result.IsCanceled) return FrameLoopResult.Completed;
        }
    }

    private static bool TryParseFrameLength(ReadOnlySequence<byte> buffer, out int frameLength)
    {
        if (buffer.Length < FramingConstants.HeaderSize)
        {
            frameLength = 0;
            return false;
        }

        var seqReader = new SequenceReader<byte>(buffer);
        if (!seqReader.TryReadLittleEndian(out int rawSize))
        {
            frameLength = 0;
            return false;
        }

        var sizeU32 = (uint)rawSize;
        frameLength = sizeU32 > int.MaxValue ? int.MaxValue : (int)sizeU32;
        return true;
    }

    private static uint ReadPackedOpcode(ReadOnlySequence<byte> frameSeq)
    {
        var seqReader = new SequenceReader<byte>(frameSeq);
        seqReader.Advance(FramingConstants.MajorOpcodeOffset);

        seqReader.TryReadLittleEndian(out short rawMajor);
        seqReader.TryReadLittleEndian(out short rawMinor);

        var major = (ushort)rawMajor;
        var minor = (ushort)rawMinor;
        return ((uint)major << 16) | minor;
    }

    private static void DispatchFrame(
        ReadOnlySequence<byte> frameSeq,
        SessionId sessionId,
        uint packedOpcode,
        IFrameSink sink,
        InboundDecompressDelegate? decompress)
    {
        var payloadSeq = frameSeq.Slice(FramingConstants.HeaderSize);

        if (payloadSeq.IsEmpty)
        {
            sink.OnFrame(sessionId, packedOpcode, ReadOnlySpan<byte>.Empty);
            return;
        }

        if (decompress is not null)
        {
            var rawPayload = GetContiguousPayload(payloadSeq, out var rawRented);
            try
            {
                using var decompressedOwner = decompress(rawPayload, out var decompressedLength);
                sink.OnFrame(sessionId, packedOpcode, decompressedOwner.Memory.Span[..decompressedLength]);
            }
            finally
            {
                if (rawRented is not null) ArrayPool<byte>.Shared.Return(rawRented);
            }

            return;
        }

        if (payloadSeq.IsSingleSegment)
        {
            sink.OnFrame(sessionId, packedOpcode, payloadSeq.FirstSpan);
            return;
        }

        var rented = ArrayPool<byte>.Shared.Rent((int)payloadSeq.Length);
        try
        {
            payloadSeq.CopyTo(rented);
            sink.OnFrame(sessionId, packedOpcode, rented.AsSpan(0, (int)payloadSeq.Length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static ReadOnlySpan<byte> GetContiguousPayload(
        ReadOnlySequence<byte> payloadSeq,
        out byte[]? rented)
    {
        if (payloadSeq.IsSingleSegment)
        {
            rented = null;
            return payloadSeq.FirstSpan;
        }

        var length = (int)payloadSeq.Length;
        rented = ArrayPool<byte>.Shared.Rent(length);
        payloadSeq.CopyTo(rented);
        return rented.AsSpan(0, length);
    }

    internal enum FrameLoopResult
    {
        Completed,

        FramingError
    }
}