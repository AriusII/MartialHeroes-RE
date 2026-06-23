
using Godot;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.Ui.Scenes;

public sealed partial class FrontEndAudio : Node
{
    private const string BgmPath = "data/sound/2d/920100200.ogg";
    private const string IntroBgmPath = "data/sound/2d/910061000.ogg";
    private const string LoginCurtainPath = "data/sound/2d/861010105.ogg";
    private const string CursorPath = "data/cursor/stand.dds";

    private const string UiClickPath = "data/sound/2d/861010101.ogg";

    private static readonly string[] ClassPreviewBgmByUiIndex =
    [
        "data/sound/2d/910065000.ogg",
        "data/sound/2d/910062000.ogg",
        "data/sound/2d/910064000.ogg",
        "data/sound/2d/910063000.ogg"
    ];

    private AudioStreamPlayer? _bgmPlayer;
    private AudioStreamPlayer? _curtainPlayer;
    private AudioStreamPlayer? _introPlayer;
    private AudioStreamPlayer? _uiClickPlayer;

    public override void _Ready()
    {
        using var ra = RealClientAssets.TryOpen();
        if (ra is not null)
            LoadCursor(ra);
    }


    public void PlayBgm()
    {
        EnsureBgmPlayer();
        if (_bgmPlayer is null || _bgmPlayer.Stream is null) return;
        if (_bgmPlayer.Playing) return;
        _bgmPlayer.Play();
        GD.Print("[FrontEndAudio] Lobby BGM 920100200 started (loop).");
    }

    public void StopBgm()
    {
        _bgmPlayer?.Stop();
        _introPlayer?.Stop();
        GD.Print("[FrontEndAudio] BGM stopped (entering world).");
    }

    public void PlayIntroBgm()
    {
        EnsureIntroPlayer();
        if (_introPlayer is null || _introPlayer.Stream is null) return;
        _introPlayer.Stop();
        _introPlayer.Play();
        GD.Print("[FrontEndAudio] Opening BGM 910061000 started (loop).");
    }

    public void PlayUiClick()
    {
        EnsureUiClickPlayer();
        if (_uiClickPlayer is null || _uiClickPlayer.Stream is null) return;
        if (_uiClickPlayer.Playing) _uiClickPlayer.Stop();
        _uiClickPlayer.Play();
    }

    public void PlayClassPreviewBgm(int uiClassIndex)
    {
        if (uiClassIndex < 0 || uiClassIndex >= ClassPreviewBgmByUiIndex.Length) return;
        EnsureBgmPlayer();
        if (_bgmPlayer is null) return;

        using var ra = RealClientAssets.TryOpen();
        if (ra is null) return;
        var s = LoadOgg(ra, ClassPreviewBgmByUiIndex[uiClassIndex], true);
        if (s is null) return;
        _bgmPlayer.Stop();
        _bgmPlayer.Stream = s;
        _bgmPlayer.Play();
        GD.Print($"[FrontEndAudio] Class-preview BGM (UI {uiClassIndex}) started on category-0 slot " +
                 "(replaced lobby BGM). spec: sound.md §15.6b.");
    }

    public void PlayLoginCurtainSfx()
    {
        EnsureCurtainPlayer();
        if (_curtainPlayer is null || _curtainPlayer.Stream is null) return;
        if (_curtainPlayer.Playing) _curtainPlayer.Stop();
        _curtainPlayer.Play();
        GD.Print("[FrontEndAudio] Login curtain SFX 861010105 played.");
    }


    private void EnsureBgmPlayer()
    {
        if (_bgmPlayer is not null) return;
        _bgmPlayer = new AudioStreamPlayer { Name = "BgmPlayer", VolumeDb = 0f };
        AddChild(_bgmPlayer);
        using var ra = RealClientAssets.TryOpen();
        if (ra is null) return;
        var s = LoadOgg(ra, BgmPath, true);
        if (s is not null) _bgmPlayer.Stream = s;
    }

    private void EnsureIntroPlayer()
    {
        if (_introPlayer is not null) return;
        _introPlayer = new AudioStreamPlayer { Name = "IntroPlayer", VolumeDb = 0f };
        AddChild(_introPlayer);
        using var ra = RealClientAssets.TryOpen();
        if (ra is null) return;
        var s = LoadOgg(ra, IntroBgmPath, true);
        if (s is not null) _introPlayer.Stream = s;
    }

    private void EnsureUiClickPlayer()
    {
        if (_uiClickPlayer is not null) return;
        _uiClickPlayer = new AudioStreamPlayer { Name = "UiClickPlayer", VolumeDb = 0f };
        AddChild(_uiClickPlayer);
        using var ra = RealClientAssets.TryOpen();
        if (ra is null) return;
        var s = LoadOgg(ra, UiClickPath, false);
        if (s is not null) _uiClickPlayer.Stream = s;
    }

    private void EnsureCurtainPlayer()
    {
        if (_curtainPlayer is not null) return;
        _curtainPlayer = new AudioStreamPlayer { Name = "CurtainPlayer", VolumeDb = 0f };
        AddChild(_curtainPlayer);
        using var ra = RealClientAssets.TryOpen();
        if (ra is null) return;
        var s = LoadOgg(ra, LoginCurtainPath, false);
        if (s is not null) _curtainPlayer.Stream = s;
    }

    private static AudioStream? LoadOgg(RealClientAssets ra, string vfsPath, bool looping)
    {
        try
        {
            var raw = ra.GetRaw(vfsPath);
            if (raw.IsEmpty) return null;
            var stream = AudioStreamOggVorbis.LoadFromBuffer(raw.ToArray());
            if (stream is null) return null;
            stream.Loop = looping;
            return stream;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[FrontEndAudio] LoadOgg('{vfsPath}'): {ex.Message}");
            return null;
        }
    }

    private static void LoadCursor(RealClientAssets ra)
    {
        try
        {
            Texture2D? cursor = ra.LoadTexture(CursorPath);
            if (cursor is not null)
                global::Godot.Input.SetCustomMouseCursor(cursor,
                    global::Godot.Input.CursorShape.Arrow, Vector2.Zero);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[FrontEndAudio] LoadCursor: {ex.Message}");
        }
    }
}