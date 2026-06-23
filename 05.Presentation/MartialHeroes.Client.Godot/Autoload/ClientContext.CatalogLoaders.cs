using Godot;
using MartialHeroes.Assets.Vfs;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Godot.Ui.Scenes.Opening;
using MartialHeroes.Client.Infrastructure.Catalog;

namespace MartialHeroes.Client.Godot.Autoload;

public sealed partial class ClientContext
{
    private static VfsCatalogueLoader BuildCatalogueLoader()
    {
        var clientDir = ClientPathResolver.ResolveClientDir()
                        ?? throw new InvalidOperationException(
                            "[ClientContext] No VFS client directory found. " +
                            "Set MH_CLIENT_DIR, create client_dir.cfg, or install the client at a known path.");

        var infPath = Path.Combine(clientDir, "data.inf");
        var vfsPath = Path.Combine(clientDir, "data", "data.vfs");
        GD.Print($"[ClientContext] CatalogueLoader: using resolved client dir '{clientDir}'.");
        return new VfsCatalogueLoader(infPath, vfsPath);
    }

    private static MappedVfsArchive OpenVfsForTerrain()
    {
        var clientDir = ClientPathResolver.ResolveClientDir()
                        ?? throw new InvalidOperationException(
                            "[ClientContext] No VFS client directory found — cannot open terrain VFS. " +
                            "Set MH_CLIENT_DIR, create client_dir.cfg, or install the client at a known path.");

        var infPath = Path.Combine(clientDir, "data.inf");
        var vfsPath = Path.Combine(clientDir, "data", "data.vfs");
        var archive = MappedVfsArchive.Open(infPath, vfsPath);
        GD.Print($"[ClientContext] Terrain VFS opened from '{clientDir}' ({archive.EntryCount} entries).");
        return archive;
    }

    private static VfsResourcePipeline MountLoadResourcePipeline()
    {
        var clientDir = ClientPathResolver.ResolveClientDir()
                        ?? throw new InvalidOperationException(
                            "[ClientContext] No VFS client directory found — cannot mount load resource pipeline. " +
                            "Set MH_CLIENT_DIR, create client_dir.cfg, or install the client at a known path.");

        var infPath = Path.Combine(clientDir, "data.inf");
        var vfsPath = Path.Combine(clientDir, "data", "data.vfs");
        var pipeline = VfsResourcePipeline.Mount(infPath, vfsPath);
        pipeline.TrackingEnabled = true;
        GD.Print($"[ClientContext] Load VFS pipeline mounted from '{clientDir}'. spec: resource_pipeline.md §2.");
        return pipeline;
    }

    private static string ResolveOpeningSkipCfgPath()
    {
        try
        {
            return ProjectSettings.GlobalizePath(OpeningWindow
                .SkipCfgPath);
        }
        catch
        {
            return "options.cfg";
        }
    }
}