using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Stub parser for <c>.mud</c> ambient/audio tile blobs.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/terrain.md §6. Ambient/audio tile blob — .mud
/// <para>
/// The .mud file is a fixed-size opaque binary blob read in a single operation with no
/// header parsing. Internal structure is UNVERIFIED.
/// </para>
/// <para>
/// Fixed size: 32 768 bytes (0x8000). CONFIRMED (fixed read size).
/// spec: Docs/RE/formats/terrain.md §6 — "Total file size: exactly 32 768 bytes": CONFIRMED.
/// </para>
/// <para>
/// Internal layout hypothesis: a 64×64 grid of 8-byte records (64×64×8 = 32768).
/// This hypothesis is UNVERIFIED — not derived from any observed parse loop.
/// spec: Docs/RE/formats/terrain.md §6 — "hypothesis: 64×64 grid of 8-byte records — UNVERIFIED".
/// </para>
/// <para>
/// This stub validates the fixed size and wraps the raw bytes.
/// No internal fields are decoded because none are specified.
/// </para>
/// <para>
/// ZERO rendering/engine dependencies.
/// </para>
/// </remarks>
public static class MudBlobParser
{
    /// <summary>
    /// Parses (validates size of) a <c>.mud</c> file and wraps its bytes in a <see cref="MudBlob"/>.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Decoded (opaque) mud blob.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown if the buffer is not exactly 32 768 bytes.
    /// spec: Docs/RE/formats/terrain.md §6 — "Total file size: exactly 32 768 bytes": CONFIRMED.
    /// </exception>
    public static MudBlob Parse(ReadOnlyMemory<byte> data)
    {
        // Validate fixed size.
        // spec: Docs/RE/formats/terrain.md §6 — "32 768 bytes (0x8000) — CONFIRMED (fixed read size)".
        if (data.Length != MudBlob.FixedSize)
            throw new InvalidDataException(
                $".mud parse error: expected exactly {MudBlob.FixedSize} bytes, " +
                $"got {data.Length} bytes. " +
                $"spec: Docs/RE/formats/terrain.md §6.");

        // Internal structure is UNVERIFIED — expose raw bytes without further parsing.
        // spec: Docs/RE/formats/terrain.md §6 — "internal structure UNVERIFIED".
        return new MudBlob { RawData = data };
    }
}