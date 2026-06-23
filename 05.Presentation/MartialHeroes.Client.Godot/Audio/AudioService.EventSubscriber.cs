using Godot;
using MartialHeroes.Assets.Parsers.Audio;
using MartialHeroes.Assets.Parsers.Audio.Models;
using MartialHeroes.Assets.Parsers.Texture;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.Audio;

public sealed partial class AudioService
{
    private ClientContext? _clientContextRef;

    private void PollStateMachineForAudio()
    {
        if (_clientContextRef is null)
            try
            {
                _clientContextRef = GetNode<ClientContext>("/root/ClientContext");
            }
            catch
            {
                return;
            }

        var currentState = _clientContextRef.SceneMachine.Current.State;
        if (currentState == _lastState) return;

        _lastState = currentState;

        switch (currentState)
        {
            case EngineSceneState.Select:
                if (_activeBgmId == CharSelectEnterSfxId && _bgmPlayer is not null && _bgmPlayer.Playing)
                {
                    GD.Print($"[AudioService] State→CharacterSelection: BGM {CharSelectEnterSfxId} already " +
                             "looping — idempotent skip (§3.8.1 fix contract). No second voice started.");
                }
                else
                {
                    GD.Print($"[AudioService] State→CharacterSelection: BGM not yet playing — " +
                             $"starting {CharSelectEnterSfxId} via StartBgm (dedup guard inside). §3.8.1.");
                    StartBgm(CharSelectEnterSfxId);
                }

                break;

            case EngineSceneState.InGame:
                GD.Print("[AudioService] State→World: entering game world. " +
                         "Spawn SFX is 3D actor-routed (kind 5); area BGM via .bgm table.");

                _ = Task.Run(TryStartAreaBgmAsync);
                break;
        }
    }


    private void TryStartAreaBgmAsync()
    {
        if (!_vfsAvailable || _assets is null) return;

        try
        {
            var areaId = TryGetActiveAreaId();
            var tag = AreaTag(areaId);

            if (IsAreaIndoor(areaId))
            {
                GD.Print($"[AudioService] Area {areaId} indoor flag set — forcing indoor BGM override " +
                         $"{IndoorBgmOverrideId} (§6.6).");
                var indoorId = IndoorBgmOverrideId;
                Callable.From(() => StartBgm(indoorId)).CallDeferred();
                return;
            }

            var bgmPath = $"data/map{tag}/soundtable{areaId}.bgm";
            if (!_assets.Contains(bgmPath))
                bgmPath = $"data/map{tag}/soundtable{tag}.bgm";

            if (!_assets.Contains(bgmPath))
            {
                GD.Print($"[AudioService] No .bgm table for area {areaId} at '{bgmPath}' — no area BGM.");
                return;
            }

            var raw = _assets.GetRaw(bgmPath);
            if (raw.IsEmpty)
            {
                GD.Print($"[AudioService] .bgm table empty at '{bgmPath}'.");
                return;
            }

            var table = SoundTableParser.Parse(raw, SoundTableExtension.Bgm);

            uint bgmId = 0;
            for (var i = 1; i < SoundTableData.EntryCount; i++)
            {
                var entry = table.Entries[i];
                if (!entry.IsAssigned) continue;

                var anyHourActive = false;
                foreach (var h in entry.HourSchedule)
                    if (h != 0)
                    {
                        anyHourActive = true;
                        break;
                    }

                if (anyHourActive)
                {
                    bgmId = entry.SoundEntryId;
                    break;
                }
            }

            if (bgmId == 0)
            {
                GD.Print($"[AudioService] .bgm table for area {areaId} has no active entries — no area BGM.");
                return;
            }

            GD.Print(
                $"[AudioService] Area {areaId} BGM entry found: id={bgmId} — scheduling area BGM via Callable.From.");

            var bgmIdCapture = bgmId;
            Callable.From(() => StartBgm(bgmIdCapture)).CallDeferred();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AudioService] TryStartAreaBgmAsync failed: {ex.Message}");
        }
    }

    private bool IsAreaIndoor(int areaId)
    {
        if (!_vfsAvailable || _assets is null) return false;

        try
        {
            var path = $"data/sky/dat/map_option{areaId}.bin";
            if (!_assets.Contains(path)) return false;

            var raw = _assets.GetRaw(path);
            if (raw.IsEmpty) return false;

            var mapOption = EnvironmentBinParsers.ParseMapOption(raw);
            return mapOption.IndoorFlag != 0;
        }
        catch (Exception ex)
        {
            GD.Print($"[AudioService] map_option read failed for area {areaId}: {ex.Message} — treating as outdoor.");
            return false;
        }
    }

    private int TryGetActiveAreaId()
    {
        return _cachedActiveAreaId;
    }


    private void OnNodeAddedToTree(Node node)
    {
        if (node is TextureButton texBtn && texBtn.HasMeta("is_hud_button"))
            texBtn.Pressed += PlayUiClick;
    }
}