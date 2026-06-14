// Screens/CharPreview3D.cs
//
// A SubViewport-backed Control that renders a live, idle-animated skinned character preview
// for one character-select slot, matching the legacy GUCanvas3D widget semantics.
//
// SPEC FIDELITY:
//   - The legacy client builds real actors from the slot's spawn descriptor via the standard
//     player actor factory, reusing .skn/.bnd/.mot chains. spec: frontend_scenes.md §3.3.
//   - Stage X offsets {-1560…-1512} (12 apart), Z ≈ -3593, scale ×3.0. spec: §3.3/§9. CODE-CONFIRMED.
//   - Single preview rig instanced 5 times with slot-varied idle phase. Mission brief.
//   - Degrades to the existing 2D placeholder (ColorRect) when VFS is absent or loading fails.
//
// PASSIVE: zero game logic. Reads the VFS via RealClientAssets (via the shared UiAssetLoader's
// underlying asset handle), builds the render node tree via SkinnedCharacterBuilder, and exposes
// the SubViewportContainer as a Godot Control. No domain state, no packet parsing.
//
// IMPORTANT: 3D preview viewport construction is DEFERRED (via CallDeferred) so it happens safely
// on the main thread after _Ready. All Control mutation is main-thread-only.
//
// RIG RESOLUTION (per-slot, not hardcoded):
//   The skin_class (= IdB field in the .skn header) drives the asset chain:
//     skin_class → data/char/skin/g202{skin_class}10001.skn   (primary candidate)
//                → data/char/bind/g{skin_class}.bnd           (.bnd skeleton)
//                → actormotion.txt col2==skin_class col16      (idle .mot)
//   spec: CLAUDE.md §Recovered asset mappings — "skin_class → data/char/bind/g{skin_class}.bnd".
//   spec: frontend_scenes.md §3.3 — "same in-world player actor factory; no new asset loading".
//
//   The class→skin_class mapping used by the offline stub:
//     internal class 1 (Musa)    → skin_class 1  (g202110001.skn, IdB=1)
//     internal class 2 (Tao)     → skin_class 2  (g202220001.skn, IdB=2)
//     internal class 3 (Blader)  → skin_class 3  (g202130001.skn, IdB=3)
//     internal class 4 (Warrior) → skin_class 4  (g202140001.skn, IdB=4)
//   TODO: pin to a spec entry once the class→skin_class table is formally documented.
//   PLAUSIBLE — the g202{X}10001 pattern is confirmed on the real VFS for X in {1,2,3,4} and the
//   IdB field equals X; no capture or formal spec yet maps internal class id → IdB.
//   (Tracked as an open item; see frontend_scenes.md Open questions.)
//
// spec: Docs/RE/specs/frontend_scenes.md §3.3 (live 3D preview actors, stage positions, scale ×3.0).
// spec: Docs/RE/specs/ui_system.md §1 (GUCanvas3D widget role: "renders a live model into a 2D UI rect").
// spec: Docs/RE/formats/mesh.md (.skn/.bnd); Docs/RE/formats/animation.md (.mot).
// spec: Docs/RE/specs/skinning.md (CPU LBS, rest-pose cancellation).

using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Dev;
using MartialHeroes.Client.Godot.Screens.Layout;
using MartialHeroes.Client.Godot.World;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// A Godot <see cref="Control"/> that hosts a <see cref="SubViewport"/> rendering a single
/// idle-animated character preview for one character-select slot.
///
/// <para><b>Construction:</b> set <see cref="SlotIndex"/>, <see cref="IdlePhaseOffset"/>, and
/// optionally <see cref="SharedRealAssets"/> before adding to the scene tree.  The SubViewport is
/// built in <see cref="_Ready"/>.</para>
///
/// <para><b>Degradation:</b> when the VFS is offline or character asset loading fails, the control
/// renders a solid-colour placeholder at the same bounds — the screen still builds.</para>
///
/// spec: Docs/RE/specs/frontend_scenes.md §3.3 — live 3D preview actors (GUCanvas3D equivalent).
/// spec: Docs/RE/specs/ui_system.md §1 (GUCanvas3D role).
/// </summary>
public sealed partial class CharPreview3D : Control
{
    // -------------------------------------------------------------------------
    // Static initialisation — CP949 encoding registered exactly once
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers the CP949 (EUC-KR) encoding provider once per AppDomain.
    /// The static constructor fires on first use of this type, before any instance is created.
    /// Moved here from TryLoadIdleClip (where it was called on every clip load) to satisfy the
    /// "register once" contract.
    /// spec: CLAUDE.md §Core engineering constraints — "Register [CP949] once:
    ///       Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)".
    /// </summary>
    static CharPreview3D()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    // -------------------------------------------------------------------------
    // Configuration (set before _Ready)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Slot index 0..4. Determines the stage X offset for the preview camera framing.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.3 — "per-slot X offsets {-1560…-1512}". CODE-CONFIRMED.
    /// </summary>
    public int SlotIndex { get; set; }

    /// <summary>
    /// Idle animation phase offset in seconds.  Each of the 5 previews starts at a different point
    /// in the idle clip so they don't animate in lockstep.
    /// spec: Mission brief — "slot-varied idle phase".
    /// </summary>
    public float IdlePhaseOffset { get; set; }

    /// <summary>
    /// When true the slot appears "occupied" and a 3D rig is shown (if assets permit).
    /// When false a dim placeholder is rendered (empty slot visual).
    /// spec: Docs/RE/specs/frontend_scenes.md §3.1 — "occupied iff faceA nonzero". CODE-CONFIRMED.
    /// </summary>
    public bool IsOccupied { get; set; } = true;

    /// <summary>
    /// Optional: shared <see cref="RealClientAssets"/> from the owning screen.  When set, this
    /// control reuses the already-open VFS handle rather than opening another one.
    /// </summary>
    public RealClientAssets? SharedRealAssets { get; set; }

    /// <summary>
    /// The skin_class (= IdB in the .skn header) for this slot's character.
    /// Drives the asset chain: g202{SkinClassId}10001.skn → g{SkinClassId}.bnd → actormotion.txt idle.
    /// spec: CLAUDE.md §Recovered asset mappings — "skin_class → data/char/bind/g{skin_class}.bnd".
    /// PLAUSIBLE mapping (class id == IdB for player classes 1..4) — see file header TODO note.
    /// When 0, falls back to the canonical Musa skin (skin_class=1).
    /// </summary>
    public uint SkinClassId { get; set; } = 1;

    /// <summary>Character name to display on the placeholder label overlay.</summary>
    public string SlotCharacterName { get; set; } = string.Empty;

    /// <summary>Character level to display on the placeholder label overlay.</summary>
    public int SlotLevel { get; set; }

    /// <summary>
    /// Human-readable class label (e.g. "Musa", "Tao", "Blader", "Warrior") for the overlay.
    /// Sourced from the ClassLabelFallbacks in the layout, not decoded from VFS here.
    /// </summary>
    public string SlotClassName { get; set; } = string.Empty;

    // =========================================================================
    // Camera keyframe orbit — spec: Docs/RE/specs/frontend_scenes.md §3.5. CODE-CONFIRMED.
    // =========================================================================

    /// <summary>
    /// Active keyframe index (0..5).  Selects which orbit pose the preview camera is placed at.
    /// Initial value is 0 per the spec constructor (spec §3.5.3 "initial active keyframe index = 0").
    /// spec: Docs/RE/specs/frontend_scenes.md §3.5.3 CODE-CONFIRMED.
    /// </summary>
    public int ActiveKeyframe
    {
        get => _activeKeyframe;
        set
        {
            _activeKeyframe = ((value % 6) + 6) % 6; // clamp / wrap 0..5
            ApplyCameraKeyframe();
        }
    }

    private int _activeKeyframe;

    /// <summary>
    /// The 12 π-scaled angle multipliers from the spec §3.5.3.
    /// Indices 0..5 = yaw per keyframe; indices 6..11 = pitch per keyframe.
    /// (yaw/pitch assignment is MEDIUM per spec §3.5.3 — see §3.5.4 note.)
    /// spec: Docs/RE/specs/frontend_scenes.md §3.5.3 CODE-CONFIRMED (values); yaw/pitch split MEDIUM.
    /// </summary>
    private static readonly float[] AngleMultipliers =
    [
        // Yaw multipliers (indices 0..5) × π → radians
        -0.03333334f, // KF0 yaw × π = −0.10472 rad (−6.0°)  spec §3.5.3 CODE-CONFIRMED
        -0.01483333f, // KF1 yaw × π = −0.04660 rad (−2.67°) spec §3.5.3 CODE-CONFIRMED
        0.00333333f, // KF2 yaw × π =  0.01047 rad (+0.60°) spec §3.5.3 CODE-CONFIRMED
        -0.01111111f, // KF3 yaw × π = −0.03491 rad (−2.00°) spec §3.5.3 CODE-CONFIRMED
        0.04333333f, // KF4 yaw × π =  0.13614 rad (+7.80°) spec §3.5.3 CODE-CONFIRMED
        -0.07666667f, // KF5 yaw × π = −0.24086 rad (−13.8°) spec §3.5.3 CODE-CONFIRMED

        // Pitch multipliers (indices 6..11) × π → radians (split MEDIUM per spec §3.5.4 note 3)
        0.01333333f, // KF0 pitch × π =  0.04189 rad (+2.40°) spec §3.5.3 CODE-CONFIRMED
        0.00436111f, // KF1 pitch × π =  0.01370 rad (+0.79°) spec §3.5.3 CODE-CONFIRMED
        -0.20333332f, // KF2 pitch × π = −0.63879 rad (−36.6°) spec §3.5.3 CODE-CONFIRMED
        -0.44444445f, // KF3 pitch × π = −1.39626 rad (−80.0°) spec §3.5.3 CODE-CONFIRMED
        0.41276109f, // KF4 pitch × π =  1.29673 rad (+74.3°) spec §3.5.3 CODE-CONFIRMED
        0.29111111f, // KF5 pitch × π =  0.91455 rad (+52.4°) spec §3.5.3 CODE-CONFIRMED
    ];

    /// <summary>
    /// Camera orbit distance in SubViewport-local units.  The character is placed at origin,
    /// scale ×3.0.  A distance of ~12 gives a head-to-toe portrait at FOV 50°.
    /// TUNABLE: exact look-at framing is runtime-pending (spec §3.5.4 open item 4).
    /// </summary>
    private const float OrbitDistance = 12f; // TUNABLE — pending live-client debugger confirmation

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    private SubViewportContainer? _container;
    private SubViewport? _subViewport;
    private Camera3D? _camera;
    private Node3D? _charRoot;
    private bool _is3DActive;

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        // Build the 2D fallback first (always visible, even during async 3D construction).
        BuildPlaceholder();

        if (IsOccupied)
        {
            // Defer the 3D construction to the next frame so the SubViewport allocation happens
            // cleanly after all parent nodes are in the tree.
            CallDeferred(MethodName.Build3DPreview);
        }
    }

    public override void _ExitTree()
    {
        // SubViewport and its 3D content are freed with us when this Control leaves the tree.
        // Explicit cleanup is not needed; Godot frees the child tree automatically.
        _charRoot = null;
        _subViewport = null;
        _container = null;
    }

    // -------------------------------------------------------------------------
    // 2D fallback placeholder
    // -------------------------------------------------------------------------

    private void BuildPlaceholder()
    {
        // Dark slot-number coloured box as placeholder.  Slightly different per slot so they're
        // visually distinct even without 3D content.
        float brightness = IsOccupied ? 0.22f + SlotIndex * 0.03f : 0.10f;
        var placeholder = new ColorRect
        {
            Name = "Placeholder",
            Color = new Color(brightness * 0.8f, brightness * 0.6f, brightness * 1.0f, 0.9f),
        };
        placeholder.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(placeholder);

        // Slot info label — shows name/class/level for occupied slots.
        // spec: frontend_scenes.md §3.2 — "slot info line shows name, level, position". CODE-CONFIRMED.
        string labelText;
        if (!IsOccupied)
        {
            labelText = "(empty)";
        }
        else if (!string.IsNullOrEmpty(SlotCharacterName))
        {
            // Show name + class + level when the descriptor data has been supplied.
            string classLine = string.IsNullOrEmpty(SlotClassName) ? string.Empty : $"\n{SlotClassName}";
            string levelLine = SlotLevel > 0 ? $"\nLv {SlotLevel}" : string.Empty;
            labelText = $"{SlotCharacterName}{classLine}{levelLine}";
        }
        else
        {
            labelText = $"Slot {SlotIndex + 1}";
        }

        var lbl = new Label
        {
            Text = labelText,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        lbl.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        lbl.AddThemeFontSizeOverride("font_size", 11);
        lbl.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.8f));
        AddChild(lbl);
    }

    // -------------------------------------------------------------------------
    // 3D SubViewport construction (deferred, main thread)
    // -------------------------------------------------------------------------

    private void Build3DPreview()
    {
        if (!IsInstanceValid(this) || !IsOccupied)
            return;

        try
        {
            RealClientAssets? assets = SharedRealAssets;
            bool ownsAssets = false;

            if (assets is null)
            {
                // Try to open a fresh VFS handle (best-effort; null on offline builds).
                try
                {
                    assets = RealClientAssets.TryOpen();
                    ownsAssets = assets is not null;
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[CharPreview3D] slot={SlotIndex} VFS open failed: {ex.Message} — using placeholder.");
                    return;
                }
            }

            if (assets is null)
            {
                GD.Print($"[CharPreview3D] slot={SlotIndex} VFS offline — 2D placeholder active.");
                return;
            }

            try
            {
                BuildSubViewport(assets);
                GD.Print($"[CharPreview3D] slot={SlotIndex} 3D preview built (phase={IdlePhaseOffset:F2}s).");
            }
            finally
            {
                if (ownsAssets)
                    assets.Dispose();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharPreview3D] slot={SlotIndex} Build3DPreview failed: {ex.Message} — placeholder remains.");
        }
    }

    private void BuildSubViewport(RealClientAssets assets)
    {
        Vector2 mySize = Size;
        if (mySize.X < 4f || mySize.Y < 4f)
        {
            // Fallback size if the Control hasn't been laid out yet.
            mySize = new Vector2(128f, 200f);
        }

        int vpW = (int)mySize.X;
        int vpH = (int)mySize.Y;

        // --- SubViewport ---
        _subViewport = new SubViewport
        {
            Name = "CharPreviewVP",
            Size = new Vector2I(vpW, vpH),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = true,
        };

        // --- Camera — 6-keyframe orbit (CODE-CONFIRMED geometry; framing runtime-pending).
        //
        // FOV 50°, near 5.0, far 15000.0.
        // spec: Docs/RE/specs/frontend_scenes.md §3.5.1 CODE-CONFIRMED.
        //
        // The camera is placed on an orbit around the rig (at origin, scale ×3.0) using the
        // 12 π-scaled angle multipliers recovered from the spec §3.5.3.
        // Indices 0..5 = yaw per keyframe × π; indices 6..11 = pitch per keyframe × π.
        // Yaw/pitch split is MEDIUM (spec §3.5.4 note 3) — implemented here as the natural split.
        //
        // The absolute stage-world positions from §3.5.2 are NOT used directly because in our
        // isolated SubViewport the rig is at the origin (not at the stage-world centre −1536,0,−3593).
        // Instead, the 12 angle multipliers drive the orbit pose around the local origin.
        //
        // Look-at target is the rig waist (~Y=+4 in SubViewport units for a scale-×3 rig).
        // TUNABLE: exact look-at framing is runtime-pending (spec §3.5.4 open item 4).
        //
        // spec: Docs/RE/specs/frontend_scenes.md §3.5 — orbit geometry CODE-CONFIRMED; framing/easing
        //       runtime-pending; implement spec FOV/near/far and the 12 angle multipliers.
        _camera = new Camera3D
        {
            Name = "PreviewCamera",
            Fov = 50f, // spec: Docs/RE/specs/frontend_scenes.md §3.5.1 CODE-CONFIRMED
            Near = 5f, // spec: Docs/RE/specs/frontend_scenes.md §3.5.1 CODE-CONFIRMED
            Far = 15000f, // spec: Docs/RE/specs/frontend_scenes.md §3.5.1 CODE-CONFIRMED
            KeepAspect = Camera3D.KeepAspectEnum.Height, // vertical FOV + aspect = standard
        };
        _subViewport.AddChild(_camera);
        // Pre-position the camera without LookAt (which requires being in the SceneTree).
        // We compute position + rotation manually here using the same orbit formula.
        // After the SubViewport is in the tree, a deferred call corrects the rotation via LookAt.
        // spec: Docs/RE/specs/frontend_scenes.md §3.5.3 CODE-CONFIRMED (angle multipliers).
        PositionCameraWithoutLookAt(_activeKeyframe);
        // Deferred look-at once the SubViewport is in the SceneTree.
        Callable.From(ApplyCameraKeyframe).CallDeferred();

        // --- Simple key light ---
        var light = new DirectionalLight3D
        {
            Name = "PreviewLight",
            LightEnergy = 1.2f,
            LightColor = new Color(1.0f, 0.97f, 0.90f),
        };
        light.RotationDegrees = new Vector3(-45f, 30f, 0f);
        _subViewport.AddChild(light);

        // Soft fill light from below-front.
        var fill = new OmniLight3D
        {
            Name = "FillLight",
            LightEnergy = 0.4f,
            LightColor = new Color(0.6f, 0.7f, 1.0f),
            OmniRange = 30f,
            Position = new Vector3(0f, -2f, 5f),
        };
        _subViewport.AddChild(fill);

        // --- Character rig ---
        Node3D? charRoot = TryBuildCharacterRig(assets);
        if (charRoot is not null)
        {
            // Centre in front of camera.
            // The rig is recentred by SkinnedCharacterBuilder so feet are near Y=0.
            // Apply the scale spec: ×3.0. spec: frontend_scenes.md §3.3.
            // stage-X is part of the original world-space layout; in the isolated SubViewport
            // we place the rig at the world origin so the camera framing is simple.
            charRoot.Scale = Vector3.One * CharacterSelectLayout.PreviewScale; // spec §3.3/§9 CODE-CONFIRMED
            charRoot.Position = Vector3.Zero;
            _subViewport.AddChild(charRoot);
            _charRoot = charRoot;
            _is3DActive = true;
        }
        else
        {
            GD.Print($"[CharPreview3D] slot={SlotIndex} character rig failed — placeholder only.");
            _subViewport.QueueFree();
            _subViewport = null;
            return;
        }

        // --- SubViewportContainer —overlaid on top of the placeholder ---
        _container = new SubViewportContainer
        {
            Name = "PreviewContainer",
            Stretch = true,
        };
        _container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // The container must not eat mouse events meant for the parent slot selector.
        _container.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_container);
        _container.AddChild(_subViewport);

        GD.Print($"[CharPreview3D] slot={SlotIndex} SubViewport {vpW}×{vpH} built, 3D active={_is3DActive}.");
    }

    // -------------------------------------------------------------------------
    // Character rig construction
    // -------------------------------------------------------------------------

    private Node3D? TryBuildCharacterRig(RealClientAssets assets)
    {
        // Resolve skin by SkinClassId (= IdB in .skn header = the slot's skin_class / player class).
        // Asset chain: skin_class → data/char/skin/g202{skin_class}10001.skn (primary candidate).
        // spec: CLAUDE.md §Recovered asset mappings — "skin_class → data/char/bind/g{skin_class}.bnd
        //   + the .skn whose IdB == skin_class".
        // PLAUSIBLE: class id == IdB for standard player classes 1..4 — see file header TODO note.
        string? sknPath = PickSkinForClass(assets, SkinClassId);
        if (sknPath is null)
        {
            GD.Print($"[CharPreview3D] slot={SlotIndex} no humanoid skin in VFS for skin_class={SkinClassId}.");
            return null;
        }

        GD.Print($"[CharPreview3D] slot={SlotIndex} resolved skin_class={SkinClassId} → '{sknPath}'.");

        SkinnedMesh? mesh = null;
        try
        {
            ReadOnlyMemory<byte> raw = assets.GetRaw(sknPath);
            if (raw.IsEmpty)
            {
                GD.PrintErr($"[CharPreview3D] slot={SlotIndex} .skn '{sknPath}' empty in VFS.");
                return null;
            }

            mesh = SknParser.Parse(raw);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharPreview3D] slot={SlotIndex} SknParser failed: {ex.Message}");
            return null;
        }

        // Skeleton (.bnd): g{IdB}.bnd.
        // spec: Docs/RE/formats/mesh.md §.skn header (id_b → .bnd actor_id). CONFIRMED.
        Skeleton? skeleton = null;
        if (mesh.IdB != 0)
        {
            string bndPath = $"data/char/bind/g{mesh.IdB}.bnd";
            if (assets.Contains(bndPath))
            {
                try
                {
                    ReadOnlyMemory<byte> data = assets.GetRaw(bndPath);
                    if (!data.IsEmpty)
                        skeleton = BndParser.Parse(data);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[CharPreview3D] slot={SlotIndex} .bnd failed: {ex.Message}");
                }
            }
        }

        // Idle animation clip via actormotion.txt.
        // spec: Docs/RE/formats/mesh.md §actormotion.txt. CONFIRMED.
        AnimationClip? clip = TryLoadIdleClip(assets, mesh.IdB);

        // Diffuse texture.
        // spec: Docs/RE/formats/mesh.md §.skn texture binding via data/char/skin.txt. CONFIRMED.
        ImageTexture? albedo = null;
        try
        {
            albedo = CharacterTextureResolver.Resolve(assets, mesh);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharPreview3D] slot={SlotIndex} texture resolve failed: {ex.Message}");
        }

        // Build via the proven SkinnedCharacterBuilder path (same path as RealWorldRenderer).
        // Use externalDrive=false so the SubViewport's built-in _Process drives the animation.
        // spec: Docs/RE/specs/skinning.md (CPU LBS); frontend_scenes.md §3.3 (no new asset loading).
        //
        // PrintDiagnostics is a static flag. Save the previous value and restore it in finally so
        // that exceptions (or re-entrant deferred calls) cannot leave it in the wrong state.
        // CallDeferred callbacks run sequentially on the main thread, so save/restore in finally
        // is sufficient for thread-safety here. The cleaner long-term fix is a per-call parameter
        // on SkinnedCharacterBuilder.Build — but that requires changing SkinnedCharacterBuilder,
        // which is out of scope for this surgical pass.
        bool savedPrintDiagnostics = SkinnedCharacterBuilder.PrintDiagnostics;
        try
        {
            SkinnedCharacterBuilder.ForceSkinned = true;
            SkinnedCharacterBuilder.PrintDiagnostics = false; // suppress diagnostics for 5 previews
            Node3D root = SkinnedCharacterBuilder.Build(
                mesh, skeleton, clip, albedo,
                externalDrive: false,
                startPhaseSeconds: IdlePhaseOffset, // spec: mission brief — slot-varied phase
                out _,
                debugLabel: $"preview_slot{SlotIndex}");
            return root;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharPreview3D] slot={SlotIndex} SkinnedCharacterBuilder failed: {ex.Message}");
            return null;
        }
        finally
        {
            // Always restore the saved value, even if Build threw.
            SkinnedCharacterBuilder.PrintDiagnostics = savedPrintDiagnostics;
        }
    }

    // -------------------------------------------------------------------------
    // Per-class skin resolution
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves a VFS .skn path for the given <paramref name="skinClassId"/> (= IdB).
    /// <para>Asset chain (spec: CLAUDE.md §Recovered asset mappings):</para>
    /// <list type="number">
    ///   <item>Primary candidate: <c>data/char/skin/g202{skinClassId}10001.skn</c></item>
    ///   <item>Variant scan: <c>g202{skinClassId}10002</c> .. <c>g202{skinClassId}10010</c></item>
    ///   <item>Fallback to <see cref="CharacterTextureResolver.PickHumanoidPlayerSkin"/> (Musa rig).</item>
    /// </list>
    /// <para>
    /// PLAUSIBLE mapping: internal class id == IdB for standard player classes 1..4.
    /// Confirmed on real VFS: g202110001 (IdB=1), g202220001 (IdB=2), g202130001 (IdB=3),
    /// g202140001 (IdB=4). TODO: pin to a formal class→IdB spec entry.
    /// spec: CLAUDE.md §Recovered asset mappings (skin_class / IdB chain). PLAUSIBLE.
    /// </para>
    /// </summary>
    private static string? PickSkinForClass(RealClientAssets assets, uint skinClassId)
    {
        if (skinClassId == 0)
            return CharacterTextureResolver.PickHumanoidPlayerSkin(assets);

        // Primary: g202{skinClassId}10001.skn — the base skin for this class.
        // spec: CLAUDE.md §Recovered asset mappings — confirmed on real VFS for classes 1..4.
        // Pattern: 202{classDigit}10001 where classDigit is the 1-digit class index.
        // For classes 1..4 the pattern is:
        //   class 1 → g202110001.skn  (Musa)      CONFIRMED
        //   class 2 → g202220001.skn  (Tao)        CONFIRMED
        //   class 3 → g202130001.skn  (Blader)     CONFIRMED
        //   class 4 → g202140001.skn  (Warrior)    CONFIRMED
        // The numeric ID schema: 202{class}{10001} where the middle segment encodes the class.
        // Classes 1,3,4 use 2021X0001; class 2 uses 2022X0001 (Tao has a different prefix).
        // PLAUSIBLE — no formal spec; confirmed by VFS probe only.

        // Build skin ID candidates for this class.
        // The Tao class (class 2) uses 2022X0001; others use 2021X0001.
        // TODO: confirm with a formal class→skinId spec entry.
        uint[] candidates = skinClassId switch
        {
            2 => [202220001u, 202220002u, 202220003u], // Tao — "g202220001.skn" etc.
            _ =>
            [
                202100001u + skinClassId * 10000u, // formula: 202{class}10001
                202100002u + skinClassId * 10000u,
                202100003u + skinClassId * 10000u
            ],
        };

        foreach (uint skinId in candidates)
        {
            string path = $"data/char/skin/g{skinId}.skn";
            if (assets.Contains(path))
                return path;
        }

        // Fallback to the canonical Musa skin (the proven rig).
        GD.Print($"[CharPreview3D] PickSkinForClass: skin_class={skinClassId} not found, " +
                 $"falling back to Musa humanoid skin.");
        return CharacterTextureResolver.PickHumanoidPlayerSkin(assets);
    }

    private static AnimationClip? TryLoadIdleClip(RealClientAssets assets, uint actorClassId)
    {
        if (actorClassId == 0) return null;

        const string tablePath = "data/char/actormotion.txt";
        if (!assets.Contains(tablePath)) return null;

        try
        {
            // CP949 provider registered once in the static constructor; no per-call registration needed.
            // spec: CLAUDE.md §Core engineering constraints — "Register [CP949] once".
            string text = System.Text.Encoding.GetEncoding(949).GetString(assets.GetRaw(tablePath).Span);

            foreach (string rawLine in text.Split('\n'))
            {
                string[] cols = rawLine.Replace("\r", string.Empty).Split('\t');
                if (cols.Length <= 16) continue;
                if (!uint.TryParse(cols[2].Trim(), out uint classId) || classId != actorClassId) continue;

                string idle = cols[16].Trim();
                if (idle.Length == 0 || idle == "0") return null;

                string motPath = $"data/char/mot/g{idle}.mot";
                if (!assets.Contains(motPath)) return null;

                ReadOnlyMemory<byte> motData = assets.GetRaw(motPath);
                if (motData.IsEmpty) return null;

                return AnimationParser.Parse(motData);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharPreview3D] TryLoadIdleClip(actorClass={actorClassId}) failed: {ex.Message}");
        }

        return null;
    }

    // -------------------------------------------------------------------------
    // Camera keyframe application
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.2 / §3.5.3 CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Positions the camera at the orbit pose without calling LookAt (safe before node enters tree).
    /// Position is set; rotation will be corrected by the deferred <see cref="ApplyCameraKeyframe"/>
    /// call once the SubViewport is in the SceneTree.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.5.3 CODE-CONFIRMED.
    /// </summary>
    private void PositionCameraWithoutLookAt(int kf)
    {
        if (_camera is null) return;

        float yawRad = AngleMultipliers[kf] * Mathf.Pi;
        float pitchRad = AngleMultipliers[kf + 6] * Mathf.Pi;
        const float lookAtY = 4f;

        float cosPitch = Mathf.Cos(pitchRad);
        float camX = Mathf.Sin(yawRad) * cosPitch * OrbitDistance;
        float camY = Mathf.Sin(pitchRad) * OrbitDistance + lookAtY;
        float camZ = Mathf.Cos(yawRad) * cosPitch * OrbitDistance;

        _camera.Position = new Vector3(camX, camY, camZ);

        // Compute the look-at rotation manually (no SceneTree required).
        // Direction from camera toward the target (0, lookAtY, 0).
        var forward = (new Vector3(0f, lookAtY, 0f) - new Vector3(camX, camY, camZ)).Normalized();
        // Build a Basis that points -Z toward target (Godot camera looks along -Z).
        if (forward.LengthSquared() > 0.001f)
        {
            var right = Vector3.Up.Cross(forward).Normalized();
            if (right.LengthSquared() < 0.001f) right = Vector3.Right; // degenerate pole guard
            var up = forward.Cross(right).Normalized();
            // Camera forward = -Z in Godot's convention.
            _camera.Basis = new Basis(-right, up, forward).Orthonormalized();
        }
    }

    /// <summary>
    /// Places <see cref="_camera"/> at the orbit pose for <see cref="ActiveKeyframe"/>
    /// using the 12 π-scaled angle multipliers from spec §3.5.3.
    /// <para>
    /// The rig is at the SubViewport origin (scale ×3.0).  The camera is placed on a sphere of
    /// radius <see cref="OrbitDistance"/> around a look-at target at Y ≈ +4 (rig waist height).
    /// Yaw and pitch come from the spec angle-multiplier table; indices 0..5 = yaw, 6..11 = pitch.
    /// The yaw/pitch assignment is MEDIUM (spec §3.5.4 note 3) — framing is tunable pending a
    /// live-client debugger confirmation pass.
    /// </para>
    /// spec: Docs/RE/specs/frontend_scenes.md §3.5.3 CODE-CONFIRMED (values);
    ///       §3.5.4 "yaw/pitch assignment MEDIUM; framing runtime-pending".
    /// </summary>
    private void ApplyCameraKeyframe()
    {
        if (_camera is null) return;

        int kf = _activeKeyframe; // 0..5

        // Yaw and pitch in radians, derived from the spec's 12 π-scaled multipliers.
        // spec: Docs/RE/specs/frontend_scenes.md §3.5.3 CODE-CONFIRMED values; split MEDIUM.
        float yawRad = AngleMultipliers[kf] * Mathf.Pi; // indices 0..5
        float pitchRad = AngleMultipliers[kf + 6] * Mathf.Pi; // indices 6..11

        // Look-at target: rig waist at Y ≈ +4 (tunable — spec §3.5.4 open item 4).
        const float lookAtY = 4f; // TUNABLE — pending live-client debugger confirmation

        // Orbit: spherical coordinates (yaw around Y axis, pitch around X axis).
        // x = sin(yaw) * cos(pitch) * D, z = cos(yaw) * cos(pitch) * D, y = sin(pitch) * D.
        float cosPitch = Mathf.Cos(pitchRad);
        float camX = Mathf.Sin(yawRad) * cosPitch * OrbitDistance;
        float camY = Mathf.Sin(pitchRad) * OrbitDistance + lookAtY;
        float camZ = Mathf.Cos(yawRad) * cosPitch * OrbitDistance;

        // Place the camera and look at the target.
        _camera.Position = new Vector3(camX, camY, camZ);
        _camera.LookAt(new Vector3(0f, lookAtY, 0f), Vector3.Up);

        GD.Print($"[CharPreview3D] slot={SlotIndex} camera keyframe {kf}: " +
                 $"yaw={Mathf.RadToDeg(yawRad):F1}° pitch={Mathf.RadToDeg(pitchRad):F1}° " +
                 $"pos=({camX:F2},{camY:F2},{camZ:F2})");
    }
}