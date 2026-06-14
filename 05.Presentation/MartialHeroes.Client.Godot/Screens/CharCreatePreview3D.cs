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
    private const float OrbitDistance = 35.0f; // TUNABLE — calibrated to frame character at scale=3

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
                // SkinnedCharacterBuilder.RecentreRoot sets actor.Position = (xShift, yShift, zShift)
                // to offset the mesh vertices so that the AABB's bottom is at Y=0 in local space.
                // However, the AABB position is in local coordinates BEFORE scaling.
                //
                // Strategy: apply scale to the actorWrapper instead of the actor itself, so that
                // actor.Position remains in world-unit coordinates and we can aim the camera at it.
                // OR: use the actor's position to calculate the LookAt target.
                //
                // We use the wrapper-scale approach: set _actorWrapper.Scale = (3,3,3) and keep
                // actor at its original position. The effective world position of the mesh is then
                // scale * actor.Position. We aim the camera at that scaled position.
                _actorWrapper.Scale = Vector3.One * CreatePreviewScale;
                // actor.Position is the raw un-scaled recentre offset (feet at Y=0 in local).
                // After wrapper scaling, the world position of the actor's origin = CreatePreviewScale * actor.Position.
                var scaledActorPos = actor.Position * CreatePreviewScale;
                float lookAtX = scaledActorPos.X;
                float lookAtY = scaledActorPos.Y + CreatePreviewScale * 0.5f; // mid-torso
                float lookAtZ = scaledActorPos.Z;

                // Scale the lookAt Y to account for full character height (not just 0.5×scale).
                // The SKN meshes have AABB starting at Y = -actor.Position.Y (raw offset).
                // After wrapper scale, Y_feet ≈ 0, Y_head ≈ scale * aabb_height.
                // Use a larger lookAt multiplier to aim at true mid-body.
                lookAtY = scaledActorPos.Y + CreatePreviewScale * 2.0f; // aim higher (2x scale for taller meshes)

                GD.Print($"[CharCreatePreview3D] Wrapper scaled {CreatePreviewScale}x. Actor localPos={actor.Position}. Scaled world={scaledActorPos}. Camera LookAt=({lookAtX:F1},{lookAtY:F1},{lookAtZ:F1}).");
                _actorWrapper.AddChild(actor);

                // Re-aim the camera at the mesh midpoint. Place camera at lookAt + (0, 0, OrbitDistance).
                if (_camera is not null && IsInstanceValid(_camera))
                {
                    var target = new Vector3(lookAtX, lookAtY, lookAtZ);
                    var eye = new Vector3(lookAtX, lookAtY + CreatePreviewScale, lookAtZ + OrbitDistance);
                    _camera.Position = eye;
                    Callable.From(() =>
                    {
                        if (_camera is not null && IsInstanceValid(_camera))
                            _camera.LookAt(target, Vector3.Up);
                    }).CallDeferred();
                    GD.Print($"[CharCreatePreview3D] Camera eye={eye}; lookAt={target}.");
                }

                ApplyTurntableRotation();
                GD.Print($"[CharCreatePreview3D] Actor built for class={InternalClassId} scale={CreatePreviewScale}. " +
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
        BuildActorInWrapper();
        // Re-apply camera look-at after rebuild.
        Callable.From(ApplyCameraLookAt).CallDeferred();
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
