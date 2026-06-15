// Screens/LoadingScreen.cs
//
// The loading screen (Diamond_LoadingWindow analogue) shown between server-select
// and character-select (and again on enter-world).
//
// COMPOSITION (spec: Docs/RE/specs/frontend_scenes.md §2L, §9.1 — CODE-CONFIRMED):
//   - Full-screen background: one of three loading DDS chosen by rand()%3:
//       0 → data/ui/loading.dds
//       1 → data/ui/loading06.dds
//       2 → data/ui/loading08.dds
//     Drawn as a full-screen quad, UV crop V ∈ [0, 0.75] (top 3/4 of texture height).
//     spec: frontend_scenes.md §9.1 / §2L.1 "rand()%3 over exactly three DDS". CODE-CONFIRMED.
//     spec: frontend_scenes.md §9.1 "V range [0, 0.75]". PLAUSIBLE/sample-unverified.
//
//   - Progress bar laid out at 1024×768 design resolution (centre-origin, +Y up):
//       Track rect: x ∈ [−499, −170], y ∈ [−363, −140]
//       Fill width = 223 × percent / 100 (clamped to 223), growing left-to-right.
//       Fill drawn only when percent > 0 (invisible at 0%).
//       Fill art: sub-rect of the SAME loading DDS, u ∈ [≈0.754, ≈0.969], v ∈ [≈0.432, ≈0.75].
//     spec: frontend_scenes.md §9.1 / §2L.1. CODE-CONFIRMED track rect.
//     spec: frontend_scenes.md §9.1 fill UV "PLAUSIBLE/sample-unverified".
//
//   - No caption, no spinner, no percent text. Exactly two textured quads per frame.
//     spec: frontend_scenes.md §9.1 "zero text calls". CODE-CONFIRMED.
//
//   - BGM: looping cue 920100100 on the music category-0 slot, started on scene enter.
//     spec: frontend_scenes.md §9.1 / §2L.1. CODE-CONFIRMED. NOT explicitly stopped at
//     teardown in the original — the char-select scene overwrites the category-0 slot.
//     In this revival we stop it explicitly at the scene boundary (§3.8.1 fix contract).
//
//   - Advance: preload-done flag + 500 ms grace. NOT on bar reaching 100%.
//     spec: frontend_scenes.md §2L.3. CODE-CONFIRMED.
//
//   - Pacing: ~10 fps (the original ran at ≈100 ms/frame). In revival we drive a smooth
//     tween and let Godot render at full speed — the 10fps pacing is absorbed by the tween.
//
// OFFLINE / DEV MODE:
//   There is no real VFS bulk-preload worker (that is Application-side and not yet wired).
//   We drive percent 0→100 over ~1.2 seconds on a Godot timer, then after a 500 ms grace
//   emit LoadingComplete. The logic is identical to the spec model — only the data source
//   (real worker vs. timer) differs.
//   spec: frontend_scenes.md §2L.2 "a revival can drive its own 0..1 float". CODE-CONFIRMED.
//
// THREADING: all Control mutation on the main thread (_Process + timers).
// PASSIVE: zero game logic. Emits LoadingComplete; BootFlow decides what to do.

using Godot;
using MartialHeroes.Client.Godot.Dev;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// The full-screen loading screen (Diamond_LoadingWindow analogue).
/// Shown between server-select and char-select on the boot path.
///
/// <para>Emits <see cref="LoadingComplete"/> after the simulated preload finishes and a
/// 500 ms grace period elapses. BootFlow listens for this signal to advance to char-select.</para>
///
/// spec: Docs/RE/specs/frontend_scenes.md §2L, §9.1. CODE-CONFIRMED composition.
/// </summary>
public sealed partial class LoadingScreen : Control
{
    // =========================================================================
    // Constants — spec: frontend_scenes.md §2L.1 / §9.1. CODE-CONFIRMED.
    // =========================================================================

    // Design canvas dimensions (§11.0). CODE-CONFIRMED.
    private const float RefWidth = 1024f; // spec: frontend_scenes.md §11.0. CODE-CONFIRMED.
    private const float RefHeight = 768f; // spec: frontend_scenes.md §11.0. CODE-CONFIRMED.

    // Track rect in design-space (centre-origin, +Y up):
    //   x ∈ [−499, −170], y ∈ [−363, −140].
    // spec: frontend_scenes.md §9.1 / §2L.1. CODE-CONFIRMED.
    private const float TrackDesignX1 = -499f; // spec: §9.1 / §2L.1. CODE-CONFIRMED.
    private const float TrackDesignX2 = -170f; // spec: §9.1 / §2L.1. CODE-CONFIRMED.
    private const float TrackDesignY1 = -363f; // spec: §9.1 / §2L.1. CODE-CONFIRMED (bottom edge).
    private const float TrackDesignY2 = -140f; // spec: §9.1 / §2L.1. CODE-CONFIRMED (top edge).

    // Maximum fill width in design pixels.
    // spec: frontend_scenes.md §2L.1 "fill width = 223 × percent/100, clamped to 223". CODE-CONFIRMED.
    private const float MaxFillWidthDesign = 223f; // spec: §2L.1 / §9.1. CODE-CONFIRMED.

    // Background DDS candidates (rand()%3).
    // spec: frontend_scenes.md §2L.1 / §9.1 "rand()%3 over exactly three DDS". CODE-CONFIRMED.
    private static readonly string[] BgPaths =
    [
        "data/ui/loading.dds",    // index 0. spec: §9.1. CODE-CONFIRMED.
        "data/ui/loading06.dds",  // index 1. spec: §9.1. CODE-CONFIRMED.
        "data/ui/loading08.dds",  // index 2. spec: §9.1. CODE-CONFIRMED.
    ];

    // V-crop for the background: sample V ∈ [0, 0.75] (top 3/4 of the DDS).
    // spec: frontend_scenes.md §2L.1 "UV[0..0.75]". PLAUSIBLE/sample-unverified.
    private const float BgVMax = 0.75f; // spec: §2L.1. PLAUSIBLE.

    // Fill bar sub-rect UV from the same loading DDS (PLAUSIBLE/sample-unverified per spec).
    // spec: frontend_scenes.md §9.1 "u ∈ [≈0.754, ≈0.969], v ∈ [≈0.432, ≈0.75]". PLAUSIBLE.
    private const float FillUMin = 0.754f; // spec: §9.1. PLAUSIBLE.
    private const float FillUMax = 0.969f; // spec: §9.1. PLAUSIBLE.
    private const float FillVMin = 0.432f; // spec: §9.1. PLAUSIBLE.
    private const float FillVMax = 0.750f; // spec: §9.1. PLAUSIBLE.

    // BGM cue on category-0 music slot.
    // spec: frontend_scenes.md §2L.1 / §9.1 "920100100 looping, category-0". CODE-CONFIRMED.
    private const string BgmPath = "data/sound/2d/920100100.ogg"; // spec: §9.1. CODE-CONFIRMED.

    // Grace period after preload complete before emitting LoadingComplete.
    // spec: frontend_scenes.md §2L.3 "500 ms grace". CODE-CONFIRMED.
    private const float GraceSeconds = 0.5f; // spec: §2L.3. CODE-CONFIRMED.

    // Simulated preload duration (no real worker in offline mode).
    // spec: frontend_scenes.md §2L.2 "a revival can drive its own float". CODE-CONFIRMED.
    private const float SimPreloadSeconds = 1.25f; // simulated total preload time.

    // =========================================================================
    // View state
    // =========================================================================

    // Canvas inside the 1024×768 reference space — holds the BG + bar rects.
    private Control? _canvas;

    // Background quad. Styled via a custom atlas/UV-cropped shader.
    private TextureRect? _bgRect;

    // Bar fill rect (grown left-to-right each frame based on _percent).
    private ColorRect? _barFill;
    // Bar fill with texture (shown when texture loads successfully).
    private TextureRect? _barFillTex;

    // Fallback track background (solid color, shown when DDS is unavailable).
    private ColorRect? _barTrack;

    // Which background DDS was chosen this session.
    private int _bgIndex;
    private Texture2D? _bgTexture;

    // Progress: 0.0 → 1.0 float driven by the timer.
    private float _progressT; // 0..1 elapsed fraction of SimPreloadSeconds
    private int _percent; // 0..100 integer bar value
    private bool _preloadDone;
    private bool _gracePending;

    // BGM player.
    private AudioStreamPlayer? _bgmPlayer;

    // Asset loader.
    private UiAssetLoader? _sharedAssets;
    private bool _ownsAssets;

    // =========================================================================
    // Public API — set before AddChild
    // =========================================================================

    /// <summary>Optional shared asset loader (from BootFlow).</summary>
    public UiAssetLoader? SharedAssets
    {
        get => _sharedAssets;
        set => _sharedAssets = value;
    }

    // =========================================================================
    // Signal
    // =========================================================================

    /// <summary>
    /// Emitted after the preload worker completes + the 500 ms grace elapses.
    /// BootFlow listens to this to advance to char-select.
    /// spec: frontend_scenes.md §2L.3. CODE-CONFIRMED.
    /// </summary>
    [Signal]
    public delegate void LoadingCompleteEventHandler();

    // =========================================================================
    // Godot lifecycle
    // =========================================================================

    public override void _Ready()
    {
        GD.Print("[LoadingScreen] _Ready — entering loading screen. spec: frontend_scenes.md §2L.");

        // Choose background by rand()%3. spec: §2L.1. CODE-CONFIRMED.
        _bgIndex = (int)(global::Godot.GD.Randi() % 3u); // spec: §2L.1 "rand()%3". CODE-CONFIRMED.
        GD.Print($"[LoadingScreen] BG index={_bgIndex} → {BgPaths[_bgIndex]}. spec: §2L.1.");

        // Resolve asset loader.
        if (_sharedAssets is not null)
        {
            _ownsAssets = false;
        }
        else
        {
            _sharedAssets = UiAssetLoader.Open();
            _ownsAssets = true;
        }

        // Build the reference-canvas child hierarchy.
        BuildCanvas();

        // Load the background texture from the VFS.
        LoadBackgroundTexture();

        // Start the BGM.
        StartBgm();

        GD.Print($"[LoadingScreen] Built. BG={BgPaths[_bgIndex]}, simulating preload over {SimPreloadSeconds}s. spec: §2L.");
    }

    public override void _ExitTree()
    {
        _bgmPlayer?.Stop();
        if (_ownsAssets)
        {
            _sharedAssets?.Dispose();
            _sharedAssets = null;
        }
    }

    public override void _Process(double delta)
    {
        if (_preloadDone) return;

        // Advance simulated preload 0→1 linearly.
        _progressT += (float)delta / SimPreloadSeconds;
        if (_progressT > 1f) _progressT = 1f;

        int newPercent = (int)(_progressT * 100f);
        if (newPercent != _percent)
        {
            _percent = newPercent;
            UpdateBar();
        }

        if (_progressT >= 1f)
        {
            _preloadDone = true;
            _percent = 100;
            UpdateBar();
            GD.Print("[LoadingScreen] Preload simulation done. Starting 500 ms grace. spec: §2L.3 CODE-CONFIRMED.");

            // Start the 500 ms grace timer.
            // spec: frontend_scenes.md §2L.3 "500 ms grace delay before advancing". CODE-CONFIRMED.
            SceneTreeTimer grace = GetTree().CreateTimer(GraceSeconds, processAlways: true);
            grace.Timeout += OnGraceExpired;
        }
    }

    // =========================================================================
    // Event handlers
    // =========================================================================

    private void OnGraceExpired()
    {
        if (_gracePending) return;
        _gracePending = true;

        GD.Print("[LoadingScreen] Grace period elapsed → emitting LoadingComplete. spec: §2L.3.");

        // Stop BGM before char-select picks up its own 920100200.
        // spec: frontend_scenes.md §3.8.1 fix contract: "explicitly STOP the previous track at
        //   each scene boundary". CODE-CONFIRMED.
        _bgmPlayer?.Stop();

        EmitSignal(SignalName.LoadingComplete);
    }

    // =========================================================================
    // UI construction
    // =========================================================================

    private void BuildCanvas()
    {
        // This Control fills the full ScreenHost canvas (1024×768).
        // The ScreenHost already scales us; we only need to sit at (0,0) inside the canvas.
        Size = new Vector2(RefWidth, RefHeight);
        MouseFilter = MouseFilterEnum.Stop; // block clicks behind this screen

        // Inner canvas node — positions at origin, holds the BG and bar.
        _canvas = new Control
        {
            Name = "LoadingCanvas",
            Position = Vector2.Zero,
            Size = new Vector2(RefWidth, RefHeight),
        };
        _canvas.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_canvas);

        // --- Background quad ---
        // Full-screen TextureRect. UV crop handled via a shader/AtlasTexture (built in
        // LoadBackgroundTexture once the DDS is available). Fallback: solid dark color.
        _bgRect = new TextureRect
        {
            Name = "BgRect",
            Position = Vector2.Zero,
            Size = new Vector2(RefWidth, RefHeight),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _canvas.AddChild(_bgRect);

        // --- Progress bar ---
        BuildProgressBar();
    }

    private void BuildProgressBar()
    {
        if (_canvas is null) return;

        // Convert design-space (centre-origin, +Y up) to canvas-space (top-left, +Y down).
        // Centre of the 1024×768 canvas in canvas-space: (512, 384).
        //
        // Track x_left  = 512 + TrackDesignX1 = 512 + (−499) = 13
        // Track x_right = 512 + TrackDesignX2 = 512 + (−170) = 342
        // Track y_top   = 384 − TrackDesignY2 = 384 − (−140) = 524
        // Track y_bot   = 384 − TrackDesignY1 = 384 − (−363) = 747
        //
        // spec: frontend_scenes.md §9.1 / §2L.1 "x ∈ [−499, −170], y ∈ [−363, −140]". CODE-CONFIRMED.

        float cx = RefWidth / 2f;  // 512
        float cy = RefHeight / 2f; // 384

        float trackX = cx + TrackDesignX1; // 13
        float trackY = cy - TrackDesignY2;  // 524  (top-left in canvas)
        float trackW = TrackDesignX2 - TrackDesignX1; // 329
        float trackH = TrackDesignY2 - TrackDesignY1; // 223

        // Track background (shown always — solid fallback until texture is wired).
        // spec: §9.1 "track quad (always)". CODE-CONFIRMED.
        _barTrack = new ColorRect
        {
            Name = "BarTrack",
            Position = new Vector2(trackX, trackY),
            Size = new Vector2(trackW, trackH),
            Color = new Color(0.08f, 0.06f, 0.04f, 0.80f), // dark brownish fallback
        };
        _barTrack.MouseFilter = MouseFilterEnum.Ignore;
        _canvas.AddChild(_barTrack);

        // Fill rect (solid color fallback; overlaid by a texture version when DDS loads).
        // Width starts at 0 — invisible at 0%. spec: §9.1 "drawn only when percent > 0". CODE-CONFIRMED.
        _barFill = new ColorRect
        {
            Name = "BarFill",
            Position = new Vector2(trackX, trackY),
            Size = new Vector2(0f, trackH),
            Color = new Color(0.82f, 0.65f, 0.18f, 1f), // golden bar fallback
        };
        _barFill.MouseFilter = MouseFilterEnum.Ignore;
        _canvas.AddChild(_barFill);

        // TextureRect fill (shown on top of the solid fallback when DDS is available).
        // The texture region mirrors FillU/V constants set in LoadBackgroundTexture.
        _barFillTex = new TextureRect
        {
            Name = "BarFillTex",
            Position = new Vector2(trackX, trackY),
            Size = new Vector2(0f, trackH),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false, // hidden until DDS loads
        };
        _canvas.AddChild(_barFillTex);
    }

    // =========================================================================
    // Bar update
    // =========================================================================

    private void UpdateBar()
    {
        // Fill width = 223 × percent / 100, clamped to 223, growing left-to-right.
        // spec: frontend_scenes.md §2L.1 / §9.1. CODE-CONFIRMED.
        float fillW = Math.Clamp(MaxFillWidthDesign * _percent / 100f, 0f, MaxFillWidthDesign);

        // Invisible at 0% per spec §9.1. CODE-CONFIRMED.
        bool visible = _percent > 0;

        if (_barFill is not null)
        {
            _barFill.Size = _barFill.Size with { X = fillW };
            _barFill.Visible = visible;
        }

        if (_barFillTex is not null)
        {
            _barFillTex.Size = _barFillTex.Size with { X = fillW };
            _barFillTex.Visible = visible && _barFillTex.Texture is not null;
        }
    }

    // =========================================================================
    // Asset loading
    // =========================================================================

    private void LoadBackgroundTexture()
    {
        if (_sharedAssets is null || !_sharedAssets.HasVfs)
        {
            GD.Print("[LoadingScreen] VFS offline — using solid fallback background.");
            ApplyFallbackBackground();
            return;
        }

        try
        {
            // Load the chosen loading DDS from the VFS.
            // spec: frontend_scenes.md §2L.1 "one of three DDS". CODE-CONFIRMED.
            Texture2D? tex = _sharedAssets.LoadAtlas(BgPaths[_bgIndex]);
            if (tex is null)
            {
                GD.Print($"[LoadingScreen] Loading DDS not found: {BgPaths[_bgIndex]} — using fallback.");
                ApplyFallbackBackground();
                return;
            }

            _bgTexture = tex;

            // Apply the background with V-crop [0, 0.75].
            // spec: frontend_scenes.md §2L.1 "UV crop V ∈ [0, 0.75]". PLAUSIBLE/sample-unverified.
            ApplyBackgroundTexture(tex);

            // Wire the fill TextureRect with the fill sub-rect UV.
            // spec: frontend_scenes.md §9.1 "fill = sub-rect of same DDS". CODE-CONFIRMED.
            // UV values: u ∈ [≈0.754, ≈0.969], v ∈ [≈0.432, ≈0.75]. PLAUSIBLE/sample-unverified.
            ApplyFillTexture(tex);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LoadingScreen] LoadBackgroundTexture failed: {ex.Message} — using fallback.");
            ApplyFallbackBackground();
        }
    }

    private void ApplyBackgroundTexture(Texture2D tex)
    {
        if (_bgRect is null) return;

        // Use an AtlasTexture to crop the V axis to [0, 0.75].
        // spec: frontend_scenes.md §2L.1 "V range [0, 0.75]". PLAUSIBLE.
        int texW = tex.GetWidth();
        int texH = tex.GetHeight();

        // Region: full width, top 75% of height.
        float cropH = texH * BgVMax; // spec: §2L.1 V crop. PLAUSIBLE.

        var atlas = new AtlasTexture
        {
            Atlas = tex,
            Region = new Rect2(0f, 0f, texW, cropH),
            FilterClip = false,
        };

        _bgRect.Texture = atlas;
        _bgRect.StretchMode = TextureRect.StretchModeEnum.Scale;
        GD.Print($"[LoadingScreen] BG texture applied ({texW}×{texH}, V-crop to {cropH}px). spec: §2L.1.");
    }

    private void ApplyFillTexture(Texture2D tex)
    {
        if (_barFillTex is null) return;

        int texW = tex.GetWidth();
        int texH = tex.GetHeight();

        // Fill sub-rect: u ∈ [0.754, 0.969], v ∈ [0.432, 0.75].
        // spec: frontend_scenes.md §9.1. PLAUSIBLE/sample-unverified.
        float srcX = texW * FillUMin; // spec: §9.1. PLAUSIBLE.
        float srcW = texW * (FillUMax - FillUMin); // spec: §9.1. PLAUSIBLE.
        float srcY = texH * FillVMin; // spec: §9.1. PLAUSIBLE.
        float srcH = texH * (FillVMax - FillVMin); // spec: §9.1. PLAUSIBLE.

        var fillAtlas = new AtlasTexture
        {
            Atlas = tex,
            Region = new Rect2(srcX, srcY, srcW, srcH),
            FilterClip = false,
        };

        _barFillTex.Texture = fillAtlas;
        _barFillTex.Visible = false; // starts hidden; UpdateBar() shows it when percent > 0
        GD.Print($"[LoadingScreen] Fill bar texture applied (sub-rect U[{FillUMin:F3},{FillUMax:F3}] V[{FillVMin:F3},{FillVMax:F3}]). spec: §9.1 PLAUSIBLE.");
    }

    private void ApplyFallbackBackground()
    {
        if (_bgRect is null || _canvas is null) return;

        // Solid dark background as fallback when VFS is offline.
        var fallback = new ColorRect
        {
            Name = "BgFallback",
            Position = Vector2.Zero,
            Size = new Vector2(RefWidth, RefHeight),
            Color = new Color(0.05f, 0.04f, 0.06f, 1f),
        };
        fallback.MouseFilter = MouseFilterEnum.Ignore;
        // Insert behind the bar track (add before _barTrack in the tree).
        _canvas.AddChild(fallback);
        _canvas.MoveChild(fallback, 0);

        GD.Print("[LoadingScreen] Using solid fallback background (VFS offline).");
    }

    // =========================================================================
    // BGM
    // =========================================================================

    private void StartBgm()
    {
        // Load and start loading BGM 920100100 (looping, category-0 music slot).
        // spec: frontend_scenes.md §2L.1 / §9.1 "920100100 looping". CODE-CONFIRMED.
        // The player is added as a child so Godot manages its lifecycle.
        _bgmPlayer = new AudioStreamPlayer
        {
            Name = "LoadingBgmPlayer",
            VolumeDb = 0f,
            Bus = "Master",
        };
        AddChild(_bgmPlayer);

        try
        {
            RealClientAssets? ra = null;
            bool ownsRa = false;

            // Try to piggyback on the shared loader's VFS access.
            // UiAssetLoader doesn't expose a GetRaw directly for sound; open our own thin handle.
            ra = RealClientAssets.TryOpen();
            ownsRa = true;

            try
            {
                if (ra is not null)
                {
                    ReadOnlyMemory<byte> raw = ra.GetRaw(BgmPath);
                    if (!raw.IsEmpty)
                    {
                        byte[] bytes = raw.ToArray();
                        var stream = AudioStreamOggVorbis.LoadFromBuffer(bytes);
                        if (stream is not null)
                        {
                            stream.Loop = true; // spec: §2L.1 "looping cue". CODE-CONFIRMED.
                            _bgmPlayer.Stream = stream;
                            _bgmPlayer.Play();
                            GD.Print($"[LoadingScreen] BGM 920100100 started (looping). spec: §2L.1 CODE-CONFIRMED.");
                        }
                        else
                        {
                            GD.Print($"[LoadingScreen] BGM OGG decode failed for {BgmPath}.");
                        }
                    }
                    else
                    {
                        GD.Print($"[LoadingScreen] BGM not found in VFS: {BgmPath}.");
                    }
                }
                else
                {
                    GD.Print("[LoadingScreen] VFS not available for BGM — silent.");
                }
            }
            finally
            {
                if (ownsRa) ra?.Dispose();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LoadingScreen] StartBgm failed: {ex.Message} — continuing silently.");
        }
    }
}
