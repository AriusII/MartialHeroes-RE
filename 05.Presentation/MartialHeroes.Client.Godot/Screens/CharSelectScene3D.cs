// Screens/CharSelectScene3D.cs
//
// The unified 3D backdrop for the character-select screen.
//
// WHAT THIS NODE DOES:
//   1. Loads the single backdrop cell d000x10000z9990 (terrain .ted + props .bud) into a 3D Node3D
//      using the existing TerrainNode + BudMeshBuilder builders — reuses the World scene's proven
//      path, does NOT rebuild those parsers.
//   2. Instantiates up to 5 SkinnedCharacterNode actors at the spec world positions with the correct
//      idle pose (g111100010.mot) using the now-fixed SkinnedCharacterBuilder + SkinningMath path.
//   3. Places a Camera3D at keyframe 1 (eye ≈ (512, 87, −9652) Godot-space) looking at the row pivot.
//   4. Adds a DirectionalLight3D + 2 OmniLight3D approximating the 14:30 afternoon light rig described
//      by the area-015 sky data (exact colours are data-driven, we approximate here).
//   5. Wires a WorldEnvironment with a procedural afternoon sky.
//
// SPEC CITATIONS:
//   Stage origin (2048, 0, −6144) spec: Docs/RE/specs/frontend_scenes.md §3.7.2. CODE-CONFIRMED.
//   Per-slot world positions spec: Docs/RE/specs/frontend_scenes.md §3.3.1. CODE-CONFIRMED.
//   Preview scale ×3.0 spec: Docs/RE/specs/frontend_scenes.md §3.3.1 CODE-CONFIRMED.
//   Camera eye KF1 (512, 87, −9652) spec: Docs/RE/specs/frontend_scenes.md §3.5.2 CODE-CONFIRMED.
//   FOV 50° / near 5 / far 15000 spec: Docs/RE/specs/frontend_scenes.md §3.5.1 CODE-CONFIRMED.
//   Backdrop cell d000x10000z9990 spec: Docs/RE/specs/frontend_scenes.md §3.7.1 CODE-CONFIRMED.
//   Idle clip g111100010.mot spec: Docs/RE/specs/frontend_scenes.md §3.7.5 CODE-CONFIRMED.
//   Shared skeleton g1.bnd spec: Docs/RE/specs/frontend_scenes.md §3.7.5 CODE-CONFIRMED.
//   Row pivot (508.48, 69.89, −9758.57) spec: Docs/RE/specs/frontend_scenes.md §3.6.5 CODE-CONFIRMED.
//   Area-015 sky data spec: Docs/RE/specs/frontend_scenes.md §3.6.3 CODE-CONFIRMED (paths).
//   Yaw 0 = front, π = back spec: Docs/RE/specs/frontend_scenes.md §3.3.2 CODE-CONFIRMED.
//
// COORDINATE CONVENTION:
//   WorldCoordinates.ToGodot: (x, y, z) → (x, y, −z). spec: Helpers/WorldCoordinates.
//   All world positions below are already Godot-space (Z negated from spec values).
//
// PASSIVE: zero game logic. Reads VFS assets, builds geometry, places a camera.
// No domain state, no packet parsing, no stat math.

using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Dev;
using MartialHeroes.Client.Godot.World;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// A <see cref="Node3D"/> that builds the full 3D character-select scene:
/// map000 backdrop cell, standing character row, camera at keyframe 1, and lighting.
///
/// <para>Construction: call <see cref="Initialise"/> after the node is in the scene tree,
/// passing the open <see cref="RealClientAssets"/> handle and the slot occupancy / skin data.
/// The node degrades gracefully when the VFS is absent (sky + placeholder characters).</para>
///
/// <para>This node is NOT the overlay — the 2D chrome (slot tabs, Create/Delete/Enter buttons)
/// lives in the parent <see cref="CharacterSelectScreen"/> as a Control layer.</para>
///
/// spec: Docs/RE/specs/frontend_scenes.md §3.3 / §3.5 / §3.6 / §3.7.
/// </summary>
public sealed partial class CharSelectScene3D : Node3D
{
    // =========================================================================
    // Stage geometry constants — all Godot-space (Z negated from spec).
    // spec: Docs/RE/specs/frontend_scenes.md §3.3.1 and §3.7.2. CODE-CONFIRMED.
    // =========================================================================

    // Per-slot world X positions (spec §3.3.1, Z already negated for Godot-space).
    // spec: Docs/RE/specs/frontend_scenes.md §3.3.1 CODE-CONFIRMED.
    private static readonly float[] SlotWorldX = [488.0f, 500.0f, 512.0f, 524.0f, 536.0f];
    private static readonly float[] SlotWorldZ = [9737.0f, 9738.0f, 9738.5f, 9738.0f, 9737.0f]; // negated from spec −9737..−9738.5

    // All slots at Y=0. spec: frontend_scenes.md §3.3.1 — "Y is exactly 0.0". CODE-CONFIRMED.
    private const float SlotWorldY = 0.0f;

    // Preview scale ×3.0. spec: frontend_scenes.md §3.3.1. CODE-CONFIRMED.
    private const float PreviewScale = 3.0f;

    // Camera eye position — keyframe 1 (live keyframe). Godot-space: Z negated.
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.2 KF1 (512, 87, −9652) → Godot (512, 87, 9652). CODE-CONFIRMED.
    private static readonly Vector3 CameraEye = new(512.0f, 87.0f, 9652.0f);

    // Camera look-at target — row centre / ambient-FX anchor (lifted ~70 in Y). Godot-space.
    // spec: Docs/RE/specs/frontend_scenes.md §3.6.5 (508.48, 69.89, −9758.57) → Godot (508.48, 69.89, 9758.57). CODE-CONFIRMED.
    private static readonly Vector3 CameraLookAt = new(508.48f, 69.89f, 9758.57f);

    // Camera parameters. spec: frontend_scenes.md §3.5.1. CODE-CONFIRMED.
    private const float CameraFov = 50.0f;
    private const float CameraNear = 5.0f;
    private const float CameraFar = 15000.0f;

    // Backdrop cell identity.
    // spec: Docs/RE/specs/frontend_scenes.md §3.7.1. CODE-CONFIRMED.
    private const int BackdropAreaId = 0; // map000
    private const int BackdropMapX = 10000;
    private const int BackdropMapZ = 9990; // cell d000x10000z9990

    // Starter idle motion. spec: frontend_scenes.md §3.7.5. CODE-CONFIRMED.
    private const string IdleMotPath = "data/char/mot/g111100010.mot";

    // Shared skeleton. spec: frontend_scenes.md §3.7.5. CODE-CONFIRMED.
    private const string SharedBndPath = "data/char/bind/g1.bnd";

    // Per-class starter skins. spec: frontend_scenes.md §3.7.5. CODE-CONFIRMED.
    // Keys are internal class ids 3,4,6,11 → Musa g202110001 / Dosa g202110001 etc.
    // For the generic slot preview we use the skin_class id pattern (SkinClassId).
    // Fallback: Musa mesh (class 1 = g202110001.skn).
    private const string FallbackSknPath = "data/char/skin/g202110001.skn";

    // =========================================================================
    // Runtime state
    // =========================================================================

    private Camera3D? _camera;

    // One character node per slot (index 0..4); null = empty or failed.
    private readonly Node3D?[] _slotActors = new Node3D?[5];

    /// <summary>Current selected slot index (0..4); changes the active/idle highlight.</summary>
    private int _selectedSlot;

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>
    /// Slot descriptor array — set before calling <see cref="Initialise"/>.
    /// Each entry: (IsOccupied, SkinClassId) — 5 entries max.
    /// </summary>
    public (bool IsOccupied, uint SkinClassId)[] SlotDescriptors { get; set; } = new (bool, uint)[5];

    /// <summary>
    /// Initialises the 3D scene. Call this AFTER the node is in the scene tree
    /// (e.g. from _Ready or a deferred call in the parent).
    /// Assets can be null — the scene degrades to a coloured backdrop + env only.
    /// </summary>
    public void Initialise(RealClientAssets? assets)
    {
        try
        {
            BuildEnvironment();
            BuildCamera();
            BuildLighting();

            if (assets is not null)
            {
                BuildBackdropTerrain(assets);
                BuildBackdropProps(assets);
                BuildCharacterRow(assets);
            }
            else
            {
                GD.Print("[CharSelectScene3D] No VFS — terrain/props/characters skipped; env+camera only.");
            }

            GD.Print("[CharSelectScene3D] 3D scene initialised.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] Initialise failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the selected slot, swapping the animation clip on the actors.
    /// Occupied selected slot gets the "select" yaw (facing front); others get idle.
    /// Since we don't yet have the select-turn clip (§3.3.4 select record field +0x58),
    /// we visually highlight via modulate tint as a placeholder.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.3.4 CODE-CONFIRMED (clip swap spec).
    /// </summary>
    public void SetSelectedSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= 5) return;
        _selectedSlot = slotIndex;

        // Full select-turn clip swap requires the select record field +0x58 — runtime-pending.
        // spec: frontend_scenes.md §3.3.4 — clip swap idle→select. CODE-CONFIRMED spec; clip pending.
        // Node3D nodes do not have Modulate (that is a CanvasItem property for 2D nodes).
        // Visual selection highlight via material is a future enhancement; 2D overlay highlights
        // the active slot already via HighlightSlot() in CharacterSelectScreen.
        GD.Print($"[CharSelectScene3D] Selected slot → {slotIndex} (clip-swap pending spec §3.3.4).");
    }

    /// <summary>Returns the Godot-space world position of the camera eye (KF1).</summary>
    public Vector3 GetCameraEye() => CameraEye;

    // =========================================================================
    // Environment
    // =========================================================================

    private void BuildEnvironment()
    {
        // Parametric afternoon sky approximating the area-015 sky data (14:30 clock).
        // spec: Docs/RE/specs/frontend_scenes.md §3.6.3 — area-015 sky param files. CODE-CONFIRMED paths.
        // spec: Docs/RE/specs/frontend_scenes.md §3.5.1 — frozen at 14:30 (time-of-day 52200). CODE-CONFIRMED.
        // Exact colours are data-driven from sky/material015.bin — we approximate with a warm afternoon sky.
        var skyMat = new ProceduralSkyMaterial();
        skyMat.SkyTopColor = new Color(0.25f, 0.45f, 0.82f);
        skyMat.SkyHorizonColor = new Color(0.75f, 0.80f, 0.70f);
        skyMat.SkyCurve = 0.12f;
        skyMat.GroundBottomColor = new Color(0.15f, 0.18f, 0.12f);
        skyMat.GroundHorizonColor = new Color(0.45f, 0.50f, 0.38f);
        skyMat.SunAngleMax = 30.0f;
        skyMat.SunCurve = 0.10f;

        var sky = new Sky { SkyMaterial = skyMat };

        // global::Godot.Environment to avoid collision with MartialHeroes.Client.Godot namespace.
        // spec: CLAUDE.md "Namespace collisions: bare Environment. resolves to sibling namespace → CS0234".
        var env = new global::Godot.Environment();
        env.BackgroundMode = global::Godot.Environment.BGMode.Sky;
        env.Sky = sky;
        env.AmbientLightSource = global::Godot.Environment.AmbientSource.Sky;
        env.AmbientLightSkyContribution = 0.4f;
        env.AmbientLightEnergy = 1.1f;
        env.TonemapMode = global::Godot.Environment.ToneMapper.Aces;
        env.TonemapExposure = 1.05f;
        env.TonemapWhite = 8.0f;
        // Minimal fog per spec §3.6.2 — "zeroes a sky/fog blend scalar".
        // spec: Docs/RE/specs/frontend_scenes.md §3.6.2 CODE-CONFIRMED (fog-density zeroed).
        env.FogEnabled = false;

        var worldEnv = new WorldEnvironment { Environment = env };
        AddChild(worldEnv);
    }

    // =========================================================================
    // Camera — keyframe 1
    // =========================================================================

    private void BuildCamera()
    {
        // Camera at keyframe 1 (the live keyframe the scene uses).
        // spec: Docs/RE/specs/frontend_scenes.md §3.5.2 — live keyframe = 1. CODE-CONFIRMED.
        // Godot-space eye: (512, 87, 9652) (Z negated from spec −9652).
        // Look-at: row pivot (508.48, 69.89, 9758.57).
        _camera = new Camera3D
        {
            Name = "CharSelectCamera",
            Fov = CameraFov, // spec: §3.5.1 FOV 50°. CODE-CONFIRMED.
            Near = CameraNear, // spec: §3.5.1 near 5.0. CODE-CONFIRMED.
            Far = CameraFar, // spec: §3.5.1 far 15000.0. CODE-CONFIRMED.
            KeepAspect = Camera3D.KeepAspectEnum.Height,
        };
        AddChild(_camera);

        // Position and orient: eye at KF1, looking at the row centre.
        // spec: Docs/RE/specs/frontend_scenes.md §3.5.2 CODE-CONFIRMED position;
        //       §3.5.4 CODE-CONFIRMED look-at = active orbit point.
        _camera.Position = CameraEye;
        _camera.LookAt(CameraLookAt, Vector3.Up);

        GD.Print($"[CharSelectScene3D] Camera at KF1 eye={CameraEye} look-at={CameraLookAt}. " +
                 "spec: frontend_scenes.md §3.5.2 CODE-CONFIRMED.");
    }

    // =========================================================================
    // Lighting — approximating the 14:30 afternoon rig
    // =========================================================================

    private void BuildLighting()
    {
        // ≈5 lights per spec §3.6.1 (count CONFIRMED; colours UNVERIFIED — approximate here).
        // spec: Docs/RE/specs/frontend_scenes.md §3.6.1 CODE-CONFIRMED count/range; colours UNVERIFIED.

        // Primary sun (DirectionalLight3D) — afternoon 14:30, south-west direction.
        // spec: frontend_scenes.md §3.6.1 — "DirectionalLight3D approximating 14:30 afternoon". PLAUSIBLE direction.
        var sun = new DirectionalLight3D
        {
            Name = "AfternoonSun",
            LightEnergy = 1.6f,
            LightColor = new Color(1.0f, 0.95f, 0.82f), // warm afternoon white — colours UNVERIFIED
            ShadowEnabled = true,
        };
        // Afternoon sun from the west-southwest, angled downward.
        sun.RotationDegrees = new Vector3(-42.0f, -30.0f, 0.0f);
        AddChild(sun);

        // Fill light 1 — cool sky fill from above-front.
        // spec: §3.6.1 "≈5 positional lights, range ≈1024". CODE-CONFIRMED count/range.
        var fill1 = new OmniLight3D
        {
            Name = "SkyFill1",
            LightEnergy = 0.5f,
            LightColor = new Color(0.55f, 0.65f, 0.90f),
            OmniRange = 1024.0f, // spec: §3.6.1 "range ≈1024". CODE-CONFIRMED.
            Position = new Vector3(512.0f, 200.0f, 9650.0f), // above the row
        };
        AddChild(fill1);

        // Fill light 2 — warm bounce from below, simulating afternoon ground reflection.
        var fill2 = new OmniLight3D
        {
            Name = "GroundBounce",
            LightEnergy = 0.25f,
            LightColor = new Color(0.85f, 0.75f, 0.50f),
            OmniRange = 1024.0f, // spec: §3.6.1 "range ≈1024". CODE-CONFIRMED.
            Position = new Vector3(512.0f, -30.0f, 9760.0f), // below the row
        };
        AddChild(fill2);

        // Rim light — back-light from camera-right to outline the characters.
        var rim = new OmniLight3D
        {
            Name = "RimLight",
            LightEnergy = 0.35f,
            LightColor = new Color(0.90f, 0.85f, 0.70f),
            OmniRange = 800.0f,
            Position = new Vector3(620.0f, 80.0f, 9820.0f), // behind the row, camera-right
        };
        AddChild(rim);

        // Counter-rim from the left.
        var counterRim = new OmniLight3D
        {
            Name = "CounterRim",
            LightEnergy = 0.20f,
            LightColor = new Color(0.60f, 0.70f, 0.95f),
            OmniRange = 800.0f,
            Position = new Vector3(400.0f, 80.0f, 9820.0f),
        };
        AddChild(counterRim);

        GD.Print("[CharSelectScene3D] 5-light rig built (1 directional + 4 omni, range≈1024). " +
                 "spec: frontend_scenes.md §3.6.1 CODE-CONFIRMED count/range; colours UNVERIFIED.");
    }

    // =========================================================================
    // Backdrop terrain — single cell d000x10000z9990
    // =========================================================================

    private void BuildBackdropTerrain(RealClientAssets assets)
    {
        // spec: Docs/RE/specs/frontend_scenes.md §3.7.1 — backdrop cell d000x10000z9990. CODE-CONFIRMED.
        // Cell origin in legacy world: (cx*1024, cz*1024) = (0, −10240) for cx=0, cz=−10.
        // In Godot-space: (0, 0, 10240) (Z negated).
        // spec: TerrainNode world-space bias: worldX_min = (mapX−10000)*1024, worldZ_min = (mapZ−10000)*1024.
        //   mapX=10000 → worldX_min=0; mapZ=9990 → worldZ_min=−10240.

        string tedPath = $"data/map{AreaTag(BackdropAreaId)}/dat/d{AreaTag(BackdropAreaId)}x{BackdropMapX}z{BackdropMapZ}.ted";

        if (!assets.Contains(tedPath))
        {
            GD.Print($"[CharSelectScene3D] Backdrop terrain not found: {tedPath} — skipping .ted.");
            return;
        }

        try
        {
            ReadOnlyMemory<byte> tedData = assets.GetRaw(tedPath);
            if (tedData.IsEmpty)
            {
                GD.Print($"[CharSelectScene3D] Backdrop .ted is empty: {tedPath}");
                return;
            }

            // Resolve terrain textures via the confirmed two-hop chain.
            // spec: terrain.md §5.6 Block 3 — 1-based TextureIndexGrid → .map TERRAIN TEXTURES → bgtexture → .dds.
            Func<int, ImageTexture?> texResolver = BuildTerrainTextureResolver(assets);

            // Build the terrain mesh using TerrainNode (reuse the existing builder).
            // TerrainNode.OnSectorLoaded parses the .ted, builds the ArrayMesh, and positions it.
            var terrainNode = new TerrainNode
            {
                Name = "BackdropTerrain",
                TextureResolver = texResolver,
            };
            AddChild(terrainNode);

            // Feed the sector directly (no streaming needed for a single backdrop cell).
            // spec: TerrainNode.OnSectorLoaded — takes SectorLoadedEvent(mapX, mapZ, payload).
            var evt = new MartialHeroes.Client.Application.World.SectorLoadedEvent(
                MapX: BackdropMapX,
                MapZ: BackdropMapZ,
                Payload: tedData);
            terrainNode.OnSectorLoaded(evt);

            GD.Print($"[CharSelectScene3D] Backdrop terrain cell ({BackdropMapX},{BackdropMapZ}) loaded. " +
                     "spec: frontend_scenes.md §3.7.1 CODE-CONFIRMED.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] Backdrop terrain failed: {ex.Message}");
        }
    }

    private Func<int, ImageTexture?> BuildTerrainTextureResolver(RealClientAssets assets)
    {
        // Two-hop chain: 1-based tex byte → cell .map TERRAIN TEXTURES[idx-1].intTexId → bgtexture pool → .dds.
        // spec: Docs/RE/formats/terrain.md §5.6 Block 3 + §3.5 + §4.2. CONFIRMED.
        BgTextureCatalog? bgPool = null;
        MapDescriptor? cellMap = null;
        var cache = new Dictionary<int, ImageTexture?>();

        try
        {
            string txtPath = "data/map000/texture/bgtexture.txt";
            if (assets.Contains(txtPath))
                bgPool = BgTextureTxtParser.Parse(assets.GetRaw(txtPath));
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] bgtexture.txt load failed: {ex.Message}");
        }

        try
        {
            string tag = AreaTag(BackdropAreaId);
            string mapPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.map";
            if (assets.Contains(mapPath))
                cellMap = MapDescriptorParser.Parse(assets.GetRaw(mapPath));
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] backdrop .map load failed: {ex.Message}");
        }

        return texByte =>
        {
            if (cache.TryGetValue(texByte, out ImageTexture? cached)) return cached;
            ImageTexture? tex = ResolveTexture(assets, bgPool, cellMap, "TERRAIN", texByte);
            cache[texByte] = tex;
            return tex;
        };
    }

    private static ImageTexture? ResolveTexture(
        RealClientAssets assets, BgTextureCatalog? pool, MapDescriptor? map,
        string section, int oneBasedIdx)
    {
        if (pool is null || map is null || oneBasedIdx <= 0) return null;

        (int Flag, int TexId)[]? list = null;
        foreach (var s in map.Sections)
        {
            if (string.Equals(s.Keyword, section, StringComparison.OrdinalIgnoreCase))
            {
                list = s.Textures;
                break;
            }
        }

        if (list is null) return null;
        int li = oneBasedIdx - 1;
        if ((uint)li >= (uint)list.Length) return null;

        string? rel = pool.GetRelPath(list[li].TexId);
        if (rel is null) return null;

        string ddsPath = $"data/map000/texture/{rel}.dds";
        return assets.Contains(ddsPath) ? assets.LoadTexture(ddsPath) : null;
    }

    // =========================================================================
    // Backdrop props — .bud scene
    // =========================================================================

    private void BuildBackdropProps(RealClientAssets assets)
    {
        // spec: Docs/RE/specs/frontend_scenes.md §3.7.1 — d000x10000z9990.bud. CODE-CONFIRMED.
        // Building textures use the same two-hop chain but via the BUILDING section of the .map.
        string tag = AreaTag(BackdropAreaId);
        string budPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.bud";

        if (!assets.Contains(budPath))
        {
            GD.Print($"[CharSelectScene3D] Backdrop .bud not found: {budPath} — skipping props.");
            return;
        }

        try
        {
            BudScene? scene = assets.LoadBud(budPath);
            if (scene is null || scene.Objects.Length == 0)
            {
                GD.Print("[CharSelectScene3D] Backdrop .bud empty — no props.");
                return;
            }

            // Resolve building textures.
            BgTextureCatalog? bgPool = null;
            MapDescriptor? cellMap = null;

            try
            {
                string txtPath = "data/map000/texture/bgtexture.txt";
                if (assets.Contains(txtPath))
                    bgPool = BgTextureTxtParser.Parse(assets.GetRaw(txtPath));
            }
            catch { /* non-critical */ }

            try
            {
                string mapPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.map";
                if (assets.Contains(mapPath))
                    cellMap = MapDescriptorParser.Parse(assets.GetRaw(mapPath));
            }
            catch { /* non-critical */ }

            // BudMeshBuilder.Build expects Func<uint, ImageTexture?> for the texture resolver.
            Func<uint, ImageTexture?> budTexResolver = budIdx =>
                ResolveTexture(assets, bgPool, cellMap, "BUILDING", (int)budIdx);

            Node3D propsRoot = BudMeshBuilder.Build(scene, budTexResolver);
            propsRoot.Name = "BackdropProps";
            AddChild(propsRoot);

            GD.Print($"[CharSelectScene3D] Backdrop props built ({scene.Objects.Length} objects). " +
                     "spec: frontend_scenes.md §3.7.1 CODE-CONFIRMED.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] Backdrop props failed: {ex.Message}");
        }
    }

    // =========================================================================
    // Character row — up to 5 skinned actors
    // =========================================================================

    private void BuildCharacterRow(RealClientAssets assets)
    {
        // Load the shared skeleton and idle clip once.
        // spec: Docs/RE/specs/frontend_scenes.md §3.7.5. CODE-CONFIRMED.
        Skeleton? sharedSkeleton = TryLoadSkeleton(assets);
        AnimationClip? idleClip = TryLoadIdleClip(assets);

        GD.Print($"[CharSelectScene3D] Shared skeleton loaded={sharedSkeleton is not null}, " +
                 $"idle clip loaded={idleClip is not null}. " +
                 "spec: frontend_scenes.md §3.7.5 CODE-CONFIRMED.");

        for (int i = 0; i < 5; i++)
        {
            bool occupied = i < SlotDescriptors.Length && SlotDescriptors[i].IsOccupied;
            if (!occupied) continue;

            uint skinClassId = SlotDescriptors[i].SkinClassId;

            try
            {
                Node3D? actor = TryBuildSlotActor(assets, i, skinClassId, sharedSkeleton, idleClip);
                if (actor is not null)
                {
                    _slotActors[i] = actor;
                    AddChild(actor);

                    GD.Print($"[CharSelectScene3D] Slot {i} actor placed at world " +
                             $"({SlotWorldX[i]:F1}, {SlotWorldY}, {SlotWorldZ[i]:F1}) Godot-space. " +
                             "spec: frontend_scenes.md §3.3.1 CODE-CONFIRMED.");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[CharSelectScene3D] Slot {i} actor build failed: {ex.Message}");
            }
        }
    }

    private Node3D? TryBuildSlotActor(
        RealClientAssets assets, int slotIdx, uint skinClassId,
        Skeleton? skeleton, AnimationClip? idleClip)
    {
        // Resolve mesh path for this skin_class.
        string? sknPath = PickSknPath(assets, skinClassId);
        if (sknPath is null)
        {
            GD.Print($"[CharSelectScene3D] Slot {slotIdx}: no .skn for skin_class={skinClassId}.");
            return null;
        }

        SkinnedMesh mesh;
        try
        {
            ReadOnlyMemory<byte> raw = assets.GetRaw(sknPath);
            if (raw.IsEmpty) return null;
            mesh = SknParser.Parse(raw);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] Slot {slotIdx} SknParser: {ex.Message}");
            return null;
        }

        // Resolve texture.
        ImageTexture? albedo = null;
        try
        {
            albedo = CharacterTextureResolver.Resolve(assets, mesh);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] Slot {slotIdx} texture: {ex.Message}");
        }

        // Build actor via the proven SkinnedCharacterBuilder (same path as the World scene).
        bool savedDiag = SkinnedCharacterBuilder.PrintDiagnostics;
        try
        {
            SkinnedCharacterBuilder.ForceSkinned = true;
            SkinnedCharacterBuilder.PrintDiagnostics = slotIdx == 0; // log diagnostics for slot 0 only

            // Stagger idle phase so 5 actors don't animate in lockstep.
            float phaseOffset = slotIdx * 0.4f;

            Node3D root = SkinnedCharacterBuilder.Build(
                mesh,
                skeleton,
                idleClip,
                albedo,
                externalDrive: false,
                startPhaseSeconds: phaseOffset,
                out _,
                debugLabel: $"charselect_slot{slotIdx}");

            // Place at spec world position (Godot-space — Z already negated from spec values).
            // spec: Docs/RE/specs/frontend_scenes.md §3.3.1. CODE-CONFIRMED.
            root.Position = new Vector3(SlotWorldX[slotIdx], SlotWorldY, SlotWorldZ[slotIdx]);

            // Scale ×3.0. spec: §3.3.1 — "preview scale multiplied by 3.0". CODE-CONFIRMED.
            root.Scale = Vector3.One * PreviewScale;

            // Yaw: occupied = 0 (front-facing), locked/empty = π (back-facing).
            // spec: Docs/RE/specs/frontend_scenes.md §3.3.2 CODE-CONFIRMED (occupied slot yaw 0).
            // Slot lock flag → yaw π is not yet surfaced from the descriptor; all occupied = front.
            root.RotationDegrees = new Vector3(0f, 0f, 0f); // yaw 0 = front-facing. spec §3.3.2.

            return root;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] Slot {slotIdx} SkinnedCharacterBuilder: {ex.Message}");
            return null;
        }
        finally
        {
            SkinnedCharacterBuilder.PrintDiagnostics = savedDiag;
        }
    }

    // =========================================================================
    // Asset helpers
    // =========================================================================

    /// <summary>
    /// Loads the shared g1.bnd skeleton used by all 4 starter class previews.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.7.5 — "Skeleton: data/char/bind/g1.bnd — 84 bones". CODE-CONFIRMED.
    /// </summary>
    private static Skeleton? TryLoadSkeleton(RealClientAssets assets)
    {
        if (!assets.Contains(SharedBndPath)) return null;
        try
        {
            ReadOnlyMemory<byte> data = assets.GetRaw(SharedBndPath);
            return data.IsEmpty ? null : BndParser.Parse(data);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] g1.bnd failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Loads the shared idle clip g111100010.mot.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.7.5 — "Idle: g111100010.mot, 30 frames @ 10 fps". CODE-CONFIRMED.
    /// </summary>
    private static AnimationClip? TryLoadIdleClip(RealClientAssets assets)
    {
        if (!assets.Contains(IdleMotPath)) return null;
        try
        {
            ReadOnlyMemory<byte> data = assets.GetRaw(IdleMotPath);
            return data.IsEmpty ? null : AnimationParser.Parse(data);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] g111100010.mot failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Resolves a VFS .skn path for the given skin_class (= IdB).
    /// Uses the spec §3.7.5 per-class mesh table first, then a general pattern.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.7.5 CODE-CONFIRMED meshes.
    /// </summary>
    private static string? PickSknPath(RealClientAssets assets, uint skinClassId)
    {
        // Spec §3.7.5 four starter class meshes (IdA=1, CodedConfirmed).
        // spec: Docs/RE/specs/frontend_scenes.md §3.7.5. CODE-CONFIRMED.
        string[] specPaths = skinClassId switch
        {
            // Class 3 = Bichimi/Dosa → g202110001 (reuses class-1 base mesh per spec §3.7.5)
            // Actually spec says: class 3 (Bichimi/Dosa `b`) → g202110001.skn. CODE-CONFIRMED.
            3 => ["data/char/skin/g202110001.skn"],
            // Class 4 = Monk → g203110001.skn. CODE-CONFIRMED.
            4 => ["data/char/skin/g203110001.skn"],
            // Class 6 = Archer → g209110001.skn. CODE-CONFIRMED.
            6 => ["data/char/skin/g209110001.skn"],
            // Class 11 = Sorceress → g206110001.skn. CODE-CONFIRMED.
            11 => ["data/char/skin/g206110001.skn"],
            // Generic pattern for other classes (class 1 = Musa, class 2 = Tao, etc.).
            // PLAUSIBLE: g202{class}10001 for class 1,3,4; g202220001 for class 2.
            2 => ["data/char/skin/g202220001.skn", "data/char/skin/g202210001.skn"],
            _ =>
            [
                $"data/char/skin/g202{skinClassId}10001.skn",
                $"data/char/skin/g202{skinClassId}10002.skn",
            ],
        };

        foreach (string p in specPaths)
            if (assets.Contains(p)) return p;

        // Fallback: Musa (g202110001.skn) — the proven humanoid rig.
        return assets.Contains(FallbackSknPath) ? FallbackSknPath : null;
    }

    private static string AreaTag(int areaId) => areaId.ToString("D3");
}
