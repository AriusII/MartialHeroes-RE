// Audio/AudioService.Playback.cs
//
// Partial class — public audio API (PlayUiClick / Play2dById / StartBgm / StopBgm),
// internal player management (BuildPlayers, PlayStream, EnsureAudioBusLayout, BusExists),
// and the path helper (AreaTag).
//
// spec: Docs/RE/specs/sound.md §4.2 (BGM always streams — 2D, > 512 KiB). CODE-CONFIRMED.
// spec: Docs/RE/specs/sound.md §5 (volume curve, CODE-CONFIRMED exact expression).
// spec: Docs/RE/specs/sound.md §6.6 (playMusicZone dedup). CODE-CONFIRMED.
// spec: Docs/RE/specs/sound.md §10.1 (four buses), §12.1 (Godot mapping). DOCUMENTED SIMPLIFICATION.

using Godot;

namespace MartialHeroes.Client.Godot.Audio;

public sealed partial class AudioService
{
    // -------------------------------------------------------------------------
    // Public audio API
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Plays the standard UI click SFX (ID 861010101).
    ///     spec: Docs/RE/names.yaml runtime_constants.UI_CLICK_SFX_ID — value=861010101.
    ///     spec: Docs/RE/specs/frontend_scenes.md — standard button click SFX. CODE-CONFIRMED.
    /// </summary>
    public void PlayUiClick()
    {
        Play2dById(UiClickSfxId);
    }

    /// <summary>
    ///     Plays a 2D (non-positional) sound by ID.
    ///     VFS path: data/sound/2d/{id}.ogg
    ///     spec: Docs/RE/specs/sound.md §3.2 (2D directory = data/sound/2d/). SAMPLE-VERIFIED.
    ///     spec: Docs/RE/specs/sound.md §2 (decimal stem, no zero-padding, .ogg unconditional). CODE-CONFIRMED.
    /// </summary>
    /// <param name="id">The 9-digit sound entry ID.</param>
    /// <param name="busName">Audio bus name ("Music" or "Sfx").</param>
    /// <param name="loop">Whether to loop the stream.</param>
    public void Play2dById(uint id, string busName = SfxBusName, bool loop = false)
    {
        var stream = GetOrLoadStream2d(id);
        if (stream is null) return;

        // Music-exempt IDs always play at full amplitude regardless of Music bus gain.
        // spec: Docs/RE/specs/sound.md §10.6 (exempt IDs 861010109/861010110). CODE-CONFIRMED.
        var volumeLinear = id is MusicExemptIdA or MusicExemptIdB
            ? 1.0f
            : DefaultSfxVolume;

        PlayStream(_sfxPlayer, stream, busName, loop, volumeLinear);
    }

    /// <summary>
    ///     Starts a BGM track looping on the Music bus.
    ///     Deduplicates: if the same ID is already playing, does not restart.
    ///     spec: Docs/RE/specs/sound.md §6.6 (playMusicZone dedup). CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/sound.md §4.2 (BGM always streams — 2D, > 512 KiB). CODE-CONFIRMED.
    /// </summary>
    /// <param name="id">The 9-digit BGM entry ID.</param>
    public void StartBgm(uint id)
    {
        // Empirical dispatch probe: this GD.Print fires when StartBgm is successfully invoked on
        // the main thread (either directly or via Callable.From(...).CallDeferred()).
        // Used to verify that Callable.From dispatch works in the headless verify loop.
        GD.Print($"[AudioService] StartBgm called: id={id} (main-thread dispatch confirmed).");

        // Dedup: skip if already playing the same BGM.
        // spec: Docs/RE/specs/sound.md §6.6 — "if the requested BGM ID is already playing, not restarted". CODE-CONFIRMED.
        if (_activeBgmId == id && _bgmPlayer is not null && _bgmPlayer.Playing)
        {
            GD.Print($"[AudioService] BGM {id} already playing — dedup skip.");
            return;
        }

        // Stop the current BGM.
        if (_bgmPlayer is not null && _bgmPlayer.Playing)
        {
            GD.Print($"[AudioService] BGM {_activeBgmId} stopped.");
            try
            {
                _bgmPlayer.Stop();
            }
            catch
            {
                /* headless guard */
            }
        }

        _activeBgmId = id;

        var stream = GetOrLoadStream2d(id);
        if (stream is null)
        {
            GD.Print($"[AudioService] BGM {id}: stream not available — no playback.");
            return;
        }

        PlayStream(_bgmPlayer, stream, MusicBusName, true, DefaultMusicVolume);
    }

    /// <summary>
    ///     Stops the currently playing BGM, if any.
    ///     spec: Docs/RE/specs/sound.md §6.6 (stopMusicZone). CODE-CONFIRMED.
    /// </summary>
    public void StopBgm()
    {
        _activeBgmId = 0;
        try
        {
            _bgmPlayer?.Stop();
        }
        catch
        {
            /* headless guard */
        }

        GD.Print("[AudioService] BGM stopped.");
    }

    // -------------------------------------------------------------------------
    // Internal: play stream via an AudioStreamPlayer
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Configures and plays a stream on the given <see cref="AudioStreamPlayer" /> node.
    ///     Volume mapping: linear amplitude [0,1] → Godot VolumeDb.
    ///     Silence (0.0) maps to hard mute (player disabled or -80 dB).
    ///     spec: Docs/RE/specs/sound.md §5 (volume curve, CODE-CONFIRMED exact expression).
    ///     Here we use the simplified standard linear→dB conversion (documented above).
    /// </summary>
    private static void PlayStream(
        AudioStreamPlayer? player,
        AudioStreamOggVorbis stream,
        string busName,
        bool loop,
        float volumeLinear)
    {
        if (player is null) return;

        try
        {
            // Set loop mode on the stream resource.
            // spec: Docs/RE/specs/sound.md §4.2 — BGM always streaming/looping; 3D SFX one-shot.
            stream.Loop = loop;

            player.Stream = stream;
            player.Bus = busName;

            // Volume: linear amplitude [0,1] → dB.
            // spec: Docs/RE/specs/sound.md §5 — X=0 → full silence (−10000 mB equivalent; here -80 dB).
            // We use a simplified linear→dB conversion: VolumeDb = 20 * log10(X).
            // DOCUMENTED SIMPLIFICATION of the legacy nested-logf curve.
            if (volumeLinear <= 0f)
                player.VolumeDb = -80f; // near-silence equivalent of −10000 mB
            else
                player.VolumeDb = 20f * MathF.Log10(volumeLinear);

            player.Play();
        }
        catch (Exception ex)
        {
            // Headless guard: audio device may be absent. Log and continue.
            // spec: CLAUDE.md Headless Verify Loop — "guard with try/catch".
            GD.PrintErr($"[AudioService] PlayStream failed (headless?): {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Internal: Godot audio bus layout
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Ensures Godot AudioServer has "Music" and "Sfx" buses in addition to the default Master bus.
    ///     The legacy model has four buses: Music / Terrain+Ambient / Char / Mob.
    ///     We simplify to two additional buses (Music and Sfx) routed through Master.
    ///     spec: Docs/RE/specs/sound.md §10.1 (four buses), §12.1 (Godot mapping). DOCUMENTED SIMPLIFICATION.
    /// </summary>
    private static void EnsureAudioBusLayout()
    {
        try
        {
            // Create "Music" bus if absent.
            if (!BusExists(MusicBusName))
            {
                var idx = AudioServer.BusCount;
                AudioServer.AddBus(idx);
                AudioServer.SetBusName(idx, MusicBusName);
                AudioServer.SetBusSend(idx, "Master");
                GD.Print($"[AudioService] Created audio bus '{MusicBusName}' at index {idx}.");
            }

            // Create "Sfx" bus if absent.
            if (!BusExists(SfxBusName))
            {
                var idx = AudioServer.BusCount;
                AudioServer.AddBus(idx);
                AudioServer.SetBusName(idx, SfxBusName);
                AudioServer.SetBusSend(idx, "Master");
                GD.Print($"[AudioService] Created audio bus '{SfxBusName}' at index {idx}.");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AudioService] EnsureAudioBusLayout failed: {ex.Message}");
        }
    }

    private static bool BusExists(string name)
    {
        for (var i = 0; i < AudioServer.BusCount; i++)
            if (AudioServer.GetBusName(i) == name)
                return true;

        return false;
    }

    // -------------------------------------------------------------------------
    // Internal: build player nodes
    // -------------------------------------------------------------------------

    private void BuildPlayers()
    {
        try
        {
            // BGM player — Music bus, looping.
            _bgmPlayer = new AudioStreamPlayer
            {
                Name = "BgmPlayer",
                Bus = MusicBusName,
                VolumeDb = 0f
            };
            AddChild(_bgmPlayer);

            // SFX player — Sfx bus, one-shot (reused for sequential SFX; previous stops on new play).
            _sfxPlayer = new AudioStreamPlayer
            {
                Name = "SfxPlayer",
                Bus = SfxBusName,
                VolumeDb = 0f
            };
            AddChild(_sfxPlayer);

            GD.Print("[AudioService] AudioStreamPlayer nodes created (BgmPlayer, SfxPlayer).");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AudioService] BuildPlayers failed: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Path helper
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Converts an area ID to a 3-digit area tag string.
    ///     spec: Docs/RE/formats/terrain.md §1.1 — d0=areaId/100, d1=(areaId/10)%10, d2=areaId%10. CONFIRMED.
    /// </summary>
    private static string AreaTag(int areaId)
    {
        var d0 = areaId / 100;
        var d1 = areaId / 10 % 10;
        var d2 = areaId % 10;
        return $"{d0}{d1}{d2}";
    }
}