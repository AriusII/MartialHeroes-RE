// Audio/AudioService.EventSubscriber.cs
//
// Partial class — event subscription, state-machine polling, and area BGM resolution.
// All public-facing audio logic is in AudioService.Playback.cs.
// Stream loading is in AudioService.StreamCache.cs.
//
// spec: Docs/RE/specs/sound.md §6.6 (BGM zone change + indoor override).
// spec: Docs/RE/specs/frontend_scenes.md §3.8.1 (double-music defect + fix contract).

using Godot;
using MartialHeroes.Assets.Parsers.Audio;
using MartialHeroes.Assets.Parsers.Audio.Models;
using MartialHeroes.Assets.Parsers.Texture;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.Audio;

public sealed partial class AudioService
{
    // -------------------------------------------------------------------------
    // StateMachine polling for world-entry and char-select SFX
    // -------------------------------------------------------------------------

    private ClientContext? _clientContextRef;

    private void PollStateMachineForAudio()
    {
        // Cache the ClientContext reference after first resolution.
        if (_clientContextRef is null)
            try
            {
                _clientContextRef = GetNode<ClientContext>("/root/ClientContext");
            }
            catch
            {
                return; // Not yet available.
            }

        var currentState = _clientContextRef.SceneMachine.Current.State;
        if (currentState == _lastState) return;

        // State transition detected.
        _lastState = currentState;

        switch (currentState)
        {
            case EngineSceneState.Select:
                // spec: Docs/RE/specs/frontend_scenes.md §3.8.1 — char-select BGM 920100200 is started
                // by the select-window constructor (state-4 enter); the looping front-end BGM from
                // FrontEndAudio.PlayBgm() is ALREADY playing this same cue when the state transition fires.
                // The fix contract (§3.8.1): guard re-issue so entering the scene with the cue already
                // playing on the music slot is IDEMPOTENT — do not start a second voice.
                // FrontEndAudio has already started 920100200 looping via StartBgm-equivalent; the
                // AudioService one-shot here would cause the double-music defect: two voices on the
                // Music bus for the same cue. Drop the one-shot; the looping BGM continues seamlessly.
                // spec: Docs/RE/specs/frontend_scenes.md §3.8.1 (double-music defect + fix contract).
                if (_activeBgmId == CharSelectEnterSfxId && _bgmPlayer is not null && _bgmPlayer.Playing)
                {
                    GD.Print($"[AudioService] State→CharacterSelection: BGM {CharSelectEnterSfxId} already " +
                             "looping — idempotent skip (§3.8.1 fix contract). No second voice started.");
                }
                else
                {
                    // BGM is not yet playing (e.g. VFS absent / FrontEndAudio not initialised).
                    // Start it now to fulfil the spec requirement that the cue plays on char-select entry.
                    // spec: Docs/RE/specs/frontend_scenes.md §3.8.1 — 920100200 looped on state-4 enter.
                    GD.Print($"[AudioService] State→CharacterSelection: BGM not yet playing — " +
                             $"starting {CharSelectEnterSfxId} via StartBgm (dedup guard inside). §3.8.1.");
                    StartBgm(CharSelectEnterSfxId);
                }

                break;

            case EngineSceneState.InGame:
                // spec: Docs/RE/names.yaml runtime_constants.SPAWN_SFX_ID (862010105) — 3D directional
                //       spawn sound, kind 5; 200-unit audible radius. CODE-CONFIRMED.
                // NOTE: 910066000 (former "WorldEntryBgmId") is UNVERIFIED per sound.md §15.6c and is
                //       NOT played here. Per-area BGM comes from TryStartAreaBgmAsync below.
                GD.Print("[AudioService] State→World: entering game world. " +
                         "Spawn SFX is 3D actor-routed (kind 5); area BGM via .bgm table.");

                // Attempt to load the area BGM from the .bgm sound table.
                // The area ID is resolved from the RealWorldRenderer (if active) or defaults to 0.
                // spec: Docs/RE/formats/sound_tables.md §Sound ID semantics — .bgm → data/sound/2d/.
                _ = Task.Run(TryStartAreaBgmAsync);
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Area BGM from .bgm sound table
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Tries to select and start the per-area BGM looping on the Music bus.
    ///     Sound table path: data/map{tag}/soundtable{tag}.bgm
    ///     The area ID is read from the resolved target area in the scene (defaults to 0 if unavailable).
    ///     This runs on a background task to avoid blocking the main thread while reading the VFS.
    ///     The actual StartBgm call is marshalled back to the main thread via CallDeferred.
    ///     BGM selection (spec §6.6): the original picks the .bgm table entry whose index is the MUD cell
    ///     byte at offset +0x02 under the local player's (X,Z), and — if the player's indoor/instanced flag
    ///     is set — forces the override ID 863500002 instead of the table entry. The dedup in StartBgm
    ///     reproduces the original playMusicZone "already-playing → no restart" behaviour.
    ///     PORTING GAP (DEFERRED — documented, not invented): AudioService is not fed the per-frame player
    ///     (X,Z) → MUD-cell byte index, nor the player-actor instanced-indoor flag. Both belong to the world
    ///     ambient driver (RealWorldRenderer / actor state) which this service does not own. Until that input
    ///     is plumbed in, we apply the parts that ARE resolvable from the VFS and from area-level state:
    ///     1. The indoor override, gated on the AREA-level indoor flag from map_option{areaId}.bin (a
    ///     legitimate VFS-readable approximation of the per-player instanced flag — see §6.6 note below).
    ///     2. The deterministic per-area default entry (mud-cell index 0 → the table's first active entry)
    ///     used until the live mud-cell byte is available.
    ///     3. The StartBgm dedup.
    ///     spec: Docs/RE/specs/sound.md §6.2 (cell lookup at player X,Z), §6.6 (BGM zone change + indoor override).
    ///     spec: Docs/RE/formats/sound_tables.md §Sound ID semantics — .bgm → data/sound/2d/.
    /// </summary>
    private void TryStartAreaBgmAsync()
    {
        if (!_vfsAvailable || _assets is null) return;

        try
        {
            // Resolve area ID: try to find the RealWorldRenderer to read its TargetAreaId.
            // If unavailable, default to area 0.
            // spec: Docs/RE/formats/terrain.md §1.1 — area id digit decomposition. CONFIRMED.
            var areaId = TryGetActiveAreaId();
            var tag = AreaTag(areaId);

            // Indoor/instanced override (§6.6 step 2): when the player's indoor flag is set the BGM is
            // forced to 863500002 instead of the table entry. The per-player instanced flag is not
            // plumbed here (DEFERRED); we approximate it with the AREA-level indoor flag from
            // map_option{areaId}.bin (bare-decimal area id, no zero-padding — VFS-confirmed path).
            // spec: Docs/RE/specs/sound.md §6.6 — indoor override → 863500002.
            // spec: Docs/RE/formats/environment_bins.md — data/sky/dat/<name><id>.bin path family.
            if (IsAreaIndoor(areaId))
            {
                GD.Print($"[AudioService] Area {areaId} indoor flag set — forcing indoor BGM override " +
                         $"{IndoorBgmOverrideId} (§6.6).");
                var indoorId = IndoorBgmOverrideId;
                Callable.From(() => StartBgm(indoorId)).CallDeferred();
                return;
            }

            // spec: Docs/RE/formats/sound_tables.md §Identification — found in data/map<id>/soundtable<id>.<ext>.
            var bgmPath = $"data/map{tag}/soundtable{areaId}.bgm";
            if (!_assets.Contains(bgmPath))
                // Some areas use a zero-padded tag in the filename — try alternate naming.
                // The path pattern "data/map{tag}/soundtable{tag}.bgm" is spec-confirmed;
                // the number suffix may match the tag digits (e.g. soundtable002.bgm for area 2).
                // spec: Docs/RE/formats/sound_tables.md §Identification — "data/map<id>/soundtable<id>.<ext>".
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

            // Parse using the stage-1 SoundTableParser.
            // spec: Docs/RE/formats/sound_tables.md §File layout — 256 × 48 bytes runtime region.
            var table = SoundTableParser.Parse(raw, SoundTableExtension.Bgm);

            // DEFERRED: the correct entry is table[mud-cell byte +0x02] at the player's (X,Z). That live
            // mud-cell byte is not plumbed to AudioService (see method-level PORTING GAP). Until it is,
            // select the first ACTIVE entry as the per-area default (equivalent to a cell index that
            // points at the first populated, hour-active slot). Entry index 0 is the null sentinel.
            // spec: Docs/RE/specs/sound.md §6.6 — slot indexed by mud-cell +0x02; index 0 is the null sentinel.
            uint bgmId = 0;
            for (var i = 1; i < SoundTableData.EntryCount; i++)
            {
                var entry = table.Entries[i];
                if (!entry.IsAssigned) continue;

                // Check hour schedule (all bytes must be non-zero for always-active).
                // For simplicity: if any hour is active, play the track.
                // spec: Docs/RE/formats/sound_tables.md §Per-entry layout — hour_schedule non-zero = active.
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

            // Marshal StartBgm back to the main thread.
            // spec: PRESERVATION_AND_ARCHITECTURE.md — all Godot node mutation on main thread.
            //
            // EMPIRICAL NOTE (verified in headless run 2026-06-13):
            //   CallDeferred(MethodName.StartBgm, bgmIdCapture) invokes the method by name via
            //   Godot's reflection bridge. For plain C# methods (not [GodotMethod] attributed or
            //   virtual overrides) this dispatch is NOT guaranteed — the MethodName string may not
            //   be registered in the Godot method table. Switched to Callable.From which wraps the
            //   C# delegate directly and routes through Godot's deferred-call queue without any
            //   name lookup, making the dispatch unambiguous regardless of Godot/C# reflection state.
            var bgmIdCapture = bgmId;
            Callable.From(() => StartBgm(bgmIdCapture)).CallDeferred();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AudioService] TryStartAreaBgmAsync failed: {ex.Message}");
        }
    }

    /// <summary>
    ///     Returns true when the given area's <c>map_option{areaId}.bin</c> indoor flag is set.
    ///     This is the AREA-level indoor flag and is used here as a VFS-readable approximation of the
    ///     per-player instanced-indoor flag that §6.6 actually keys the BGM override on (the player-actor
    ///     flag is not plumbed to AudioService — see <see cref="TryStartAreaBgmAsync" /> PORTING GAP).
    ///     Returns false when the file is absent, malformed, or the VFS is unavailable.
    ///     spec: Docs/RE/specs/sound.md §6.6 — indoor override.
    ///     spec: Docs/RE/formats/environment_bins.md §1.1 — map_option indoor flag (MAPHIDE).
    /// </summary>
    private bool IsAreaIndoor(int areaId)
    {
        if (!_vfsAvailable || _assets is null) return false;

        try
        {
            // Bare-decimal area id, no zero-padding — VFS-confirmed path family.
            // spec: Docs/RE/formats/environment_bins.md — data/sky/dat/<name><id>.bin.
            var path = $"data/sky/dat/map_option{areaId}.bin";
            if (!_assets.Contains(path)) return false;

            var raw = _assets.GetRaw(path);
            if (raw.IsEmpty) return false;

            var mapOption = EnvironmentBinParsers.ParseMapOption(raw);
            return mapOption.IndoorFlag != 0;
        }
        catch (Exception ex)
        {
            // Tolerant: a missing/malformed map_option just means "not known indoor".
            // spec: Docs/RE/formats/environment_bins.md §Overview Sibling tolerance — skip-and-default.
            GD.Print($"[AudioService] map_option read failed for area {areaId}: {ex.Message} — treating as outdoor.");
            return false;
        }
    }

    /// <summary>
    ///     Returns the current active area ID from the main-thread-cached value.
    ///     The cache is refreshed every frame in <see cref="_Process" /> (main thread only) by
    ///     performing the <c>FindChild("RealWorldRenderer")</c> scene-tree lookup there. This method
    ///     is safe to call from any thread (including the thread-pool worker launched by
    ///     <c>Task.Run(TryStartAreaBgmAsync)</c>) because it reads only the volatile cached int —
    ///     no scene-tree access occurs here.
    ///     Returns 0 when the renderer is not yet present or before the first main-thread frame.
    ///     spec: Docs/RE/formats/terrain.md §1.1 — area id used for path construction.
    /// </summary>
    private int TryGetActiveAreaId()
    {
        // Volatile read: the compiler / JIT will not reorder this past any preceding write,
        // and the hardware guarantees visibility of the value last written by _Process.
        return _cachedActiveAreaId;
    }

    // -------------------------------------------------------------------------
    // HudButton UI-click hook via scene tree NodeAdded
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Called whenever any node is added to the scene tree. Hooks the Ui/ <c>HudButton</c> substrate
    ///     so every button press plays the UI click SFX without coupling AudioService to the widget type:
    ///     a HudButton tags its backing <see cref="global::Godot.TextureButton" /> with the meta key
    ///     "is_hud_button" at construction, and we wire its Pressed signal here.
    ///     HudButtons are allocated once per HUD/scene session (never re-parented), so a direct C# event
    ///     subscription is safe — they enter the tree exactly once.
    ///     spec: Docs/RE/specs/sound.md — UI click SFX 861010101 on button presses; central subscription.
    /// </summary>
    private void OnNodeAddedToTree(Node node)
    {
        if (node is TextureButton texBtn && texBtn.HasMeta("is_hud_button"))
            texBtn.Pressed += PlayUiClick;
    }
}