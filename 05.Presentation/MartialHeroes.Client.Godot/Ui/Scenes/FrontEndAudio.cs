// Screens/FrontEndAudio.cs
//
// Per-scene audio node: loads and plays exactly the cue(s) required by its owning scene.
// Each scene creates its own instance and calls only the play method(s) it needs;
// streams are loaded lazily on first play so no scene touches another scene's cues.
//
// Scene cue assignments (spec: Docs/RE/specs/sound.md §15.2 / §15.6c):
//   Login  (state 1): 861010105 login curtain SFX only — no BGM, no lobby cue.
//   Opening(state 3): 910061000 opening BGM (looped) only.
//   Select (state 4): 920100200 lobby BGM (looped) — PlayBgm().
//
// Sound path rule: category < 5 → data/sound/2d/<id>.ogg.
//   spec: Docs/RE/specs/sound.md §BGM in data/sound/2d/. CODE-CONFIRMED.
//
// Cursor: data/cursor/stand.dds — set in _Ready for all front-end scenes.
//   spec: Docs/RE/specs/frontend_scenes.md §11. CODE-CONFIRMED.
//
// THREADING: all Godot node mutation on the main thread. PASSIVE: reads VFS, plays audio.

using Godot;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.Ui.Scenes;

/// <summary>
///     Node that manages per-scene front-end audio and the custom mouse cursor.
///     Each consuming scene creates one instance and calls only its required cue(s).
///     Streams are loaded lazily on first play — no scene loads another scene's cue.
/// </summary>
public sealed partial class FrontEndAudio : Node
{
    // VFS paths. spec: Docs/RE/specs/sound.md §BGM in data/sound/2d/.
    private const string BgmPath = "data/sound/2d/920100200.ogg"; // spec: sound.md §15.2. CODE-CONFIRMED.
    private const string IntroBgmPath = "data/sound/2d/910061000.ogg"; // spec: sound.md §15.6c. CODE-CONFIRMED.
    private const string LoginCurtainPath = "data/sound/2d/861010105.ogg"; // spec: sound.md §15.2. CODE-CONFIRMED.
    private const string CursorPath = "data/cursor/stand.dds"; // spec: frontend_scenes.md §11.

    // Players are created lazily on first play call.
    private AudioStreamPlayer? _bgmPlayer;
    private AudioStreamPlayer? _curtainPlayer;
    private AudioStreamPlayer? _introPlayer;

    public override void _Ready()
    {
        // Cursor is scene-global; set it immediately.
        using var ra = RealClientAssets.TryOpen();
        if (ra is not null)
            LoadCursor(ra);
    }

    // -------------------------------------------------------------------------
    // Public API — call only the method(s) your scene requires.
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Starts the lobby BGM loop (920100200). Select (state 4) only.
    ///     spec: Docs/RE/specs/sound.md §15.2. CODE-CONFIRMED.
    /// </summary>
    public void PlayBgm()
    {
        EnsureBgmPlayer();
        if (_bgmPlayer is null || _bgmPlayer.Stream is null) return;
        if (_bgmPlayer.Playing) return;
        _bgmPlayer.Play();
        GD.Print("[FrontEndAudio] Lobby BGM 920100200 started (loop).");
    }

    /// <summary>
    ///     Stops the lobby BGM. Called when the world scene is entered.
    ///     spec: Docs/RE/specs/sound.md §15.2.
    /// </summary>
    public void StopBgm()
    {
        _bgmPlayer?.Stop();
        _introPlayer?.Stop();
        GD.Print("[FrontEndAudio] BGM stopped (entering world).");
    }

    /// <summary>
    ///     Starts the looped opening BGM (910061000). Opening (state 3) only.
    ///     spec: Docs/RE/specs/sound.md §15.6c. CODE-CONFIRMED.
    /// </summary>
    public void PlayIntroBgm()
    {
        EnsureIntroPlayer();
        if (_introPlayer is null || _introPlayer.Stream is null) return;
        _introPlayer.Stop();
        _introPlayer.Play();
        GD.Print("[FrontEndAudio] Opening BGM 910061000 started (loop).");
    }

    /// <summary>
    ///     Plays the login curtain SFX (861010105) as a one-shot. Login (state 1) only.
    ///     Fired at login sub-state 1→2.
    ///     spec: Docs/RE/specs/frontend_scenes.md §1.5 sub-state 1. CODE-CONFIRMED.
    /// </summary>
    public void PlayLoginCurtainSfx()
    {
        EnsureCurtainPlayer();
        if (_curtainPlayer is null || _curtainPlayer.Stream is null) return;
        if (_curtainPlayer.Playing) _curtainPlayer.Stop();
        _curtainPlayer.Play();
        GD.Print("[FrontEndAudio] Login curtain SFX 861010105 played.");
    }

    // -------------------------------------------------------------------------
    // Lazy player initialisation
    // -------------------------------------------------------------------------

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
        // spec: Docs/RE/specs/frontend_scenes.md §11 — data/cursor/stand.dds. CODE-CONFIRMED.
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