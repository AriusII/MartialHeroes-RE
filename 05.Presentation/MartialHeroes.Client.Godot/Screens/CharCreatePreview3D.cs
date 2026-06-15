// Screens/CharCreatePreview3D.cs
//
// The character-CREATION preview — REWRITTEN FROM SCRATCH against the recovered spec (CAMPAIGN 9
// WAVE 3). A SubViewport-backed Control that renders ONE enlarged create-preview actor in front of
// the SAME carved-stone-relief temple backdrop as character-select. Every value is a real VFS asset
// or a spec-cited IDA constant; there is NO procedural sky, NO hand-placed omni rig, NO hand-tuned
// look-at.
//
// BACKDROP TRUTH (§3.7.6 VFS-VERIFIED): creation reuses the IDENTICAL cell / stage / camera /
//   environment as select. data/map000 contains exactly ONE cell, d000x10000z9990; the carved
//   stone wall (suksang01..04 / walll04*) is BAKED into that cell's .bud. Select→create differs
//   only by: the camera STAYS at KF1 (it does not move), and a single create actor is placed ≈56
//   units NEARER the camera (a Z shift only) in place of the 5-slot row.
//
// CAMERA (§3.5.2 / §3.5.4): held at KF1 = world (512, 87, −9652) → Godot (512, 87, +9652), framed
//   by a documented LookAt toward the row pivot (the exact free-look Euler is debugger-pending);
//   projection FOV 50 / near 5 / far 15000.
//
// ENVIRONMENT (§3.6): the area-0 values — a WHITE ambient floor (OPTION_BRIGHT/100 = 1.0, the MAIN
//   illuminant), a faint achromatic directional (≈0.047), fog OFF, an achromatic dark background.
//   NO coloured lamps, NO procedural sky.
//
// ACTOR (§4.2 / §3.5.4): ONE actor at the row pivot shifted +56.5 NEARER the camera (Godot −Z),
//   PreviewScale ×3.0 (the unit-reconciled slot-row scale), rotated by a press-and-hold turntable
//   (≈±2 rad/s) — NOT an auto-spin. Its rig + idle clip resolve from the mesh's OWN id_b, per class.
//
// HOST API PRESERVED (read by CharacterSelectScreen — keep these exact):
//   - public int InternalClassId { get; set; }
//   - public RealClientAssets? SharedRealAssets { get; set; }
//   - public void RebuildForClass()
//   - public void RotateLeft(float deltaSeconds) / RotateRight(float deltaSeconds)
//
// COORDINATE CONVENTION: world geometry negates Z (Helpers/WorldCoordinates.ToGodot).
// NO FALLBACK (missing asset → log + skip, no crash, no synthetic data). PASSIVE: view state only.

using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Dev;
using MartialHeroes.Client.Godot.Helpers;
using MartialHeroes.Client.Godot.World;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// The enlarged, turntable-rotatable character-creation preview, rendered in front of the real
/// carved-stone-relief temple backdrop (cell <c>d000x10000z9990</c> — the SAME cell as
/// character-select; §3.7.6). Set <see cref="InternalClassId"/> then call
/// <see cref="RebuildForClass"/>; the backdrop / camera / environment persist, only the actor rebuilds.
///
/// spec: Docs/RE/specs/frontend_scenes.md §3.7.6 / §3.5.4 / §4.2.
/// </summary>
public sealed partial class CharCreatePreview3D : Control
{
    // =========================================================================
    // Backdrop cell identity (§3.7.1 / §3.7.6).
    // =========================================================================

    private const int BackdropAreaId = 0;   // map000. spec: §3.7.1
    private const int BackdropMapX = 10000; // cell d000x10000z9990. spec: §3.7.1
    private const int BackdropMapZ = 9990;  // cell d000x10000z9990. spec: §3.7.1

    // =========================================================================
    // Camera (held KF1) & row pivot — all from the spec, converted to Godot-space (Z negated once).
    // =========================================================================

    // KF1 resting eye = world (512, 87, −9652). EXACT. The camera holds this for both select and
    // create (it does not move). spec: §3.5.2 / §3.5.4.
    private static readonly Vector3 CameraEyeGodot = ToGodotVec(512.0f, 87.0f, -9652.0f);

    // Row pivot = world (508.48, 69.89, −9758.57); the camera look-at sits over it. spec: §3.6.5 / §3.7.2.
    private const float RowPivotLegacyX = 508.48f;
    private const float RowPivotLegacyY = 69.89f;
    private const float RowPivotLegacyZ = -9758.57f;
    private static readonly Vector3 RowPivotGodot = ToGodotVec(RowPivotLegacyX, RowPivotLegacyY, RowPivotLegacyZ);

    // Camera look-at = the row pivot (documented framing; the exact free-look Euler is debugger-
    // pending — no aesthetic aim). spec: §3.5.4 / §3.6.5.
    private static readonly Vector3 CameraLookAtGodot = RowPivotGodot;

    // The create actor sits +56.5 units NEARER the camera than the row (a Z shift only). In Godot-
    // space the camera (Z≈9652) is at SMALLER Z than the row (Z≈9758.57), so "nearer the camera" =
    // a 56.5-unit DECREASE in Godot Z. spec: §3.5.4 / §4.2 — "+56.5 units nearer the camera". CODE-CONFIRMED.
    private const float CreateActorZNudgeGodot = -56.5f;

    // Projection — identical to select. spec: §3.5.1 — FOV 50° / near 5 / far 15000. CODE-CONFIRMED.
    private const float CameraFov = 50.0f;
    private const float CameraNear = 5.0f;
    private const float CameraFar = 15000.0f;

    // =========================================================================
    // Actor scale & turntable.
    // =========================================================================

    // PreviewScale ×3.0 — the unit-reconciled slot-row scale (the create actor shares the backdrop's
    // world frame with the .ted/.bud). spec: §3.3.1 / §4.2 (the legacy literals 81/70 are LEGACY-
    // space, not a Godot multiplier; ×3.0 is the verified Godot equivalent).
    private const float CreatePreviewScale = 3.0f;

    // Turntable rate ≈±2 rad/s (press-and-hold, NOT auto-spin). spec: §4.2 CODE-CONFIRMED.
    private const float TurntableRadPerSec = 2.0f;

    // Environment — the area-0 values (identical to select; §3.6).
    private const float AmbientFloorEnergy = 1.0f;                       // OPTION_BRIGHT/100 = 1.0. spec: §3.6.2
    private static readonly Color BackgroundColorAchromatic = new(0.04f, 0.04f, 0.04f); // achromatic. spec: §3.6.3
    private const float DirectionalEnergy = 0.047f;                      // area-0 kf-29 directional. spec: §11.3
    private static readonly Vector3 DirectionalDirGodot = ToGodotVec(-7.0f, 7.0f, 20.0f).Normalized(); // spec: §11.2

    // =========================================================================
    // Per-class skin path (§4.2 / §3.7.5). Each mesh carries a DISTINCT id_b that drives its rig + clip.
    // =========================================================================

    private static string SknPathForClass(int internalClass) => internalClass switch
    {
        1 => "data/char/skin/g202110001.skn",
        2 => "data/char/skin/g202220001.skn",
        3 => "data/char/skin/g202130001.skn",
        4 => "data/char/skin/g202140001.skn",
        _ => "data/char/skin/g202110001.skn",
    };

    // =========================================================================
    // Host API (read by CharacterSelectScreen).
    // =========================================================================

    /// <summary>Internal class id 1..4. Changing it requires <see cref="RebuildForClass"/>.</summary>
    public int InternalClassId { get; set; } = 1;

    /// <summary>Optional shared VFS handle from the owning screen.</summary>
    public RealClientAssets? SharedRealAssets { get; set; }

    // =========================================================================
    // View state & node references.
    // =========================================================================

    private float _turntableYRot; // radians; view state only
    private Node3D? _actorWrapper;
    private SubViewport? _subViewport;
    private Camera3D? _camera;
    private TerrainNode? _backdropTerrain;
    private float _rowGroundY = RowPivotLegacyY; // platform Y for actor placement (spec §3.6.5)
    private bool _builtOnce;

    // =========================================================================
    // Lifecycle.
    // =========================================================================

    public override void _Ready() => CallDeferred(MethodName.DeferredBuild);

    public override void _ExitTree()
    {
        _actorWrapper = null;
        _subViewport = null;
        _camera = null;
        _backdropTerrain = null;
    }

    // =========================================================================
    // Public API.
    // =========================================================================

    /// <summary>Rebuilds the create actor for the current <see cref="InternalClassId"/> (backdrop /
    /// camera / environment persist — only the actor rebuilds; §4.2). Main thread only.</summary>
    public void RebuildForClass()
    {
        if (!_builtOnce) return; // the deferred build will pick up InternalClassId
        BuildActorInWrapper();
    }

    /// <summary>Rotates the preview left while held (≈±2 rad/s). spec: §4.2 CODE-CONFIRMED.</summary>
    public void RotateLeft(float deltaSeconds)
    {
        _turntableYRot -= TurntableRadPerSec * deltaSeconds;
        ApplyTurntableRotation();
    }

    /// <summary>Rotates the preview right while held (≈±2 rad/s). spec: §4.2 CODE-CONFIRMED.</summary>
    public void RotateRight(float deltaSeconds)
    {
        _turntableYRot += TurntableRadPerSec * deltaSeconds;
        ApplyTurntableRotation();
    }

    // =========================================================================
    // Build pipeline.
    // =========================================================================

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
        int vpW = Size.X > 4 ? (int)Size.X : 420;
        int vpH = Size.Y > 4 ? (int)Size.Y : 600;

        _subViewport = new SubViewport
        {
            Name = "CreatePreviewVP",
            Size = new Vector2I(vpW, vpH),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false, // the backdrop cell IS the background (the carved temple), like select
        };

        // Add to tree FIRST so children (camera, light, terrain) are in-tree (LookAt requires it).
        var container = new SubViewportContainer
        {
            Name = "CreatePreviewContainer",
            Stretch = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        container.AddChild(_subViewport);
        AddChild(container);

        BuildEnvironment();
        BuildLighting();
        BuildCamera();

        RealClientAssets? assets = SharedRealAssets;
        bool ownsAssets = false;
        if (assets is null)
        {
            try { assets = RealClientAssets.TryOpen(); ownsAssets = assets is not null; }
            catch (Exception ex) { GD.PrintErr($"[CharCreatePreview3D] VFS open failed: {ex.Message}"); }
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

    // =========================================================================
    // Environment — area-0 white ambient floor + achromatic dark BG + fog OFF (NO procedural sky).
    // =========================================================================

    private void BuildEnvironment()
    {
        if (_subViewport is null) return;

        var env = new global::Godot.Environment
        {
            BackgroundMode = global::Godot.Environment.BGMode.Color,
            BackgroundColor = BackgroundColorAchromatic,                 // spec: §3.6.3 (achromatic, no skybox)
            AmbientLightSource = global::Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(1.0f, 1.0f, 1.0f),
            AmbientLightEnergy = AmbientFloorEnergy,                     // spec: §3.6.2 OPTION_BRIGHT/100 = 1.0
            TonemapMode = global::Godot.Environment.ToneMapper.Linear,  // faithful D3D9 linear output
            TonemapExposure = 1.0f,
            FogEnabled = false,                                          // spec: §3.6.2 distance fog OFF
        };

        var worldEnv = new WorldEnvironment { Environment = env };
        _subViewport.AddChild(worldEnv);

        GD.Print("[CharCreatePreview3D] Area-0 environment: achromatic dark BG + WHITE ambient floor (1.0) + fog OFF. " +
                 "NO procedural sky. spec: §3.6 + environment_bins.md.");
    }

    // =========================================================================
    // Lighting — ONLY the faint achromatic area-0 directional. NO coloured/omni lamps.
    // =========================================================================

    private void BuildLighting()
    {
        if (_subViewport is null) return;

        // The scene builds NO point-lights (§3.6.1 / §3.6.6). The only light is the faint achromatic
        // area-0 directional (kf-29, 14:30). The warm look would come from the additive xeff fire
        // texture, not lamps — so this preview adds no coloured omni fills.
        var sun = new DirectionalLight3D
        {
            Name = "Area0Directional",
            LightEnergy = DirectionalEnergy,          // spec: environment_bins.md §11.3 (≈0.047)
            LightColor = new Color(1.0f, 1.0f, 1.0f), // achromatic — area-0 R=G=B. spec: §11.2
            ShadowEnabled = false,
        };
        _subViewport.AddChild(sun);

        var pivot = ToGodotVec(RowPivotLegacyX, 200.0f, RowPivotLegacyZ);
        sun.LookAtFromPosition(pivot, pivot + DirectionalDirGodot, Vector3.Up);

        GD.Print("[CharCreatePreview3D] Lighting: faint achromatic directional (0.047) ONLY; " +
                 "NO point-lights. spec: §3.6.1 / §3.6.6.");
    }

    // =========================================================================
    // Camera — held KF1 (the camera does NOT move; §3.5.4).
    // =========================================================================

    private void BuildCamera()
    {
        if (_subViewport is null) return;

        _camera = new Camera3D
        {
            Name = "CreatePreviewCam",
            Fov = CameraFov,   // spec: §3.5.1 FOV 50°
            Near = CameraNear, // spec: §3.5.1 near 5.0
            Far = CameraFar,   // spec: §3.5.1 far 15000.0
            KeepAspect = Camera3D.KeepAspectEnum.Height,
        };
        _subViewport.AddChild(_camera);

        _camera.Position = CameraEyeGodot;                 // held at KF1
        _camera.LookAt(CameraLookAtGodot, Vector3.Up);     // toward the row pivot

        GD.Print($"[CharCreatePreview3D] Held-KF1 camera: eye={CameraEyeGodot} look-at(row pivot)={CameraLookAtGodot}; " +
                 $"FOV {CameraFov}/near {CameraNear}/far {CameraFar}. spec: §3.5.2/§3.5.4 (camera does not move).");
    }

    // =========================================================================
    // Backdrop terrain — the real cell d000x10000z9990.ted via TerrainNode.
    // =========================================================================

    private void BuildBackdropTerrain(RealClientAssets assets)
    {
        if (_subViewport is null) return;

        string tag = AreaTag(BackdropAreaId);
        string tedPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.ted";
        if (!assets.Contains(tedPath))
        {
            GD.Print($"[CharCreatePreview3D] Backdrop .ted absent: {tedPath} — terrain skipped.");
            return;
        }

        try
        {
            ReadOnlyMemory<byte> tedData = assets.GetRaw(tedPath);
            if (tedData.IsEmpty)
            {
                GD.Print($"[CharCreatePreview3D] Backdrop .ted empty: {tedPath} — terrain skipped.");
                return;
            }

            var terrainNode = new TerrainNode
            {
                Name = "BackdropTerrain",
                TextureResolver = BuildTerrainTextureResolver(assets),
            };
            _subViewport.AddChild(terrainNode);
            _backdropTerrain = terrainNode;

            terrainNode.OnSectorLoaded(new MartialHeroes.Client.Application.World.SectorLoadedEvent(
                MapX: BackdropMapX, MapZ: BackdropMapZ, Payload: tedData));

            // The actor stands on the .bud platform (spec row-pivot Y ≈70); the .ted sampler returns
            // the raw soil floor (NOT the platform top), used for diagnostics only. spec: §3.6.5.
            _rowGroundY = RowPivotLegacyY;
            if (terrainNode.TryGetGroundHeight(RowPivotLegacyX, RowPivotLegacyZ, out float sampledY, RowPivotLegacyY))
                GD.Print($"[CharCreatePreview3D] Terrain sampler at pivot = {sampledY:F3} (soil floor); placing actor on platform Y={_rowGroundY:F2}. spec: §3.6.5.");

            GD.Print($"[CharCreatePreview3D] Backdrop terrain cell ({BackdropMapX},{BackdropMapZ}) loaded. spec: §3.7.1.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] Backdrop terrain failed: {ex.Message}");
        }
    }

    // =========================================================================
    // Backdrop props — the carved suksang*/walll04* wall baked into d000x10000z9990.bud.
    // =========================================================================

    private void BuildBackdropProps(RealClientAssets assets)
    {
        if (_subViewport is null) return;

        string tag = AreaTag(BackdropAreaId);
        string budPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.bud";
        if (!assets.Contains(budPath))
        {
            GD.Print($"[CharCreatePreview3D] Backdrop .bud absent: {budPath} — carved wall skipped.");
            return;
        }

        try
        {
            BudScene? scene = assets.LoadBud(budPath);
            if (scene is null || scene.Objects.Length == 0)
            {
                GD.Print("[CharCreatePreview3D] Backdrop .bud empty — no carved-wall props.");
                return;
            }

            BgTextureCatalog? bgPool = TryLoadBgPool(assets);
            MapDescriptor? cellMap = TryLoadCellMap(assets);

            Func<uint, ImageTexture?> budTexResolver = budIdx =>
                ResolveTexture(assets, bgPool, cellMap, "BUILDING", (int)budIdx);

            Node3D propsRoot = BudMeshBuilder.Build(scene, budTexResolver);
            propsRoot.Name = "BackdropProps";
            _subViewport.AddChild(propsRoot);

            GD.Print($"[CharCreatePreview3D] Carved-wall props built ({scene.Objects.Length} objects). spec: §3.7.6 / §3.7.3.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] Backdrop props failed: {ex.Message}");
        }
    }

    // =========================================================================
    // Create actor — single forward-placed actor (+56.5 nearer the camera).
    // =========================================================================

    private void BuildActorInWrapper()
    {
        if (_actorWrapper is null) return;

        foreach (Node child in _actorWrapper.GetChildren())
            child.QueueFree();

        // Position the wrapper at the row pivot, shifted +56.5 nearer the camera (Godot −Z), on the
        // platform Y, at the unit-reconciled ×3.0 scale. spec: §4.2 / §3.5.4. The wrapper carries the
        // turntable rotation; the actor's own Position is the recentre offset from the builder.
        float actorZ = RowPivotGodot.Z + CreateActorZNudgeGodot;
        _actorWrapper.Position = new Vector3(RowPivotGodot.X, _rowGroundY, actorZ);
        _actorWrapper.Scale = Vector3.One * CreatePreviewScale;
        ApplyTurntableRotation();

        RealClientAssets? assets = SharedRealAssets;
        bool ownsAssets = false;
        if (assets is null)
        {
            try { assets = RealClientAssets.TryOpen(); ownsAssets = assets is not null; }
            catch (Exception ex) { GD.PrintErr($"[CharCreatePreview3D] VFS open failed: {ex.Message}"); }
        }

        if (assets is null)
        {
            GD.Print("[CharCreatePreview3D] VFS offline — no create actor (no synthetic placeholder).");
            return;
        }

        try
        {
            Node3D? actor = TryBuildActorForClass(assets, InternalClassId);
            if (actor is not null)
            {
                _actorWrapper.AddChild(actor);
                GD.Print($"[CharCreatePreview3D] Create actor (class={InternalClassId}) at Godot " +
                         $"({RowPivotGodot.X:F1}, {_rowGroundY:F2}, {actorZ:F1}) (+56.5 nearer camera), scale {CreatePreviewScale}. spec: §4.2 / §3.5.4.");
            }
            else
            {
                GD.Print($"[CharCreatePreview3D] No create actor built for class={InternalClassId} (asset absent — skipped).");
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
        string sknPath = SknPathForClass(internalClass);
        if (!assets.Contains(sknPath))
        {
            GD.PrintErr($"[CharCreatePreview3D] .skn absent: {sknPath} — skipped.");
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
            GD.PrintErr($"[CharCreatePreview3D] SknParser failed '{sknPath}': {ex.Message}");
            return null;
        }

        // Rig + idle clip resolve from the mesh's OWN id_b (per class) — never a shared rig.
        // spec: skinning.md §8(e) — data/char/bind/g{id_b}.bnd + actormotion.txt col2==id_b→col16.
        Skeleton? skeleton = TryLoadSkeletonForIdB(assets, mesh.IdB);
        AnimationClip? idleClip = TryLoadIdleClipForIdB(assets, mesh.IdB);

        ImageTexture? albedo = null;
        try { albedo = CharacterTextureResolver.Resolve(assets, mesh.IdA); }
        catch (Exception ex) { GD.PrintErr($"[CharCreatePreview3D] texture resolve failed: {ex.Message}"); }

        bool savedDiag = SkinnedCharacterBuilder.PrintDiagnostics;
        try
        {
            SkinnedCharacterBuilder.ForceSkinned = true;
            SkinnedCharacterBuilder.PrintDiagnostics = false;
            return SkinnedCharacterBuilder.Build(
                mesh, skeleton, idleClip, albedo,
                externalDrive: false, startPhaseSeconds: 0f, out _,
                debugLabel: $"create_preview_class{internalClass}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] SkinnedCharacterBuilder failed: {ex.Message}");
            return null;
        }
        finally
        {
            SkinnedCharacterBuilder.PrintDiagnostics = savedDiag;
        }
    }

    private static Skeleton? TryLoadSkeletonForIdB(RealClientAssets assets, uint idB)
    {
        if (idB == 0) return null;
        string bndPath = $"data/char/bind/g{idB}.bnd";
        if (!assets.Contains(bndPath))
        {
            GD.PrintErr($"[CharCreatePreview3D] .bnd absent for id_b={idB}: {bndPath} — rest pose.");
            return null;
        }
        try
        {
            ReadOnlyMemory<byte> data = assets.GetRaw(bndPath);
            return data.IsEmpty ? null : BndParser.Parse(data);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] BndParser failed '{bndPath}': {ex.Message}");
            return null;
        }
    }

    private static AnimationClip? TryLoadIdleClipForIdB(RealClientAssets assets, uint idB)
    {
        if (idB == 0) return null;
        const string tablePath = "data/char/actormotion.txt";
        if (!assets.Contains(tablePath)) return null;

        try
        {
            string text = System.Text.Encoding.GetEncoding(949).GetString(assets.GetRaw(tablePath).Span);
            foreach (string rawLine in text.Split('\n'))
            {
                string[] cols = rawLine.Replace("\r", string.Empty).Split('\t');
                if (cols.Length <= 16) continue;
                if (!uint.TryParse(cols[2].Trim(), out uint classId) || classId != idB) continue;

                string idle = cols[16].Trim(); // col 16 = idle motion id. spec: §3.3.4
                if (idle.Length == 0 || idle == "0") return null;

                string motPath = $"data/char/mot/g{idle}.mot";
                if (!assets.Contains(motPath)) return null;

                ReadOnlyMemory<byte> motData = assets.GetRaw(motPath);
                return motData.IsEmpty ? null : AnimationParser.Parse(motData);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] TryLoadIdleClipForIdB(id_b={idB}) failed: {ex.Message}");
        }
        return null;
    }

    private void ApplyTurntableRotation()
    {
        if (_actorWrapper is null || !IsInstanceValid(_actorWrapper)) return;
        _actorWrapper.RotationDegrees = new Vector3(0f, Mathf.RadToDeg(_turntableYRot), 0f);
    }

    // ── Terrain/building texture two-hop chain (terrain.md §5.6 / §3.5 / §4.2) ───────────────────

    private Func<int, ImageTexture?> BuildTerrainTextureResolver(RealClientAssets assets)
    {
        BgTextureCatalog? bgPool = TryLoadBgPool(assets);
        MapDescriptor? cellMap = TryLoadCellMap(assets);
        var cache = new Dictionary<int, ImageTexture?>();
        return texByte =>
        {
            if (cache.TryGetValue(texByte, out ImageTexture? cached)) return cached;
            ImageTexture? tex = ResolveTexture(assets, bgPool, cellMap, "TERRAIN", texByte);
            cache[texByte] = tex;
            return tex;
        };
    }

    private static BgTextureCatalog? TryLoadBgPool(RealClientAssets assets)
    {
        try
        {
            const string txtPath = "data/map000/texture/bgtexture.txt";
            return assets.Contains(txtPath) ? BgTextureTxtParser.Parse(assets.GetRaw(txtPath)) : null;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] bgtexture.txt load failed: {ex.Message}");
            return null;
        }
    }

    private static MapDescriptor? TryLoadCellMap(RealClientAssets assets)
    {
        try
        {
            string tag = AreaTag(BackdropAreaId);
            string mapPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.map";
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

    // ── Conversions ─────────────────────────────────────────────────────────────────────────────

    private static Vector3 ToGodotVec(float legacyX, float legacyY, float legacyZ)
    {
        (float gx, float gy, float gz) = WorldCoordinates.ToGodot(legacyX, legacyY, legacyZ);
        return new Vector3(gx, gy, gz);
    }

    private static string AreaTag(int areaId) => areaId.ToString("D3");
}
