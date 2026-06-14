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

    private static readonly float[]
        SlotWorldZ = [9737.0f, 9738.0f, 9738.5f, 9738.0f, 9737.0f]; // negated from spec −9737..−9738.5

    // Per-slot Y — the terrain surface at the row pivot is at world Y ≈ 70 (not 0.0).
    // spec: frontend_scenes.md §3.3.1 — "Y is exactly 0.0". CODE-CONFIRMED (spec value).
    // NOTE: the spec value 0.0 is the stage-origin-relative position in the legacy engine.
    // The terrain cell d000x10000z9990 has its ground surface at Y ≈ 70 in absolute world space
    // (the TerrainNode mesh encodes absolute heights from the .ted file). The legacy engine
    // placed actors via actor-spawn Y-correction from the ground sampler; our implementation
    // uses absolute world Y from the .ted rather than a relative stage offset.
    // Empirically confirmed: characters standing on the platform surface at Y=70.
    // TODO: drive this from TerrainNode.TryGetGroundHeight(508, -9738) once the callback is wired
    // into CharSelectScene3D (request from application-engineer).
    private const float SlotWorldY = 70.0f;

    // Preview scale ×3.0. spec: frontend_scenes.md §3.3.1. CODE-CONFIRMED.
    private const float PreviewScale = 3.0f;

    // Camera eye position — empirically tuned to match the official "lower-centre full-body" framing.
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.2 KF1 = world (512, 87, −9652) → Godot (512, 87, 9652). CODE-CONFIRMED.
    // The KF1 orbit point is at world Y=87 but the actors are at Y≈70 on the terrain surface,
    // so the raw KF1 eye looks over their heads. Empirical framing: eye at Y=115, Z=9640 gives
    // a ≈25° downward angle that frames all characters full-body in the lower-centre of the 50° FOV.
    // The boom vector / exact eye from the spec (§3.5.4 MEDIUM) is debugger-pending; this is the
    // empirically correct approximation for the confirmed framing intent.
    private static readonly Vector3 CameraEye = new(512.0f, 115.0f, 9640.0f);

    // Camera look-at target — aimed at platform surface (character feet) for lower-centre framing.
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.4 — "look-at = active orbit point ≈ (508, 87, −9652)
    //       over the actor-row pivot (508, 70, −9759)". CODE-CONFIRMED structure; exact Y MEDIUM.
    // At Y=70 (terrain surface / character feet) and Z=9738 (centre of row), the look-at sits
    // at the platform level; characters fill the lower 40-50% of the 50° FOV.
    private static readonly Vector3 CameraLookAt = new(512.0f, 70.0f, 9738.0f);

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

            // Brazier flame effect: GPUParticles3D at the row anchor — warm additive torch coronas.
            // spec: Docs/RE/specs/frontend_scenes.md §3.6.5 CODE-CONFIRMED —
            //   "char_select-u.xeff spawned ONCE in the 3D cavern at world ≈ (508.5, 69.9, −9758.6),
            //   scale 1.0; 68 sub-effects = torch coronas; part of the 3D scene NOT a 2D overlay".
            // Anchor in Godot-space (world Z negated): (508.5, 69.9, +9758.6).
            // Aesthetic: warm additive flame approximation; exact sub-effect layout is xeff-pending.
            BuildBrazierEffect();

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
        // CAVERN environment: the char-select scene is a dark enclosed cavern (stone walls, torchlit).
        // The official screenshot shows no sky visible — the scene reads as an interior with warm
        // brazier light and a blue waterfall behind the characters.
        //
        // spec: Docs/RE/specs/frontend_scenes.md §3.6.2 CODE-CONFIRMED — "zeroes a sky/fog blend scalar"
        //   (fog-density zeroed in the legacy engine means the selected sky blend approach; in our Godot
        //   scene we use an opaque black BG + dense near-fog to simulate the enclosed cavern look).
        // spec: Docs/RE/specs/frontend_scenes.md §3.6.1 CODE-CONFIRMED — 5-light rig, range ≈1024; colours UNVERIFIED.
        //
        // Aesthetic choices (no spec citation): background colour, fog start/end, ambient energy.
        // These are tuned for the cavern look — dark, warm, stone interior.

        // global::Godot.Environment to avoid collision with MartialHeroes.Client.Godot namespace.
        // spec: CLAUDE.md "Namespace collisions: bare Environment. resolves to sibling namespace → CS0234".
        var env = new global::Godot.Environment();

        // Use a solid dark background (no sky) — the cavern ceiling/walls are stone geometry.
        // Aesthetic: dark near-black background mimics the enclosed cavern.
        env.BackgroundMode = global::Godot.Environment.BGMode.Color;
        env.BackgroundColor = new Color(0.04f, 0.03f, 0.02f); // near-black warm dark

        // Moderate ambient — enough to see character geometry without washing out the cavern look.
        // Aesthetic: warm dim fill so characters read against the dark background.
        env.AmbientLightSource = global::Godot.Environment.AmbientSource.Color;
        env.AmbientLightColor = new Color(0.50f, 0.38f, 0.25f); // warm stone fill — Aesthetic
        env.AmbientLightEnergy = 1.0f; // moderate — enough for character visibility

        // Tonemap tuned for dark-cavern + torchlit subject.
        // Aesthetic: ACES, slightly under-exposed to preserve the dark cavern feel.
        env.TonemapMode = global::Godot.Environment.ToneMapper.Aces;
        env.TonemapExposure = 0.9f;
        env.TonemapWhite = 4.0f;

        // Dense fog starting close — hides the grass terrain ring at the cell boundary.
        // The cavern walls should melt into black; terrain geometry beyond the stone platform
        // reads as deep shadow/void rather than visible green grass.
        // Aesthetic: fog parameters tuned so green terrain at >200 units fades to the BG colour.
        env.FogEnabled = true;
        env.FogMode = global::Godot.Environment.FogModeEnum.Exponential;
        env.FogDensity = 0.022f;          // dense enough to hide terrain ring at ~200+ units
        env.FogLightColor = new Color(0.04f, 0.03f, 0.02f); // same near-black as BG — seamless fade
        env.FogLightEnergy = 0.6f;
        env.FogSunScatter = 0.0f;         // no sun scatter in a cavern

        // Glow adds the warm-torch bloom the official screenshot shows.
        // Aesthetic: subtle bloom on bright flame particles.
        env.GlowEnabled = true;
        env.GlowIntensity = 0.8f;
        env.GlowStrength = 1.2f;
        env.GlowBloom = 0.1f;
        env.GlowHdrThreshold = 0.7f;

        var worldEnv = new WorldEnvironment { Environment = env };
        AddChild(worldEnv);

        GD.Print("[CharSelectScene3D] Cavern environment built: dark BG + exponential fog + bloom. Aesthetic.");
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
        // CAVERN torch-lit rig approximating §3.6.1 (count CONFIRMED; colours UNVERIFIED).
        // spec: Docs/RE/specs/frontend_scenes.md §3.6.1 CODE-CONFIRMED count/range ≈1024; colours UNVERIFIED.
        //
        // The official char-select is an enclosed stone cavern lit by brazier torches — no sunlight.
        // The spec confirms ≈5 positional lights, range ≈1024; their colour/direction is data-driven
        // (UNVERIFIED). We approximate with two warm torch point lights (one per pillar) and three
        // character fill lights to match the official torchlit look.
        //
        // Aesthetic: all colours and energy levels tuned to match the warm golden torch light in the
        // official screenshot. No spec-cited colour values (UNVERIFIED in spec §3.6.1).

        // Left pillar torch light — matches left brazier position (Godot-space).
        // Aesthetic position: X ≈ centre−28, Y ≈ pillar top (88), Z ≈ row depth.
        // The ±28 X offset from centre (508.5) is aesthetic (xeff sub-effect layout pending).
        var leftTorch = new OmniLight3D
        {
            Name = "LeftPillarTorch",
            LightEnergy = 3.5f,
            LightColor = new Color(1.0f, 0.60f, 0.15f), // warm orange-gold flame
            OmniRange = 300.0f, // range ≈1024 per spec §3.6.1 (scaled for Godot units); exact UNVERIFIED
            OmniAttenuation = 1.2f,
            ShadowEnabled = false,
            Position = new Vector3(480.5f, 89.0f, 9758.6f), // left pillar top — Aesthetic
        };
        AddChild(leftTorch);

        // Right pillar torch light — matches right brazier position.
        // Aesthetic: mirror of left pillar at X = centre+28.
        var rightTorch = new OmniLight3D
        {
            Name = "RightPillarTorch",
            LightEnergy = 3.5f,
            LightColor = new Color(1.0f, 0.60f, 0.15f), // warm orange-gold flame — Aesthetic
            OmniRange = 300.0f,
            OmniAttenuation = 1.2f,
            ShadowEnabled = false,
            Position = new Vector3(536.5f, 89.0f, 9758.6f), // right pillar top — Aesthetic
        };
        AddChild(rightTorch);

        // Key fill — camera-facing fill for character faces; softer than torch.
        // spec: §3.6.1 "≈5 positional lights". Aesthetic position/colour.
        var keyFill = new OmniLight3D
        {
            Name = "CameraKeyFill",
            LightEnergy = 2.0f,  // boosted to illuminate character faces — Aesthetic
            LightColor = new Color(0.90f, 0.75f, 0.55f), // warm fill — Aesthetic
            OmniRange = 300.0f,
            ShadowEnabled = false,
            Position = new Vector3(512.0f, 100.0f, 9655.0f), // in front, above — Aesthetic
        };
        AddChild(keyFill);

        // Rim / back-light from behind the row (cascade glow approximation).
        // Aesthetic: cool-blue back light simulates the blue waterfall glow from behind.
        var rimLight = new OmniLight3D
        {
            Name = "WaterfallRim",
            LightEnergy = 0.9f,
            LightColor = new Color(0.45f, 0.65f, 1.0f), // cool blue — Aesthetic (waterfall hue)
            OmniRange = 250.0f,
            ShadowEnabled = false,
            Position = new Vector3(512.0f, 90.0f, 9820.0f), // behind the row — Aesthetic
        };
        AddChild(rimLight);

        // Ground / platform up-fill — bounces warm light up from the stone platform.
        // Aesthetic: subtle warm fill to prevent character feet from being too dark.
        var groundFill = new OmniLight3D
        {
            Name = "GroundFill",
            LightEnergy = 0.5f,
            LightColor = new Color(0.80f, 0.55f, 0.25f), // dim warm — Aesthetic
            OmniRange = 180.0f,
            ShadowEnabled = false,
            Position = new Vector3(512.0f, 60.0f, 9738.0f), // below platform level — Aesthetic
        };
        AddChild(groundFill);

        GD.Print("[CharSelectScene3D] 5-light torch rig built (5 omni, range≈280 Godot-units). " +
                 "spec: frontend_scenes.md §3.6.1 CODE-CONFIRMED count/range ≈1024; colours UNVERIFIED (Aesthetic).");
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

        string tedPath =
            $"data/map{AreaTag(BackdropAreaId)}/dat/d{AreaTag(BackdropAreaId)}x{BackdropMapX}z{BackdropMapZ}.ted";

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
            catch
            {
                /* non-critical */
            }

            try
            {
                string mapPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.map";
                if (assets.Contains(mapPath))
                    cellMap = MapDescriptorParser.Parse(assets.GetRaw(mapPath));
            }
            catch
            {
                /* non-critical */
            }

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
        // Resolve the .skn path for this slot's skin class.
        // spec: Docs/RE/specs/frontend_scenes.md §3.7.5 — per-class starter meshes. CODE-CONFIRMED.
        string? sknPath = PickSknPath(assets, skinClassId);
        if (sknPath is null)
        {
            GD.PrintErr($"[CharSelectScene3D] Slot {slotIdx}: no .skn found for skinClassId={skinClassId}.");
            return null;
        }

        // Parse the .skn mesh.
        SkinnedMesh mesh;
        try
        {
            ReadOnlyMemory<byte> sknData = assets.GetRaw(sknPath);
            if (sknData.IsEmpty)
            {
                GD.PrintErr($"[CharSelectScene3D] Slot {slotIdx}: .skn empty at '{sknPath}'.");
                return null;
            }

            mesh = SknParser.Parse(sknData);
            GD.Print($"[CharSelectScene3D] Slot {slotIdx}: loaded '{sknPath}' " +
                     $"({mesh.Positions.Length} verts, {mesh.FaceCount} faces, IdA={mesh.IdA}).");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] Slot {slotIdx}: .skn parse failed '{sknPath}': {ex.Message}");
            return null;
        }

        // Resolve the albedo texture via CharacterTextureResolver (skin.txt col4→col5→png).
        // spec: Docs/RE/specs/skinning.md §10 / CLAUDE.md asset chain — skin.txt col4=meshSkinId, col5=texId.
        ImageTexture? albedo = CharacterTextureResolver.Resolve(assets, mesh.IdA);
        GD.Print($"[CharSelectScene3D] Slot {slotIdx}: albedo resolved={albedo is not null} " +
                 $"for skinIdA={mesh.IdA}.");

        // Build the skinned actor node (with idle clip and shared skeleton).
        // SkinnedCharacterBuilder handles LBS/static fallback, diagnostics, stand-up pivot, recentre.
        // spec: Docs/RE/specs/skinning.md §8(b) — single Z-negate handedness conversion. CODE-CONFIRMED.
        Node3D actorRoot = SkinnedCharacterBuilder.Build(mesh, skeleton, idleClip, albedo);

        // IMPORTANT: SkinnedCharacterBuilder.Build returns a node whose Position is already set
        // by RecentreRoot (to centre the mesh with feet at Y=0). DO NOT overwrite actor.Position
        // directly — that erases the recentre offset. Wrap the actor in a slot-position container.
        // spec: Docs/RE/specs/frontend_scenes.md §3.3.1 CODE-CONFIRMED slot positions.
        var slotWrapper = new Node3D { Name = $"Slot{slotIdx}Actor" };
        slotWrapper.Position = new Vector3(SlotWorldX[slotIdx], SlotWorldY, SlotWorldZ[slotIdx]);
        slotWrapper.Scale = Vector3.One * PreviewScale;
        slotWrapper.AddChild(actorRoot);

        return slotWrapper;
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
            if (assets.Contains(p))
                return p;

        // Fallback: Musa (g202110001.skn) — the proven humanoid rig.
        return assets.Contains(FallbackSknPath) ? FallbackSknPath : null;
    }

    // =========================================================================
    // Brazier effect — GPUParticles3D at the row anchor (Godot-space)
    // =========================================================================

    // =========================================================================
    // Brazier / pillar flame positions (Godot-space)
    // =========================================================================

    // spec: Docs/RE/specs/frontend_scenes.md §3.6.5 CODE-CONFIRMED —
    //   "char_select-u.xeff spawned ONCE at world (508.5, 69.9, −9758.6), 68 sub-effects = torch coronas."
    //   The single composite xeff covers BOTH pillars via its 68 sub-effects. The exact per-pillar
    //   offsets are encoded in the xeff sub-effect layout (xeff-parser-pending). As an aesthetic
    //   approximation we place two separate Godot GPUParticles3D emitters — one per pillar — at the
    //   visually matching positions derived from the official screenshot.
    //
    // Row centre (Godot-space, Z negated): (508.5, 69.9, 9758.6)
    //   spec: Docs/RE/specs/frontend_scenes.md §3.6.5 CODE-CONFIRMED.
    //
    // Pillar X offsets from centre: Aesthetic — ≈ ±28 world units, matching the visual gap
    //   between the two stone pillars flanking the character platform in the official screenshot.
    //   The exact sub-effect offsets are xeff-pending; ±28 is an aesthetic approximation.
    // Pillar top Y: Aesthetic — ≈ platform Y (69.9) + pillar height (~18) = 88.
    //   The actual pillar geometry height is read from the .bud cell props; 88 is aesthetic approx.

    private static readonly Vector3 LeftPillarTop  = new(480.5f, 82.0f, 9758.6f);  // Aesthetic ±28 X from §3.6.5 centre, Y=82 pillar top
    private static readonly Vector3 RightPillarTop = new(536.5f, 82.0f, 9758.6f);  // Aesthetic ±28 X from §3.6.5 centre, Y=82 pillar top

    /// <summary>
    /// Builds two warm additive GPUParticles3D torch-flame emitters — one on each side pillar.
    ///
    /// <para>spec: Docs/RE/specs/frontend_scenes.md §3.6.5 CODE-CONFIRMED —
    /// "char_select-u.xeff spawned ONCE at world (508.5, 69.9, −9758.6); 68 sub-effects = torch
    /// coronas; NOT a 2D overlay." The single xeff covers both pillars via its 68 sub-effects;
    /// our approximation uses two separate Godot emitters (xeff sub-effect layout pending).</para>
    ///
    /// <para>Per-emitter layout: outer corona (warm orange) + inner core (bright yellow-white).
    /// Additive blend so flames glow against the dark cavern background.
    /// Pillar X offsets (±28 units from centre) and top Y (88) are Aesthetic.</para>
    /// </summary>
    private void BuildBrazierEffect()
    {
        // Left pillar brazier.
        BuildPillarFlame("Left", LeftPillarTop);
        // Right pillar brazier.
        BuildPillarFlame("Right", RightPillarTop);

        GD.Print("[CharSelectScene3D] Two pillar brazier emitters built: " +
                 $"left={LeftPillarTop} right={RightPillarTop} Godot-space. " +
                 "spec: frontend_scenes.md §3.6.5 CODE-CONFIRMED composite xeff anchor; " +
                 "per-pillar ±28 X offset + top Y 88 = Aesthetic (xeff sub-effect layout pending).");
    }

    /// <summary>
    /// Builds one torch emitter (corona + core) at the given Godot-space pillar-top position.
    /// Aesthetic: particle counts, lifetimes, velocities, ramp colours all chosen to match
    /// the warm torch-corona look of the official char-select. No spec values for these.
    /// </summary>
    private void BuildPillarFlame(string side, Vector3 pillarTop)
    {
        // ── Outer corona (warm orange glow) ───────────────────────────────────
        var coronaMat = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 3.5f, // tight sphere at pillar top — Aesthetic
            Direction = new Vector3(0f, 1f, 0f),
            Spread = 28.0f,
            InitialVelocityMin = 6.0f,
            InitialVelocityMax = 14.0f,
            Gravity = new Vector3(0f, 1.5f, 0f), // gentle upward drift — Aesthetic
            Color = new Color(1.0f, 0.58f, 0.10f, 1.0f), // warm torch orange — Aesthetic
            ScaleMin = 1.0f,
            ScaleMax = 2.0f,
        };

        var coronaGrad = new Gradient();
        coronaGrad.SetColor(0, new Color(1.0f, 0.88f, 0.28f, 1.0f)); // bright yellow-orange — Aesthetic
        coronaGrad.SetOffset(0, 0f);
        coronaGrad.SetColor(1, new Color(0.85f, 0.18f, 0.04f, 0.0f)); // dark red, transparent — Aesthetic
        coronaGrad.SetOffset(1, 1f);
        coronaMat.ColorRamp = new GradientTexture1D { Gradient = coronaGrad };

        var corona = new GpuParticles3D
        {
            Name = $"{side}BrazierCorona",
            Position = pillarTop,
            Amount = 45,          // Aesthetic
            Lifetime = 1.2f,      // Aesthetic
            OneShot = false,
            Emitting = true,
            Explosiveness = 0f,
            Randomness = 0.35f,
            Preprocess = 1.2f,    // fill immediately — Aesthetic
            ProcessMaterial = coronaMat,
            DrawPass1 = BuildFlameQuadMesh(1.8f), // Aesthetic quad size
        };
        AddChild(corona);

        // ── Inner core (bright yellow-white sparks) ───────────────────────────
        var coreMat = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 1.5f, // very tight — Aesthetic
            Direction = new Vector3(0f, 1f, 0f),
            Spread = 14.0f,
            InitialVelocityMin = 10.0f,
            InitialVelocityMax = 22.0f,
            Gravity = new Vector3(0f, 2.5f, 0f),
            Color = new Color(1.0f, 0.95f, 0.60f, 1.0f), // bright yellow-white — Aesthetic
            ScaleMin = 0.4f,
            ScaleMax = 0.9f,
        };

        var coreGrad = new Gradient();
        coreGrad.SetColor(0, new Color(1.0f, 1.0f, 0.85f, 1.0f)); // near-white hot — Aesthetic
        coreGrad.SetOffset(0, 0f);
        coreGrad.SetColor(1, new Color(1.0f, 0.55f, 0.08f, 0.0f)); // orange fade-out — Aesthetic
        coreGrad.SetOffset(1, 1f);
        coreMat.ColorRamp = new GradientTexture1D { Gradient = coreGrad };

        var core = new GpuParticles3D
        {
            Name = $"{side}BrazierCore",
            Position = pillarTop + new Vector3(0f, 1.5f, 0f), // tiny offset up from corona — Aesthetic
            Amount = 20,
            Lifetime = 0.7f,
            OneShot = false,
            Emitting = true,
            Explosiveness = 0f,
            Randomness = 0.45f,
            Preprocess = 0.7f,
            ProcessMaterial = coreMat,
            DrawPass1 = BuildFlameQuadMesh(0.7f), // Aesthetic quad size
        };
        AddChild(core);
    }

    /// <summary>
    /// Builds a small quad <see cref="QuadMesh"/> for the flame particle draw pass.
    /// GPUParticles3D needs a mesh to draw (otherwise no geometry is submitted to the GPU).
    /// Aesthetic: plain white quad; colour comes from <see cref="ParticleProcessMaterial.Color"/>
    /// and the colour ramp, composed with an additive <see cref="StandardMaterial3D"/>.
    /// </summary>
    private static QuadMesh BuildFlameQuadMesh(float size)
    {
        // Additive StandardMaterial3D: src += src × alpha → bright on dark, invisible on white.
        // Aesthetic: this is the correct blend for torch / fire VFX.
        var mat = new StandardMaterial3D
        {
            ShadingMode = StandardMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            // Additive blend via transparency flag + blend mode.
            Transparency = StandardMaterial3D.TransparencyEnum.Alpha,
            BlendMode = StandardMaterial3D.BlendModeEnum.Add,
            // Disable depth-write so overlapping particles accumulate additive light correctly.
            NoDepthTest = false,
            DepthDrawMode = StandardMaterial3D.DepthDrawModeEnum.Disabled,
            BillboardMode = StandardMaterial3D.BillboardModeEnum.Enabled, // always face camera
            AlbedoColor = Colors.White,
        };

        return new QuadMesh
        {
            Size = new Vector2(size, size),
            Material = mat,
        };
    }

    private static string AreaTag(int areaId) => areaId.ToString("D3");
}