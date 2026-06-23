namespace MartialHeroes.Client.Application.Diagnostics;

public interface IUnhandledOpcodeSink
{
    void Record(uint packedOpcode, int payloadLength);
}

public sealed class CountingUnhandledOpcodeSink : IUnhandledOpcodeSink
{
    private long _count;

    public long Count => Interlocked.Read(ref _count);

    public void Record(uint packedOpcode, int payloadLength)
    {
        Interlocked.Increment(ref _count);
    }
}