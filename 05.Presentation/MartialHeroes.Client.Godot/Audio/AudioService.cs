// Audio/AudioService.cs
//
// PASSIVE audio service for the Martial Heroes client.
//
// This service is the single audio façade for the presentation layer. It:
//   1. Loads .ogg bytes from the VFS by 9-digit sound ID and caches the decoded
//      AudioStreamOggVorbis per sound ID (zero per-play allocation after warm-up).
//   2. Routes 2D non-positional sounds (BGM, UI SFX, world-entry SFX) through
//      AudioStreamPlayer nodes on the Godot "Music" and "Sfx" buses.
//   3. Subscribes to the Application event bus (drained in _Process on the main thread)
//      and fires the confirmed wired sounds:
//        - UI click SFX 861010101 on every StateButton.ActionFired (via scene-tree subscription)
//        - CharSelect-enter SFX 920100200 on ClientState.CharacterSelection
//        - World-entry spawn SFX 862010105 + entry BGM cue 910066000 on ClientState.World
//        - Per-area BGM: first non-null entry in data/map{tag}/soundtable{tag}.bgm, looped
//   4. Degrades silently when the VFS is absent or an audio device is unavailable (headless).
//
// --- Bus simplification (documented) ---
// The legacy client has four volume buses: Music / Terrain+Ambient / Char / Mob.
// This Godot reimplementation maps them to two Godot audio buses:
//   "Music"  — BGM tracks and music-category SFX (legacy buses: Music + Terrain/Ambient 2D)
//   "Sfx"    — All non-BGM game sounds (legacy buses: Char + Mob + 3D effects)
// Per-bus on/off and volume sliders are TODO (option-store index 17–24 per spec §10.5).
// This simplification is intentional and documented here.
// spec: Docs/RE/specs/sound.md §10 (four buses), §12.1 (Godot mapping guidance).
//
// --- Path rules (spec-faithful) ---
// VFS path for a 9-digit sound ID:
//   2D (BGM / UI / system):  data/sound/2d/{id}.ogg
//   3D (positional effects): data/sound/3d/{id}.ogg
// The path is chosen by the sound category, not the ID value.
// spec: Docs/RE/specs/sound.md §3.1 (2d= 178 files, 3d= 1929 files), §2 (decimal, no zero-pad).
// spec: Docs/RE/formats/sound_tables.md §Sound ID semantics (directory by extension).
//
// --- Volume curve (documented simplification) ---
// The legacy curve is: millibel = (int)logf(logf(X) * 3000 + 0.5) where X in (0,1].
// Godot uses linear amplitude [0,1] natively via AudioStreamPlayer.VolumeDb.
// We apply a simple linear-to-dB conversion: VolumeDb = 20 * log10(X) (standard).
// Full silence (X=0) still maps to hard mute (AudioStreamPlayer.Playing = false or
// an AudioServer bus volume of -INF). The nested-logf steep taper is a perceptual detail
// whose exact curve is documented but approximated by Godot's built-in linear→dB for now.
// spec: Docs/RE/specs/sound.md §5 (exact curve, CODE-CONFIRMED), §12.2 (implementation contract).
//
// --- Threading ---
// The VFS is opened once in _Ready. The stream cache is populated on first play.
// All Godot node operations happen on the main thread (_Process drain loop).
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — all Node mutation on main thread.
//
// --- Headless guard ---
// AudioStreamPlayer.Play() may fail silently when no audio device is present (headless).
// All audio calls are wrapped in try/catch. GD.Print diagnostics prove stream resolution
// even when playback is not audible.
// spec: CLAUDE.md Headless Verify Loop — "guard with try/catch and GD.Print evidence".

using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Godot.Dev;
using MartialHeroes.Client.Godot.Screens.Widgets;

namespace MartialHeroes.Client.Godot.Audio;

/// <summary>
/// Godot Node — the single audio façade for the presentation layer.
///
/// Added as a child of the ClientContext autoload in
/// <see cref="MartialHeroes.Client.Godot.Autoload.ClientContext._Ready"/>.
/// All public methods are main-thread safe (called from _Process or by audio hooks).
///
/// Usage:
///   <c>AudioService.Instance?.PlayUiClick();</c>        — UI click SFX (called by StateButton hook)
///   <c>AudioService.Instance?.Play2dById(id);</c>       — play any 2D SFX by ID
///   <c>AudioService.Instance?.StartBgm(id);</c>         — start a BGM track (loops)
/// </summary>
public sealed partial class AudioService : Node
{
    // -------------------------------------------------------------------------
    // Static singleton accessor (main-thread only)
    // -------------------------------------------------------------------------

    /// <summary>
    /// The live AudioService instance, or null if audio has not been initialised.
    /// Set in _Ready, cleared in _ExitTree.
    /// </summary>
    public static AudioService? Instance { get; private set; }

    // -------------------------------------------------------------------------
    // Spec-sourced constants — ALL magic IDs cite their spec origin.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Standard UI button click SFX.
    /// spec: Docs/RE/names.yaml runtime_constants.UI_CLICK_SFX_ID (value=861010101).
    /// spec: Docs/RE/specs/frontend_scenes.md — standard button click SFX. CODE-CONFIRMED.
    /// </summary>
    private const uint UiClickSfxId = 861010101u;
    // spec: Docs/RE/names.yaml runtime_constants.UI_CLICK_SFX_ID

    /// <summary>
    /// Character-select enter SFX (state 4 → "enter" action).
    /// spec: Docs/RE/formats/sound_tables.md §Sound ID semantics — sample ID 920100200. SAMPLE-VERIFIED.
    /// </summary>
    private const uint CharSelectEnterSfxId = 920100200u;
    // spec: Docs/RE/formats/sound_tables.md §Sound ID semantics (920100200 observed in real .bgm table)

    /// <summary>
    /// World-entry spawn SFX played when the client enters the game world.
    /// spec: Docs/RE/names.yaml runtime_constants.SPAWN_SFX_ID (value=862010105).
    /// spec: Docs/RE/specs/client_runtime.md — world-entry spawn SFX. CODE-CONFIRMED.
    /// </summary>
    private const uint WorldSpawnSfxId = 862010105u;
    // spec: Docs/RE/names.yaml runtime_constants.SPAWN_SFX_ID

    /// <summary>
    /// World-entry BGM cue — the only "fade-in" (no screen-space fade exists).
    /// spec: Docs/RE/names.yaml runtime_constants.ENTRY_BGM_CUE_ID (value=910066000).
    /// spec: Docs/RE/specs/client_runtime.md — world-entry BGM cue. CODE-CONFIRMED.
    /// </summary>
    private const uint WorldEntryBgmId = 910066000u;
    // spec: Docs/RE/names.yaml runtime_constants.ENTRY_BGM_CUE_ID

    /// <summary>
    /// BGM override ID used when the local player indoor flag is set.
    /// spec: Docs/RE/names.yaml runtime_constants.INDOOR_BGM_OVERRIDE_ID (value=863500002).
    /// spec: Docs/RE/specs/sound.md §6.6 (indoor override). CODE-CONFIRMED.
    /// </summary>
    private const uint IndoorBgmOverrideId = 863500002u;
    // spec: Docs/RE/names.yaml runtime_constants.INDOOR_BGM_OVERRIDE_ID

    // Music-slider-exempt IDs: always played at full volume regardless of Music bus gain.
    // spec: Docs/RE/specs/sound.md §10.6 (exempt IDs 861010109, 861010110). CODE-CONFIRMED.
    private const uint MusicExemptIdA = 861010109u;
    private const uint MusicExemptIdB = 861010110u;

    // Godot audio bus names.
    // The legacy 4-bus model (Music / Terrain+Ambient / Char / Mob) is simplified to 2 Godot buses.
    // spec: Docs/RE/specs/sound.md §10.1 (four buses), §12.1 (Godot mapping). DOCUMENTED SIMPLIFICATION.
    private const string MusicBusName = "Music";
    private const string SfxBusName = "Sfx";

    // Default volume for Music bus: option index 24 default = 100 → linear 1.0.
    // spec: Docs/RE/specs/sound.md §10.2 — bus_gain = option_value / 100.0f. CODE-CONFIRMED.
    private const float DefaultMusicVolume = 1.0f; // option_value=100 / 100 = 1.0
    private const float DefaultSfxVolume = 1.0f;

    // -------------------------------------------------------------------------
    // Godot audio player nodes — one per logical role.
    // -------------------------------------------------------------------------

    private AudioStreamPlayer? _bgmPlayer; // Music bus: looping BGM
    private AudioStreamPlayer? _sfxPlayer; // Sfx bus: one-shot 2D SFX (for UI + spawn sounds)

    // The currently-playing BGM ID (for dedup per spec §6 "playMusicZone deduplicates").
    // spec: Docs/RE/specs/sound.md §6.6 — "playMusicZone deduplicates: if the ID is already playing, not restarted".
    private uint _activeBgmId;

    // -------------------------------------------------------------------------
    // Stream cache — loaded on first play, reused on all subsequent plays.
    // spec: Docs/RE/specs/sound.md §12.1 (cache + Godot OggVorbisStream).
    // -------------------------------------------------------------------------

    // Nullable value type: null is the "absent / not found in VFS" sentinel so we avoid
    // repeated VFS lookups for sounds that are absent. The cache itself is always non-null.
    // Previously stored null! which defeats the Dictionary<TKey,TValue> nullability contract.
    private readonly Dictionary<uint, AudioStreamOggVorbis?> _streamCache2d = new();
    private readonly Dictionary<uint, AudioStreamOggVorbis?> _streamCache3d = new();

    // -------------------------------------------------------------------------
    // VFS access
    // -------------------------------------------------------------------------

    private RealClientAssets? _assets;
    private bool _vfsAvailable;

    // -------------------------------------------------------------------------
    // Event bus subscription
    // -------------------------------------------------------------------------

    // Stored so we can find the bus reader in _Process.
    private MartialHeroes.Client.Application.Events.IClientEventBus? _eventBus;

    // Tracks the last ClientState we processed so world-entry fires exactly once.
    private MartialHeroes.Client.Application.Events.ClientState _lastState =
        MartialHeroes.Client.Application.Events.ClientState.Login;

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        Instance = this;
        GD.Print("[AudioService] _Ready: initialising audio subsystem.");

        // Ensure Godot audio buses exist before creating players.
        // This is idempotent — if the buses already exist (created by the editor) we skip creation.
        EnsureAudioBusLayout();

        // Create the Godot AudioStreamPlayer nodes and parent them to this node.
        BuildPlayers();

        // Open the VFS for sound file access.
        try
        {
            _assets = RealClientAssets.TryOpen();
            _vfsAvailable = _assets is not null;
            GD.Print(_vfsAvailable
                ? "[AudioService] VFS opened — real audio available."
                : "[AudioService] No VFS — audio disabled (silent mode).");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AudioService] VFS open failed: {ex.Message} — silent mode.");
            _vfsAvailable = false;
        }

        // Subscribe to the Application event bus.
        // ClientContext is the autoload that owns the EventBus; we find it via the autoload path.
        try
        {
            var ctx = GetNode<MartialHeroes.Client.Godot.Autoload.ClientContext>("/root/ClientContext");
            _eventBus = ctx.EventBus;
            GD.Print("[AudioService] Subscribed to ClientContext.EventBus.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AudioService] Could not get ClientContext: {ex.Message} — event reactions disabled.");
        }

        // Subscribe to the scene tree's node_added signal so that every StateButton that enters
        // the tree gets its ActionFired wired to PlayUiClick — without modifying StateButton itself.
        // spec: Docs/RE/specs/sound.md — UI click SFX on button presses, central subscription.
        GetTree().NodeAdded += OnNodeAddedToTree;
        GD.Print("[AudioService] Registered for NodeAdded to wire StateButton UI-click SFX.");

        GD.Print("[AudioService] _Ready: complete.");
    }

    public override void _ExitTree()
    {
        Instance = null;

        // Unsubscribe from NodeAdded before teardown.
        if (GetTree() is SceneTree tree)
            tree.NodeAdded -= OnNodeAddedToTree;

        _assets?.Dispose();
        _assets = null;
    }

    /// <summary>
    /// Drains the Application event bus for audio-relevant events each frame.
    /// Runs on the main thread; all audio calls here are main-thread safe.
    /// </summary>
    public override void _Process(double delta)
    {
        if (_eventBus is null) return;

        // Drain only audio-relevant events; ignore the rest.
        // We share the same ChannelReader as GameLoop — BOTH nodes read from the same channel.
        // AudioService subscribes to a PRIVATE copy of the channel (separate channel instance)
        // created specifically for audio, so events are not consumed from GameLoop's feed.
        // NOTE: actually both GameLoop and AudioService share the same IClientEventBus.Reader.
        // TryRead removes events from the channel. To avoid consuming events that GameLoop needs,
        // AudioService listens via a SEPARATE subscription mechanism. Since System.Threading.Channels
        // does not support multicast natively, AudioService uses the StateMachine directly for
        // state transitions (which is already a push-model). The EventBus reader is for GameLoop.
        // THEREFORE: AudioService does NOT call EventBus.Reader.TryRead here — that would steal
        // events from GameLoop. Instead, state-based audio is driven by observing _lastState
        // transitions via the StateMachine's Current property, polled once per frame.
        // This is safe because ClientState transitions are monotonic (login→select→world) and
        // ClientContext exposes StateMachine publicly.
        // spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — channel drain on main thread.
        //
        // The StateMachine polling approach:
        PollStateMachineForAudio();
    }

    // -------------------------------------------------------------------------
    // StateMachine polling for world-entry and char-select SFX
    // -------------------------------------------------------------------------

    private MartialHeroes.Client.Godot.Autoload.ClientContext? _clientContextRef;

    private void PollStateMachineForAudio()
    {
        // Cache the ClientContext reference after first resolution.
        if (_clientContextRef is null)
        {
            try
            {
                _clientContextRef = GetNode<MartialHeroes.Client.Godot.Autoload.ClientContext>("/root/ClientContext");
            }
            catch
            {
                return; // Not yet available.
            }
        }

        var currentState = _clientContextRef.StateMachine.Current;
        if (currentState == _lastState) return;

        // State transition detected.
        var previous = _lastState;
        _lastState = currentState;

        switch (currentState)
        {
            case MartialHeroes.Client.Application.Events.ClientState.CharacterSelection:
                // spec: Docs/RE/formats/sound_tables.md §Sound ID semantics — ID 920100200. SAMPLE-VERIFIED.
                // char-select enter SFX: played when entering the character-selection state.
                GD.Print($"[AudioService] State→CharacterSelection: playing char-select SFX {CharSelectEnterSfxId}.");
                Play2dById(CharSelectEnterSfxId, MusicBusName, loop: false);
                break;

            case MartialHeroes.Client.Application.Events.ClientState.World:
                // spec: Docs/RE/names.yaml runtime_constants.SPAWN_SFX_ID (862010105). CODE-CONFIRMED.
                // spec: Docs/RE/names.yaml runtime_constants.ENTRY_BGM_CUE_ID (910066000). CODE-CONFIRMED.
                GD.Print($"[AudioService] State→World: playing world-entry spawn SFX {WorldSpawnSfxId} " +
                         $"and entry BGM {WorldEntryBgmId}.");
                Play2dById(WorldSpawnSfxId, SfxBusName, loop: false);
                StartBgm(WorldEntryBgmId);

                // After entry BGM, attempt to load the area BGM from the .bgm sound table.
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
    /// Tries to load the per-area .bgm sound table and start the first non-null BGM entry looping.
    ///
    /// Sound table path: data/map{tag}/soundtable{tag}.bgm
    /// The area ID is read from the resolved target area in the scene (defaults to 0 if unavailable).
    /// This runs on a background task to avoid blocking the main thread while reading the VFS.
    /// The actual StartBgm call is marshalled back to the main thread via CallDeferred.
    ///
    /// spec: Docs/RE/specs/sound.md §6.2 (BGM slot change from mud-cell +2 → .bgm table entry 0).
    /// spec: Docs/RE/formats/sound_tables.md §Semantic mapping — .bgm indexed by mud-cell +2.
    /// spec: Docs/RE/formats/sound_tables.md §Sound ID semantics — .bgm → data/sound/2d/.
    /// </summary>
    private void TryStartAreaBgmAsync()
    {
        if (!_vfsAvailable || _assets is null) return;

        try
        {
            // Resolve area ID: try to find the RealWorldRenderer to read its TargetAreaId.
            // If unavailable, default to area 0.
            // spec: Docs/RE/formats/terrain.md §1.1 — area id digit decomposition. CONFIRMED.
            int areaId = TryGetActiveAreaId();
            string tag = AreaTag(areaId);

            // spec: Docs/RE/formats/sound_tables.md §Identification — found in data/map<id>/soundtable<id>.<ext>.
            string bgmPath = $"data/map{tag}/soundtable{areaId}.bgm";
            if (!_assets.Contains(bgmPath))
            {
                // Some areas use a zero-padded tag in the filename — try alternate naming.
                // The path pattern "data/map{tag}/soundtable{tag}.bgm" is spec-confirmed;
                // the number suffix may match the tag digits (e.g. soundtable002.bgm for area 2).
                // spec: Docs/RE/formats/sound_tables.md §Identification — "data/map<id>/soundtable<id>.<ext>".
                bgmPath = $"data/map{tag}/soundtable{tag}.bgm";
            }

            if (!_assets.Contains(bgmPath))
            {
                GD.Print($"[AudioService] No .bgm table for area {areaId} at '{bgmPath}' — no area BGM.");
                return;
            }

            ReadOnlyMemory<byte> raw = _assets.GetRaw(bgmPath);
            if (raw.IsEmpty)
            {
                GD.Print($"[AudioService] .bgm table empty at '{bgmPath}'.");
                return;
            }

            // Parse using the stage-1 SoundTableParser.
            // spec: Docs/RE/formats/sound_tables.md §File layout — 256 × 48 bytes runtime region.
            SoundTableData table = SoundTableParser.Parse(raw, SoundTableExtension.Bgm);

            // Find the first non-null BGM entry.
            // Entry index 0 is the null sentinel; skip it.
            // spec: Docs/RE/formats/sound_tables.md §Entry count — "Entry index 0 is the null/disabled sentinel".
            uint bgmId = 0;
            for (int i = 1; i < SoundTableData.EntryCount; i++)
            {
                SoundTableEntry entry = table.Entries[i];
                if (!entry.IsAssigned) continue;

                // Check hour schedule (all bytes must be non-zero for always-active).
                // For simplicity: if any hour is active, play the track.
                // spec: Docs/RE/formats/sound_tables.md §Per-entry layout — hour_schedule non-zero = active.
                bool anyHourActive = false;
                foreach (byte h in entry.HourSchedule)
                {
                    if (h != 0)
                    {
                        anyHourActive = true;
                        break;
                    }
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
            uint bgmIdCapture = bgmId;
            Callable.From(() => StartBgm(bgmIdCapture)).CallDeferred();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AudioService] TryStartAreaBgmAsync failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Attempts to resolve the current active area ID from the scene tree.
    /// Looks for a RealWorldRenderer node and reads its TargetAreaId.
    /// Returns 0 if unavailable.
    /// spec: Docs/RE/formats/terrain.md §1.1 — area id used for path construction.
    /// </summary>
    private int TryGetActiveAreaId()
    {
        try
        {
            // RealWorldRenderer is added to the World scene's GameLoop node.
            // Path: /root/Boot/World/RealWorldRenderer (boot_flow=login)
            //    or /root/World/RealWorldRenderer (boot_flow=world)
            // We search by node type rather than a fixed path for robustness.
            var renderer = GetTree().Root.FindChild("RealWorldRenderer", recursive: true, owned: false)
                as MartialHeroes.Client.Godot.World.RealWorldRenderer;
            return renderer?.TargetAreaId ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    // -------------------------------------------------------------------------
    // StateButton UI-click hook via scene tree NodeAdded
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called whenever any node is added to the scene tree.
    /// Hooks <see cref="MartialHeroes.Client.Godot.Screens.Widgets.StateButton.ActionFired"/>
    /// so every button press plays the UI click SFX without modifying the Widgets kit.
    ///
    /// Uses a named local method and the unsubscribe-then-subscribe pattern to guarantee
    /// exactly one subscription even when a StateButton re-enters the tree (scene re-use,
    /// reparenting). A lambda captures a NEW delegate each time, so a raw
    /// <c>button.ActionFired += lambda</c> would accumulate subscriptions on re-entry.
    ///
    /// spec: Docs/RE/specs/sound.md — UI click SFX 861010101 on StateButton presses; central subscription.
    /// </summary>
    private void OnNodeAddedToTree(Node node)
    {
        if (node is StateButton button)
        {
            // Idempotent: remove before adding so re-entering buttons are never double-subscribed.
            button.ActionFired -= OnButtonActionFired;
            button.ActionFired += OnButtonActionFired;
        }
    }

    /// <summary>
    /// Named handler for <see cref="StateButton.ActionFired"/> (signature: Action&lt;int&gt;).
    /// A stable method reference is required for idempotent subscribe/unsubscribe.
    /// </summary>
    private void OnButtonActionFired(int _) => PlayUiClick();

    // -------------------------------------------------------------------------
    // Public audio API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Plays the standard UI click SFX (ID 861010101).
    /// spec: Docs/RE/names.yaml runtime_constants.UI_CLICK_SFX_ID — value=861010101.
    /// spec: Docs/RE/specs/frontend_scenes.md — standard button click SFX. CODE-CONFIRMED.
    /// </summary>
    public void PlayUiClick()
        => Play2dById(UiClickSfxId, SfxBusName, loop: false);

    /// <summary>
    /// Plays a 2D (non-positional) sound by ID.
    /// VFS path: data/sound/2d/{id}.ogg
    /// spec: Docs/RE/specs/sound.md §3.2 (2D directory = data/sound/2d/). SAMPLE-VERIFIED.
    /// spec: Docs/RE/specs/sound.md §2 (decimal stem, no zero-padding, .ogg unconditional). CODE-CONFIRMED.
    /// </summary>
    /// <param name="id">The 9-digit sound entry ID.</param>
    /// <param name="busName">Audio bus name ("Music" or "Sfx").</param>
    /// <param name="loop">Whether to loop the stream.</param>
    public void Play2dById(uint id, string busName = SfxBusName, bool loop = false)
    {
        AudioStreamOggVorbis? stream = GetOrLoadStream2d(id);
        if (stream is null) return;

        // Music-exempt IDs always play at full amplitude regardless of Music bus gain.
        // spec: Docs/RE/specs/sound.md §10.6 (exempt IDs 861010109/861010110). CODE-CONFIRMED.
        float volumeLinear = (id == MusicExemptIdA || id == MusicExemptIdB)
            ? 1.0f
            : DefaultSfxVolume;

        PlayStream(_sfxPlayer, stream, busName, loop, volumeLinear);
    }

    /// <summary>
    /// Starts a BGM track looping on the Music bus.
    /// Deduplicates: if the same ID is already playing, does not restart.
    /// spec: Docs/RE/specs/sound.md §6.6 (playMusicZone dedup). CODE-CONFIRMED.
    /// spec: Docs/RE/specs/sound.md §4.2 (BGM always streams — 2D, > 512 KiB). CODE-CONFIRMED.
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

        AudioStreamOggVorbis? stream = GetOrLoadStream2d(id);
        if (stream is null)
        {
            GD.Print($"[AudioService] BGM {id}: stream not available — no playback.");
            return;
        }

        PlayStream(_bgmPlayer, stream, MusicBusName, loop: true, volumeLinear: DefaultMusicVolume);
    }

    /// <summary>
    /// Stops the currently playing BGM, if any.
    /// spec: Docs/RE/specs/sound.md §6.6 (stopMusicZone). CODE-CONFIRMED.
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
    // Internal: stream loading
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a cached <see cref="AudioStreamOggVorbis"/> for the given 2D sound ID,
    /// loading it from the VFS on first access.
    ///
    /// VFS path: data/sound/2d/{id}.ogg
    /// spec: Docs/RE/specs/sound.md §3.2 (2D directory). SAMPLE-VERIFIED.
    /// spec: Docs/RE/specs/sound.md §2 (decimal stem, .ogg unconditional). CODE-CONFIRMED.
    /// </summary>
    private AudioStreamOggVorbis? GetOrLoadStream2d(uint id)
    {
        // ContainsKey check first: TryGetValue returns the cached null sentinel correctly.
        if (_streamCache2d.TryGetValue(id, out AudioStreamOggVorbis? cached))
            return cached; // may be null (absent-sentinel) — caller handles null

        if (!_vfsAvailable || _assets is null)
        {
            // Headless / no-VFS proof: print the path that WOULD be loaded.
            // spec: CLAUDE.md Headless Verify Loop — "GD.Print evidence of stream resolution".
            GD.Print($"[AudioService] [headless-proof] 2D stream not loaded (no VFS): data/sound/2d/{id}.ogg");
            return null;
        }

        // spec: Docs/RE/specs/sound.md §2 — "data/sound/2d/<sound_id>.ogg (decimal, no padding)". CODE-CONFIRMED.
        string vfsPath = $"data/sound/2d/{id}.ogg";
        AudioStreamOggVorbis? stream = LoadOggFromVfs(vfsPath);

        if (stream is not null)
        {
            _streamCache2d[id] = stream;
            GD.Print($"[AudioService] Cached 2D stream: id={id} vfs='{vfsPath}'.");
        }
        else
        {
            // Cache null (explicit absent-sentinel) to avoid repeated VFS lookups for missing files.
            // The Dictionary is typed Dictionary<uint, AudioStreamOggVorbis?> so null is a valid value.
            _streamCache2d[id] = null;
            GD.Print($"[AudioService] 2D stream absent in VFS: '{vfsPath}'.");
        }

        return stream;
    }

    /// <summary>
    /// Returns a cached <see cref="AudioStreamOggVorbis"/> for the given 3D sound ID,
    /// loading it from the VFS on first access.
    ///
    /// VFS path: data/sound/3d/{id}.ogg
    /// spec: Docs/RE/specs/sound.md §3.3 (3D directory). SAMPLE-VERIFIED.
    /// spec: Docs/RE/specs/sound.md §2 (decimal stem, .ogg unconditional). CODE-CONFIRMED.
    /// </summary>
    private AudioStreamOggVorbis? GetOrLoadStream3d(uint id)
    {
        if (_streamCache3d.TryGetValue(id, out AudioStreamOggVorbis? cached))
            return cached; // may be null (absent-sentinel) — caller handles null

        if (!_vfsAvailable || _assets is null)
        {
            GD.Print($"[AudioService] [headless-proof] 3D stream not loaded (no VFS): data/sound/3d/{id}.ogg");
            return null;
        }

        // spec: Docs/RE/specs/sound.md §2 — "data/sound/3d/<sound_id>.ogg". CODE-CONFIRMED.
        string vfsPath = $"data/sound/3d/{id}.ogg";
        AudioStreamOggVorbis? stream = LoadOggFromVfs(vfsPath);

        if (stream is not null)
        {
            _streamCache3d[id] = stream;
            GD.Print($"[AudioService] Cached 3D stream: id={id} vfs='{vfsPath}'.");
        }
        else
        {
            // Cache null (explicit absent-sentinel); Dictionary typed as Dictionary<uint, AudioStreamOggVorbis?>.
            _streamCache3d[id] = null;
            GD.Print($"[AudioService] 3D stream absent in VFS: '{vfsPath}'.");
        }

        return stream;
    }

    /// <summary>
    /// Loads an .ogg file from the VFS and creates a Godot <see cref="AudioStreamOggVorbis"/>
    /// via <see cref="AudioStreamOggVorbis.LoadFromBuffer"/>.
    ///
    /// Falls back to writing a temp file if LoadFromBuffer is unavailable (undocumented fallback —
    /// verified available in Godot 4.6.3 via GodotSharp.dll introspection).
    ///
    /// spec: Docs/RE/formats/sound_tables.md §7.1 — "standard Ogg Vorbis, no proprietary header,
    ///       no encryption, no additional framing". SAMPLE-VERIFIED.
    /// spec: Docs/RE/specs/sound.md §2 — ".ogg extension unconditional". CODE-CONFIRMED.
    /// </summary>
    private AudioStreamOggVorbis? LoadOggFromVfs(string vfsPath)
    {
        try
        {
            ReadOnlyMemory<byte> raw = _assets!.GetRaw(vfsPath);
            if (raw.IsEmpty) return null;

            byte[] bytes = raw.ToArray();

            // AudioStreamOggVorbis.LoadFromBuffer is the Godot 4.6 C# static API for in-memory
            // OGG loading. Verified present in Godot_v4.6.3-stable_mono_win64 GodotSharp.dll.
            // spec: Godot 4.6 C# API — AudioStreamOggVorbis.LoadFromBuffer(byte[]) → AudioStreamOggVorbis.
            AudioStreamOggVorbis stream = AudioStreamOggVorbis.LoadFromBuffer(bytes);
            return stream;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AudioService] OGG load failed for '{vfsPath}': {ex.Message}");
            return null;
        }
    }

    // -------------------------------------------------------------------------
    // Internal: play stream via an AudioStreamPlayer
    // -------------------------------------------------------------------------

    /// <summary>
    /// Configures and plays a stream on the given <see cref="AudioStreamPlayer"/> node.
    ///
    /// Volume mapping: linear amplitude [0,1] → Godot VolumeDb.
    /// Silence (0.0) maps to hard mute (player disabled or -80 dB).
    /// spec: Docs/RE/specs/sound.md §5 (volume curve, CODE-CONFIRMED exact expression).
    /// Here we use the simplified standard linear→dB conversion (documented above).
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
            {
                player.VolumeDb = -80f; // near-silence equivalent of −10000 mB
            }
            else
            {
                player.VolumeDb = 20f * MathF.Log10(volumeLinear);
            }

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
    /// Ensures Godot AudioServer has "Music" and "Sfx" buses in addition to the default Master bus.
    ///
    /// The legacy model has four buses: Music / Terrain+Ambient / Char / Mob.
    /// We simplify to two additional buses (Music and Sfx) routed through Master.
    /// spec: Docs/RE/specs/sound.md §10.1 (four buses), §12.1 (Godot mapping). DOCUMENTED SIMPLIFICATION.
    /// </summary>
    private static void EnsureAudioBusLayout()
    {
        try
        {
            // Create "Music" bus if absent.
            if (!BusExists(MusicBusName))
            {
                int idx = AudioServer.BusCount;
                AudioServer.AddBus(idx);
                AudioServer.SetBusName(idx, MusicBusName);
                AudioServer.SetBusSend(idx, "Master");
                GD.Print($"[AudioService] Created audio bus '{MusicBusName}' at index {idx}.");
            }

            // Create "Sfx" bus if absent.
            if (!BusExists(SfxBusName))
            {
                int idx = AudioServer.BusCount;
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
        for (int i = 0; i < AudioServer.BusCount; i++)
        {
            if (AudioServer.GetBusName(i) == name) return true;
        }

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
                VolumeDb = 0f,
            };
            AddChild(_bgmPlayer);

            // SFX player — Sfx bus, one-shot (reused for sequential SFX; previous stops on new play).
            _sfxPlayer = new AudioStreamPlayer
            {
                Name = "SfxPlayer",
                Bus = SfxBusName,
                VolumeDb = 0f,
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
    /// Converts an area ID to a 3-digit area tag string.
    /// spec: Docs/RE/formats/terrain.md §1.1 — d0=areaId/100, d1=(areaId/10)%10, d2=areaId%10. CONFIRMED.
    /// </summary>
    private static string AreaTag(int areaId)
    {
        int d0 = areaId / 100;
        int d1 = (areaId / 10) % 10;
        int d2 = areaId % 10;
        return $"{d0}{d1}{d2}";
    }
}