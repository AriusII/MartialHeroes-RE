namespace MartialHeroes.Assets.Parsers.Models;

/// <summary>
/// Decoded contents of <c>data/cursor/game.ver</c> — a 28-byte binary file holding
/// 7 × u32 LE fields that supply the client version number used by the enter-game token.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §7 — "flat array of 7 × u32, little-endian (28 bytes)":
///   CONFIRMED.
/// <para>
/// Only <c>VersionSourceField</c> (field index 5, offset 0x14) is consumed by the protocol
/// formula.  The remaining six fields are present on disk but their semantics are UNVERIFIED.
/// spec: Docs/RE/formats/config_tables.md §7 §Layout — "version_source (field5) @ 0x14": CONFIRMED.
/// </para>
/// <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public sealed record GameVerData
{
    /// <summary>
    /// field0 — u32 LE @ offset 0x00.  Semantic UNVERIFIED.
    /// spec: Docs/RE/formats/config_tables.md §7 §Layout — "field0 @ 0x00: UNVERIFIED (value present)".
    /// </summary>
    public required uint Field0 { get; init; }

    /// <summary>
    /// field1 — u32 LE @ offset 0x04.  Semantic UNVERIFIED.
    /// spec: Docs/RE/formats/config_tables.md §7 §Layout — "field1 @ 0x04: UNVERIFIED".
    /// </summary>
    public required uint Field1 { get; init; }

    /// <summary>
    /// field2 — u32 LE @ offset 0x08.  Semantic UNVERIFIED.
    /// spec: Docs/RE/formats/config_tables.md §7 §Layout — "field2 @ 0x08: UNVERIFIED".
    /// </summary>
    public required uint Field2 { get; init; }

    /// <summary>
    /// field3 — u32 LE @ offset 0x0C.  Possibly a build number.  Semantic UNVERIFIED.
    /// spec: Docs/RE/formats/config_tables.md §7 §Layout — "field3 @ 0x0C: UNVERIFIED".
    /// </summary>
    public required uint Field3 { get; init; }

    /// <summary>
    /// field4 — u32 LE @ offset 0x10.  Semantic UNVERIFIED.
    /// spec: Docs/RE/formats/config_tables.md §7 §Layout — "field4 @ 0x10: UNVERIFIED".
    /// </summary>
    public required uint Field4 { get; init; }

    /// <summary>
    /// <b>version_source</b> — u32 LE @ offset <c>0x14</c> (field index 5).
    /// This is the single field consumed by the enter-game version token formula.
    /// Observed value in the real client: <c>2114</c>.
    /// spec: Docs/RE/formats/config_tables.md §7 §Layout —
    ///   "version_source (field5) @ 0x14 — CONFIRMED (role)".
    /// </summary>
    public required uint VersionSourceField { get; init; }

    /// <summary>
    /// field6 — u32 LE @ offset 0x18.  Semantic UNVERIFIED.
    /// spec: Docs/RE/formats/config_tables.md §7 §Layout — "field6 @ 0x18: UNVERIFIED".
    /// </summary>
    public required uint Field6 { get; init; }

    /// <summary>
    /// The enter-game / login version token derived from <see cref="VersionSourceField"/>.
    /// Formula: <c>10 × version_source + 9</c>.
    /// With the observed <c>version_source = 2114</c> this yields <c>21149</c>.
    /// spec: Docs/RE/formats/config_tables.md §7 §Version token derivation —
    ///   "version_token = 10 × version_source + 9": CONFIRMED.
    /// </summary>
    public uint EnterGameVersionToken => 10u * VersionSourceField + 9u;
}