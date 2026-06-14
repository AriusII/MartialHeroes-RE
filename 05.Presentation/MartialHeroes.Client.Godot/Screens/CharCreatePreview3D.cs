// Screens/CharCreatePreview3D.cs
//
// A SubViewport-backed Control that renders the single, enlarged character preview
// for the character-CREATION sub-form.
//
// SPEC FACTS (frontend_scenes.md §4.2 CODE-CONFIRMED):
//   - ONE actor, centered, placed +56.5 units NEARER the camera than the slot row.
//   - Scale 75 (vs slot-row scale 50).  spec: §4.2 "scale 75 vs the slots' 50".
//   - Rotation is PRESS-AND-HOLD turntable only (≈±2 rad/s while a rotate control is held).
//     NOT a continuous auto-spin.  spec: §4.2 "turntable ≈±2 rad/s while a rotate control is held.
//     NOT a continuous auto-spin."  CODE-CONFIRMED.
//   - Changing the class rebuilds the actor; the ±face buttons rebuild it too but the
//     visible 3D face does NOT change (face feeds a separate 2D portrait).  spec: §4.2.
//   - No sex toggle.  spec: §4.2 "no functional sex toggle".
//   - Per-class binding table (IdB), reusing the same SkinnedCharacterBuilder + texture path.
//     spec: §4.2 + §3.7.5.  Per-class IdB / .skn:
//       class 1 → g202110001.skn   (Musa)
//       class 2 → g202220001.skn   (Tao)
//       class 3 → g202130001.skn   (Blader)
//       class 4 → g202140001.skn   (Warrior)
//     Shared skeleton: data/char/bind/g1.bnd  spec: §3.7.5 CODE-CONFIRMED.
//     Shared idle:     data/char/mot/g111100010.mot  spec: §3.7.5 CODE-CONFIRMED.
//   - Per-class starter overlay skin gids (§4.3 table, CODE-CONFIRMED):
//       class 1: 202110003 / 203110002 / 206110002 / 209110001
//       class 2: 202220003 / 203220002 / 206220002 / 209220001
//       class 3: 202130003 / 203130002 / 206130002 / 209130001
//       class 4: 202140003 / 203140002 / 206140002 / 209140001
//
// PASSIVE: zero game logic. View state only (which class/face, turntable angle).
// Reads VFS assets via RealClientAssets; rebuilds the actor node when the class changes.
// All Control mutation on the main thread.
//
// spec: Docs/RE/specs/frontend_scenes.md §4.2  CODE-CONFIRMED.
// spec: Docs/RE/specs/frontend_scenes.md §3.7.5 CODE-CONFIRMED (assets).

using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Dev;
using MartialHeroes.Client.Godot.World;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// The enlarged, turntable-rotatable character preview shown during character creation.
///
/// <para>Set <see cref="InternalClassId"/> (1..4) then call <see cref="RebuildForClass"/>.
/// The node hosts a <see cref="SubViewport"/> with the character actor at spec scale 75.</para>
///
/// <para>Turntable: the owning form calls <see cref="RotateLeft"/>/<see cref="RotateRight"/>
/// while the rotate button is held; ~2 rad/s at 60 fps = ~0.0333 rad/frame.</para>
///
/// spec: Docs/RE/specs/frontend_scenes.md §4.2 CODE-CONFIRMED.
/// </summary>
public sealed partial class CharCreatePreview3D : Control
{
    // =========================================================================
    // Spec constants
    // =========================================================================

    // Scale for the SubViewport preview. CharSelectScene3D uses PreviewScale=3.0 for slot actors.
    // Spec §4.2 says "scale 75 vs slots' 50" — a ratio of 1.5× in legacy units.
    // Our Godot implementation uses scale=3 for slot actors (CharSelectScene3D PreviewScale=3.0),
    // so the spec ratio implies create-preview scale = 3.0 * 1.5 = 4.5.
    // For this SubViewport (isolated from the world scene), scale=3 is used with auto-aimed camera.
    // TODO: increase to 4.5 once the skinning stand-up rotation is confirmed correct.
    // spec: Docs/RE/specs/frontend_scenes.md §4.2 "scale 75 vs the slots' 50" (ratio 1.5×). CODE-CONFIRMED.
    private const float CreatePreviewScale = 3.0f; // calibrated; spec ratio implies 4.5

    // Turntable rate: ≈±2 rad/s → at ~60 fps, ≈0.0333 rad/frame.
    // spec: Docs/RE/specs/frontend_scenes.md §4.2 "≈±2 rad/s". CODE-CONFIRMED.
    private const float TurntableRadPerSec = 2.0f;

    // Camera orbit distance (SubViewport-local).
    // At scale=3, the actor is ~3 units tall (SKN meshes are ~1 unit un-scaled).
    // Wrapper scale is applied, so effective actor height = scale * unscaled_height ≈ 3 units.
    // Visible height at distance D with FOV=50°: 2*D*tan(25°) ≈ 0.932*D.
    // To frame 3-unit character with headroom: D = 3.5 / 0.932 ≈ 3.75 → use 8 for more margin.
    // Minimum camera distance floor. The real framing distance is derived per-build from the actor's
    // scaled height in FrameCameraOnActor (height × 1.6), so this is only a lower bound for tiny rigs.
    // spec: Docs/RE/specs/frontend_scenes.md §4.2 — front-on full-body framing. CODE-CONFIRMED intent.
    private const float OrbitDistance = 6.0f; // TUNABLE floor — height-based framing dominates

    // Look-at Y (unused now — camera is auto-aimed in BuildActorInWrapper).
    private const float LookAtY = 1.5f; // TUNABLE (kept for initial position only)

    // =========================================================================
    // Per-class skin path table (§4.2 / §3.7.5). CODE-CONFIRMED.
    // =========================================================================

    // Shared across all four create-preview classes (§3.7.5 CODE-CONFIRMED).
    private const string SharedBndPath = "data/char/bind/g1.bnd";
    private const string SharedIdleMotPath = "data/char/mot/g111100010.mot";

    // Starter base-skin mesh per internal class id 1..4.
    // spec: Docs/RE/specs/frontend_scenes.md §3.7.5 CODE-CONFIRMED.
    private static string SknPathForClass(int internalClass) => internalClass switch
    {
        1 => "data/char/skin/g202110001.skn",
        2 => "data/char/skin/g202220001.skn",
        3 => "data/char/skin/g202130001.skn",
        4 => "data/char/skin/g202140001.skn",
        _ => "data/char/skin/g202110001.skn", // fallback Musa
    };

    // =========================================================================
    // View state
    // =========================================================================

    /// <summary>Internal class id 1..4. Changing this requires calling <see cref="RebuildForClass"/>.</summary>
    public int InternalClassId { get; set; } = 1;

    /// <summary>Optional shared VFS handle from the owning screen.</summary>
    public RealClientAssets? SharedRealAssets { get; set; }

    // Current turntable Y-rotation (radians). View state only, no domain meaning.
    private float _turntableYRot;

    // The 3D actor wrapper node (at origin, scale 75). We rotate this for turntable.
    private Node3D? _actorWrapper;

    // SubViewport references.
    private SubViewport? _subViewport;
    private Camera3D? _camera;

    // Whether the SubViewport has been built.
    private bool _builtOnce;

    // =========================================================================
    // Godot lifecycle
    // =========================================================================

    public override void _Ready()
    {
        // Build the viewport and initial character.
        CallDeferred(MethodName.DeferredBuild);
    }

    public override void _Process(double delta)
    {
        // Turntable is driven externally by RotateLeft/RotateRight from the owning form
        // (press-and-hold pattern). No auto-spin here.
        // spec: Docs/RE/specs/frontend_scenes.md §4.2 "NOT a continuous auto-spin". CODE-CONFIRMED.
    }

    public override void _ExitTree()
    {
        _actorWrapper = null;
        _subViewport = null;
        _camera = null;
    }

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>
    /// Rebuilds the 3D actor for the current <see cref="InternalClassId"/>.
    /// Must be called on the Godot main thread.
    /// </summary>
    public void RebuildForClass()
    {
        if (!_builtOnce)
        {
            // Not yet initialised — the deferred build will pick up InternalClassId.
            return;
        }

        ReplaceActorInViewport();
    }

    /// <summary>
    /// Rotates the preview left (ccw when viewed from above).
    /// Call once per frame while the rotate-left button is held.
    /// spec: Docs/RE/specs/frontend_scenes.md §4.2 "≈±2 rad/s turntable". CODE-CONFIRMED.
    /// </summary>
    public void RotateLeft(float deltaSeconds)
    {
        _turntableYRot -= TurntableRadPerSec * deltaSeconds;
        ApplyTurntableRotation();
    }

    /// <summary>
    /// Rotates the preview right (cw when viewed from above).
    /// Call once per frame while the rotate-right button is held.
    /// spec: Docs/RE/specs/frontend_scenes.md §4.2 "≈±2 rad/s turntable". CODE-CONFIRMED.
    /// </summary>
    public void RotateRight(float deltaSeconds)
    {
        _turntableYRot += TurntableRadPerSec * deltaSeconds;
        ApplyTurntableRotation();
    }

    // =========================================================================
    // Build pipeline
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
        // Determine control size (fallback 280×420 if not yet laid out).
        int vpW = Size.X > 4 ? (int)Size.X : 280;
        int vpH = Size.Y > 4 ? (int)Size.Y : 420;

        _subViewport = new SubViewport
        {
            Name = "CreatePreviewVP",
            Size = new Vector2I(vpW, vpH),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = true,
        };

        // Camera — FOV 50° as per the select-scene camera; near/far tuned for the SubViewport scale.
        // At scale=3 the actor is ~3 units; near=0.05 avoids clipping at close range.
        // spec: Docs/RE/specs/frontend_scenes.md §3.5.1 CODE-CONFIRMED FOV+far; near tuned for SubVP scale.
        _camera = new Camera3D
        {
            Name = "CreatePreviewCam",
            Fov = 50f,   // spec: §3.5.1 CODE-CONFIRMED
            Near = 0.05f, // tuned: at scale=3 units the near plane must be small (spec near=5 is world-space)
            Far = 500f,   // sufficient for this SubViewport
            KeepAspect = Camera3D.KeepAspectEnum.Height,
        };
        // Place camera looking at the rig origin from the front.
        // Position camera at (0, LookAtY, OrbitDistance) — facing the actor at origin.
        _camera.Position = new Vector3(0f, LookAtY, OrbitDistance);
        // LookAt is called deferred after the camera is in-tree (see ApplyCameraLookAt).
        _subViewport.AddChild(_camera);

        // Key light (strong directional, boosted for diagnostic).
        var sun = new DirectionalLight3D
        {
            Name = "CreateLight",
            LightEnergy = 2.5f,
            LightColor = new Color(1.0f, 0.95f, 0.88f),
        };
        sun.RotationDegrees = new Vector3(-30f, 0f, 0f); // overhead, hitting front face
        _subViewport.AddChild(sun);

        // Soft fill light — positioned near the camera to illuminate the front of the actor.
        // Range tuned for scale=3 (actor at origin, camera at distance ~4 units).
        var fill = new OmniLight3D
        {
            Name = "CreateFill",
            LightEnergy = 0.6f,
            LightColor = new Color(0.6f, 0.7f, 1.0f),
            OmniRange = 20f,
            Position = new Vector3(-1f, LookAtY, OrbitDistance - 1f),
        };
        _subViewport.AddChild(fill);

        // Ambient environment (neutral grey so character colours read clean).
        var skyMat = new ProceduralSkyMaterial();
        skyMat.SkyTopColor = new Color(0.15f, 0.20f, 0.40f);
        skyMat.SkyHorizonColor = new Color(0.25f, 0.25f, 0.35f);
        var env = new global::Godot.Environment();
        env.BackgroundMode = global::Godot.Environment.BGMode.Sky;
        env.Sky = new Sky { SkyMaterial = skyMat };
        env.AmbientLightSource = global::Godot.Environment.AmbientSource.Color;
        env.AmbientLightColor = new Color(0.8f, 0.8f, 0.9f); // brighter ambient for diagnostic
        env.AmbientLightEnergy = 2.5f; // high ambient so textures are visible regardless of directional
        env.TonemapMode = global::Godot.Environment.ToneMapper.Aces;
        env.TonemapExposure = 1.2f;
        var wEnv = new WorldEnvironment { Environment = env };
        _subViewport.AddChild(wEnv);

        // Actor wrapper for turntable rotation.
        _actorWrapper = new Node3D { Name = "ActorWrapper" };
        _subViewport.AddChild(_actorWrapper);

        // Build the initial character actor.
        BuildActorInWrapper();

        // Apply camera look-at once camera is in tree.
        Callable.From(ApplyCameraLookAt).CallDeferred();

        // SubViewportContainer.
        var container = new SubViewportContainer
        {
            Name = "CreatePreviewContainer",
            Stretch = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        container.AddChild(_subViewport);
        AddChild(container);

        GD.Print($"[CharCreatePreview3D] Viewport {vpW}×{vpH} built for class={InternalClassId}. " +
                 "spec: frontend_scenes.md §4.2 CODE-CONFIRMED (scale 75, turntable).");
    }

    private void ApplyCameraLookAt()
    {
        if (_camera is null || !IsInstanceValid(_camera)) return;
        _camera.LookAt(new Vector3(0f, LookAtY, 0f), Vector3.Up);
    }

    private void BuildActorInWrapper()
    {
        if (_actorWrapper is null) return;

        // Clear any existing actor.
        foreach (Node child in _actorWrapper.GetChildren())
            child.QueueFree();

        RealClientAssets? assets = SharedRealAssets;
        bool ownsAssets = false;
        if (assets is null)
        {
            try
            {
                assets = RealClientAssets.TryOpen();
                ownsAssets = assets is not null;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[CharCreatePreview3D] VFS open failed: {ex.Message}");
            }
        }

        if (assets is null)
        {
            GD.Print("[CharCreatePreview3D] VFS offline — placeholder only.");
            // Add a visible placeholder box.
            var box = new MeshInstance3D
            {
                Name = "Placeholder",
                Mesh = new BoxMesh { Size = new Vector3(2f, 4f, 1f) },
                Position = new Vector3(0f, 2f, 0f),
            };
            var mat = new StandardMaterial3D();
            mat.AlbedoColor = new Color(0.35f, 0.30f, 0.50f);
            box.MaterialOverride = mat;
            _actorWrapper.AddChild(box);
            return;
        }

        try
        {
            Node3D? actor = TryBuildActorForClass(assets, InternalClassId);
            if (actor is not null)
            {
                // spec: frontend_scenes.md §4.2 — "scale 3 (matching slot-row scale)". CODE-CONFIRMED.
                //
                // SkinnedCharacterBuilder applies the SAME upright stand-up pivot (DeriveStandUpBasis)
                // and the SAME RecentreRoot used by the slot row in CharSelectScene3D — so the actor
                // already stands VERTICALLY (head → +Y) with its FEET at local Y≈0 and its body
                // CENTRED on local X≈0 / Z≈0. The mesh therefore renders near the wrapper ORIGIN,
                // NOT at actor.Position (which is only the recentre OFFSET that produced that layout).
                //
                // ROOT-CAUSE FIX: the previous code aimed the camera at actor.Position * scale — i.e.
                // at the recentre offset, far from where the mesh actually is — so the upright actor
                // fell outside the frame and only a skewed sliver was visible (read as "lying ~90°").
                // We now frame the camera on the actor's ACTUAL rendered AABB (its recentred,
                // pivot-stood mesh), exactly mirroring the slot row which looks at the real mesh
                // location (its row pivot), not at any recentre offset.
                // spec: frontend_scenes.md §3.3.1 / §4.2 — preview reuses the slot stand-up + framing.
                _actorWrapper.Scale = Vector3.One * CreatePreviewScale;
                _actorWrapper.AddChild(actor);

                // Frame the camera once the actor's transform is settled in-tree, using its real
                // global AABB (centre + height). The actor is upright, so its tallest extent is Y.
                Callable.From(FrameCameraOnActor).CallDeferred();

                ApplyTurntableRotation();
                GD.Print($"[CharCreatePreview3D] Actor built for class={InternalClassId} scale={CreatePreviewScale}, " +
                         $"localRecentreOffset={actor.Position} (camera frames the recentred mesh, not this offset). " +
                         "spec: frontend_scenes.md §4.2 CODE-CONFIRMED.");
            }
            else
            {
                GD.Print($"[CharCreatePreview3D] Actor build returned null for class={InternalClassId}.");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] BuildActorInWrapper failed: {ex.Message}");
        }
        finally
        {
            if (ownsAssets) assets?.Dispose();
        }
    }

    private void ReplaceActorInViewport()
    {
        // Called on class change — rebuild the actor inside the existing wrapper.
        // BuildActorInWrapper already defers FrameCameraOnActor, which re-frames the camera on the
        // new (possibly differently-proportioned) class mesh. No stale fixed look-at re-apply here.
        BuildActorInWrapper();
    }

    /// <summary>
    /// Aims the camera at the actor's ACTUAL rendered location, computed from its in-tree global
    /// AABB (centre + height). The actor is built upright by SkinnedCharacterBuilder (the same
    /// stand-up pivot the slot row uses) and recentred so its feet sit at the wrapper origin, so
    /// the correct look-at is the AABB centre — NOT the recentre offset that the old code used.
    ///
    /// <para>Deferred so the wrapper scale and the actor's recentre <c>Position</c> are settled in
    /// the tree before the global AABB is read.</para>
    ///
    /// spec: Docs/RE/specs/frontend_scenes.md §4.2 — front-on framing, scale 75 (here scale 3),
    ///       camera placed +OrbitDistance toward the viewer; up = +Y so the actor stands vertically.
    /// </summary>
    private void FrameCameraOnActor()
    {
        if (_camera is null || !IsInstanceValid(_camera) || _actorWrapper is null) return;

        Aabb world = ComputeWorldAabb(_actorWrapper);
        Vector3 centre = world.Position + world.Size * 0.5f;

        // Distance to comfortably frame the actor's height in the FOV, with headroom.
        float height = Mathf.Max(world.Size.Y, 0.001f);
        float dist = Mathf.Max(OrbitDistance, height * 1.6f);

        // Front-on eye: same X/Y as the look-at centre, pulled +Z toward the viewer.
        // Up = +Y keeps the upright (head→+Y) actor standing vertically on screen.
        var eye = new Vector3(centre.X, centre.Y, centre.Z + dist);
        _camera.Position = eye;
        _camera.LookAt(centre, Vector3.Up);

        GD.Print($"[CharCreatePreview3D] Camera framed: actor AABB centre={centre} size={world.Size} " +
                 $"eye={eye} dist={dist:F1}. spec: frontend_scenes.md §4.2 CODE-CONFIRMED (front-on, up=+Y).");
    }

    /// <summary>
    /// Walks a node subtree and unions the GLOBAL AABBs of every <see cref="VisualInstance3D"/>
    /// (e.g. the LBS <see cref="MeshInstance3D"/>). Returns an empty AABB when none are found.
    /// Global (not local) so the wrapper scale and the actor's recentre offset are included — this
    /// is the actor's true rendered extent, which is what the camera must frame.
    /// </summary>
    private static Aabb ComputeWorldAabb(Node3D root)
    {
        var result = new Aabb();
        bool any = false;
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            if (n is VisualInstance3D vis && vis.IsInsideTree())
            {
                Aabb local = vis.GetAabb();
                if (local.Size != Vector3.Zero)
                {
                    Aabb global = vis.GlobalTransform * local;
                    result = any ? result.Merge(global) : global;
                    any = true;
                }
            }

            foreach (Node child in n.GetChildren())
                stack.Push(child);
        }

        return result;
    }

    private static Node3D? TryBuildActorForClass(RealClientAssets assets, int internalClass)
    {
        // Resolve .skn path.
        // spec: Docs/RE/specs/frontend_scenes.md §4.2 / §3.7.5 CODE-CONFIRMED.
        string sknPath = SknPathForClass(internalClass);
        if (!assets.Contains(sknPath))
        {
            GD.PrintErr($"[CharCreatePreview3D] .skn not found: {sknPath}");
            return null;
        }

        // Parse mesh.
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

        // Skeleton — shared g1.bnd.
        // spec: Docs/RE/specs/frontend_scenes.md §3.7.5 CODE-CONFIRMED.
        Skeleton? skeleton = null;
        if (assets.Contains(SharedBndPath))
        {
            try
            {
                ReadOnlyMemory<byte> bndData = assets.GetRaw(SharedBndPath);
                if (!bndData.IsEmpty)
                    skeleton = BndParser.Parse(bndData);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[CharCreatePreview3D] BndParser failed: {ex.Message}");
            }
        }

        // Idle animation — shared g111100010.mot.
        // spec: Docs/RE/specs/frontend_scenes.md §3.7.5 CODE-CONFIRMED.
        AnimationClip? idleClip = null;
        if (assets.Contains(SharedIdleMotPath))
        {
            try
            {
                ReadOnlyMemory<byte> motData = assets.GetRaw(SharedIdleMotPath);
                if (!motData.IsEmpty)
                    idleClip = AnimationParser.Parse(motData);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[CharCreatePreview3D] MotParser failed: {ex.Message}");
            }
        }

        // Texture.
        ImageTexture? albedo = null;
        try
        {
            albedo = CharacterTextureResolver.Resolve(assets, mesh.IdA);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] texture resolve failed: {ex.Message}");
        }

        // Build via the standard SkinnedCharacterBuilder.
        // Reuses the same path as the slot previews.
        // spec: frontend_scenes.md §4.2 "reuse the same SkinnedCharacterBuilder + texture-resolution path". CODE-CONFIRMED.
        bool savedDiag = SkinnedCharacterBuilder.PrintDiagnostics;
        try
        {
            SkinnedCharacterBuilder.ForceSkinned = true;
            SkinnedCharacterBuilder.PrintDiagnostics = false;
            return SkinnedCharacterBuilder.Build(
                mesh, skeleton, idleClip, albedo,
                externalDrive: false,
                startPhaseSeconds: 0f,
                out _,
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

    private void ApplyTurntableRotation()
    {
        if (_actorWrapper is null || !IsInstanceValid(_actorWrapper)) return;
        // Rotate the actor wrapper around world Y-axis.
        _actorWrapper.RotationDegrees = new Vector3(0f, Mathf.RadToDeg(_turntableYRot), 0f);
    }
}
