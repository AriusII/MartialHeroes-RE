using Godot;
using MartialHeroes.Assets.Parsers.DataTables;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.Adapters;

/// <summary>
///     Real <see cref="IClientVersionSource" /> backed by the on-disk <c>data/cursor/game.ver</c> read
///     through the VFS. Layer-04 cannot touch the VFS (DAG), so this composition-root adapter opens the
///     file, parses it with <see cref="GameVerParser" />, and exposes field index 5 (the version source).
///     The 1/9 enter-game token is then derived by <see cref="ClientVersionToken.Derive" /> as
///     <c>10 × field + 9</c>.
///     <para>
///         This replaces the prior <c>versionSource: null → DefaultClientVersionSource</c> wiring, which
///         was correct ONLY by coincidence (the hardcoded constant 2114 happened to equal the real
///         on-disk field). Sourcing it from the asset removes that fragility: the binary itself loads
///         <c>data/cursor/game.ver</c> at enter time (SelectWindow_EnterGame → GameVer_LoadAndParse,
///         field index 5, token = 10·field+9). If the file is absent/unreadable we fall back to
///         <see cref="DefaultClientVersionSource" /> (logged, never fabricated).
///     </para>
///     spec: Docs/RE/specs/login_flow.md §3.3 / §7; Docs/RE/formats/game_ver.md (field5 @ 0x14 = version source).
/// </summary>
public sealed class GameVerClientVersionSource : IClientVersionSource
{
    /// <summary>The VFS key the binary opens verbatim. spec: login_flow.md §3.3 ("data/cursor/game.ver").</summary>
    public const string GameVerVfsPath = "data/cursor/game.ver";

    private GameVerClientVersionSource(uint versionField)
    {
        VersionField = versionField;
    }

    /// <inheritdoc />
    public uint VersionField { get; }

    /// <summary>
    ///     Resolves the version source from the real <c>game.ver</c> via <paramref name="assets" />. Falls
    ///     back to <see cref="DefaultClientVersionSource" /> (logged) when the VFS handle is null, the file
    ///     is absent, or parsing fails — never fabricates a value.
    /// </summary>
    public static IClientVersionSource Resolve(RealClientAssets? assets)
    {
        try
        {
            if (assets is not null && assets.Contains(GameVerVfsPath))
            {
                var parsed = GameVerParser.Parse(assets.GetRaw(GameVerVfsPath));
                GD.Print(
                    $"[GameVerClientVersionSource] game.ver field5={parsed.VersionSourceField} → 1/9 token " +
                    $"{ClientVersionToken.Derive(parsed.VersionSourceField)} (asset-sourced, NOT the hardcoded constant). " +
                    "spec: login_flow.md §3.3 / §7; game_ver.md.");
                return new GameVerClientVersionSource(parsed.VersionSourceField);
            }

            GD.PrintErr(
                $"[GameVerClientVersionSource] {GameVerVfsPath} absent on the VFS — falling back to " +
                $"DefaultClientVersionSource (field {ClientVersionToken.SampledVersionField}). spec: login_flow.md §3.3.");
        }
        catch (Exception ex)
        {
            GD.PrintErr(
                $"[GameVerClientVersionSource] game.ver read/parse failed: {ex.Message} — falling back to " +
                "DefaultClientVersionSource. spec: login_flow.md §3.3.");
        }

        return DefaultClientVersionSource.Instance;
    }
}
