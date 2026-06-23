using Godot;
using MartialHeroes.Client.Godot.Audio;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Load;

public sealed partial class LoadingWindow : Control
{
    [Signal]
    public delegate void LoadingCompleteEventHandler();

    private const float RefWidth = 1024f;
    private const float RefHeight = 768f;

    private const float TrackDesignXLeft = -499f;
    private const float TrackDesignXRight = -170f;

    private const float TrackDesignYTop = -363f;

    private const float TrackCanvasX = RefWidth / 2f + TrackDesignXLeft;
    private const float TrackCanvasY = RefHeight / 2f + TrackDesignYTop;
    private const float TrackCanvasWidth = TrackDesignXRight - TrackDesignXLeft;

    private const float FillMaxPx = 223f;

    private const float GaugeSrcULeft = 443f;
    private const float GaugeSrcVTop = 768f;

    private const float DdsHeight = 1024f;

    private const uint BgmSoundId = 920100100u;

    private const float GraceSeconds = 0.5f;

    private static readonly string[] BgPaths =
    [
        "data/ui/loading.dds",
        "data/ui/loading06.dds",
        "data/ui/loading08.dds"
    ];


    private TextureRect? _bgRect;

    private Texture2D? _chosenTex;

    private AtlasTexture? _fillAtlas;
    private float _fillPx;
    private TextureRect? _fillRect;
    private bool _gracePending;
    private bool _workerDone;


    public HudAtlasLibrary? Atlas { get; set; }

    public Func<int>? ProgressProvider { get; set; }

    public bool PlayOwnCue { get; set; } = true;


    public override void _Ready()
    {
        GD.Print("[LoadingWindow] _Ready — state-2 loading screen. spec: frontend_layout_tables.md §5.");

        var bgIdx = (int)(GD.Randi() % 3u);
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

        var newFill = Math.Clamp(FillMaxPx * pct / 100f, 0f, FillMaxPx);
        if (Math.Abs(newFill - _fillPx) > 0.01f)
        {
            _fillPx = newFill;
            UpdateFill();
        }
    }


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


    private void BuildLayout(int bgIdx)
    {
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
        _fillRect = new TextureRect
        {
            Name = "FillRect",
            Position = new Vector2(TrackCanvasX, TrackCanvasY),
            Size = new Vector2(TrackCanvasWidth, 0f),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false
        };
        AddChild(_fillRect);

        _fillAtlas = new AtlasTexture { FilterClip = false };
        _fillRect.Texture = _fillAtlas;

        ApplyFillTexture();
    }

    private void UpdateFill()
    {
        if (_fillRect is null) return;

        _fillRect.Size = new Vector2(TrackCanvasWidth, _fillPx);
        _fillRect.Visible = _fillPx > 0f && _fillRect.Texture is not null;

        ApplyFillTexture();
    }


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
        if (_fillAtlas is null || _chosenTex is null) return;

        var fillDssPx = _fillPx * (DdsHeight / RefHeight);
        _fillAtlas.Atlas = _chosenTex;
        _fillAtlas.Region = new Rect2(GaugeSrcULeft, GaugeSrcVTop, TrackCanvasWidth, fillDssPx);
    }


    private void StartBgm()
    {
        if (AudioService.Instance is { } audio)
        {
            audio.StartBgm(BgmSoundId);
            GD.Print($"[LoadingWindow] BGM {BgmSoundId} started.");
        }
    }
}