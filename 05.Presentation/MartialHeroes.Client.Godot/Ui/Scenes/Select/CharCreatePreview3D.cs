using Godot;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Character;
using MartialHeroes.Assets.Parsers.Mesh;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Assets.Parsers.Terrain;
using MartialHeroes.Assets.Parsers.Terrain.Models;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Godot.World;
using MartialHeroes.Client.Presentation.Helpers;
using MartialHeroes.Client.Presentation.Screens;
using Environment = Godot.Environment;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

public sealed partial class CharCreatePreview3D : Control
{
    private const int BackdropAreaId = 0;
    private const int BackdropMapX = 10000;
    private const int BackdropMapZ = 9990;

    private const float RowPivotLegacyX = 508.48f;
    private const float RowPivotLegacyY = 69.89f;
    private const float RowPivotLegacyZ = -9758.57f;


    private const float CreateActorLegacyZ = -9682.0f;

    private const float CreateActorGodotX = 511.5f;

    private const float CreateSubjectCentreRiseFallback = 17.0f;

    private const float CameraFov = 50.0f;
    private const float CameraNear = 5.0f;
    private const float CameraFar = 15000.0f;


    private const float LineupLegacyScale = 70.0f;
    private const float CreateLegacyScale = 81.0f;

    private const float
        LineupGodotScale = 6.0f;

    private const float CreatePreviewScale = LineupGodotScale * (CreateLegacyScale / LineupLegacyScale);

    private const float TurntableRadPerSec = 2.0f;

    private const float AmbientFloorEnergy = 1.0f;

    private const float AmbientFloorEnergyGodot = 0.65f;

    private const float SkyHazeArea0Kf29 = 0.004303f;

    private const float DirectionalEnergy = 0.047f;

    private const int ReferenceCanvasHeight = 768;
    private const int FallbackViewportHeight = ReferenceCanvasHeight;
    private const int FallbackViewportWidth = ReferenceCanvasHeight * 3 / 4;

    private const int FaceMinIndex = 1;
    private const int FaceMaxIndex = 7;

    private const string SkinTxtTablePath = "data/char/skin.txt";

    private const int AppearanceSeedCount = 6;


    private static readonly Vector3 CameraEyeGodot = ToGodotVec(512.0f, 87.0f, -9652.0f);
    private static readonly Vector3 RowPivotGodot = ToGodotVec(RowPivotLegacyX, RowPivotLegacyY, RowPivotLegacyZ);

    private static readonly Vector3 CameraLookAtGodot = RowPivotGodot;

    private static readonly Color BackgroundColorAchromatic =
        new(SkyHazeArea0Kf29, SkyHazeArea0Kf29, SkyHazeArea0Kf29);

    private static readonly Color DirectionalColor = new(1.0f, 1.0f, 1.0f);

    private static readonly Vector3 DirectionalDirGodot = ToGodotVec(-7.0f, 7.0f, 20.0f).Normalized();
    private readonly int[] _appearanceSeeds = new int[AppearanceSeedCount];
    private Node3D? _actorWrapper;
    private TerrainNode? _backdropTerrain;
    private bool _builtOnce;
    private Camera3D? _camera;
    private float _rowGroundY = RowPivotLegacyY;
    private SubViewport? _subViewport;


    private float _turntableYRot;


    public int InternalClassId { get; set; } = 1;

    public int FaceIndex { get; private set; } = FaceMinIndex;

    public RealClientAssets? SharedRealAssets { get; set; }


    private static string? SknPathForClass(RealClientAssets assets, int internalClass)
    {
        var bodyModelClassId = ClassAppearanceResolver.StarterBodyModelClassId(internalClass);
        if (bodyModelClassId <= 0) return null;

        if (!assets.Contains(SkinTxtTablePath))
        {
            GD.PrintErr(
                $"[CharCreatePreview3D] DATA GAP — '{SkinTxtTablePath}' absent; cannot resolve body for class={internalClass}. spec: skinning.md §3.5.3.");
            return null;
        }

        int? meshGid;
        try
        {
            meshGid = SkinTxtParser.Parse(assets.GetRaw(SkinTxtTablePath)).GetBodyMeshGid(bodyModelClassId);
        }
        catch (Exception ex)
        {
            GD.PrintErr(
                $"[CharCreatePreview3D] skin.txt body resolve failed (class={internalClass}, model_class_id={bodyModelClassId}): {ex.Message}");
            return null;
        }

        if (meshGid is null)
        {
            GD.PrintErr(
                $"[CharCreatePreview3D] DATA GAP — no skin.txt body row for (slot=3, model_class_id={bodyModelClassId}) class={internalClass} — skipped (no wrong-class fallback). spec: §3.7.5 / skinning.md §3.5.3.");
            return null;
        }

        return ClassAppearanceResolver.BodySknPathForMeshGid(meshGid.Value);
    }


    public override void _Ready()
    {
        CallDeferred(MethodName.DeferredBuild);
    }

    public override void _ExitTree()
    {
        _actorWrapper = null;
        _subViewport = null;
        _camera = null;
        _backdropTerrain = null;
    }


    public void RebuildForClass()
    {
        if (!_builtOnce) return;
        BuildActorInWrapper();
    }

    public int UpdateFaceIndex(int faceIndex)
    {
        var clamped = Math.Clamp(faceIndex, FaceMinIndex, FaceMaxIndex);
        if (clamped == FaceIndex) return clamped;

        FaceIndex = clamped;
        GD.Print($"[CharCreatePreview3D] face index recorded = {FaceIndex} (range {FaceMinIndex}..{FaceMaxIndex}); " +
                 "NO 3D actor rebuild (faithful). spec: frontend_scenes.md §4.2.");
        return FaceIndex;
    }

    public int UpdateAppearanceSeed(int seedIndex, int delta)
    {
        if (seedIndex < 0 || seedIndex >= AppearanceSeedCount) return 0;
        var next = Math.Max(0, _appearanceSeeds[seedIndex] + delta);
        _appearanceSeeds[seedIndex] = next;
        GD.Print($"[CharCreatePreview3D] appearance seed[{seedIndex}] recorded = {next} " +
                 "(2D-only; NO 3D rebuild). spec: frontend_scenes.md §4.2.");
        return next;
    }

    public void RotateLeft(float deltaSeconds)
    {
        _turntableYRot -= TurntableRadPerSec * deltaSeconds;
        ApplyTurntableRotation();
    }

    public void RotateRight(float deltaSeconds)
    {
        _turntableYRot += TurntableRadPerSec * deltaSeconds;
        ApplyTurntableRotation();
    }


    private void DeferredBuild()
    {
        if (!IsInstanceValid(this)) return;
        try
        {
            BuildViewport();
            _builtOnce = true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] DeferredBuild failed: {ex.Message}");
        }
    }

    private void BuildViewport()
    {
        var vpW = Size.X > 4 ? (int)Size.X : FallbackViewportWidth;
        var vpH = Size.Y > 4 ? (int)Size.Y : FallbackViewportHeight;

        _subViewport = new SubViewport
        {
            Name = "CreatePreviewVP",
            Size = new Vector2I(vpW, vpH),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false,
            OwnWorld3D = true
        };

        var container = new SubViewportContainer
        {
            Name = "CreatePreviewContainer",
            Stretch = true,
            MouseFilter = MouseFilterEnum.Ignore
        };
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        container.AddChild(_subViewport);
        AddChild(container);

        BuildEnvironment();
        BuildLighting();
        BuildCamera();

        var assets = SharedRealAssets;
        var ownsAssets = false;
        if (assets is null)
            try
            {
                assets = RealClientAssets.TryOpen();
                ownsAssets = assets is not null;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[CharCreatePreview3D] VFS open failed: {ex.Message}");
            }

        if (assets is not null)
        {
            BuildBackdropTerrain(assets);
            BuildBackdropProps(assets);
        }
        else
        {
            GD.Print("[CharCreatePreview3D] VFS offline — no carved-wall backdrop; actor skipped.");
        }

        _actorWrapper = new Node3D { Name = "ActorWrapper" };
        _subViewport.AddChild(_actorWrapper);
        BuildActorInWrapper();

        if (ownsAssets) assets?.Dispose();

        GD.Print($"[CharCreatePreview3D] Viewport {vpW}×{vpH} built for class={InternalClassId} " +
                 "(cell d000x10000z9990 + held-KF1 camera + area-0 env). spec: §3.7.6 / §3.5.4 / §4.2.");
    }


    private void BuildEnvironment()
    {
        if (_subViewport is null) return;

        var env = new Environment
        {
            BackgroundMode = Environment.BGMode.Color,
            BackgroundColor = BackgroundColorAchromatic,
            AmbientLightSource = Environment.AmbientSource.Color,
            AmbientLightColor = new Color(1.0f, 1.0f, 1.0f),
            AmbientLightSkyContribution = 0.0f,
            AmbientLightEnergy = AmbientFloorEnergyGodot,
            TonemapMode = Environment.ToneMapper.Linear,
            TonemapExposure = 1.0f,
            FogEnabled = false
        };

        var worldEnv = new WorldEnvironment { Environment = env };
        _subViewport.AddChild(worldEnv);

        GD.Print(
            $"[CharCreatePreview3D] Area-0 environment: achromatic dark BG + WHITE (1,1,1) ambient floor " +
            $"(OPTION_BRIGHT/100=1.0 white, Godot parity energy {AmbientFloorEnergyGodot}, sky-contrib 0) + fog OFF. " +
            "NO warm tint. NO procedural sky. spec: rendering.md §9.3/§3.6.1 (white (1,1,1) CODE-CONFIRMED).");
    }


    private void BuildLighting()
    {
        if (_subViewport is null) return;

        var sun = new DirectionalLight3D
        {
            Name = "Area0Directional",
            LightEnergy = DirectionalEnergy,
            LightColor = DirectionalColor,
            ShadowEnabled = false
        };
        _subViewport.AddChild(sun);

        var pivot = ToGodotVec(RowPivotLegacyX, 200.0f, RowPivotLegacyZ);
        sun.LookAtFromPosition(pivot, pivot + DirectionalDirGodot, Vector3.Up);

        GD.Print("[CharCreatePreview3D] Lighting: faint achromatic directional (0.047) ONLY; " +
                 "NO point-lights. spec: §3.6.1 / §3.6.6.");
    }


    private void BuildCamera()
    {
        if (_subViewport is null) return;

        _camera = new Camera3D
        {
            Name = "CreatePreviewCam",
            Fov = CameraFov,
            Near = CameraNear,
            Far = CameraFar,
            KeepAspect = Camera3D.KeepAspectEnum.Height
        };
        _subViewport.AddChild(_camera);

        var createSubject =
            ToGodotVec(RowPivotLegacyX, RowPivotLegacyY + CreateSubjectCentreRiseFallback, CreateActorLegacyZ);

        _camera.Position = CameraEyeGodot;
        _camera.LookAt(createSubject, Vector3.Up);

        GD.Print(
            $"[CharCreatePreview3D] Held-KF1 camera (NO boom — A5 actor-only): eye={_camera.GlobalPosition} " +
            $"placeholder look-at={createSubject}; FOV {CameraFov}/near {CameraNear}/far {CameraFar}. " +
            "Re-aimed at the figure's measured vertical centre once built. spec: §4.2 (actor-only; full-figure).");
    }


    private void BuildBackdropTerrain(RealClientAssets assets)
    {
        if (_subViewport is null) return;

        var tag = AreaTag(BackdropAreaId);
        var tedPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.ted";
        if (!assets.Contains(tedPath))
        {
            GD.Print($"[CharCreatePreview3D] Backdrop .ted absent: {tedPath} — terrain skipped.");
            return;
        }

        try
        {
            var tedData = assets.GetRaw(tedPath);
            if (tedData.IsEmpty)
            {
                GD.Print($"[CharCreatePreview3D] Backdrop .ted empty: {tedPath} — terrain skipped.");
                return;
            }

            var terrainNode = new TerrainNode
            {
                Name = "BackdropTerrain",
                TextureResolver = BuildTerrainTextureResolver(assets)
            };
            _subViewport.AddChild(terrainNode);
            _backdropTerrain = terrainNode;

            terrainNode.OnSectorLoaded(new SectorLoadedEvent(
                BackdropMapX, BackdropMapZ, tedData));

            _rowGroundY = RowPivotLegacyY;
            if (terrainNode.TryGetGroundHeight(RowPivotLegacyX, RowPivotLegacyZ, out var sampledY, RowPivotLegacyY))
                GD.Print(
                    $"[CharCreatePreview3D] Terrain sampler at pivot = {sampledY:F3} (soil floor); placing actor on platform Y={_rowGroundY:F2}. spec: §3.6.5.");

            GD.Print(
                $"[CharCreatePreview3D] Backdrop terrain cell ({BackdropMapX},{BackdropMapZ}) loaded. spec: §3.7.1.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] Backdrop terrain failed: {ex.Message}");
        }
    }


    private void BuildBackdropProps(RealClientAssets assets)
    {
        if (_subViewport is null) return;

        var tag = AreaTag(BackdropAreaId);
        var budPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.bud";
        if (!assets.Contains(budPath))
        {
            GD.Print($"[CharCreatePreview3D] Backdrop .bud absent: {budPath} — carved wall skipped.");
            return;
        }

        try
        {
            var scene = assets.LoadBud(budPath);
            if (scene is null || scene.Objects.Length == 0)
            {
                GD.Print("[CharCreatePreview3D] Backdrop .bud empty — no carved-wall props.");
                return;
            }

            var bgPool = TryLoadBgPool(assets);
            var cellMap = TryLoadCellMap(assets);

            Func<uint, ImageTexture?> budTexResolver = budIdx =>
                ResolveTexture(assets, bgPool, cellMap, "BUILDING", (int)budIdx);

            var propsRoot = BudMeshBuilder.Build(scene, budTexResolver);
            propsRoot.Name = "BackdropProps";
            _subViewport.AddChild(propsRoot);

            var culled = CullForegroundOccluders(propsRoot, ToGodotVec(0f, 0f, CreateActorLegacyZ).Z);

            GD.Print(
                $"[CharCreatePreview3D] Carved-wall props built ({scene.Objects.Length} objects, " +
                $"{culled} foreground occluder mesh(es) culled). spec: §3.7.6 / §3.7.3.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] Backdrop props failed: {ex.Message}");
        }
    }

    private static int CullForegroundOccluders(Node3D propsRoot, float subjectGodotZ)
    {
        var culled = 0;
        var stack = new Stack<Node>();
        stack.Push(propsRoot);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is MeshInstance3D mi && mi.Mesh is not null)
            {
                var local = mi.Mesh.GetAabb();
                var global = mi.GlobalTransform * local;
                var maxZ = global.Position.Z + global.Size.Z;
                if (maxZ < subjectGodotZ)
                {
                    mi.Visible = false;
                    culled++;
                }
            }

            foreach (var child in node.GetChildren())
                stack.Push(child);
        }

        return culled;
    }


    private void BuildActorInWrapper()
    {
        if (_actorWrapper is null) return;

        foreach (var child in _actorWrapper.GetChildren())
            child.QueueFree();

        var actorZ = ToGodotVec(0f, 0f, CreateActorLegacyZ).Z;
        _actorWrapper.Position = new Vector3(CreateActorGodotX, _rowGroundY, actorZ);
        _actorWrapper.Scale = Vector3.One * CreatePreviewScale;
        ApplyTurntableRotation();

        var assets = SharedRealAssets;
        var ownsAssets = false;
        if (assets is null)
            try
            {
                assets = RealClientAssets.TryOpen();
                ownsAssets = assets is not null;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[CharCreatePreview3D] VFS open failed: {ex.Message}");
            }

        if (assets is null)
        {
            GD.Print("[CharCreatePreview3D] VFS offline — no create actor (no synthetic placeholder).");
            return;
        }

        try
        {
            var actor = TryBuildActorForClass(assets, InternalClassId);
            if (actor is not null)
            {
                _actorWrapper.AddChild(actor);

                AimCameraAtActorCentre(actor);

                GD.Print($"[CharCreatePreview3D] Create actor (class={InternalClassId}) at Godot " +
                         $"({CreateActorGodotX:F1}, {_rowGroundY:F2}, {actorZ:F1}) (world Z≈{CreateActorLegacyZ}, ~56u nearer camera), " +
                         $"scale {CreatePreviewScale:F3} (legacy {CreateLegacyScale}). CLOSE-UP: one big character. spec: §4.2 / §3.5.4.");
            }
            else
            {
                GD.Print(
                    $"[CharCreatePreview3D] No create actor built for class={InternalClassId} (asset absent — skipped).");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] BuildActorInWrapper failed: {ex.Message}");
        }
        finally
        {
            if (ownsAssets) assets.Dispose();
        }
    }

    private static Node3D? TryBuildActorForClass(RealClientAssets assets, int internalClass)
    {
        var sknPath = SknPathForClass(assets, internalClass);
        if (sknPath is null)
        {
            GD.PrintErr(
                $"[CharCreatePreview3D] No create-preview body .skn for class={internalClass} — skipped (no wrong-class fallback, no fabrication). spec: §3.7.5 / skinning.md §3.5.3 / §4.2.");
            return null;
        }

        if (!assets.Contains(sknPath))
        {
            GD.PrintErr($"[CharCreatePreview3D] .skn absent: {sknPath} — skipped.");
            return null;
        }

        SkinnedMesh mesh;
        try
        {
            var raw = assets.GetRaw(sknPath);
            if (raw.IsEmpty) return null;
            mesh = SknParser.Parse(raw);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] SknParser failed '{sknPath}': {ex.Message}");
            return null;
        }

        var registry = CharVisualRegistry.GetOrBuild(assets);
        var appearanceKey = ClassAppearanceResolver.StarterBodyModelClassId(internalClass);
        var poolKey = ClassAppearanceResolver.SkeletonIdBForModelClassId(appearanceKey);
        if (poolKey == 0) poolKey = (int)mesh.IdB;

        var skeleton = TryLoadSkeletonForIdB(registry, poolKey);
        var idleClip = TryLoadIdleClipForIdB(assets, registry, appearanceKey, (int)mesh.IdB);

        ImageTexture? albedo = null;
        try
        {
            albedo = CharacterTextureResolver.Resolve(assets, mesh.IdA);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] texture resolve failed: {ex.Message}");
        }

        try
        {
            return SkinnedCharacterBuilder.Build(
                mesh, skeleton, idleClip, albedo,
                false, 0f, out _,
                $"create_preview_class{internalClass}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] SkinnedCharacterBuilder failed: {ex.Message}");
            return null;
        }
    }

    private static Skeleton? TryLoadSkeletonForIdB(CharVisualRegistry? registry, int idB)
    {
        if (idB <= 0 || registry is null) return null;
        var skeleton = registry.TryGetSkeletonByIdB(idB);
        if (skeleton is null)
            GD.PrintErr(
                $"[CharCreatePreview3D] no .bnd registered with parsed actor_id={idB} in bindlist.txt — rest pose. " +
                "spec: skinning.md §8(e) / formats/bindlist.md.");
        return skeleton;
    }

    private static AnimationClip? TryLoadIdleClipForIdB(
        RealClientAssets assets, CharVisualRegistry? registry, int appearanceKey, int meshIdB)
    {
        if (registry is null) return null;

        try
        {
            var entry = registry.ActorMotion.GetBySkinClass(meshIdB);
            if (entry is null) return null;

            var idle = entry.IdleMotionId;
            if (idle <= 0) return null;

            var motPath = registry.ResolveMotPath(idle);
            if (motPath is null || !assets.Contains(motPath)) return null;

            var motData = assets.GetRaw(motPath);
            return motData.IsEmpty ? null : AnimationParser.Parse(motData);
        }
        catch (Exception ex)
        {
            GD.PrintErr(
                $"[CharCreatePreview3D] TryLoadIdleClipForIdB(appearanceKey={appearanceKey}) failed: {ex.Message}");
        }

        return null;
    }

    private void AimCameraAtActorCentre(Node3D actor)
    {
        if (_camera is null || !IsInstanceValid(_camera)) return;

        if (!TryGetCombinedGlobalAabb(actor, out var aabb) || aabb.Size == Vector3.Zero)
            return;

        var centreY = aabb.Position.Y + aabb.Size.Y * 0.5f;
        var actorZ = ToGodotVec(0f, 0f, CreateActorLegacyZ).Z;
        var aim = new Vector3(CreateActorGodotX, centreY, actorZ);
        _camera.LookAt(aim, Vector3.Up);

        GD.Print($"[CharCreatePreview3D] Camera re-aimed at figure centre Y={centreY:F2} " +
                 $"(AABB minY={aabb.Position.Y:F2} h={aabb.Size.Y:F2}); full figure framed. spec: §4.2.");
    }

    private static bool TryGetCombinedGlobalAabb(Node root, out Aabb combined)
    {
        combined = default;
        var any = false;
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is MeshInstance3D mi && mi.Mesh is not null)
            {
                var g = mi.GlobalTransform * mi.Mesh.GetAabb();
                combined = any ? combined.Merge(g) : g;
                any = true;
            }

            foreach (var child in node.GetChildren())
                stack.Push(child);
        }

        return any;
    }

    private void ApplyTurntableRotation()
    {
        if (_actorWrapper is null || !IsInstanceValid(_actorWrapper)) return;
        _actorWrapper.RotationDegrees = new Vector3(0f, Mathf.RadToDeg(_turntableYRot), 0f);
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

            GD.PrintErr($"[CharCreatePreview3D] bgtexture.lst absent ({lstPath}) — terrain/buildings stay untextured.");
            return null;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] bgtexture.lst load failed: {ex.Message}");
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
            GD.PrintErr($"[CharCreatePreview3D] backdrop .map load failed: {ex.Message}");
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
}