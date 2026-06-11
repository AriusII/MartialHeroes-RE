using System.Buffers.Binary;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Network.Transport.Pipelines;

namespace MartialHeroes.Network.Transport.Pipelines.Tests;

/// <summary>
/// Unit tests for <see cref="FrameSplitter"/> driven by an in-memory <see cref="Pipe"/>.
/// No real socket is required.
///
/// Frame wire format (spec: Docs/RE/opcodes.md — Wire frame header):
///   [+0 u16 LE size (total, incl. header)]
///   [+2 u16 LE unused/zero]
///   [+4 u16 LE major opcode]
///   [+6 u16 LE minor opcode]
///   [+8 .. payload]
/// </summary>
public sealed class FrameSplitterTests
{
    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    /// <summary>
    /// Builds a well-formed frame byte array.
    /// spec: Docs/RE/opcodes.md — total size includes the 8-byte header.
    /// </summary>
    private static byte[] BuildFrame(ushort major, ushort minor, ReadOnlySpan<byte> payload)
    {
        int totalSize = FramingConstants.HeaderSize + payload.Length;
        byte[] frame = new byte[totalSize];

        // +0: u16 LE total size
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(0), (ushort)totalSize);
        // +2: unused, stays zero
        // +4: u16 LE major
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), major);
        // +6: u16 LE minor
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6), minor);
        // +8: payload
        payload.CopyTo(frame.AsSpan(8));
        return frame;
    }

    /// <summary>
    /// Writes each chunk to <paramref name="writer"/> in order, then completes the writer
    /// so the framing loop sees a clean EOF.
    /// </summary>
    private static async Task FeedAndCompleteAsync(PipeWriter writer, params ReadOnlyMemory<byte>[] chunks)
    {
        foreach (ReadOnlyMemory<byte> chunk in chunks)
        {
            await writer.WriteAsync(chunk);
        }

        await writer.CompleteAsync();
    }

    /// <summary>
    /// Collects every (packedOpcode, payload-copy) pair delivered by <see cref="FrameSplitter"/>.
    /// </summary>
    private sealed class CapturingFrameSink : IFrameSink
    {
        private readonly List<(uint PackedOpcode, byte[] Payload)> _frames = new();

        public IReadOnlyList<(uint PackedOpcode, byte[] Payload)> Frames => _frames;

        public void OnFrame(SessionId sessionId, uint packedOpcode, ReadOnlySpan<byte> payload)
        {
            // Copy — the span is stack-bound and must not escape the call.
            _frames.Add((packedOpcode, payload.ToArray()));
        }
    }

    /// <summary>
    /// Feeds chunks via <paramref name="writer"/>, concurrently runs the framing loop on the
    /// paired <paramref name="reader"/>, and asserts a clean completion. Returns captured frames.
    /// </summary>
    private static async Task<IReadOnlyList<(uint PackedOpcode, byte[] Payload)>>
        FeedAndFrameAsync(
            PipeWriter writer,
            PipeReader reader,
            params ReadOnlyMemory<byte>[] chunks)
    {
        var sink = new CapturingFrameSink();
        var sessionId = new SessionId(1);

        // Run feeder and splitter concurrently; feeder completes the writer which
        // signals EOF to the splitter.
        await Task.WhenAll(
            FeedAndCompleteAsync(writer, chunks),
            FrameSplitter.RunAsync(reader, sessionId, sink, CancellationToken.None).AsTask());

        return sink.Frames;
    }

    // -------------------------------------------------------------------
    // (a) One frame split across two reads
    // -------------------------------------------------------------------

    /// <summary>
    /// The first read delivers only the header + part of the payload; the second read delivers
    /// the remainder. The splitter must buffer and emit exactly one frame.
    /// </summary>
    [Fact]
    public async Task OneFrame_SplitAcrossTwoReads_ProducesOneFrame()
    {
        byte[] payload = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];
        byte[] frame = BuildFrame(major: 5, minor: 13, payload);

        // Split the frame in the middle of the payload (first 12 bytes, then the rest).
        int splitAt = FramingConstants.HeaderSize + 4;
        byte[] part1 = frame[..splitAt];
        byte[] part2 = frame[splitAt..];

        var pipe = new Pipe();
        IReadOnlyList<(uint PackedOpcode, byte[] Payload)> frames =
            await FeedAndFrameAsync(pipe.Writer, pipe.Reader, part1, part2);

        Assert.Single(frames);
        Assert.Equal(0x5000dU, frames[0].PackedOpcode); // (5 << 16) | 13
        Assert.Equal(payload, frames[0].Payload);
    }

    // -------------------------------------------------------------------
    // (b) Two frames coalesced in one read
    // -------------------------------------------------------------------

    /// <summary>
    /// A single pipe write contains two complete frames. The splitter must emit two frames, in
    /// order, with the correct opcodes and payloads.
    /// </summary>
    [Fact]
    public async Task TwoFrames_InOneRead_ProducesTwoFrames()
    {
        byte[] payload1 = [0xAA, 0xBB];
        byte[] payload2 = [0x11, 0x22, 0x33];

        byte[] frame1 = BuildFrame(major: 3, minor: 1, payload1);
        byte[] frame2 = BuildFrame(major: 4, minor: 2, payload2);
        byte[] combined = [..frame1, ..frame2];

        var pipe = new Pipe();
        IReadOnlyList<(uint PackedOpcode, byte[] Payload)> frames =
            await FeedAndFrameAsync(pipe.Writer, pipe.Reader, combined);

        Assert.Equal(2, frames.Count);

        // Frame 1: major=3, minor=1 → packed 0x30001
        Assert.Equal(0x30001U, frames[0].PackedOpcode);
        Assert.Equal(payload1, frames[0].Payload);

        // Frame 2: major=4, minor=2 → packed 0x40002
        Assert.Equal(0x40002U, frames[1].PackedOpcode);
        Assert.Equal(payload2, frames[1].Payload);
    }

    // -------------------------------------------------------------------
    // (c) Length prefix (u16 size field) split across two reads
    // -------------------------------------------------------------------

    /// <summary>
    /// The first read delivers only 1 byte — the very first byte of the 2-byte size field.
    /// The splitter must retain that byte and wait for the rest before emitting any frame.
    /// </summary>
    [Fact]
    public async Task Frame_LengthPrefixSplitAcrossReads_ProducesOneFrame()
    {
        byte[] payload = [0xDE, 0xAD, 0xBE, 0xEF];
        byte[] frame = BuildFrame(major: 5, minor: 3, payload);

        byte[] part1 = frame[..1]; // single byte: low half of the u16 size
        byte[] part2 = frame[1..]; // the rest

        var pipe = new Pipe();
        IReadOnlyList<(uint PackedOpcode, byte[] Payload)> frames =
            await FeedAndFrameAsync(pipe.Writer, pipe.Reader, part1, part2);

        Assert.Single(frames);
        Assert.Equal(0x50003U, frames[0].PackedOpcode); // (5 << 16) | 3
        Assert.Equal(payload, frames[0].Payload);
    }

    // -------------------------------------------------------------------
    // (d) Trailing partial bytes are retained; only complete frames emitted
    // -------------------------------------------------------------------

    /// <summary>
    /// Two complete frames arrive followed by the header-only prefix of a third frame (no payload).
    /// The splitter must emit exactly two frames; the incomplete third is held until EOF and then
    /// discarded (never emitted).
    /// </summary>
    [Fact]
    public async Task TrailingPartialFrame_IsRetained_OnlyCompletedFramesEmitted()
    {
        byte[] payload1 = [0x01];
        byte[] payload2 = [0x02, 0x03];
        byte[] payload3 = [0x10, 0x20, 0x30];

        byte[] frame1 = BuildFrame(major: 1, minor: 0, payload1);
        byte[] frame2 = BuildFrame(major: 2, minor: 0, payload2);
        byte[] frame3 = BuildFrame(major: 3, minor: 0, payload3);

        // Only send the 8-byte header of frame3 — payload is absent.
        byte[] partial3 = frame3[..FramingConstants.HeaderSize];
        byte[] combined = [..frame1, ..frame2, ..partial3];

        var pipe = new Pipe();
        IReadOnlyList<(uint PackedOpcode, byte[] Payload)> frames =
            await FeedAndFrameAsync(pipe.Writer, pipe.Reader, combined);

        Assert.Equal(2, frames.Count);
        Assert.Equal(0x10000U, frames[0].PackedOpcode); // major=1, minor=0
        Assert.Equal(payload1, frames[0].Payload);
        Assert.Equal(0x20000U, frames[1].PackedOpcode); // major=2, minor=0
        Assert.Equal(payload2, frames[1].Payload);
    }

    // -------------------------------------------------------------------
    // (e) Malformed size field (below HeaderSize) triggers FramingError
    // -------------------------------------------------------------------

    /// <summary>
    /// A frame whose declared size is smaller than the 8-byte header minimum must cause
    /// <see cref="FrameSplitter.RunAsync"/> to return <see cref="FrameSplitter.FrameLoopResult.FramingError"/>
    /// rather than dispatching a bogus frame.
    /// spec: Docs/RE/opcodes.md — size field is u16; any value below HeaderSize is illegal.
    /// </summary>
    [Fact]
    public async Task MalformedFrameSize_BelowHeaderSize_TriggersFramingError()
    {
        // size = 3 < HeaderSize(8) — invalid frame.
        byte[] malformed = new byte[FramingConstants.HeaderSize];
        BinaryPrimitives.WriteUInt16LittleEndian(malformed.AsSpan(0), 3); // size field
        BinaryPrimitives.WriteUInt16LittleEndian(malformed.AsSpan(4), 1); // major = 1
        BinaryPrimitives.WriteUInt16LittleEndian(malformed.AsSpan(6), 0); // minor = 0

        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(malformed);
        await pipe.Writer.CompleteAsync();

        var sink = new CapturingFrameSink();
        FrameSplitter.FrameLoopResult result =
            await FrameSplitter.RunAsync(pipe.Reader, new SessionId(1), sink, CancellationToken.None);

        Assert.Equal(FrameSplitter.FrameLoopResult.FramingError, result);
        Assert.Empty(sink.Frames);
    }

    // -------------------------------------------------------------------
    // (f) Header-only frame (zero-length payload) is emitted correctly
    // -------------------------------------------------------------------

    /// <summary>
    /// A frame whose total size exactly equals the 8-byte header (zero payload bytes) must be
    /// emitted with an empty payload, not skipped.
    /// </summary>
    [Fact]
    public async Task HeaderOnlyFrame_ZeroPayload_IsEmitted()
    {
        byte[] frame = BuildFrame(major: 0, minor: 0, ReadOnlySpan<byte>.Empty);
        Assert.Equal(FramingConstants.HeaderSize, frame.Length);

        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(frame);
        await pipe.Writer.CompleteAsync();

        var sink = new CapturingFrameSink();
        FrameSplitter.FrameLoopResult result =
            await FrameSplitter.RunAsync(pipe.Reader, new SessionId(1), sink, CancellationToken.None);

        Assert.Equal(FrameSplitter.FrameLoopResult.Completed, result);
        Assert.Single(sink.Frames);
        Assert.Equal(0U, sink.Frames[0].PackedOpcode);
        Assert.Empty(sink.Frames[0].Payload);
    }
}