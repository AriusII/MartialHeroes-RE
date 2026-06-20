using MartialHeroes.Client.Application.Contracts.Events;

namespace MartialHeroes.Client.Application.World;

/// <summary>
///     Published when a terrain sector becomes resident (its bytes were loaded through the
///     <see cref="ITerrainSectorSource" /> port). Immutable snapshot the Godot layer consumes to build
///     the cell mesh. spec: Docs/RE/formats/terrain.md §9 (cell streaming policy).
/// </summary>
/// <param name="MapX">Biased sector X. spec: terrain.md §Overview.</param>
/// <param name="MapZ">Biased sector Z. spec: terrain.md §Overview.</param>
/// <param name="Payload">
///     The raw cell bytes from the port (neutral handle; Application does not parse it). Empty when the
///     area manifest had no such cell. spec: terrain.md §1.2.
/// </param>
public sealed record SectorLoadedEvent(int MapX, int MapZ, ReadOnlyMemory<byte> Payload) : IClientEvent;

/// <summary>
///     Published when a resident terrain sector is evicted (its Chebyshev distance from the new centre
///     exceeded the eviction radius). Immutable snapshot the Godot layer consumes to release the cell.
///     spec: Docs/RE/formats/terrain.md §9.3 (eviction: distance strictly &gt; 2).
/// </summary>
/// <param name="MapX">Biased sector X of the evicted cell.</param>
/// <param name="MapZ">Biased sector Z of the evicted cell.</param>
public sealed record SectorUnloadedEvent(int MapX, int MapZ) : IClientEvent;