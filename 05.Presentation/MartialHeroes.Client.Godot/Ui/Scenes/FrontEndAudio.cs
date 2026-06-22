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

    // Generic front-end UI click cue — the project-wide "a button did something" sound,
    // fired by the char-create / char-select action handler at the head of every action branch
    // (appearance steppers, name/create/delete/rename confirms, slot-select confirm, back/cancel).
    // Played on the category-2 multi-voice 2D SFX path (NOT the single category-0 BGM slot), so
    // a click never displaces the lobby BGM and consecutive clicks can overlap.
    // spec: Docs/RE/specs/sound.md §15.1 (silent buttons; owner-window plays the cue) +
    //       §15.2 / §15.6 (cue id 861010101, category 2, data/sound/2d/<id>.ogg). CODE-CONFIRMED.
    private const string UiClickPath = "data/sound/2d/861010101.ogg"; // spec: sound.md §15.2. CODE-CONFIRMED.

    // Per-class character-creation preview BGM — replaces the scene BGM on the single category-0
    // music slot, selected by the create-form class button (UI index, NON-identity crossover).
    // UI 0 -> 910065000 (Monk), UI 1 -> 910062000 (Musa), UI 2 -> 910064000 (Salsu), UI 3 -> 910063000 (Dosa).
    // spec: Docs/RE/specs/sound.md §15.6b + §4.1 voice-SFX table. CODE-CONFIRMED.
    private static readonly string[] ClassPreviewBgmByUiIndex =
    [
        "data/sound/2d/910065000.ogg", // UI 0 -> Monk  (internal 4). spec §15.6b. CODE-CONFIRMED.
        "data/sound/2d/910062000.ogg", // UI 1 -> Musa  (internal 1). spec §15.6b. CODE-CONFIRMED.
        "data/sound/2d/910064000.ogg", // UI 2 -> Salsu (internal 3). spec §15.6b. CODE-CONFIRMED.
        "data/sound/2d/910063000.ogg" // UI 3 -> Dosa  (internal 2). spec §15.6b. CODE-CONFIRMED.
    ];

    // Players are created lazily on first play call.
    private AudioStreamPlayer? _bgmPlayer;
    private AudioStreamPlayer? _curtainPlayer;
    private AudioStreamPlayer? _introPlayer;
    private AudioStreamPlayer? _uiClickPlayer;

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
    ///     Plays the generic front-end UI click cue (861010101) as a one-shot on the category-2
    ///     2D SFX path. The owner window calls this at the head of each char-select / char-create
    ///     action branch (the buttons themselves are silent — spec §15.1). Restarts the player so a
    ///     rapid second click re-triggers the cue rather than being swallowed mid-play; it never
    ///     touches the category-0 BGM slot, so the lobby music is undisturbed.
    ///     spec: Docs/RE/specs/sound.md §15.1 / §15.2. CODE-CONFIRMED.
    /// </summary>
    public void PlayUiClick()
    {
        EnsureUiClickPlayer();
        if (_uiClickPlayer is null || _uiClickPlayer.Stream is null) return;
        if (_uiClickPlayer.Playing) _uiClickPlayer.Stop();
        _uiClickPlayer.Play();
    }

    /// <summary>
    ///     Plays the per-class character-creation preview BGM for the given create-form UI class
    ///     index (0..3) on the single category-0 music slot — it REPLACES the lobby BGM rather than
    ///     overlaying it (the same single-voice slot that carries 920100200). spec §15.6b.
    /// </summary>
    /// <param name="uiClassIndex">Create-form class button index 0..3 (UI order, not internal id).</param>
    public void PlayClassPreviewBgm(int uiClassIndex)
    {
        if (uiClassIndex < 0 || uiClassIndex >= ClassPreviewBgmByUiIndex.Length) return;
        EnsureBgmPlayer();
        if (_bgmPlayer is null) return;

        using var ra = RealClientAssets.TryOpen();
        if (ra is null) return;
        // Replace-not-overlay: the single category-0 slot frees the prior track as the new one is
        // acquired (free-on-id-mismatch). We model that by swapping the one BGM player's stream.
        // spec: Docs/RE/specs/sound.md §15.6b (single category-0 slot, replace not overlay). CODE-CONFIRMED.
        var s = LoadOgg(ra, ClassPreviewBgmByUiIndex[uiClassIndex], true);
        if (s is null) return;
        _bgmPlayer.Stop();
        _bgmPlayer.Stream = s;
        _bgmPlayer.Play();
        GD.Print($"[FrontEndAudio] Class-preview BGM (UI {uiClassIndex}) started on category-0 slot " +
                 "(replaced lobby BGM). spec: sound.md §15.6b.");
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

    private void EnsureUiClickPlayer()
    {
        if (_uiClickPlayer is not null) return;
        _uiClickPlayer = new AudioStreamPlayer { Name = "UiClickPlayer", VolumeDb = 0f };
        AddChild(_uiClickPlayer);
        using var ra = RealClientAssets.TryOpen();
        if (ra is null) return;
        var s = LoadOgg(ra, UiClickPath, false); // one-shot, never looped
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