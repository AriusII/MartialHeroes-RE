using System.Buffers;

namespace MartialHeroes.Network.Transport.Pipelines;

/// <summary>
/// Decompresses a raw-block LZ4 inbound payload and returns the decompressed bytes via a
/// pooled <see cref="IMemoryOwner{T}"/> that the caller disposes.
/// </summary>
/// <remarks>
/// Matches the shape of <c>PayloadCompression.DecompressPayload</c> in
/// <c>MartialHeroes.Network.Crypto</c>.
/// <para>
/// Defined as a namespace-level public delegate so that <see cref="TcpTransport"/> (a public
/// class) can expose a parameter of this type without incurring a project reference to
/// <c>Network.Crypto</c> from <c>Transport.Pipelines</c>.
/// </para>
/// spec: Docs/RE/specs/crypto.md §3.2, §5 — inbound is compressed-only, no inverse cipher.
/// </remarks>
/// <param name="source">The compressed payload bytes (raw-block LZ4, no frame magic).</param>
/// <param name="decompressedLength">
/// Set to the exact byte count written into the returned buffer's
/// <see cref="IMemoryOwner{T}.Memory"/>.
/// </param>
/// <returns>
/// An <see cref="IMemoryOwner{T}"/> whose <see cref="IMemoryOwner{T}.Memory"/> contains the
/// decompressed bytes. The caller is responsible for disposing it.
/// </returns>
public delegate IMemoryOwner<byte> InboundDecompressDelegate(
    ReadOnlySpan<byte> source, out int decompressedLength);