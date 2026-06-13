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
// KNOWN LIMITATION: The character loaded here is the CANONICAL g202110001 Musa player skin (the
// same proven humanoid rig from RealWorldRenderer), NOT a slot-specific skin from the char-list
// packet — there is no Application event layer feeding the select screen yet. When the
// application-engineer wires SmsgCharacterList events, replace _sknPath with the per-slot value.
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

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    private SubViewportContainer? _container;
    private SubViewport? _subViewport;
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

        // Slot label.
        var lbl = new Label
        {
            Text = IsOccupied ? $"Slot {SlotIndex + 1}" : "(empty)",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            MouseFilter = MouseFilterEnum.Ignore,
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

        // --- Camera framing the character head-to-toe ---
        // We position the camera looking at the preview stage origin along -Z at a distance that
        // shows the character head-to-toe. Scale ×3.0 (spec §3.3) means a ~1-unit tall rest-mesh
        // fills roughly 3 world units; place camera 8 units away on +Z looking at origin.
        // spec: frontend_scenes.md §3.3 — "camera framed head-to-toe". Preview scale ×3.0. CODE-CONFIRMED.
        // Camera positioned 10 units away on +Z, angled slightly downward to frame the character.
        // Use look_at_from_position (works before the node is in the tree) rather than LookAt.
        // spec: frontend_scenes.md §3.3 — camera framed head-to-toe. Portrait feel.
        var camera = new Camera3D
        {
            Name = "PreviewCamera",
            Fov = 22f,
            Near = 0.1f,
            Far = 500f,
        };
        // Set position and rotation explicitly to avoid the "not in tree" LookAt error.
        camera.Position = new Vector3(0f, 4.0f, 10f);
        // Pitch the camera slightly down toward the character (approx atan2(1,10) ≈ 5.7°).
        camera.RotationDegrees = new Vector3(-5.7f, 0f, 0f);
        _subViewport.AddChild(camera);

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
        // Load the canonical humanoid player skin (Musa g202110001) — the proven rig.
        // In a networked build this would use the per-slot spawn descriptor's class/skin.
        // spec: frontend_scenes.md §3.3 — "same in-world player actor factory; no new asset loading".
        string? sknPath = CharacterTextureResolver.PickHumanoidPlayerSkin(assets);
        if (sknPath is null)
        {
            GD.Print($"[CharPreview3D] slot={SlotIndex} no humanoid skin in VFS.");
            return null;
        }

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
}