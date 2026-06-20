namespace MartialHeroes.Client.Application.Diagnostics;

/// <summary>
///     Injected seam for recording opcodes that arrive without a specced handler. Keeps the handler
///     free of any concrete logging/infrastructure dependency (which would be an illegal upward/outward
///     reference); the composition root supplies an implementation (e.g. backed by
///     <c>Shared.Diagnostics</c> source-generated logging).
/// </summary>
public interface IUnhandledOpcodeSink
{
    /// <summary>
    ///     Records one unhandled opcode. Must not throw and must not block the receive path.
    /// </summary>
    /// <param name="packedOpcode">The packed <c>(major &lt;&lt; 16) | minor</c> opcode.</param>
    /// <param name="payloadLength">The raw payload length in bytes (the payload itself is not retained).</param>
    void Record(uint packedOpcode, int payloadLength);
}

/// <summary>A no-op sink that simply counts unhandled opcodes. Useful as a default and in tests.</summary>
public sealed class CountingUnhandledOpcodeSink : IUnhandledOpcodeSink
{
    private long _count;

    /// <summary>Total number of unhandled opcodes recorded so far.</summary>
    public long Count => Interlocked.Read(ref _count);

    /// <inheritdoc />
    public void Record(uint packedOpcode, int payloadLength)
    {
        Interlocked.Increment(ref _count);
    }
}