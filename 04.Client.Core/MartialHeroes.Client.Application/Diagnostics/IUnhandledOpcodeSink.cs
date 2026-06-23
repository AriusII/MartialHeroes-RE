namespace MartialHeroes.Client.Application.Diagnostics;

public interface IUnhandledOpcodeSink
{
    void Record();
}

public sealed class CountingUnhandledOpcodeSink : IUnhandledOpcodeSink
{
    private long _count;

    public void Record()
    {
        Interlocked.Increment(ref _count);
    }
}