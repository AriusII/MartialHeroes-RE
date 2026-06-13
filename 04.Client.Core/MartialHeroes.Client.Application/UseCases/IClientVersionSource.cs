namespace MartialHeroes.Client.Application.UseCases;

/// <summary>
/// Injected seam supplying the client-version handshake token stamped into the 1/9 enter-game request.
/// Layer 04 cannot read the VFS (no Assets.* / Infrastructure reference — DAG), so the concrete adapter
/// that opens <c>data/cursor/game.ver</c>, reads field index 5, and derives the token lives at the
/// composition root (layer 05 / Client.Infrastructure). This layer consumes only the already-derived
/// value. spec: Docs/RE/specs/login_flow.md §3.3 / §7 (version token = 10 × versionField + 9).
/// </summary>
/// <remarks>
/// <para>
/// <b>Token derivation.</b> The token is <c>10 × versionField + 9</c>, where <c>versionField</c> is the
/// 6th little-endian u32 (zero-based field index 5) of the 28-byte <c>game.ver</c> file. The sampled
/// on-disk value is 2114, giving the token 21149 (<c>sample_verified</c>). spec: login_flow.md §3.3.
/// </para>
/// <para>
/// <b>Token placement (UNVERIFIED).</b> The exact byte offset of the token inside the 40-byte 1/9 body
/// is not pinned (only slot@0 and the 40-byte total are firm). The token <em>value</em> 21149 is firm.
/// The 1/9 builder stamps the decimal token as a NUL-terminated ASCII string into the version-token
/// region; if a capture later pins a binary placement this seam still supplies the same value. spec:
/// login_flow.md §3.3 / §9 item 5.
/// </para>
/// </remarks>
public interface IClientVersionSource
{
    /// <summary>
    /// The raw version field read from <c>game.ver</c> (field index 5; sampled value 2114). The token
    /// is derived from this via <see cref="ClientVersionToken.Derive"/>. spec: login_flow.md §3.3.
    /// </summary>
    uint VersionField { get; }
}

/// <summary>
/// Helpers for the 1/9 client-version token derivation. spec: Docs/RE/specs/login_flow.md §3.3 / §7.
/// </summary>
public static class ClientVersionToken
{
    /// <summary>
    /// The sampled <c>game.ver</c> field-index-5 value from the real on-disk file. spec:
    /// Docs/RE/specs/login_flow.md §7 (game.ver sample [4, 31, 35, 1027, 52, 2114, 8]; index 5 = 2114).
    /// </summary>
    public const uint SampledVersionField = 2114;

    /// <summary>
    /// The token derived from the sampled field: <c>10 × 2114 + 9 = 21149</c>. <c>sample_verified</c>.
    /// spec: Docs/RE/specs/login_flow.md §3.3 / §7 (version token = 21149 for this build).
    /// </summary>
    public const uint SampledToken = 21149;

    /// <summary>
    /// Derives the enter-game version token from a raw <c>game.ver</c> field: <c>10 × field + 9</c>.
    /// spec: Docs/RE/specs/login_flow.md §3.3 / §7.
    /// </summary>
    public static uint Derive(uint versionField) => (10u * versionField) + 9u;
}

/// <summary>
/// Default <see cref="IClientVersionSource"/> returning the <c>sample_verified</c> field value (2114),
/// which derives the cited token 21149. Used when no real <c>game.ver</c> adapter is wired (tests,
/// headless). spec: Docs/RE/specs/login_flow.md §3.3 / §7.
/// </summary>
// TODO(E4-c, layer 05/Infrastructure): replace with an adapter that reads data/cursor/game.ver via the
// VFS (field index 5) and exposes the real VersionField. spec: login_flow.md §3.3 (VFS-gated read).
public sealed class DefaultClientVersionSource : IClientVersionSource
{
    /// <summary>The shared singleton instance carrying the sampled field value.</summary>
    public static readonly DefaultClientVersionSource Instance = new();

    private DefaultClientVersionSource()
    {
    }

    /// <inheritdoc />
    public uint VersionField => ClientVersionToken.SampledVersionField;
}