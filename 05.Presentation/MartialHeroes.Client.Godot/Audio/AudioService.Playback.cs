using Godot;

namespace MartialHeroes.Client.Godot.Audio;

public sealed partial class AudioService
{
    public void PlayUiClick()
    {
        Play2dById(UiClickSfxId);
    }

    public void Play2dById(uint id, string busName = SfxBusName, bool loop = false)
    {
        var stream = GetOrLoadStream2d(id);
        if (stream is null) return;

        var volumeLinear = id is MusicExemptIdA or MusicExemptIdB
            ? 1.0f
            : DefaultSfxVolume;

        PlayStream(_sfxPlayer, stream, busName, loop, volumeLinear);
    }

    public void StartBgm(uint id)
    {
        GD.Print($"[AudioService] StartBgm called: id={id} (main-thread dispatch confirmed).");

        if (_activeBgmId == id && _bgmPlayer is not null && _bgmPlayer.Playing)
        {
            GD.Print($"[AudioService] BGM {id} already playing — dedup skip.");
            return;
        }

        if (_bgmPlayer is not null && _bgmPlayer.Playing)
        {
            GD.Print($"[AudioService] BGM {_activeBgmId} stopped.");
            try
            {
                _bgmPlayer.Stop();
            }
            catch
            {
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

    public void StopBgm()
    {
        _activeBgmId = 0;
        try
        {
            _bgmPlayer?.Stop();
        }
        catch
        {
        }

        GD.Print("[AudioService] BGM stopped.");
    }


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
            stream.Loop = loop;

            player.Stream = stream;
            player.Bus = busName;

            if (volumeLinear <= 0f)
                player.VolumeDb = -80f;
            else
                player.VolumeDb = 20f * MathF.Log10(volumeLinear);

            player.Play();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AudioService] PlayStream failed (headless?): {ex.Message}");
        }
    }


    private static void EnsureAudioBusLayout()
    {
        try
        {
            if (!BusExists(MusicBusName))
            {
                var idx = AudioServer.BusCount;
                AudioServer.AddBus(idx);
                AudioServer.SetBusName(idx, MusicBusName);
                AudioServer.SetBusSend(idx, "Master");
                GD.Print($"[AudioService] Created audio bus '{MusicBusName}' at index {idx}.");
            }

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


    private void BuildPlayers()
    {
        try
        {
            _bgmPlayer = new AudioStreamPlayer
            {
                Name = "BgmPlayer",
                Bus = MusicBusName,
                VolumeDb = 0f
            };
            AddChild(_bgmPlayer);

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


    private static string AreaTag(int areaId)
    {
        var d0 = areaId / 100;
        var d1 = areaId / 10 % 10;
        var d2 = areaId % 10;
        return $"{d0}{d1}{d2}";
    }
}