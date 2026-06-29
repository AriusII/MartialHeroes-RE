using Godot;
using MartialHeroes.Assets.Mapping;

namespace MartialHeroes.Client.Godot.World;

internal static class SlotRenderer
{
    public static bool RenderSlot1Buildings(
        Node3D parent,
        AssembledCell cell,
        Func<uint, ImageTexture?> budTexResolver,
        Func<uint, byte> budKindResolver,
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
            budRoot = BudMeshBuilder.Build(budScene, id => budTexResolver(id), id => budKindResolver(id));
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

internal sealed partial class BudSwayClock : Node
{
    private const string PhaseParam = "bud_sway_phase";

    private const double TickMs = 50.0;

    private const float StepPerTick = 0.25f;

    private static readonly StringName PhaseParamName = PhaseParam;

    private static bool _globalParamRegistered;

    private double _accumMs;

    private float _phase;

    private float _velocity = 1f;

    public static void EnsureGlobalParam()
    {
        if (_globalParamRegistered) return;

        try
        {
            var present = false;
            var existing = RenderingServer.GlobalShaderParameterGetList();
            foreach (var name in existing)
                if (name.ToString() == PhaseParam)
                {
                    present = true;
                    break;
                }

            if (!present)
                RenderingServer.GlobalShaderParameterAdd(
                    PhaseParam, RenderingServer.GlobalShaderParameterType.Float, 0.0f);

            _globalParamRegistered = true;
            GD.Print("[BudSwayClock] Global sway clock 'bud_sway_phase' registered (shared ping-pong oscillator). " +
                     "spec: terrain_scene.md Addendum A1.4 — single global clock shared by all foliage objects.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[BudSwayClock] EnsureGlobalParam failed: {ex.Message}");
        }
    }

    public override void _Ready()
    {
        Name = "BudSwayClock";
        EnsureGlobalParam();
    }

    public override void _Process(double delta)
    {
        _accumMs += delta * 1000.0;

        var advanced = false;
        while (_accumMs >= TickMs)
        {
            _accumMs -= TickMs;
            _phase += _velocity * StepPerTick;
            if (_phase >= 1f)
            {
                _phase = 1f;
                _velocity = -_velocity;
            }
            else if (_phase <= -1f)
            {
                _phase = -1f;
                _velocity = -_velocity;
            }

            advanced = true;
        }

        if (advanced)
            RenderingServer.GlobalShaderParameterSet(PhaseParamName, _phase);
    }
}