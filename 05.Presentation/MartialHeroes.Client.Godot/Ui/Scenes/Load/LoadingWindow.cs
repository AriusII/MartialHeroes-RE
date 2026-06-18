// Ui/Scenes/Load/LoadingWindow.cs
//
// State-2 LoadingWindow — IMMEDIATE-MODE renderer of two textured quads:
//   1. Full-screen background DDS (rand()%3).
//   2. Progress bar quad: 329×223 px track, fill grows LEFT→RIGHT by U-axis (width fill).
//
// SPEC (authoritative): Docs/RE/specs/frontend_layout_tables.md §5 (supersedes frontend_scenes.md §2L).
//
// BACKGROUND:
//   rand()%3 → data/ui/loading.dds | data/ui/loading06.dds | data/ui/loading08.dds.
//   Full-screen (0,0,screenW,screenH). spec: §5.
//
// PROGRESS BAR:
//   Track in design-space (centre-origin ortho): X span −499..−170 (329 px wide),
//   Y span −363..−140 (223 px tall). Lower-center placement.
//   Fill = clamp(223 · pct/100, 0, 223), normalized /1024 → max U 223/1024 ≈ 0.2178.
//   Fill is a WIDTH fill (U-axis), bar height is always 223 px.
//   spec: Docs/RE/specs/frontend_layout_tables.md §5.
//
// AUDIO:
//   Looped 2D cue 920100100, category 0 (single direct voice → cannot double-stack).
//   spec: Docs/RE/specs/frontend_layout_tables.md §5/§7.
//
// COMPLETION:
//   Worker done + 500 ms grace → emit LoadingComplete. LoadScene drives the advance.
//   spec: §5 "loading active flag cleared by background loader + 500 ms grace".
//
// THREADING: all Control mutation on the main thread (_Process + timers). PASSIVE.

using Godot;
using MartialHeroes.Client.Godot.Audio;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Load;

/// <summary>
/// Full-screen loading window (Diamond_LoadingWindow analogue) — immediate-mode two-quad renderer.
///
/// <para>Emits <see cref="LoadingCompleteEventHandler"/> after the external load worker completes
/// plus a 500 ms grace period. LoadScene advances the scene spine on this signal.</para>
///
/// <para>Strictly passive: reads atlases from HudAtlasLibrary, turns worker progress into a
/// U-axis fill width, plays BGM 920100100 via AudioService. No domain mutation.</para>
///
/// spec: Docs/RE/specs/frontend_layout_tables.md §5.
/// </summary>
public sealed partial class LoadingWindow : Control
{
    // =========================================================================
    // Constants — spec: frontend_layout_tables.md §5.
    // =========================================================================

    // Design canvas dimensions (centre-origin ortho, 1024×768).
    // spec: frontend_layout_tables.md §5 / §1 "reference canvas 1024×768". CODE-CONFIRMED.
    private const float RefWidth = 1024f; // spec §1.
    private const float RefHeight = 768f; // spec §1.

    // Background DDS candidates — rand()%3.
    // spec: Docs/RE/specs/frontend_layout_tables.md §5.
    private static readonly string[] BgPaths =
    [
        "data/ui/loading.dds", // index 0. spec §5.
        "data/ui/loading06.dds", // index 1. spec §5.
        "data/ui/loading08.dds", // index 2. spec §5.
    ];

    // Progress bar track in design-space (centre-origin ortho):
    //   X span −499..−170 → 329 px wide.
    //   Y span −363..−140 → 223 px tall.
    // spec: Docs/RE/specs/frontend_layout_tables.md §5.
    private const float TrackDesignX1 = -499f; // left edge.  spec §5.
    private const float TrackDesignX2 = -170f; // right edge. spec §5.
    private const float TrackDesignY1 = -363f; // bottom (in centre-origin, +Y up). spec §5.
    private const float TrackDesignY2 = -140f; // top.        spec §5.
    private const float TrackDesignWidth = TrackDesignX2 - TrackDesignX1; // 329 px. spec §5.
    private const float TrackDesignHeight = TrackDesignY2 - TrackDesignY1; // 223 px. spec §5.

    // Centre-origin → canvas (top-left +Y down) for track top-left corner:
    //   cx = 512, cy = 384
    //   x = cx + TrackDesignX1 = 512 + (−499) = 13
    //   y = cy − TrackDesignY2 = 384 − (−140) = 524
    // spec: Docs/RE/specs/frontend_layout_tables.md §5.
    private const float TrackCanvasX = RefWidth / 2f + TrackDesignX1; // 13.  spec §5.
    private const float TrackCanvasY = RefHeight / 2f - TrackDesignY2; // 524. spec §5.

    // Fill: fill_px = clamp(223 · pct / 100, 0, 223); U = fill_px / 1024 → max 223/1024.
    // Fill is a WIDTH (U-axis) fill — bar height is always 223 px.
    // spec: Docs/RE/specs/frontend_layout_tables.md §5 "max U 223/1024 ≈ 0.2178".
    private const float FillMaxPx = 223f; // max fill width (screen px). spec §5.
    private const float FillMaxU = 223f / 1024f; // max U fraction ≈ 0.2178. spec §5.

    // BGM sound id. spec: frontend_layout_tables.md §5/§7 "920100100 looped category 0". CODE-CONFIRMED.
    private const uint BgmSoundId = 920100100u; // spec §7.

    // Grace period after external worker completes.
    // spec: frontend_layout_tables.md §5 "500 ms grace before advancing". CODE-CONFIRMED.
    private const float GraceSeconds = 0.5f; // spec §5.

    // =========================================================================
    // Public inputs (set by LoadScene before adding to the tree)
    // =========================================================================

    /// <summary>
    /// Shared HUD atlas library — used to load the loading DDS textures.
    /// Must be set before <see cref="_Ready"/> fires.
    /// </summary>
    public HudAtlasLibrary? Atlas { get; set; }

    /// <summary>
    /// Live progress provider from LoadOrchestrator.ProgressQuotient (0..100, clamped by caller).
    /// LoadScene wires this. When null the bar stays at 0.
    /// spec: Docs/RE/specs/resource_pipeline.md §2.4.
    /// </summary>
    public Func<int>? ProgressProvider { get; set; }

    /// <summary>
    /// When true, LoadScene manages the loading BGM via GodotLoadingSoundSink / AudioService.
    /// When false (offline), this window starts the BGM itself to avoid doubling.
    /// spec: frontend_layout_tables.md §5/§7; sound.md §15.6a.
    /// </summary>
    public bool PlayOwnCue { get; set; } = true;

    // =========================================================================
    // Signal
    // =========================================================================

    /// <summary>
    /// Emitted after the external worker completes + 500 ms grace elapses.
    /// LoadScene advances the scene spine on this signal.
    /// spec: Docs/RE/specs/frontend_layout_tables.md §5.
    /// </summary>
    [Signal]
    public delegate void LoadingCompleteEventHandler();

    // =========================================================================
    // View state
    // =========================================================================

    private TextureRect? _bgRect;
    private TextureRect? _fillRect;
    private Texture2D? _chosenTex;

    // Current bar fill width in screen pixels (0..223).
    private float _fillPx;

    private bool _workerDone;
    private bool _gracePending;

    // =========================================================================
    // Godot lifecycle
    // =========================================================================

    public override void _Ready()
    {
        GD.Print("[LoadingWindow] _Ready — state-2 loading screen. spec: frontend_layout_tables.md §5.");

        // Choose background by rand()%3. spec: frontend_layout_tables.md §5 "rand()%3". CODE-CONFIRMED.
        int bgIdx = (int)(global::Godot.GD.Randi() % 3u);
        GD.Print($"[LoadingWindow] BG index={bgIdx} → {BgPaths[bgIdx]}.");

        Size = new Vector2(RefWidth, RefHeight);
        MouseFilter = MouseFilterEnum.Stop;

        BuildLayout(bgIdx);

        if (PlayOwnCue)
            StartBgm();

        GD.Print($"[LoadingWindow] Built. BG={BgPaths[bgIdx]}, PlayOwnCue={PlayOwnCue}.");

        Dev.LayoutDump.DumpDeferred(this, "LOAD");
    }

    public override void _Process(double delta)
    {
        if (_workerDone) return;

        // Pull progress from the LoadOrchestrator. spec: resource_pipeline.md §2.4.
        int pct = ProgressProvider is not null
            ? Math.Clamp(ProgressProvider(), 0, 100)
            : 0;

        // Fill width: clamp(223 · pct / 100, 0, 223). spec: frontend_layout_tables.md §5.
        float newFill = Math.Clamp(FillMaxPx * pct / 100f, 0f, FillMaxPx);
        if (Math.Abs(newFill - _fillPx) > 0.01f)
        {
            _fillPx = newFill;
            UpdateFill();
        }
    }

    // =========================================================================
    // External API (called by LoadScene on the main thread via CallDeferred)
    // =========================================================================

    /// <summary>
    /// Called by LoadScene when the LoadOrchestrator worker completes.
    /// Starts the 500 ms grace timer before emitting LoadingComplete.
    /// spec: Docs/RE/specs/frontend_layout_tables.md §5.
    /// </summary>
    public void CompleteExternalLoad()
    {
        if (_workerDone) return;
        _workerDone = true;

        // Snap to full fill. spec §5.
        _fillPx = FillMaxPx;
        UpdateFill();

        GD.Print("[LoadingWindow] Worker done — starting 500 ms grace. spec §5.");

        SceneTreeTimer grace = GetTree().CreateTimer(GraceSeconds, processAlways: true);
        grace.Timeout += OnGraceExpired;
    }

    // =========================================================================
    // Event handlers
    // =========================================================================

    private void OnGraceExpired()
    {
        if (_gracePending) return;
        _gracePending = true;

        GD.Print("[LoadingWindow] Grace expired → emitting LoadingComplete. spec §5.");
        EmitSignal(SignalName.LoadingComplete);
    }

    // =========================================================================
    // UI construction
    // =========================================================================

    private void BuildLayout(int bgIdx)
    {
        // ── Background quad — full-screen ──────────────────────────────────────
        // spec: frontend_layout_tables.md §5 "full-screen (0,0,screenW,screenH)".
        _bgRect = new TextureRect
        {
            Name = "BgRect",
            Position = Vector2.Zero,
            Size = new Vector2(RefWidth, RefHeight),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            // IgnoreSize: respect the 1024×768 Size instead of ballooning to the 1024×1024 DDS.
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_bgRect);

        LoadBackgroundTexture(bgIdx);

        // ── Progress bar fill — 329×223 px track, U-axis fill ──────────────────
        // spec: frontend_layout_tables.md §5.
        BuildFillRect();
    }

    private void BuildFillRect()
    {
        // Track top-left in canvas space (top-left origin, +Y down):
        //   x = 512 + (−499) = 13
        //   y = 384 − (−140) = 524
        // Track dimensions: 329 × 223 px.
        // Fill starts at 0 width; bar height is always 223 px (full height from start).
        // spec: Docs/RE/specs/frontend_layout_tables.md §5.
        _fillRect = new TextureRect
        {
            Name = "FillRect",
            Position = new Vector2(TrackCanvasX, TrackCanvasY), // top-left of track. spec §5.
            Size = new Vector2(0f, TrackDesignHeight), // width grows, height fixed. spec §5.
            StretchMode = TextureRect.StretchModeEnum.Scale,
            // IgnoreSize: the bar must respect its (fill_px × 223) Size, not balloon to the DDS size.
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        AddChild(_fillRect);

        ApplyFillTexture();
    }

    // =========================================================================
    // Bar update (called from _Process and CompleteExternalLoad)
    // =========================================================================

    private void UpdateFill()
    {
        if (_fillRect is null) return;

        // Width fill: _fillPx ∈ [0,223] screen px. Position is fixed (left edge of track).
        // spec: Docs/RE/specs/frontend_layout_tables.md §5 "U-axis fill, bar height always 223 px".
        _fillRect.Size = new Vector2(_fillPx, TrackDesignHeight);
        _fillRect.Visible = _fillPx > 0f && _fillRect.Texture is not null;

        ApplyFillTexture();
    }

    // =========================================================================
    // Asset loading
    // =========================================================================

    private void LoadBackgroundTexture(int bgIdx)
    {
        if (Atlas is null)
        {
            GD.Print("[LoadingWindow] No atlas library — background transparent (offline).");
            return;
        }

        try
        {
            Texture2D? tex = Atlas.GetByPath(BgPaths[bgIdx]);
            if (tex is null)
            {
                GD.PrintErr($"[LoadingWindow] Loading DDS absent: {BgPaths[bgIdx]} — transparent.");
                return;
            }

            _chosenTex = tex;
            GD.Print($"[LoadingWindow] BG {BgPaths[bgIdx]} loaded ({tex.GetWidth()}×{tex.GetHeight()}).");
            _bgRect!.Texture = tex;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LoadingWindow] LoadBackgroundTexture failed: {ex.Message} — transparent.");
        }
    }

    private void ApplyFillTexture()
    {
        if (_fillRect is null || _chosenTex is null) return;

        // Fill samples the background DDS from U=0 up to fill_px/1024.
        // fill_px = _fillPx (0..223); U_max = _fillPx/1024 → max 223/1024 ≈ 0.2178.
        // Height: full texture height (V=0..1) so the bar shows the bottom strip of the DDS.
        // spec: Docs/RE/specs/frontend_layout_tables.md §5 "max U 223/1024".
        int texW = _chosenTex.GetWidth(); // typically 1024
        int texH = _chosenTex.GetHeight(); // typically 1024

        float uWidthPx = texW * (_fillPx / 1024f); // fill_px normalised by 1024 → texture pixels
        // spec: §5 fill_px/1024 = U fraction; multiply by texW for AtlasTexture region.

        _fillRect.Texture = new AtlasTexture
        {
            Atlas = _chosenTex,
            Region = new Rect2(0f, 0f, uWidthPx, texH), // U-axis fill; full V. spec §5.
            FilterClip = false,
        };

        GD.Print(FormattableString.Invariant(
            $"[LoadingWindow] Fill width={_fillPx:F1}/{FillMaxPx:F0}px, U={_fillPx / 1024f:F4} (max={FillMaxU:F4}). spec: frontend_layout_tables.md §5."));
    }

    // =========================================================================
    // BGM (offline-only path)
    // =========================================================================

    private void StartBgm()
    {
        // Looped BGM 920100100, category 0. spec: frontend_layout_tables.md §5/§7. CODE-CONFIRMED.
        if (AudioService.Instance is { } audio)
        {
            audio.StartBgm(BgmSoundId); // spec §7 "920100100 looped category 0". CODE-CONFIRMED.
            GD.Print($"[LoadingWindow] BGM {BgmSoundId} started (offline path). spec §5/§7.");
        }
        else
        {
            GD.Print($"[LoadingWindow] AudioService unavailable — BGM {BgmSoundId} skipped (headless).");
        }
    }
}