
using Godot;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.World;

namespace MartialHeroes.Client.Godot.Dev;

public partial class AssetPreviewNode : Node3D
{
    public override void _Ready()
    {
        string vfsPath = OS.GetEnvironment("MH_PREVIEW_PATH");
        string kind = OS.GetEnvironment("MH_PREVIEW_KIND").ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(vfsPath))
        {
            GD.PrintErr("[AssetPreview] MH_PREVIEW_PATH not set — nothing to preview.");
            return;
        }

        GD.Print($"[AssetPreview] kind={kind} path={vfsPath}");

        Node3D? built = null;
        try
        {

            ReadOnlyMemory<byte> bytes = LoadAssetBytes(vfsPath);

            switch (kind)
            {
                case "bud":
                {

                    BudScene scene = ParseBud(bytes);
                    built = BudMeshBuilder.Build(scene, textureResolver: null);
                    break;
                }
                case "skn":
                {

                    SkinnedMesh mesh = ParseSkn(bytes);
                    built = SknMeshBuilder.Build(mesh, albedoTexture: null);
                    break;
                }
                case "ted":
                default:
                    GD.PrintErr($"[AssetPreview] kind '{kind}' — for .ted/.map follow the World/TerrainNode "
                                + "per-patch ArrayMesh pattern (16x16 patches, 65x65 grid); GltfDocument is banned.");
                    return;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AssetPreview] failed to build '{vfsPath}': {ex}");
            return;
        }

        if (built is null)
        {
            GD.PrintErr("[AssetPreview] builder returned null (no geometry).");
            return;
        }

        AddChild(built);
        FrameWithCameraAndLight(built);
        GD.Print("[AssetPreview] built + framed — ready for screenshot.");
    }

    private void FrameWithCameraAndLight(Node3D meshRoot)
    {
        Aabb bounds = ComputeAabb(meshRoot);
        Vector3 center = bounds.GetCenter();
        float radius = MathF.Max(bounds.Size.Length() * 0.5f, 1f);

        var cam = new Camera3D { Name = "PreviewCamera" };

        Vector3 camPos = center + new Vector3(radius * 1.2f, radius * 0.9f, radius * 2.2f);
        cam.GlobalPosition = camPos;
        cam.LookAtFromPosition(camPos, center, Vector3.Up);
        cam.Far = radius * 20f + 100f;
        AddChild(cam);
        cam.Current = true;

        var sun = new DirectionalLight3D { Name = "PreviewSun" };
        sun.RotationDegrees = new Vector3(-45f, -30f, 0f);
        sun.LightEnergy = 1.2f;
        AddChild(sun);

        var env = new WorldEnvironment { Name = "PreviewEnv" };
        var e = new global::Godot.Environment
        {
            BackgroundMode = global::Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.12f, 0.13f, 0.16f),
            AmbientLightSource = global::Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(1f, 1f, 1f),
            AmbientLightEnergy = 0.5f,
        };
        env.Environment = e;
        AddChild(env);
    }

    private static Aabb ComputeAabb(Node node)
    {
        Aabb? acc = null;
        foreach (Node child in IterateTree(node))
        {
            if (child is MeshInstance3D mi && mi.Mesh is not null)
            {
                Aabb local = mi.GetAabb();

                Aabb world = mi.Transform * local;
                acc = acc is null ? world : acc.Value.Merge(world);
            }
        }
        return acc ?? new Aabb(Vector3.Zero, Vector3.One);
    }

    private static IEnumerable<Node> IterateTree(Node root)
    {
        yield return root;
        foreach (Node c in root.GetChildren())
            foreach (Node d in IterateTree(c))
                yield return d;
    }

    private static ReadOnlyMemory<byte> LoadAssetBytes(string vfsPath)
        => throw new NotImplementedException(
            "Wire to the project's VFS loader (e.g. VfsCatalogueLoader / Assets.Vfs). "
            + $"Requested: {vfsPath}");

    private static BudScene ParseBud(ReadOnlyMemory<byte> bytes)
        => throw new NotImplementedException("Wire to the current .bud parser in Assets.Parsers.");

    private static SkinnedMesh ParseSkn(ReadOnlyMemory<byte> bytes)
        => throw new NotImplementedException("Wire to the current .skn parser in Assets.Parsers.");
}
