using Godot;

namespace MartialHeroes.Explorer.Viewer;

internal enum EvictionPolicy
{
    None,
    Targeted,
    Full
}

internal static class PreviewLifecycle
{
    internal static void Unload(
        ref Node3D? currentPreview,
        Node3D sceneParent,
        IReadOnlySet<string>? sessionPaths,
        EvictionPolicy policy)
    {
        if (currentPreview is not null)
        {
            var childCount = CountDescendants(currentPreview);
            sceneParent.RemoveChild(currentPreview);
            currentPreview.QueueFree();
            currentPreview = null;
            GD.Print($"[PreviewLifecycle] Unloaded preview subtree ({childCount} nodes queued for free).");
        }

        switch (policy)
        {
            case EvictionPolicy.Full:
                ViewerTextures.EvictAll();
                break;
            case EvictionPolicy.Targeted:
                ViewerTextures.EvictPaths(sessionPaths ?? new HashSet<string>());
                break;
        }
    }

    internal static void Register(Node3D preview, ref Node3D? currentPreview, Node3D sceneParent)
    {
        sceneParent.AddChild(preview);
        currentPreview = preview;
    }

    private static int CountDescendants(Node node)
    {
        var count = 1;
        for (var i = 0; i < node.GetChildCount(); i++)
            count += CountDescendants(node.GetChild(i));
        return count;
    }
}