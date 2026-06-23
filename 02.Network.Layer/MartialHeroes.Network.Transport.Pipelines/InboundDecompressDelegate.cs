using System.Buffers;

namespace MartialHeroes.Network.Transport.Pipelines;

public delegate IMemoryOwner<byte> InboundDecompressDelegate(
    ReadOnlySpan<byte> source, out int decompressedLength);