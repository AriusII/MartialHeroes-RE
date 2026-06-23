
using Godot;
using MartialHeroes.Assets.Mapping;

namespace MartialHeroes.Client.Godot.World;

internal static class SlotRenderer
{

    public static bool RenderSlot1Buildings(
        Node3D parent,
        AssembledCell cell,
        Func<uint, ImageTexture?> budTexResolver,
        (int MapX, int MapZ) cellMapXZ)
    {
        var budScene = cell.Slot1BuildingObjectGrid;
        if (budScene is null)
        {
            GD.Print($"[SlotRenderer] Cell ({cellMapXZ.MapX},{cellMapXZ.MapZ}) slot 1: absent (terrain-only cell). " +
                     "spec: area_inventory.md §3.2 — absent .bud is not an error.");
            return false;
        }

        if (budScene.Objects.Length == 0)
        {
            GD.Print($"[SlotRenderer] Cell ({cellMapXZ.MapX},{cellMapXZ.MapZ}) slot 1: BUD scene empty.");
            return false;
        }

        Node3D budRoot;
        try
        {
            budRoot = BudMeshBuilder.Build(budScene, id => budTexResolver(id));
        }
        catch (Exception ex)
        {
            GD.PrintErr(
                $"[SlotRenderer] Cell ({cellMapXZ.MapX},{cellMapXZ.MapZ}) slot 1: BudMeshBuilder.Build failed: {ex.Message}");
            return false;
        }

        budRoot.Name = $"ComposedBudSlot1_({cellMapXZ.MapX},{cellMapXZ.MapZ})";
        parent.AddChild(budRoot);

        GD.Print($"[SlotRenderer][ComposeRender] Cell ({cellMapXZ.MapX},{cellMapXZ.MapZ}) slot 1: " +
                 $"{budScene.Objects.Length} buildings spawned from assembled cell. " +
                 "spec: assembly_graph.md §1 — slot 1 mass/building grid.");
        return true;
    }


    public static int RenderFxSlots(
        Node3D parent,
        AssembledCell cell,
        Vector3 cellWorldOriginGodot,
        (int MapX, int MapZ) cellMapXZ,
        FxMeshBuilder.FxTextureResolver? fxTexResolver = null)
    {
        var rendered = 0;


        if (cell.Slot2Fx1 is not null)
            rendered += AttachFxSubtree(parent, FxMeshBuilder.BuildFx1(cell.Slot2Fx1, fxTexResolver), 2, cellMapXZ);
        if (cell.Slot3Fx2 is not null)
            rendered += AttachFxSubtree(parent, FxMeshBuilder.BuildFx2(cell.Slot3Fx2, fxTexResolver), 3, cellMapXZ);
        if (cell.Slot4Fx3 is not null)
            rendered += AttachFxSubtree(parent, FxMeshBuilder.BuildFx3(cell.Slot4Fx3, fxTexResolver), 4, cellMapXZ);
        if (cell.Slot5Fx4 is not null)
            rendered += AttachFxSubtree(parent, FxMeshBuilder.BuildFx4(cell.Slot5Fx4, fxTexResolver), 5, cellMapXZ);
        if (cell.Slot6Fx5 is not null)
            rendered += AttachFxSubtree(parent, FxMeshBuilder.BuildFx5(cell.Slot6Fx5, fxTexResolver), 6, cellMapXZ);
        if (cell.Slot7Fx6 is not null)
            rendered += AttachFxSubtree(parent, FxMeshBuilder.BuildFx6(cell.Slot7Fx6, fxTexResolver), 7, cellMapXZ);
        if (cell.Slot8Fx7 is not null)
            rendered += AttachFxSubtree(parent, FxMeshBuilder.BuildFx7(cell.Slot8Fx7, fxTexResolver), 8, cellMapXZ);

        if (rendered > 0)
            GD.Print($"[SlotRenderer][ComposeRender] Cell ({cellMapXZ.MapX},{cellMapXZ.MapZ}) " +
                     $"slots 2-8: {rendered} FX overlay layer(s) built (origin {cellWorldOriginGodot}). " +
                     "spec: assembly_graph.md §1; terrain_layers.md §4.2.");
        else
            GD.Print($"[SlotRenderer][ComposeRender] Cell ({cellMapXZ.MapX},{cellMapXZ.MapZ}) " +
                     "slots 2-8: all absent (no FX overlays for this cell). spec: area_inventory.md §3.2.");

        return rendered;
    }


    private static int AttachFxSubtree(Node3D parent, Node3D fxRoot, int slotIndex, (int MapX, int MapZ) cellMapXZ)
    {
        if (fxRoot.GetChildCount() == 0)
        {
            fxRoot.QueueFree();
            return 0;
        }

        fxRoot.Name = $"ComposedFxSlot{slotIndex}_({cellMapXZ.MapX},{cellMapXZ.MapZ})";
        parent.AddChild(fxRoot);
        return 1;
    }
}