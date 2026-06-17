// Screens/LoadingScreen.cs
//
// The loading screen (Diamond_LoadingWindow analogue) shown between server-select
// and character-select (and again on enter-world).
//
// COMPOSITION (spec: Docs/RE/specs/frontend_scenes.md §2L, §9.1 — CODE-CONFIRMED):
//
//   ┌─ Background ──────────────────────────────────────────────────────────────────┐
//   │  One full-screen quad chosen by rand()%3 from exactly three DDS:              │
//   │    0 → data/ui/loading.dds                                                    │
//   │    1 → data/ui/loading06.dds                                                  │
//   │    2 → data/ui/loading08.dds                                                  │
//   │  Drawn full-screen, UV U[0,1] V[0,0.75] (top 3/4 of DDS height).             │
//   │  spec: frontend_scenes.md §2L.1 / §9.1. CODE-CONFIRMED (V=0.75 CONFIRMED).   │
//   └───────────────────────────────────────────────────────────────────────────────┘
//
//   ┌─ Progress bar ────────────────────────────────────────────────────────────────┐
//   │  Track rect in 1024×768 design-space (centre-origin, +Y up):                 │
//   │    x ∈ [−499, −170], y ∈ [−363, −140]  (lower-left of centre)               │
//   │  Fill = sub-rect of the SAME loading DDS:                                     │
//   │    u ∈ [≈0.754, ≈0.969], v ∈ [≈0.432, ≈0.75]   PLAUSIBLE/sample-unverified  │
//   │  Fill width = 223 × percent/100 (≤ 223), left-to-right, invisible at 0%.     │
//   │  spec: frontend_scenes.md §2L.1 / §9.1. CODE-CONFIRMED rect + fill rule.     │
//   │  NO solid-colour fallback bar. If DDS absent → bar is simply not drawn.      │
//   └───────────────────────────────────────────────────────────────────────────────┘
//
//   ┌─ Audio ───────────────────────────────────────────────────────────────────────┐
//   │  BGM 920100100 (data/sound/2d/920100100.ogg), looping, category-0 slot.      │
//   │  spec: frontend_scenes.md §2L.1 / §9.1. CODE-CONFIRMED.                      │
//   │  Stopped explicitly before emitting LoadingComplete so the category-0 slot    │
//   │  is clean for char-select BGM 920100200. spec: §3.8.1 fix contract.           │
//   └───────────────────────────────────────────────────────────────────────────────┘
//
//   ┌─ No caption / no spinner / no percent text ───────────────────────────────────┐
//   │  Zero font/text calls. Any wording visible to the player is baked into the   │
//   │  DDS art. spec: frontend_scenes.md §9.1. CODE-CONFIRMED.                     │
//   └───────────────────────────────────────────────────────────────────────────────┘
//
//   ┌─ Advance ─────────────────────────────────────────────────────────────────────┐
//   │  Preload-done flag + 500 ms grace → emit LoadingComplete.                     │
//   │  NOT on bar reaching 100%. spec: frontend_scenes.md §2L.3. CODE-CONFIRMED.   │
//   └───────────────────────────────────────────────────────────────────────────────┘
//
// OFFLINE / DEV MODE:
//   When no external worker is supplied, the legacy BootFlow path can still drive percent 0→100
//   over ~1.2 s. SceneHost/LoadScene supplies the real Application LoadOrchestrator worker and
//   progress provider instead.
//   spec: frontend_scenes.md §2L.2 "a revival can drive its own 0..1 float". CODE-CONFIRMED.
//
// THREADING: all Control mutation on the main thread (_Process + timers).
// PASSIVE: zero game logic. Emits LoadingComplete; BootFlow decides what to do.

using Godot;
using MartialHeroes.Client.Godot.Dev;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// Full-screen loading screen (Diamond_LoadingWindow analogue).
/// Emits <see cref="LoadingCompleteEventHandler"/> after the simulated preload finishes and
/// a 500 ms grace period elapses. BootFlow advances to char-select on this signal.
/// <para>spec: Docs/RE/specs/frontend_scenes.md §2L, §9.1. CODE-CONFIRMED.</para>
/// </summary>
public sealed partial class LoadingScreen : Control
{
    // =========================================================================
    // Constants — spec: frontend_scenes.md §2L.1 / §9.1. CODE-CONFIRMED.
    // =========================================================================

    // Design canvas dimensions. spec: frontend_scenes.md §11.0. CODE-CONFIRMED.
    private const float RefWidth = 1024f; // spec §11.0. CODE-CONFIRMED.
    private const float RefHeight = 768f; // spec §11.0. CODE-CONFIRMED.

    // Track rect in design-space (centre-origin, +Y up).
    // spec: frontend_scenes.md §2L.1 / §9.1 "x ∈ [−499,−170], y ∈ [−363,−140]". CODE-CONFIRMED.
    private const float TrackDesignX1 = -499f; // left edge.  spec §2L.1. CODE-CONFIRMED.
    private const float TrackDesignX2 = -170f; // right edge. spec §2L.1. CODE-CONFIRMED.
    private const float TrackDesignY1 = -363f; // bottom (lower in +Y-up space). spec §2L.1. CODE-CONFIRMED.
    private const float TrackDesignY2 = -140f; // top    (upper in +Y-up space). spec §2L.1. CODE-CONFIRMED.

    // Maximum fill width in design-pixels.
    // spec: frontend_scenes.md §2L.1 "fill width = 223 × percent/100, clamped to 223". CODE-CONFIRMED.
    private const float MaxFillWidthDesign = 223f; // spec §2L.1. CODE-CONFIRMED.

    // Background DDS candidates (rand()%3 over exactly these three files).
    // spec: frontend_scenes.md §2L.1 / §9.1. CODE-CONFIRMED.
    private static readonly string[] BgPaths =
    [
        "data/ui/loading.dds", // index 0. spec §9.1. CODE-CONFIRMED.
        "data/ui/loading06.dds", // index 1. spec §9.1. CODE-CONFIRMED.
        "data/ui/loading08.dds", // index 2. spec §9.1. CODE-CONFIRMED.
    ];

    // Background UV V-crop: sample V ∈ [0, 0.75] (top 3/4 of the DDS height).
    // spec: frontend_scenes.md §2L.1 "V[0,0.75]". CODE-CONFIRMED (V=0.75 CONFIRMED).
    private const float BgVCrop = 0.75f; // spec §2L.1. CODE-CONFIRMED.

    // Fill bar sub-rect UV from the same loading DDS.
    // spec: frontend_scenes.md §9.1 "u ∈ [≈0.754,≈0.969], v ∈ [≈0.432,≈0.75]". PLAUSIBLE.
    private const float FillUMin = 0.754f; // spec §9.1. PLAUSIBLE.
    private const float FillUMax = 0.969f; // spec §9.1. PLAUSIBLE.
    private const float FillVMin = 0.432f; // spec §9.1. PLAUSIBLE.
    private const float FillVMax = 0.750f; // spec §9.1. PLAUSIBLE.

    // BGM. spec: frontend_scenes.md §2L.1 / §9.1 "920100100 looping category-0". CODE-CONFIRMED.
    private const string BgmPath = "data/sound/2d/920100100.ogg"; // spec §9.1. CODE-CONFIRMED.

    // Grace period after preload complete. spec: frontend_scenes.md §2L.3 "500 ms". CODE-CONFIRMED.
    private const float GraceSeconds = 0.5f; // spec §2L.3. CODE-CONFIRMED.

    // Simulated preload duration (offline mode; no real VFS bulk-preload worker wired yet).
    // spec: frontend_scenes.md §2L.2 "revival drives its own float". CODE-CONFIRMED.
    private const float SimPreloadSeconds = 1.25f;

    // =========================================================================
    // View state
    // =========================================================================

    // Canvas inside 1024×768 reference space.
    private Control? _canvas;

    // Background quad — textured from the chosen loading DDS with V-crop.
    // If the DDS is absent the node has no texture and renders as transparent (no fallback).
    private TextureRect? _bgRect;

    // Bar fill — textured from the same loading DDS sub-rect.
    // If the DDS is absent the fill TextureRect has no texture and is invisible.
    // NO solid-colour fallback bar per spec §9.1 and brief rule.
    private TextureRect? _barFillTex;

    // Progress.
    private float _progressT; // 0..1 elapsed fraction of SimPreloadSeconds
    private int _percent; // 0..100 integer bar value
    private bool _preloadDone;
    private bool _gracePending;

    // BGM player.
    private AudioStreamPlayer? _bgmPlayer;

    // Asset loader.
    private UiAssetLoader? _sharedAssets;
    private bool _ownsAssets;

    private Func<int>? _percentProvider;
    private bool _externalCompletion;
    private bool _playOwnBgm = true;

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>Optional shared asset loader (injected by BootFlow).</summary>
    public UiAssetLoader? SharedAssets
    {
        get => _sharedAssets;
        set => _sharedAssets = value;
    }

    /// <summary>
    /// Optional external progress source (0..100). LoadScene wires this to LoadOrchestrator so the
    /// screen renders the real state-2 worker's progress instead of the offline simulation.
    /// spec: Docs/RE/specs/resource_pipeline.md §2.4; frontend_scenes.md §2L.2.
    /// </summary>
    public Func<int>? PercentProvider
    {
        get => _percentProvider;
        set => _percentProvider = value;
    }

    /// <summary>
    /// True when an external worker calls <see cref="CompleteExternalLoad"/>. In this mode _Process
    /// only renders progress and never self-completes from the placeholder timer.
    /// </summary>
    public bool ExternalCompletion
    {
        get => _externalCompletion;
        set => _externalCompletion = value;
    }

    /// <summary>
    /// Legacy BootFlow compatibility: when false, audio is owned by the Application loading sound
    /// sink (AudioService) rather than this screen-local player.
    /// </summary>
    public bool PlayOwnBgm
    {
        get => _playOwnBgm;
        set => _playOwnBgm = value;
    }

    // =========================================================================
    // Signal
    // =========================================================================

    /// <summary>
    /// Emitted after the preload worker completes + the 500 ms grace elapses.
    /// BootFlow advances to char-select on this signal.
    /// <para>spec: Docs/RE/specs/frontend_scenes.md §2L.3. CODE-CONFIRMED.</para>
    /// </summary>
    [Signal]
    public delegate void LoadingCompleteEventHandler();

    // =========================================================================
    // Godot lifecycle
    // =========================================================================

    public override void _Ready()
    {
        GD.Print("[LoadingScreen] _Ready — entering loading screen. spec: frontend_scenes.md §2L.");

        // Choose background by rand()%3. spec §2L.1. CODE-CONFIRMED.
        int bgIndex = (int)(global::Godot.GD.Randi() % 3u); // spec §2L.1 "rand()%3". CODE-CONFIRMED.
        GD.Print($"[LoadingScreen] BG index={bgIndex} → {BgPaths[bgIndex]}. spec §2L.1.");

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

        BuildCanvas(bgIndex);
        if (_playOwnBgm)
        {
            StartBgm();
        }

        GD.Print(
            $"[LoadingScreen] Built. BG={BgPaths[bgIndex]}, externalCompletion={_externalCompletion}. spec §2L.");
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

        if (_percentProvider is not null)
        {
            int provided = Math.Clamp(_percentProvider(), 0, 100);
            if (provided != _percent)
            {
                _percent = provided;
                UpdateBar();
            }

            if (_externalCompletion)
            {
                return;
            }
        }

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
            GD.Print("[LoadingScreen] Preload simulation done. Starting 500 ms grace. spec §2L.3. CODE-CONFIRMED.");

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

        GD.Print("[LoadingScreen] Grace period elapsed → emitting LoadingComplete. spec §2L.3.");

        // Stop BGM before char-select takes the category-0 slot with 920100200.
        // spec: frontend_scenes.md §3.8.1 fix contract "explicitly STOP at each scene boundary".
        _bgmPlayer?.Stop();

        EmitSignal(SignalName.LoadingComplete);
    }

    /// <summary>
    /// Called by LoadScene when the real Application LoadOrchestrator completes. The screen then
    /// preserves the confirmed 500 ms grace before emitting LoadingComplete.
    /// spec: Docs/RE/specs/frontend_scenes.md §2L.3.
    /// </summary>
    public void CompleteExternalLoad()
    {
        if (_preloadDone)
        {
            return;
        }

        _preloadDone = true;
        if (_percentProvider is not null)
        {
            _percent = Math.Clamp(_percentProvider(), 0, 100);
        }

        UpdateBar();
        GD.Print("[LoadingScreen] External preload done. Starting 500 ms grace. spec §2L.3.");
        SceneTreeTimer grace = GetTree().CreateTimer(GraceSeconds, processAlways: true);
        grace.Timeout += OnGraceExpired;
    }

    // =========================================================================
    // UI construction
    // =========================================================================

    private void BuildCanvas(int bgIndex)
    {
        // This Control fills the full ScreenHost canvas (1024×768).
        Size = new Vector2(RefWidth, RefHeight);
        MouseFilter = MouseFilterEnum.Stop; // block clicks behind this screen

        _canvas = new Control
        {
            Name = "LoadingCanvas",
            Position = Vector2.Zero,
            Size = new Vector2(RefWidth, RefHeight),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_canvas);

        // ── Background quad ──────────────────────────────────────────────────────────
        // Full-screen TextureRect, UV V-cropped to [0, 0.75] via AtlasTexture.
        // spec: frontend_scenes.md §2L.1 / §9.1. CODE-CONFIRMED.
        // If the DDS is absent: the TextureRect has no texture → renders transparent.
        _bgRect = new TextureRect
        {
            Name = "BgRect",
            Position = Vector2.Zero,
            Size = new Vector2(RefWidth, RefHeight),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _canvas.AddChild(_bgRect);

        LoadBackgroundTexture(bgIndex);

        // ── Progress bar ─────────────────────────────────────────────────────────────
        BuildProgressBar();
    }

    private void BuildProgressBar()
    {
        if (_canvas is null) return;

        // Convert design-space (centre-origin, +Y up) to canvas-space (top-left, +Y down).
        // Canvas centre in canvas-space: (RefWidth/2, RefHeight/2) = (512, 384).
        // spec: frontend_scenes.md §2L.1 / §9.1 "x ∈ [−499,−170], y ∈ [−363,−140]". CODE-CONFIRMED.
        //
        //   canvas_x_left  = 512 + (−499) = 13
        //   canvas_y_top   = 384 − (−140) = 524
        //   canvas_y_bot   = 384 − (−363) = 747

        float cx = RefWidth / 2f; // 512
        float cy = RefHeight / 2f; // 384

        float trackX = cx + TrackDesignX1; // 13
        float trackY = cy - TrackDesignY2; // 524
        float trackH = TrackDesignY2 - TrackDesignY1; // 223 (note: track height ≈ max fill width)

        // Fill bar: textured sub-rect of the loading DDS.
        // spec: frontend_scenes.md §9.1 "fill = sub-rect of same DDS". CODE-CONFIRMED.
        // NO solid-colour fallback. spec §9.1 / brief rule.
        _barFillTex = new TextureRect
        {
            Name = "BarFillTex",
            Position = new Vector2(trackX, trackY),
            Size = new Vector2(0f, trackH), // width starts at 0 (invisible at 0%). spec §9.1. CODE-CONFIRMED.
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false, // hidden until DDS is wired and percent > 0
        };
        _canvas.AddChild(_barFillTex);
    }

    // =========================================================================
    // Bar update — spec: frontend_scenes.md §2L.1 / §9.1. CODE-CONFIRMED.
    // =========================================================================

    private void UpdateBar()
    {
        // Fill width = 223 × percent / 100, clamped to 223, growing left-to-right.
        // spec: frontend_scenes.md §2L.1 "fill width = 223·percent/100". CODE-CONFIRMED.
        float fillW = Math.Clamp(MaxFillWidthDesign * _percent / 100f, 0f, MaxFillWidthDesign);

        // Invisible at 0%. spec: §9.1 "drawn only when percent ≠ 0". CODE-CONFIRMED.
        bool visible = _percent > 0 && _barFillTex?.Texture is not null;

        if (_barFillTex is not null)
        {
            _barFillTex.Size = _barFillTex.Size with { X = fillW };
            _barFillTex.Visible = visible;
        }
    }

    // =========================================================================
    // Asset loading
    // =========================================================================

    private void LoadBackgroundTexture(int bgIndex)
    {
        if (_sharedAssets is null || !_sharedAssets.HasVfs)
        {
            GD.Print("[LoadingScreen] VFS offline — background quad will be transparent (no DDS available).");
            return;
        }

        try
        {
            // Load the chosen loading DDS. spec: frontend_scenes.md §2L.1 "one of three DDS". CODE-CONFIRMED.
            Texture2D? tex = _sharedAssets.LoadAtlas(BgPaths[bgIndex]);
            if (tex is null)
            {
                GD.Print(
                    $"[LoadingScreen] Loading DDS not found in VFS: {BgPaths[bgIndex]} — background will be transparent.");
                return;
            }

            GD.Print($"[LoadingScreen] BG ... {BgPaths[bgIndex]} loaded ({tex.GetWidth()}×{tex.GetHeight()}).");

            // Apply background with V-crop [0, 0.75]. spec §2L.1 "V[0,0.75]". CODE-CONFIRMED.
            ApplyBackgroundTexture(tex);

            // Wire the fill bar with the same DDS sub-rect.
            // spec: frontend_scenes.md §9.1 "fill = sub-rect of same DDS". CODE-CONFIRMED.
            ApplyFillTexture(tex);
        }
        catch (Exception ex)
        {
            GD.PrintErr(
                $"[LoadingScreen] LoadBackgroundTexture failed: {ex.Message} — background will be transparent.");
        }
    }

    private void ApplyBackgroundTexture(Texture2D tex)
    {
        if (_bgRect is null) return;

        int texW = tex.GetWidth();
        int texH = tex.GetHeight();

        // Crop: full width, top 75% of texture height.
        // spec: frontend_scenes.md §2L.1 "V[0,0.75]". CODE-CONFIRMED.
        float cropH = texH * BgVCrop; // spec §2L.1. CODE-CONFIRMED.

        var atlas = new AtlasTexture
        {
            Atlas = tex,
            Region = new Rect2(0f, 0f, texW, cropH),
            FilterClip = false,
        };

        _bgRect.Texture = atlas;
        _bgRect.StretchMode = TextureRect.StretchModeEnum.Scale;
        GD.Print($"[LoadingScreen] BG applied ({texW}×{texH}, V-crop to {cropH:F0}px). spec §2L.1.");
    }

    private void ApplyFillTexture(Texture2D tex)
    {
        if (_barFillTex is null) return;

        int texW = tex.GetWidth();
        int texH = tex.GetHeight();

        // Fill sub-rect: u ∈ [0.754, 0.969], v ∈ [0.432, 0.75].
        // spec: frontend_scenes.md §9.1. PLAUSIBLE/sample-unverified.
        float srcX = texW * FillUMin; // spec §9.1. PLAUSIBLE.
        float srcW = texW * (FillUMax - FillUMin); // spec §9.1. PLAUSIBLE.
        float srcY = texH * FillVMin; // spec §9.1. PLAUSIBLE.
        float srcH = texH * (FillVMax - FillVMin); // spec §9.1. PLAUSIBLE.

        var fillAtlas = new AtlasTexture
        {
            Atlas = tex,
            Region = new Rect2(srcX, srcY, srcW, srcH),
            FilterClip = false,
        };

        _barFillTex.Texture = fillAtlas;
        // _barFillTex remains invisible until UpdateBar() is called with percent > 0.
        GD.Print(
            $"[LoadingScreen] Fill bar sub-rect applied (U[{FillUMin:F3},{FillUMax:F3}] V[{FillVMin:F3},{FillVMax:F3}]). spec §9.1 PLAUSIBLE.");
    }

    // =========================================================================
    // BGM
    // =========================================================================

    private void StartBgm()
    {
        // Load and start loading BGM 920100100 (looping, category-0 music slot).
        // spec: frontend_scenes.md §2L.1 / §9.1 "920100100 looping". CODE-CONFIRMED.
        _bgmPlayer = new AudioStreamPlayer
        {
            Name = "LoadingBgmPlayer",
            VolumeDb = 0f,
            Bus = "Master",
        };
        AddChild(_bgmPlayer);

        try
        {
            using RealClientAssets? ra = RealClientAssets.TryOpen();

            if (ra is not null)
            {
                ReadOnlyMemory<byte> raw = ra.GetRaw(BgmPath);
                if (!raw.IsEmpty)
                {
                    var stream = AudioStreamOggVorbis.LoadFromBuffer(raw.ToArray());
                    if (stream is not null)
                    {
                        stream.Loop = true; // spec §2L.1 "looping cue". CODE-CONFIRMED.
                        _bgmPlayer.Stream = stream;
                        _bgmPlayer.Play();
                        GD.Print($"[LoadingScreen] BGM 920100100 started (looping). spec §2L.1. CODE-CONFIRMED.");
                    }
                    else
                    {
                        GD.Print($"[LoadingScreen] BGM OGG decode failed for {BgmPath} — silent.");
                    }
                }
                else
                {
                    GD.Print($"[LoadingScreen] BGM not found in VFS: {BgmPath} — silent.");
                }
            }
            else
            {
                GD.Print("[LoadingScreen] VFS not available for BGM — silent.");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LoadingScreen] StartBgm failed: {ex.Message} — continuing silently.");
        }
    }
}