using Godot;
using MartialHeroes.Assets.Vfs;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Godot.Ui.Scenes.Opening;
using MartialHeroes.Client.Infrastructure.Catalog;

namespace MartialHeroes.Client.Godot.Autoload;

public sealed partial class ClientContext
{
    // -------------------------------------------------------------------------
    // VFS / catalogue helpers
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Builds a <see cref="VfsCatalogueLoader" /> using <see cref="ClientPathResolver.ResolveClientDir" />.
    ///     Throws <see cref="InvalidOperationException" /> when no valid client directory is found;
    ///     the client requires the real VFS.
    ///     Path resolution is delegated entirely to <see cref="ClientPathResolver" /> (env-var override,
    ///     then client_dir.cfg, then auto-detection). No direct environment-variable read here.
    ///     spec: PRESERVATION_AND_ARCHITECTURE.md §Non-distribution rules (user supplies originals).
    ///     spec: Docs/RE/formats/pak.md §Two-file scheme.
    /// </summary>
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

    /// <summary>
    ///     Opens the VFS archive for terrain sector streaming using <see cref="ClientPathResolver.ResolveClientDir" />.
    ///     Throws when no valid client directory is found; the client requires the real VFS.
    ///     spec: Docs/RE/formats/terrain.md §1.2 / §1.3.
    ///     spec: PRESERVATION_AND_ARCHITECTURE.md §Non-distribution rules.
    /// </summary>
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
            // spec: Docs/RE/specs/resource_pipeline.md §2.5 — OPENNING/SKIP is read from the
            // per-account/config-singleton INI path, not the dev VFS locator client_dir.cfg.
            return ProjectSettings.GlobalizePath(OpeningWindow
                .SkipCfgPath);
        }
        catch
        {
            return "options.cfg";
        }
    }
}