namespace MartialHeroes.Client.Application.Assets;

/// <summary>
/// Engine-free byte-source seam used by the state-2 load worker. Presentation/infrastructure adapters
/// may back this with the mounted VFS or loose files, but the Application layer only observes logical
/// paths and byte counts. spec: Docs/RE/specs/resource_pipeline.md §1 / §2.1 / §3A.1.
/// </summary>
public interface ILoadResourceSource
{
    ValueTask<long> LoadAsync(string logicalPath, CancellationToken cancellationToken = default);
}