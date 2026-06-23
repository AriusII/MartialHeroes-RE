using Godot;
using MartialHeroes.Assets.Parsers.DataTables;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.Adapters;

public sealed class GameVerClientVersionSource : IClientVersionSource
{
    public const string GameVerVfsPath = "data/cursor/game.ver";

    private GameVerClientVersionSource(uint versionField)
    {
        VersionField = versionField;
    }

    public uint VersionField { get; }

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