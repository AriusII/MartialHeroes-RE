using Godot;
using MartialHeroes.Client.Godot.Audio;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Load;

/// <summary>
///     Full-screen loading window (Diamond_LoadingWindow analogue) — immediate-mode two-quad renderer.
///     Background DDS rand()%3 is always present from the real VFS.
///     Progress bar fills VERTICALLY (top→down): fixed X extents (329 design units), animated bottom-vertex Y
///     and V texcoord. spec: Docs/RE/scenes/load.md §5A.4 (GAP-1); Docs/RE/specs/frontend_layout_tables.md §5.
///     Emits <see cref="LoadingCompleteEventHandler" /> after the external load worker completes + 500 ms grace.
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

    // Progress bar track in design-space (centre-origin ortho, Y-down).
    // X-left  = −499, X-right = −170  → X span = 329 design units.
    // Y-top   = −363, Y-bottom = −140 → Y span = 223 design units (fill max).
    // spec: Docs/RE/specs/frontend_layout_tables.md §5; Docs/RE/scenes/load.md §5A.2 steps 21/§5A.4.
    private const float TrackDesignXLeft = -499f; // spec §5 "X-left  xScale·−499".
    private const float TrackDesignXRight = -170f; // spec §5 "X-right xScale·−170".

    private const float TrackDesignYTop = -363f; // spec §5 "Y-top   yScale·−363".
    // TrackDesignYBottom = −140 is the fully-filled position (pct=100) — not used directly.

    // Track in canvas space (top-left origin, +Y down): centre = (512, 384).
    // X-left  canvas = 512 + (−499) = 13.   spec §5.
    // X-right canvas = 512 + (−170) = 342.  spec §5. Width = 329.
    // Y-top   canvas = 384 + (−363) = 21.   spec §5.
    private const float TrackCanvasX = RefWidth / 2f + TrackDesignXLeft; // 13.  spec §5.
    private const float TrackCanvasY = RefHeight / 2f + TrackDesignYTop; // 21.  spec §5.
    private const float TrackCanvasWidth = TrackDesignXRight - TrackDesignXLeft; // 329. spec §5.

    // Fill height: clamp(223 · pct / 100, 0, 223) design-px — grows downward from Y-top.
    // spec: Docs/RE/scenes/load.md §5A.4; frontend_layout_tables.md §5 "fill_px = clamp(223·pct/100,0,223)".
    private const float FillMaxPx = 223f; // spec §5 / load.md §5A.4 "max 223 ref-units".

    // UV sub-rect of the bg DDS for the gauge fill band (pixels, before normalization).
    // U: 443..772 (329 src px wide, fixed). V-top: 576 (fixed). V-bottom: 576+fill_px (animated).
    // spec: Docs/RE/specs/frontend_layout_tables.md §5 "U 443/1024..772/1024, V 576/768..744/768".
    // The DDS is 1024×1024 (inferred V=0.75=768/1024 per load.md §5A.3); pixel coords are into that DDS.
    private const float GaugeSrcULeft = 443f; // spec §5.
    private const float GaugeSrcVTop = 576f; // spec §5 "V 576/768" → pixel row 576 in the 1024×1024 DDS.

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

        // Fill height (vertical, top→down): clamp(223 · pct / 100, 0, 223).
        // spec: Docs/RE/scenes/load.md §5A.4; Docs/RE/specs/frontend_layout_tables.md §5.
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
        // Track top-left in canvas space: x=13, y=21. Width fixed at 329. Height starts at 0 (no fill yet).
        // spec: Docs/RE/specs/frontend_layout_tables.md §5; Docs/RE/scenes/load.md §5A.4.
        _fillRect = new TextureRect
        {
            Name = "FillRect",
            Position = new Vector2(TrackCanvasX, TrackCanvasY), // (13, 21). spec §5.
            Size = new Vector2(TrackCanvasWidth, 0f), // width=329 fixed; height=0 until fill starts.
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

        // Fill grows VERTICALLY (top→down): height = fill_px, X extents are FIXED at 329 wide.
        // spec: Docs/RE/scenes/load.md §5A.4 "the fill grows downward from a fixed top edge".
        _fillRect.Size = new Vector2(TrackCanvasWidth, _fillPx);
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

        // V-axis fill: sample a fixed U band [443..772] and animate the V-bottom downward.
        // U extents are fixed at 329 source pixels: U-left=443, U-width=329.
        // V-top is fixed at pixel row 576. V-height = fill_px (grows 0..223).
        // The DDS is 1024×1024 (see load.md §5A.3); texcoord delta = fill_px / 1024.
        // spec: Docs/RE/specs/frontend_layout_tables.md §5 "U 443/1024..772/1024, V 576/768..744/768";
        //        Docs/RE/scenes/load.md §5A.4 "V texcoord of the two moving vertices shifts by fill_px·(1/1024)".
        _fillRect.Texture = new AtlasTexture
        {
            Atlas = _chosenTex,
            Region = new Rect2(GaugeSrcULeft, GaugeSrcVTop, TrackCanvasWidth, _fillPx),
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