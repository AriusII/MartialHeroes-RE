// AssetPreviewNode.cs — TEMPORARY single-asset preview harness for the Martial Heroes client.
//
// Purpose: load ONE real asset from the VFS, build Godot geometry from the PARSED data using the
// project's own ArrayMesh builders (NEVER GltfDocument — it crashes natively on this project), and
// frame it with a camera + key light so a screenshot shows the parser's output.
//
// Lifecycle (managed by the godot-asset-preview skill):
//   1. Copy to res://Dev/AssetPreviewNode.cs.
//   2. Attach to a one-node res://Dev/AssetPreview.tscn (script as a PROPERTY LINE — see
//      godot-scene-author) OR register as a temporary autoload.
//   3. Set env vars MH_PREVIEW_PATH (VFS path) and MH_PREVIEW_KIND (bud|skn|ted), build, screenshot.
//   4. *** REMOVE the node/scene/autoload + this file afterwards. ***
//
// THIS IS A TEMPLATE. The exact VFS-open and parse calls depend on the project's current
// Assets.Vfs / Assets.Parsers API surface — adjust the two clearly-marked TODO regions to the
// real method names. The coordinate handling is delegated to BudMeshBuilder / SknMeshBuilder, which
// already encode the confirmed conventions:
//   * WORLD geometry (.bud, terrain) negates Z   — Helpers/WorldCoordinates.ToGodot: (x,y,z)->(x,y,-z)
//   * MESH-LOCAL geometry (.skn) negates X        — SknMeshBuilder (plus CW->CCW + UV v-flip)
// Do NOT re-apply a second flip here; the builders own it.

using Godot;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.World;

namespace MartialHeroes.Client.Godot.Dev;

/// <summary>
/// One-shot preview node: builds a single VFS asset into an <see cref="ArrayMesh"/> scene graph and
/// frames it for a screenshot. Temporary diagnostic — not a shipped node.
/// </summary>
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
            // ---- TODO(adjust to current API): open the VFS and read the asset bytes -------------
            // Use the project's VFS catalogue/loader (arrives transitively via Assets.Mapping).
            // Example shape (rename to the real types/methods):
            //     var vfs = VfsCatalogueLoader.Open("D:/MartialHeroesClient");
            //     ReadOnlyMemory<byte> bytes = vfs.Read(vfsPath);
            // -------------------------------------------------------------------------------------
            ReadOnlyMemory<byte> bytes = LoadAssetBytes(vfsPath);

            // ---- TODO(adjust to current API): parse + build with the matching builder -----------
            switch (kind)
            {
                case "bud":
                {
                    // BudScene scene = BudParser.Parse(bytes.Span);   // rename to real parser
                    BudScene scene = ParseBud(bytes);
                    built = BudMeshBuilder.Build(scene, textureResolver: null); // null => grey fallback
                    break;
                }
                case "skn":
                {
                    // SkinnedMesh mesh = SknParser.Parse(bytes.Span); // rename to real parser
                    SkinnedMesh mesh = ParseSkn(bytes);
                    built = SknMeshBuilder.Build(mesh, albedoTexture: null); // static bind pose (TODO: skinning)
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

    // -------------------------------------------------------------------------
    // Camera + light so the screenshot is lit and the mesh fills the frame.
    // -------------------------------------------------------------------------
    private void FrameWithCameraAndLight(Node3D meshRoot)
    {
        Aabb bounds = ComputeAabb(meshRoot);
        Vector3 center = bounds.GetCenter();
        float radius = MathF.Max(bounds.Size.Length() * 0.5f, 1f);

        var cam = new Camera3D { Name = "PreviewCamera" };
        // Pull back along +Z/+Y by a few radii and look at the mesh centre.
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
                // Transform the local AABB into the mesh root's space via the instance transform.
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

    // -------------------------------------------------------------------------
    // TODO stubs — replace with the project's real VFS/parser calls.
    // These throw so the harness fails loudly until wired to the current API.
    // -------------------------------------------------------------------------
    private static ReadOnlyMemory<byte> LoadAssetBytes(string vfsPath)
        => throw new NotImplementedException(
            "Wire to the project's VFS loader (e.g. VfsCatalogueLoader / Assets.Vfs). "
            + $"Requested: {vfsPath}");

    private static BudScene ParseBud(ReadOnlyMemory<byte> bytes)
        => throw new NotImplementedException("Wire to the current .bud parser in Assets.Parsers.");

    private static SkinnedMesh ParseSkn(ReadOnlyMemory<byte> bytes)
        => throw new NotImplementedException("Wire to the current .skn parser in Assets.Parsers.");
}
