// Ui/Scenes/Load/LoadingWindow.cs
//
// State-2 LoadingWindow rebuilt FROM SCRATCH on the Ui/Scenes substrate.
//
// COMPOSITION (spec: Docs/RE/specs/frontend_scenes.md §2L, §9.1 — CODE-CONFIRMED):
//
//   Background:
//     One full-screen quad chosen by rand()%3 from exactly three DDS:
//       0 → data/ui/loading.dds
//       1 → data/ui/loading06.dds
//       2 → data/ui/loading08.dds
//     UV: U[0,1], V[0,0.75] (top 3/4 of DDS height).
//     spec: frontend_scenes.md §2L.1 / §9.1. CODE-CONFIRMED (V=0.75 CONFIRMED).
//
//   Progress bar:
//     Fill width = 223 × progress / 100 px, clamped to 223 px.
//     The fill is a horizontal (U-axis) sub-rect of the SAME chosen loading DDS.
//     Left-anchored, grows left→right. Fill U-max = 223/1024 ≈ 0.21777.
//     NO text / percent label — any visible caption is baked into the art.
//     spec: frontend_scenes.md §2L.1 / §9.1. CODE-CONFIRMED.
//
//   Progress source:
//     Driven from LoadOrchestrator.ProgressQuotient (cumulative corpus bytes / denominator).
//     It is faithful that the bar barely advances — do not smooth it.
//     spec: frontend_scenes.md §2L.2; resource_pipeline.md §2.4.
//
//   Loading cue:
//     BGM 920100100, looping, category-0 music slot via AudioService.StartBgm().
//     spec: frontend_scenes.md §2L.1 / §9.1. CODE-CONFIRMED.
//     The LoadOrchestrator also plays this cue via GodotLoadingSoundSink; LoadScene sets
//     PlayOwnCue=false when that sink is active so the two routes are not doubled.
//
//   Exit:
//     Scene advances on worker completion (not bar==100%). LoadScene drives the advance
//     by listening to LoadingComplete. PRESERVE that contract.
//     spec: frontend_scenes.md §2L.3. CODE-CONFIRMED.
//
//   Offline / VFS-absent:
//     Background quad is transparent, bar is invisible. GD.PrintErr + skip on missing asset.
//     Audio is silently skipped when VFS is absent.
//
// SUBSTRATE (Phase-A):
//   HudAtlasLibrary (GetByPath) — loads DDS by VFS path into Godot Texture2D + AtlasTexture.
//   ScreenHost — the 1024×768 canvas host; do NOT set FullRect anchors on window root.
//   AudioService.Instance.StartBgm() — BGM routing via the shared audio slot.
//
// THREADING: all Control mutation on the main thread (_Process + timers).
// PASSIVE: zero game logic. Emits LoadingComplete; LoadScene decides what to do.
//
// spec: Docs/RE/specs/frontend_scenes.md §2L, §9.1 (CODE-CONFIRMED).
// spec: Docs/RE/specs/resource_pipeline.md §2 (LoadOrchestrator progress contract).

using Godot;
using MartialHeroes.Client.Application.Assets;
using MartialHeroes.Client.Godot.Audio;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Load;

/// <summary>
/// Full-screen loading window (Diamond_LoadingWindow analogue) built on the Ui/Scenes substrate.
///
/// <para>Emits <see cref="LoadingCompleteEventHandler"/> after the external load worker completes
/// plus a 500 ms grace period. LoadScene advances the scene spine on this signal.</para>
///
/// <para>Strictly passive: reads atlases from HudAtlasLibrary, turns worker progress into a
/// bar width, plays BGM 920100100 via AudioService. No domain mutation.</para>
///
/// spec: Docs/RE/specs/frontend_scenes.md §2L, §9.1. CODE-CONFIRMED.
/// </summary>
public sealed partial class LoadingWindow : Control
{
    // =========================================================================
    // Constants — spec: frontend_scenes.md §2L.1 / §9.1. CODE-CONFIRMED.
    // =========================================================================

    // Design canvas dimensions. spec: frontend_scenes.md §11.0. CODE-CONFIRMED.
    private const float RefWidth  = 1024f; // spec §11.0. CODE-CONFIRMED.
    private const float RefHeight = 768f;  // spec §11.0. CODE-CONFIRMED.

    // Background DDS candidates — rand()%3 over exactly these three files.
    // spec: frontend_scenes.md §2L.1 / §9.1. CODE-CONFIRMED.
    private static readonly string[] BgPaths =
    [
        "data/ui/loading.dds",    // index 0. spec §9.1. CODE-CONFIRMED.
        "data/ui/loading06.dds",  // index 1. spec §9.1. CODE-CONFIRMED.
        "data/ui/loading08.dds",  // index 2. spec §9.1. CODE-CONFIRMED.
    ];

    // Background V-crop: sample V ∈ [0, 0.75] (top 3/4 of DDS height).
    // spec: frontend_scenes.md §2L.1 "V[0,0.75]". CODE-CONFIRMED (V=0.75 CONFIRMED).
    private const float BgVCrop = 0.75f; // spec §2L.1. CODE-CONFIRMED.

    // Maximum fill width in design-pixels.
    // Fill = sub-rect of the SAME loading DDS, U ∈ [0, 223/1024].
    // spec: frontend_scenes.md §2L.1 "fill width = 223 × percent/100, clamped to 223". CODE-CONFIRMED.
    private const float MaxFillPx = 223f; // spec §2L.1. CODE-CONFIRMED.

    // Fill bar sub-rect U-fraction: 223 px wide on a 1024-wide DDS.
    // spec: frontend_scenes.md §2L.1 "U clamp = 223/1024 ≈ 0.21777". CODE-CONFIRMED.
    private const float FillUMax = 223f / 1024f; // ≈ 0.21777. spec §2L.1. CODE-CONFIRMED.

    // Progress bar track rect in 1024×768 design-space (centre-origin, +Y up):
    //   x ∈ [−499, −170], y ∈ [−363, −140]
    // spec: frontend_scenes.md §2L.1. CODE-CONFIRMED (confirmed in LoadingScreen numeric reference).
    private const float TrackDesignX1 = -499f; // left edge.   spec §2L.1. CODE-CONFIRMED.
    private const float TrackDesignY1 = -363f; // bottom edge. spec §2L.1. CODE-CONFIRMED.
    private const float TrackDesignY2 = -140f; // top edge.    spec §2L.1. CODE-CONFIRMED.

    // BGM sound id and VFS path. spec: frontend_scenes.md §2L.1 / §9.1. CODE-CONFIRMED.
    private const uint BgmSoundId = 920100100u; // spec §9.1. CODE-CONFIRMED.

    // Grace period after external worker completes.
    // spec: frontend_scenes.md §2L.3 "500 ms grace before advancing". CODE-CONFIRMED.
    private const float GraceSeconds = 0.5f; // spec §2L.3. CODE-CONFIRMED.

    // =========================================================================
    // Public inputs (set by LoadScene before adding to the tree)
    // =========================================================================

    /// <summary>
    /// Shared HUD atlas library — used to load the loading DDS textures.
    /// Must be set before <see cref="_Ready"/> fires.
    /// </summary>
    public HudAtlasLibrary? Atlas { get; set; }

    /// <summary>
    /// Live progress provider from LoadOrchestrator.ProgressQuotient (0..N, not clamped).
    /// LoadScene wires this. When null the bar stays at 0 and the screen waits for
    /// <see cref="CompleteExternalLoad"/>.
    /// spec: Docs/RE/specs/resource_pipeline.md §2.4.
    /// </summary>
    public Func<int>? ProgressProvider { get; set; }

    /// <summary>
    /// When true, LoadScene manages the loading BGM via GodotLoadingSoundSink / AudioService
    /// (routed through LoadOrchestrator). When false (offline), this window starts the BGM itself.
    /// Prevents doubling the cue.
    /// spec: frontend_scenes.md §2L.1; sound.md §15.6a.
    /// </summary>
    public bool PlayOwnCue { get; set; } = true;

    // =========================================================================
    // Signal
    // =========================================================================

    /// <summary>
    /// Emitted after the external worker completes + 500 ms grace elapses.
    /// LoadScene advances the scene spine on this signal.
    /// spec: Docs/RE/specs/frontend_scenes.md §2L.3. CODE-CONFIRMED.
    /// </summary>
    [Signal]
    public delegate void LoadingCompleteEventHandler();

    // =========================================================================
    // View state
    // =========================================================================

    // Background TextureRect (V-cropped AtlasTexture from the chosen DDS).
    private TextureRect? _bgRect;

    // Progress bar fill TextureRect (U sub-rect of the same DDS, growing left→right).
    private TextureRect? _fillRect;

    // The raw Texture2D for the chosen loading DDS (needed to build fill AtlasTexture).
    private Texture2D? _chosenTex;

    // Current bar width in design-pixels.
    private float _fillPx;

    // Completion tracking.
    private bool _workerDone;
    private bool _gracePending;

    // =========================================================================
    // Godot lifecycle
    // =========================================================================

    public override void _Ready()
    {
        GD.Print("[LoadingWindow] _Ready — entering state-2 loading window. spec: frontend_scenes.md §2L.");

        // Choose background by rand()%3. spec: frontend_scenes.md §2L.1. CODE-CONFIRMED.
        int bgIdx = (int)(global::Godot.GD.Randi() % 3u); // spec §2L.1 "rand()%3". CODE-CONFIRMED.
        GD.Print($"[LoadingWindow] BG index={bgIdx} → {BgPaths[bgIdx]}. spec §2L.1.");

        // Size this control to the reference canvas (ScreenHost calls SetScreen which already
        // sets our Size — but set it here defensively so _Ready is idempotent).
        Size = new Vector2(RefWidth, RefHeight);
        MouseFilter = MouseFilterEnum.Stop;

        BuildLayout(bgIdx);

        if (PlayOwnCue)
            StartBgm();

        GD.Print($"[LoadingWindow] Built. BG={BgPaths[bgIdx]}, PlayOwnCue={PlayOwnCue}.");
    }

    public override void _Process(double delta)
    {
        if (_workerDone) return;

        // Pull progress from the LoadOrchestrator (cumulative bytes / denominator → quotient).
        // spec: resource_pipeline.md §2.4 "ProgressQuotient". CODE-CONFIRMED.
        int pct = ProgressProvider is not null
            ? Math.Clamp(ProgressProvider(), 0, 100)
            : 0;

        float newFill = Math.Clamp(MaxFillPx * pct / 100f, 0f, MaxFillPx);
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
    /// spec: Docs/RE/specs/frontend_scenes.md §2L.3. CODE-CONFIRMED.
    /// </summary>
    public void CompleteExternalLoad()
    {
        if (_workerDone) return;
        _workerDone = true;

        // Snap to full fill (worker done → 100%). spec §2L.3. CODE-CONFIRMED.
        _fillPx = MaxFillPx;
        UpdateFill();

        GD.Print("[LoadingWindow] Worker done — starting 500 ms grace. spec §2L.3. CODE-CONFIRMED.");

        // spec: frontend_scenes.md §2L.3 "500 ms grace delay before advancing". CODE-CONFIRMED.
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

        GD.Print("[LoadingWindow] Grace expired → emitting LoadingComplete. spec §2L.3.");
        EmitSignal(SignalName.LoadingComplete);
    }

    // =========================================================================
    // UI construction
    // =========================================================================

    private void BuildLayout(int bgIdx)
    {
        // ── Background quad ──────────────────────────────────────────────────
        // Full-screen TextureRect; V-cropped to [0, 0.75] via AtlasTexture.
        // spec: frontend_scenes.md §2L.1 "V[0,0.75]". CODE-CONFIRMED.
        _bgRect = new TextureRect
        {
            Name        = "BgRect",
            Position    = Vector2.Zero,
            Size        = new Vector2(RefWidth, RefHeight),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_bgRect);

        LoadBackgroundTexture(bgIdx);

        // ── Progress bar fill ─────────────────────────────────────────────────
        BuildFillRect();
    }

    private void BuildFillRect()
    {
        // Track rect: design-space (centre-origin +Y-up) → canvas-space (top-left +Y-down).
        // Canvas centre: (512, 384).
        // track left:  512 + (−499) = 13
        // track top:   384 − (−140) = 524
        // track height: (−140) − (−363) = 223
        // spec: frontend_scenes.md §2L.1. CODE-CONFIRMED.
        float cx = RefWidth / 2f;   // 512
        float cy = RefHeight / 2f;  // 384

        float trackX = cx + TrackDesignX1;        // 13
        float trackY = cy - TrackDesignY2;        // 524
        float trackH = TrackDesignY2 - TrackDesignY1; // 223

        // Fill bar starts at 0 width (invisible at 0%).
        // spec: frontend_scenes.md §2L.1 "fill width = 223·percent/100". CODE-CONFIRMED.
        _fillRect = new TextureRect
        {
            Name        = "FillRect",
            Position    = new Vector2(trackX, trackY),
            Size        = new Vector2(0f, trackH), // starts at 0 width. spec §2L.1. CODE-CONFIRMED.
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible     = false,
        };
        AddChild(_fillRect);

        // Wire fill sub-rect from the already-loaded texture (if available).
        ApplyFillTexture();
    }

    // =========================================================================
    // Bar update
    // =========================================================================

    private void UpdateFill()
    {
        if (_fillRect is null) return;

        bool visible = _fillPx > 0f && _fillRect.Texture is not null;
        _fillRect.Size = _fillRect.Size with { X = _fillPx };
        _fillRect.Visible = visible;
    }

    // =========================================================================
    // Asset loading
    // =========================================================================

    private void LoadBackgroundTexture(int bgIdx)
    {
        if (Atlas is null)
        {
            GD.Print("[LoadingWindow] No atlas library — background will be transparent (offline).");
            return;
        }

        try
        {
            // Load the full DDS from the VFS via HudAtlasLibrary.GetByPath.
            // spec: frontend_scenes.md §2L.1 — loading DDS by hard-coded VFS path. CODE-CONFIRMED.
            Texture2D? tex = Atlas.GetByPath(BgPaths[bgIdx]);
            if (tex is null)
            {
                GD.PrintErr(
                    $"[LoadingWindow] Loading DDS absent from VFS: {BgPaths[bgIdx]} — background transparent.");
                return;
            }

            _chosenTex = tex;
            GD.Print($"[LoadingWindow] BG {BgPaths[bgIdx]} loaded ({tex.GetWidth()}×{tex.GetHeight()}).");

            ApplyBackgroundCrop(tex);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LoadingWindow] LoadBackgroundTexture failed: {ex.Message} — background transparent.");
        }
    }

    private void ApplyBackgroundCrop(Texture2D tex)
    {
        if (_bgRect is null) return;

        int texW = tex.GetWidth();
        int texH = tex.GetHeight();

        // Crop: full width, top 75% of height.
        // spec: frontend_scenes.md §2L.1 "V[0,0.75]". CODE-CONFIRMED.
        float cropH = texH * BgVCrop; // spec §2L.1. CODE-CONFIRMED.

        _bgRect.Texture = new AtlasTexture
        {
            Atlas  = tex,
            Region = new Rect2(0f, 0f, texW, cropH),
            FilterClip = false,
        };

        GD.Print($"[LoadingWindow] BG crop applied ({texW}×{texH} → V-crop {cropH:F0}px). spec §2L.1.");
    }

    private void ApplyFillTexture()
    {
        if (_fillRect is null || _chosenTex is null) return;

        int texW = _chosenTex.GetWidth();
        int texH = _chosenTex.GetHeight();

        // Fill bar = horizontal U sub-rect of the SAME loading DDS.
        // U ∈ [0, 223/1024 ≈ 0.21777]. V = full height (0..1).
        // The TextureRect is sized by width (0..223 px) so only the left portion of the
        // sub-rect shows at each progress step — reproducing the left-to-right fill.
        // spec: frontend_scenes.md §2L.1 "fill = horizontal U-sub-rect of same DDS". CODE-CONFIRMED.
        float uMaxPx = texW * FillUMax; // 223/1024 × texW. spec §2L.1. CODE-CONFIRMED.

        _fillRect.Texture = new AtlasTexture
        {
            Atlas  = _chosenTex,
            Region = new Rect2(0f, 0f, uMaxPx, texH),
            FilterClip = false,
        };

        GD.Print($"[LoadingWindow] Fill sub-rect applied (U[0,{FillUMax:F5}] = 0..{uMaxPx:F0}px). spec §2L.1.");
    }

    // =========================================================================
    // BGM (offline-only path — LoadOrchestrator/GodotLoadingSoundSink owns the
    // live path when PlayOwnCue=false)
    // =========================================================================

    private void StartBgm()
    {
        // Play looping BGM 920100100 via the shared AudioService BGM slot.
        // spec: frontend_scenes.md §2L.1 / §9.1 "920100100 looping category-0". CODE-CONFIRMED.
        if (AudioService.Instance is { } audio)
        {
            audio.StartBgm(BgmSoundId); // spec §9.1. CODE-CONFIRMED.
            GD.Print($"[LoadingWindow] BGM {BgmSoundId} requested via AudioService.StartBgm (offline path). spec §2L.1.");
        }
        else
        {
            GD.Print($"[LoadingWindow] AudioService unavailable — BGM {BgmSoundId} skipped (offline/headless).");
        }
    }
}
