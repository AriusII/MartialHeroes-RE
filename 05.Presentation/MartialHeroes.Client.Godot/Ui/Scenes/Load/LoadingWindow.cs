using Godot;
using MartialHeroes.Client.Godot.Audio;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Load;

/// <summary>
///     Full-screen loading window (Diamond_LoadingWindow analogue) — immediate-mode two-quad renderer.
///     Background DDS rand()%3 is always present from the real VFS. Progress bar is a U-axis width fill.
///     Emits <see cref="LoadingCompleteEventHandler" /> after the external load worker completes + 500 ms grace.
///     spec: Docs/RE/specs/frontend_layout_tables.md §5.
/// </summary>
public sealed partial class LoadingWindow : Control
{
    // ── Signal ───────────────────────────────────────────────────────────────

    /// <summary>Emitted after the external worker completes + 500 ms grace elapses.</summary>
    [Signal]
    public delegate void LoadingCompleteEventHandler();

    // spec: frontend_layout_tables.md §5 / §1 "reference canvas 1024×768".
    private const float RefWidth = 1024f;
    private const float RefHeight = 768f;

    // Progress bar track in design-space (centre-origin ortho).
    // spec: Docs/RE/specs/frontend_layout_tables.md §5.
    private const float TrackDesignX1 = -499f;
    private const float TrackDesignY2 = -140f;
    private const float TrackDesignHeight = 223f; // spec §5 "223 px tall".

    // Track top-left in canvas space (top-left origin, +Y down).
    // x = 512 + (−499) = 13; y = 384 − (−140) = 524. spec: frontend_layout_tables.md §5.
    private const float TrackCanvasX = RefWidth / 2f + TrackDesignX1; // 13.  spec §5.
    private const float TrackCanvasY = RefHeight / 2f - TrackDesignY2; // 524. spec §5.

    // Fill width: clamp(223 · pct / 100, 0, 223). U = fill_px / 1024 → max 223/1024 ≈ 0.2178.
    // spec: frontend_layout_tables.md §5.
    private const float FillMaxPx = 223f;

    // BGM. spec: frontend_layout_tables.md §5/§7 "920100100 looped category 0".
    private const uint BgmSoundId = 920100100u;

    // Grace after worker completes. spec: frontend_layout_tables.md §5 "500 ms grace".
    private const float GraceSeconds = 0.5f;

    // Background DDS candidates — rand()%3. spec: frontend_layout_tables.md §5.
    private static readonly string[] BgPaths =
    [
        "data/ui/loading.dds",
        "data/ui/loading06.dds",
        "data/ui/loading08.dds"
    ];

    // ── View state ───────────────────────────────────────────────────────────

    private TextureRect? _bgRect;
    private Texture2D? _chosenTex;
    private float _fillPx;
    private TextureRect? _fillRect;
    private bool _gracePending;
    private bool _workerDone;

    // ── Public inputs (set by LoadScene before adding to the tree) ───────────

    /// <summary>Shared HUD atlas library — loads the loading DDS textures from the real VFS.</summary>
    public HudAtlasLibrary? Atlas { get; set; }

    /// <summary>Live progress provider from LoadOrchestrator.ProgressQuotient (0..100, clamped by caller).</summary>
    public Func<int>? ProgressProvider { get; set; }

    /// <summary>
    ///     When true LoadScene manages the loading BGM via GodotLoadingSoundSink / AudioService.
    ///     When false this window starts the BGM itself to avoid doubling.
    ///     spec: frontend_layout_tables.md §5/§7; sound.md §15.6a.
    /// </summary>
    public bool PlayOwnCue { get; set; } = true;

    // ── Godot lifecycle ──────────────────────────────────────────────────────

    public override void _Ready()
    {
        GD.Print("[LoadingWindow] _Ready — state-2 loading screen. spec: frontend_layout_tables.md §5.");

        var bgIdx = (int)(GD.Randi() % 3u); // spec §5 "rand()%3".
        GD.Print($"[LoadingWindow] BG index={bgIdx} → {BgPaths[bgIdx]}.");

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

        var pct = ProgressProvider is not null
            ? Math.Clamp(ProgressProvider(), 0, 100)
            : 0;

        // Fill width: clamp(223 · pct / 100, 0, 223). spec: frontend_layout_tables.md §5.
        var newFill = Math.Clamp(FillMaxPx * pct / 100f, 0f, FillMaxPx);
        if (Math.Abs(newFill - _fillPx) > 0.01f)
        {
            _fillPx = newFill;
            UpdateFill();
        }
    }

    // ── External API ─────────────────────────────────────────────────────────

    /// <summary>Called by LoadScene when the LoadOrchestrator worker completes. Starts the 500 ms grace timer.</summary>
    public void CompleteExternalLoad()
    {
        if (_workerDone) return;
        _workerDone = true;

        _fillPx = FillMaxPx;
        UpdateFill();

        GD.Print("[LoadingWindow] Worker done — starting 500 ms grace. spec §5.");

        var grace = GetTree().CreateTimer(GraceSeconds);
        grace.Timeout += OnGraceExpired;
    }

    private void OnGraceExpired()
    {
        if (_gracePending) return;
        _gracePending = true;

        GD.Print("[LoadingWindow] Grace expired → emitting LoadingComplete. spec §5.");
        EmitSignal(SignalName.LoadingComplete);
    }

    // ── UI construction ──────────────────────────────────────────────────────

    private void BuildLayout(int bgIdx)
    {
        // Background quad — full-screen. spec: frontend_layout_tables.md §5.
        _bgRect = new TextureRect
        {
            Name = "BgRect",
            Position = Vector2.Zero,
            Size = new Vector2(RefWidth, RefHeight),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_bgRect);

        LoadBackgroundTexture(bgIdx);
        BuildFillRect();
    }

    private void BuildFillRect()
    {
        // Track top-left: x=13, y=524 in canvas space. spec: frontend_layout_tables.md §5.
        _fillRect = new TextureRect
        {
            Name = "FillRect",
            Position = new Vector2(TrackCanvasX, TrackCanvasY),
            Size = new Vector2(0f, TrackDesignHeight),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false
        };
        AddChild(_fillRect);

        ApplyFillTexture();
    }

    private void UpdateFill()
    {
        if (_fillRect is null) return;

        _fillRect.Size = new Vector2(_fillPx, TrackDesignHeight);
        _fillRect.Visible = _fillPx > 0f && _fillRect.Texture is not null;

        ApplyFillTexture();
    }

    // ── Asset loading ────────────────────────────────────────────────────────

    private void LoadBackgroundTexture(int bgIdx)
    {
        var tex = Atlas?.GetByPath(BgPaths[bgIdx]);
        if (tex is null)
        {
            GD.PrintErr($"[LoadingWindow] Loading DDS absent: {BgPaths[bgIdx]}.");
            return;
        }

        _chosenTex = tex;
        GD.Print($"[LoadingWindow] BG {BgPaths[bgIdx]} loaded ({tex.GetWidth()}×{tex.GetHeight()}).");
        _bgRect!.Texture = tex;
    }

    private void ApplyFillTexture()
    {
        if (_fillRect is null || _chosenTex is null) return;

        // U-axis fill: fill_px / 1024 → max 223/1024 ≈ 0.2178. spec: frontend_layout_tables.md §5.
        var texW = _chosenTex.GetWidth();
        var texH = _chosenTex.GetHeight();
        var uWidthPx = texW * (_fillPx / 1024f);

        _fillRect.Texture = new AtlasTexture
        {
            Atlas = _chosenTex,
            Region = new Rect2(0f, 0f, uWidthPx, texH),
            FilterClip = false
        };
    }

    // ── BGM ──────────────────────────────────────────────────────────────────

    private void StartBgm()
    {
        // spec: frontend_layout_tables.md §5/§7 "920100100 looped category 0".
        if (AudioService.Instance is { } audio)
        {
            audio.StartBgm(BgmSoundId);
            GD.Print($"[LoadingWindow] BGM {BgmSoundId} started.");
        }
    }
}