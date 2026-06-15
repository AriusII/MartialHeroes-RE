// Screens/FrontEndAudio.cs
//
// Front-end audio manager: BGM loop + UI-click SFX for the pre-world screens
// (intro / login / server-select / PIN / char-select).
//
// SPEC:
//   BGM:      data/sound/2d/920100200.ogg — looped while on any front-end screen.
//             spec: Docs/RE/specs/sound.md §front-end cue map. CODE-CONFIRMED.
//   UI click: data/sound/2d/861010101.ogg — one-shot on each button activation.
//             spec: Docs/RE/specs/sound.md §front-end cue map. CODE-CONFIRMED.
//   Intro BGM: data/sound/2d/910061000.ogg — one-shot at OpeningWindow scene start.
//             spec: Docs/RE/specs/intro_sequence.md §4. CODE-CONFIRMED.
//   Login curtain SFX: data/sound/2d/861010105.ogg — one-shot at login-curtain sub-state 1→2.
//             spec: Docs/RE/specs/frontend_scenes.md §1.5 sub-state 1. CODE-CONFIRMED.
//   Sound path rule: category < 5 → data/sound/2d/<id>.ogg.
//             spec: Docs/RE/specs/sound.md §BGM in data/sound/2d/. CODE-CONFIRMED.
//
// CURSOR:
//   data/cursor/stand.dds — the front-end sword/arrow cursor image.
//   Loaded from the VFS once and set via global::Godot.Input.SetCustomMouseCursor.
//   spec: Docs/RE/specs/frontend_scenes.md §11 (cursor asset). CODE-CONFIRMED.
//
// THREADING: all Godot node mutation on the main thread.
// PASSIVE: reads VFS, plays audio. Zero game logic.

using Godot;
using MartialHeroes.Client.Godot.Dev;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// Node that manages the front-end BGM, UI-click SFX, and custom mouse cursor.
/// Add it as a child of the BootFlow node; call <see cref="PlayClickSfx"/> from any
/// button handler on the front-end screens.
///
/// <para>BGM <c>920100200</c> loops until <see cref="StopBgm"/> is called (called when the world
/// scene starts). Intro BGM <c>910061000</c> is a one-shot fired from
/// <see cref="PlayIntroBgm"/>.</para>
/// </summary>
public sealed partial class FrontEndAudio : Node
{
    // -------------------------------------------------------------------------
    // Sound-file VFS paths. spec: sound.md §BGM in data/sound/2d/. CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    // Front-end BGM (lobby loop). spec: sound.md front-end cue map. CODE-CONFIRMED.
    private const string BgmPath = "data/sound/2d/920100200.ogg"; // spec: sound.md. CODE-CONFIRMED.

    // Intro stinger (OpeningWindow). spec: intro_sequence.md §4. CODE-CONFIRMED.
    private const string IntroBgmPath = "data/sound/2d/910061000.ogg"; // spec: intro_sequence.md §4. CODE-CONFIRMED.

    // UI click SFX. spec: sound.md front-end cue map. CODE-CONFIRMED.
    private const string ClickSfxPath = "data/sound/2d/861010101.ogg"; // spec: sound.md. CODE-CONFIRMED.

    // Login curtain SFX — fired at login sub-state 1→2 (letterbox open starts).
    // spec: Docs/RE/specs/frontend_scenes.md §1.5 sub-state 1. CODE-CONFIRMED.
    private const string LoginCurtainSfxPath = "data/sound/2d/861010105.ogg"; // spec §1.5. CODE-CONFIRMED.

    // Front-end cursor. spec: frontend_scenes.md §11. CODE-CONFIRMED.
    private const string CursorPath = "data/cursor/stand.dds"; // spec: frontend_scenes.md §11. CODE-CONFIRMED.

    // -------------------------------------------------------------------------
    // AudioStreamPlayer nodes (created in _Ready)
    // -------------------------------------------------------------------------

    private AudioStreamPlayer? _bgmPlayer;
    private AudioStreamPlayer? _clickPlayer;
    private AudioStreamPlayer? _introPlayer;
    private AudioStreamPlayer? _curtainPlayer;

    // -------------------------------------------------------------------------
    // Injection point (set by BootFlow before AddChild)
    // -------------------------------------------------------------------------

    /// <summary>
    /// A shared <see cref="UiAssetLoader"/> that already has the VFS open.
    /// When null this node opens its own RealClientAssets handle for the sound/cursor files.
    /// </summary>
    public UiAssetLoader? SharedAssets { get; set; }

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        // Create the AudioStreamPlayer nodes on the main thread.
        _bgmPlayer = new AudioStreamPlayer { Name = "BgmPlayer", VolumeDb = 0f };
        _clickPlayer = new AudioStreamPlayer { Name = "ClickPlayer", VolumeDb = 0f };
        _introPlayer = new AudioStreamPlayer { Name = "IntroPlayer", VolumeDb = 0f };
        _curtainPlayer = new AudioStreamPlayer { Name = "CurtainPlayer", VolumeDb = 0f };
        AddChild(_bgmPlayer);
        AddChild(_clickPlayer);
        AddChild(_introPlayer);
        AddChild(_curtainPlayer);

        // Load assets. Try the shared loader first; fall back to our own VFS open.
        RealClientAssets? ra = null;
        bool ownsRa = false;
        if (SharedAssets is { HasVfs: true })
        {
            // Use the shared loader's raw-access path.
            // UiAssetLoader does not expose an RCA handle, so we open a thin handle ourselves.
            ra = RealClientAssets.TryOpen();
            ownsRa = true;
        }
        else
        {
            ra = RealClientAssets.TryOpen();
            ownsRa = true;
        }

        try
        {
            if (ra is not null)
            {
                LoadBgm(ra);
                LoadClickSfx(ra);
                LoadIntroBgm(ra);
                LoadLoginCurtainSfx(ra);
                LoadCursor(ra);
            }
            else
            {
                GD.Print("[FrontEndAudio] VFS unavailable — audio/cursor will be silent.");
            }
        }
        finally
        {
            if (ownsRa) ra?.Dispose();
        }
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Starts the front-end lobby BGM loop (<c>920100200.ogg</c>).
    /// Safe to call multiple times — already-looping music is not restarted.
    /// spec: Docs/RE/specs/sound.md front-end cue map. CODE-CONFIRMED.
    /// </summary>
    public void PlayBgm()
    {
        if (_bgmPlayer is null) return;
        if (_bgmPlayer.Playing) return;
        if (_bgmPlayer.Stream is null)
        {
            GD.Print("[FrontEndAudio] BGM stream not loaded — skip play.");
            return;
        }

        _bgmPlayer.Play();
        GD.Print("[FrontEndAudio] BGM 920100200 started (loop).");
    }

    /// <summary>
    /// Stops the front-end BGM. Called when the world scene is entered.
    /// spec: Docs/RE/specs/sound.md front-end cue map.
    /// </summary>
    public void StopBgm()
    {
        _bgmPlayer?.Stop();
        _introPlayer?.Stop();
        GD.Print("[FrontEndAudio] BGM stopped (entering world).");
    }

    /// <summary>
    /// Plays the intro stinger (<c>910061000.ogg</c>) once at OpeningWindow scene start.
    /// spec: Docs/RE/specs/intro_sequence.md §4. CODE-CONFIRMED.
    /// </summary>
    public void PlayIntroBgm()
    {
        if (_introPlayer is null || _introPlayer.Stream is null) return;
        _introPlayer.Stop();
        _introPlayer.Play();
        GD.Print("[FrontEndAudio] Intro stinger 910061000 played.");
    }

    /// <summary>
    /// Plays the login-curtain intro SFX (<c>861010105.ogg</c>) as a one-shot.
    /// Fired at login sub-state 1→2 (the letterbox/two-edge curtain open begins).
    /// spec: Docs/RE/specs/frontend_scenes.md §1.5 sub-state 1. CODE-CONFIRMED.
    /// </summary>
    public void PlayLoginCurtainSfx()
    {
        if (_curtainPlayer is null || _curtainPlayer.Stream is null) return;
        if (_curtainPlayer.Playing) _curtainPlayer.Stop();
        _curtainPlayer.Play();
        GD.Print("[FrontEndAudio] Login curtain SFX 861010105 played.");
    }

    /// <summary>
    /// Plays the UI-click SFX (<c>861010101.ogg</c>) as a one-shot.
    /// Call from any front-end button handler (login, server-select, PIN, char-select).
    /// spec: Docs/RE/specs/sound.md front-end cue map. CODE-CONFIRMED.
    /// </summary>
    public void PlayClickSfx()
    {
        if (_clickPlayer is null || _clickPlayer.Stream is null) return;
        // Re-start from the beginning so rapid clicks always trigger.
        if (_clickPlayer.Playing) _clickPlayer.Stop();
        _clickPlayer.Play();
    }

    // -------------------------------------------------------------------------
    // Private loaders
    // -------------------------------------------------------------------------

    private void LoadBgm(RealClientAssets ra)
    {
        AudioStream? stream = LoadOgg(ra, BgmPath, looping: true);
        if (stream is not null && _bgmPlayer is not null)
        {
            _bgmPlayer.Stream = stream;
            GD.Print($"[FrontEndAudio] BGM loaded: {BgmPath}");
        }
        else
        {
            GD.Print($"[FrontEndAudio] BGM not found in VFS: {BgmPath}");
        }
    }

    private void LoadClickSfx(RealClientAssets ra)
    {
        AudioStream? stream = LoadOgg(ra, ClickSfxPath, looping: false);
        if (stream is not null && _clickPlayer is not null)
        {
            _clickPlayer.Stream = stream;
            GD.Print($"[FrontEndAudio] Click SFX loaded: {ClickSfxPath}");
        }
        else
        {
            GD.Print($"[FrontEndAudio] Click SFX not found in VFS: {ClickSfxPath}");
        }
    }

    private void LoadIntroBgm(RealClientAssets ra)
    {
        AudioStream? stream = LoadOgg(ra, IntroBgmPath, looping: false);
        if (stream is not null && _introPlayer is not null)
        {
            _introPlayer.Stream = stream;
            GD.Print($"[FrontEndAudio] Intro BGM loaded: {IntroBgmPath}");
        }
        else
        {
            GD.Print($"[FrontEndAudio] Intro BGM not found in VFS: {IntroBgmPath}");
        }
    }

    private void LoadLoginCurtainSfx(RealClientAssets ra)
    {
        // spec: Docs/RE/specs/frontend_scenes.md §1.5 sub-state 1 "play login-enter SFX 861010105". CODE-CONFIRMED.
        AudioStream? stream = LoadOgg(ra, LoginCurtainSfxPath, looping: false);
        if (stream is not null && _curtainPlayer is not null)
        {
            _curtainPlayer.Stream = stream;
            GD.Print($"[FrontEndAudio] Login curtain SFX loaded: {LoginCurtainSfxPath}");
        }
        else
        {
            GD.Print($"[FrontEndAudio] Login curtain SFX not found in VFS: {LoginCurtainSfxPath}");
        }
    }

    private static AudioStream? LoadOgg(RealClientAssets ra, string vfsPath, bool looping)
    {
        try
        {
            ReadOnlyMemory<byte> raw = ra.GetRaw(vfsPath);
            if (raw.IsEmpty)
            {
                return null;
            }

            // Load OGG Vorbis from memory via Godot's AudioStreamOggVorbis.
            byte[] bytes = raw.ToArray();
            var stream = AudioStreamOggVorbis.LoadFromBuffer(bytes);
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
        // spec: frontend_scenes.md §11 — data/cursor/stand.dds loaded from VFS.
        // CODE-CONFIRMED. Set via global::Godot.Input.SetCustomMouseCursor.
        try
        {
            Texture2D? cursor = ra.LoadTexture(CursorPath);
            if (cursor is not null)
            {
                // Set as the default system cursor (arrow shape).
                // hotspot = (0,0) — top-left of the cursor image.
                global::Godot.Input.SetCustomMouseCursor(cursor,
                    global::Godot.Input.CursorShape.Arrow,
                    hotspot: Vector2.Zero);
                GD.Print($"[FrontEndAudio] Custom cursor set: {CursorPath}");
            }
            else
            {
                GD.Print($"[FrontEndAudio] Cursor not found in VFS: {CursorPath}");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[FrontEndAudio] LoadCursor: {ex.Message}");
        }
    }
}