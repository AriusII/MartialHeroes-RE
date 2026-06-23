namespace MartialHeroes.Network.Transport.Pipelines;

internal static class FramingConstants
{
    internal const int HeaderSize = 8;

    internal const int MajorOpcodeOffset = 4;

    internal const int MaxFrameSize = HeaderSize + 0x2DA0;

    internal const long PipeResumeThreshold = 64 * 1024;

    internal const long PipePauseThreshold = 128 * 1024;

    internal const int MinReceiveBufferSize = 4096;
}