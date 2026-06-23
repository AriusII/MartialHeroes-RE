using Godot;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Terrain;
using MartialHeroes.Assets.Parsers.Terrain.Models;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Godot.World;
using MartialHeroes.Client.Presentation.Helpers;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

public sealed partial class CharSelectScene3D : Node3D
{
    private const int BackdropAreaId = 0;
    private const int BackdropMapX = 10000;
    private const int BackdropMapZ = 9990;

    private const float RowPlatformY = 0.0f;

    private const float RowPivotLegacyX = 508.48f;
    private const float RowPivotLegacyY = 69.89f;
    private const float RowPivotLegacyZ = -9758.57f;

    private const float PreviewScale = 6.0f;

    private const float SelectedActorFrontYaw = 0.0f;
    private const float DeselectedActorBackYaw = Mathf.Pi;
    private const float ManualSelectedYawRadiansPerSecond = 2.0f;

    private const float CameraFov = 50.0f;
    private const float CameraNear = 5.0f;
    private const float CameraFar = 15000.0f;


    private const int EnvFrozenKeyframe = 29;


    private static readonly float[] SlotLegacyX = [488.0f, 500.0f, 512.0f, 524.0f, 536.0f];

    private static readonly float[] SlotLegacyZ = [-9737.0f, -9738.0f, -9738.5f, -9738.0f, -9737.0f];

    private static readonly float[] SlotGodotZ = BuildSlotGodotZ();

    internal static readonly Vector3 StaticEyeGodot = ToGodotVec(515.549f, 137.266f, -9397.710f);

    private static readonly Vector3[] SlotFocusGodot =
    [
        ToGodotVec(512.0f, 87.0f, -9652.0f),
        ToGodotVec(343.0f, 104.0f, -9734.0f),
        ToGodotVec(471.0f, 115.0f, -9812.0f),
        ToGodotVec(622.0f, 75.0f, -9802.5f),
        ToGodotVec(662.0f, 130.0f, -9746.0f)
    ];


    private static readonly Vector3 XeffAnchorGodot = ToGodotVec(508.483f, 69.887f, -9758.569f);
    private readonly Node3D?[] _slotActors = new Node3D?[5];
    private RealClientAssets? _assets;
    private TerrainNode? _backdropTerrain;


    private Camera3D? _camera;

    private CharSelectCameraRig? _cameraRig;

    private EnvironmentNode? _environmentNode;

    private bool _initialised;
    private int _selectedSlot;


    public SlotDescriptor[] SlotDescriptors { get; set; } = new SlotDescriptor[5];

    private static float[] BuildSlotGodotZ()
    {
        var z = new float[SlotLegacyZ.Length];
        for (var i = 0; i < z.Length; i++)
            z[i] = WorldCoordinates.ToGodot(0f, 0f, SlotLegacyZ[i]).Z;
        return z;
    }


    public void Initialise(RealClientAssets? assets)
    {
        try
        {
            _assets = assets;

            BuildEnvironmentDataDriven(assets);
            BuildCamera();

            if (assets is not null)
            {
                BuildBackdropTerrain(assets);
                BuildBackdropProps(assets);
                BuildAmbientEffect(assets);
            }
            else
            {
                GD.Print(
                    "[CharSelectScene3D] No VFS — backdrop cell / actors / ambient effect skipped; env + camera only.");
            }

            _initialised = true;
            RefreshSlotActors(assets);

            GD.Print("[CharSelectScene3D] 3D scene initialised from real assets (no procedural sky, no omni rig). " +
                     "spec: frontend_scenes.md §3.3/§3.5/§3.6/§3.7.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] Initialise failed: {ex.Message}");
        }
    }

    public void RefreshSlotActors(RealClientAssets? assets)
    {
        if (!_initialised) return;

        try
        {
            for (var i = 0; i < _slotActors.Length; i++)
            {
                var actor = _slotActors[i];
                if (actor is not null && IsInstanceValid(actor))
                {
                    RemoveChild(actor);
                    actor.QueueFree();
                }

                _slotActors[i] = null;
            }

            if (assets is not null)
            {
                BuildCharacterRow(assets);
                ApplySlotSelectionFacing();
            }
            else
            {
                GD.Print("[CharSelectScene3D] RefreshSlotActors: no VFS — actor row cleared (no actors).");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] RefreshSlotActors failed: {ex.Message}");
        }
    }

    public void SetSelectedSlot(int slotIndex)
    {
        if ((uint)slotIndex >= 5u) return;
        _selectedSlot = slotIndex;
        ApplySlotSelectionFacing();
    }

    public int TryHitTestSlot(Vector2 viewportLocalPos)
    {
        return _cameraRig?.HitTest(viewportLocalPos) ?? -1;
    }


    private void BuildEnvironmentDataDriven(RealClientAssets? assets)
    {
        _environmentNode = new EnvironmentNode
        {
            Name = "CharSelectEnvironment",
            CycleEnabled = false
        };
        AddChild(_environmentNode);

        _environmentNode.Configure(assets, BackdropAreaId);

        _environmentNode.SetTimeOfDay(EnvFrozenKeyframe);

        var we = FindWorldEnvironmentChild(_environmentNode);
        if (we?.Environment is { } env)
        {
            env.FogEnabled = false;
            GD.Print(
                "[CharSelectScene3D] Environment fog forced OFF per §3.6.2 (fog-blend offset zeroed). CODE-CONFIRMED.");
        }
        else
        {
            GD.Print(
                "[CharSelectScene3D] WARNING: could not locate WorldEnvironment child to apply fog-off override (§3.6.2).");
        }

        GD.Print(
            $"[CharSelectScene3D] Data-driven environment: area 0, keyframe {EnvFrozenKeyframe} (14:30 = 52200 s). " +
            "spec: §3.6 / §3.6.1 / §3.6.3 CODE-CONFIRMED. NO hardcoded ambient/fog literals.");
    }

    private static WorldEnvironment? FindWorldEnvironmentChild(Node parent)
    {
        for (var i = 0; i < parent.GetChildCount(); i++)
            if (parent.GetChild(i) is WorldEnvironment we)
                return we;
        return null;
    }


    private void BuildCamera()
    {
        _camera = new Camera3D
        {
            Name = "CharSelectCamera",
            Fov = CameraFov,
            Near = CameraNear,
            Far = CameraFar,
            KeepAspect = Camera3D.KeepAspectEnum.Height
        };
        AddChild(_camera);

        _camera.GlobalPosition = StaticEyeGodot;

        if (SlotFocusGodot.Length > 0)
            _camera.LookAt(SlotFocusGodot[0], Vector3.Up);

        _cameraRig = new CharSelectCameraRig { Name = "CharSelectCameraRig" };
        AddChild(_cameraRig);
        _cameraRig.Configure(
            _camera,
            StaticEyeGodot,
            SlotLegacyX,
            SlotGodotZ,
            i => (uint)i < (uint)_slotActors.Length ? _slotActors[i] : null);

        GD.Print(
            $"[CharSelectScene3D] Camera STATIC: eye={StaticEyeGodot} " +
            $"FOV={CameraFov}/near={CameraNear}/far={CameraFar}. " +
            "spec: Docs/RE/scenes/charselect.md §6.1 §6.3");
    }


    private void BuildBackdropTerrain(RealClientAssets assets)
    {
        var tag = AreaTag(BackdropAreaId);
        var tedPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.ted";

        if (!assets.Contains(tedPath))
        {
            GD.Print($"[CharSelectScene3D] Backdrop .ted absent: {tedPath} — terrain skipped.");
            return;
        }

        try
        {
            var tedData = assets.GetRaw(tedPath);
            if (tedData.IsEmpty)
            {
                GD.Print($"[CharSelectScene3D] Backdrop .ted empty: {tedPath} — terrain skipped.");
                return;
            }

            var terrainNode = new TerrainNode
            {
                Name = "BackdropTerrain",
                TextureResolver = BuildTerrainTextureResolver(assets)
            };
            AddChild(terrainNode);
            _backdropTerrain = terrainNode;

            terrainNode.OnSectorLoaded(new SectorLoadedEvent(
                BackdropMapX, BackdropMapZ, tedData));

            GD.Print(
                $"[CharSelectScene3D] Backdrop terrain cell ({BackdropMapX},{BackdropMapZ}) loaded. spec: §3.7.1.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] Backdrop terrain failed: {ex.Message}");
        }
    }


    private void BuildBackdropProps(RealClientAssets assets)
    {
        var tag = AreaTag(BackdropAreaId);
        var budPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.bud";

        if (!assets.Contains(budPath))
        {
            GD.Print($"[CharSelectScene3D] Backdrop .bud absent: {budPath} — props skipped.");
            return;
        }

        try
        {
            var scene = assets.LoadBud(budPath);
            if (scene is null || scene.Objects.Length == 0)
            {
                GD.Print("[CharSelectScene3D] Backdrop .bud empty — no props.");
                return;
            }

            var bgPool = TryLoadBgPool(assets);
            var cellMap = TryLoadCellMap(assets);

            Func<uint, ImageTexture?> budTexResolver = budIdx =>
                ResolveTexture(assets, bgPool, cellMap, "BUILDING", (int)budIdx);

            var propsRoot = BudMeshBuilder.Build(scene, budTexResolver);


            propsRoot.Name = "BackdropProps";
            AddChild(propsRoot);

            GD.Print(
                $"[CharSelectScene3D] Backdrop props built ({scene.Objects.Length} objects, carved wall). spec: §3.7.1 / §3.7.3.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] Backdrop props failed: {ex.Message}");
        }
    }


    private void BuildAmbientEffect(RealClientAssets assets)
    {
        XeffSceneEffect.LoadAndAttach(this, XeffAnchorGodot, assets);
    }

    public override void _Process(double delta)
    {
        TickSelectedPreviewYaw((float)delta);
    }

    private void TickSelectedPreviewYaw(float dt)
    {
        var selectedActor = GetSlotActor(_selectedSlot);
        if (selectedActor is null) return;

        var yawDelta = 0.0f;
        if (global::Godot.Input.IsActionPressed("ui_left")) yawDelta -= ManualSelectedYawRadiansPerSecond * dt;
        if (global::Godot.Input.IsActionPressed("ui_right")) yawDelta += ManualSelectedYawRadiansPerSecond * dt;
        if (yawDelta == 0.0f) return;

        var rotation = selectedActor.Rotation;
        rotation.Y += yawDelta;
        selectedActor.Rotation = rotation;
    }

    private void ApplySlotSelectionFacing()
    {
        for (var i = 0; i < _slotActors.Length; i++)
        {
            var actor = GetSlotActor(i);
            if (actor is null) continue;

            var rotation = actor.Rotation;
            rotation.Y = i == _selectedSlot ? SelectedActorFrontYaw : DeselectedActorBackYaw;
            actor.Rotation = rotation;
        }
    }

    private Node3D? GetSlotActor(int slotIndex)
    {
        return (uint)slotIndex < (uint)_slotActors.Length && _slotActors[slotIndex] is { } actor &&
               IsInstanceValid(actor)
            ? actor
            : null;
    }


    private void BuildCharacterRow(RealClientAssets assets)
    {
        var rowY = RowPlatformY;
        if (_backdropTerrain is not null &&
            _backdropTerrain.TryGetGroundHeight(RowPivotLegacyX, RowPivotLegacyZ, out var sampledY))
            GD.Print($"[CharSelectScene3D] Terrain sampler at pivot = {sampledY:F3} (diagnostic only); " +
                     $"placing actors on stage-origin Y={rowY:F2} per §3.3.1. spec: §3.3.1 CODE-CONFIRMED.");

        for (var i = 0; i < 5; i++)
        {
            var occupied = i < SlotDescriptors.Length && SlotDescriptors[i].IsOccupied;
            if (!occupied) continue;

            try
            {
                var actor = TryBuildSlotActorViaResolver(assets, i, SlotDescriptors[i], rowY);
                if (actor is not null)
                {
                    _slotActors[i] = actor;
                    AddChild(actor);
                    GD.Print(
                        $"[CharSelectScene3D] Slot {i} actor at Godot ({SlotLegacyX[i]:F1}, {rowY:F2}, {SlotGodotZ[i]:F1}). spec: §3.3.1.");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[CharSelectScene3D] Slot {i} actor build failed: {ex.Message}");
            }
        }
    }

    private Node3D? TryBuildSlotActorViaResolver(
        RealClientAssets assets, int slotIdx, SlotDescriptor descriptor, float rowY)
    {
        var appearance = new SlotAppearanceResolver.SlotAppearance(
            descriptor.InternalClass,
            descriptor.Variant,
            descriptor.FaceA,
            descriptor.Equip);

        var result = SlotAppearanceResolver.BuildSlotActor(assets, appearance, $"slot{slotIdx}");

        if (result.ActorRoot is null)
            return null;

        var slotWrapper = new Node3D { Name = $"Slot{slotIdx}Actor" };
        slotWrapper.Position = new Vector3(SlotLegacyX[slotIdx], rowY, SlotGodotZ[slotIdx]);
        slotWrapper.Scale = Vector3.One * PreviewScale;

        slotWrapper.Rotation = new Vector3(
            0f,
            slotIdx == _selectedSlot ? SelectedActorFrontYaw : DeselectedActorBackYaw,
            0f);

        slotWrapper.AddChild(result.ActorRoot);
        return slotWrapper;
    }


    private Func<int, ImageTexture?> BuildTerrainTextureResolver(RealClientAssets assets)
    {
        var bgPool = TryLoadBgPool(assets);
        var cellMap = TryLoadCellMap(assets);
        var cache = new Dictionary<int, ImageTexture?>();
        return texByte =>
        {
            if (cache.TryGetValue(texByte, out var cached)) return cached;
            var tex = ResolveTexture(assets, bgPool, cellMap, "TERRAIN", texByte);
            cache[texByte] = tex;
            return tex;
        };
    }

    private static BgTextureCatalog? TryLoadBgPool(RealClientAssets assets)
    {
        try
        {
            const string lstPath = "data/map000/texture/bgtexture.lst";
            if (assets.Contains(lstPath))
                return BgTextureCatalog.FromLst(assets.GetRaw(lstPath));

            GD.PrintErr($"[CharSelectScene3D] bgtexture.lst absent ({lstPath}) — terrain/buildings stay untextured.");
            return null;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] bgtexture.lst load failed: {ex.Message}");
            return null;
        }
    }

    private static MapDescriptor? TryLoadCellMap(RealClientAssets assets)
    {
        try
        {
            var tag = AreaTag(BackdropAreaId);
            var mapPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.map";
            return assets.Contains(mapPath) ? MapDescriptorParser.Parse(assets.GetRaw(mapPath)) : null;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] backdrop .map load failed: {ex.Message}");
            return null;
        }
    }

    private static ImageTexture? ResolveTexture(
        RealClientAssets assets, BgTextureCatalog? pool, MapDescriptor? map,
        string section, int oneBasedIdx)
    {
        if (pool is null || map is null || oneBasedIdx <= 0) return null;

        (int Flag, int TexId)[]? list = null;
        foreach (var s in map.Sections)
            if (string.Equals(s.Keyword, section, StringComparison.OrdinalIgnoreCase))
            {
                list = s.Textures;
                break;
            }

        if (list is null) return null;

        var li = oneBasedIdx - 1;
        if ((uint)li >= (uint)list.Length) return null;

        var rel = pool.ResolveRelativePath(list[li].TexId);
        if (rel is null) return null;

        var ddsPath = $"data/map000/texture/{rel}.dds";
        return assets.Contains(ddsPath) ? assets.LoadTexture(ddsPath) : null;
    }


    private static Vector3 ToGodotVec(float legacyX, float legacyY, float legacyZ)
    {
        var (gx, gy, gz) = WorldCoordinates.ToGodot(legacyX, legacyY, legacyZ);
        return new Vector3(gx, gy, gz);
    }

    private static string AreaTag(int areaId)
    {
        return areaId.ToString("D3");
    }

    public readonly record struct SlotDescriptor(
        bool IsOccupied,
        uint InternalClass,
        uint Variant = 0,
        uint FaceA = 0,
        uint[]? Equip = null);
}