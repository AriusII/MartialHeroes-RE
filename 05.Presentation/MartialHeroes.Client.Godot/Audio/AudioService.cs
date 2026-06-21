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
//        - CharSelect-enter SFX 920100200 on EngineSceneState.Select
//        - Per-area BGM on EngineSceneState.InGame (910066000 = UNVERIFIED, not wired; 862010105 = 3D actor-routed, not wired here)
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
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Godot.World;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.Audio;

/// <summary>
///     Godot Node — the single audio façade for the presentation layer.
///     Added as a child of the ClientContext autoload in
///     <see cref="MartialHeroes.Client.Godot.Autoload.ClientContext._Ready" />.
///     All public methods are main-thread safe (called from _Process or by audio hooks).
///     Usage:
///     <c>AudioService.Instance?.PlayUiClick();</c>        — UI click SFX (called by StateButton hook)
///     <c>AudioService.Instance?.Play2dById(id);</c>       — play any 2D SFX by ID
///     <c>AudioService.Instance?.StartBgm(id);</c>         — start a BGM track (loops)
/// </summary>
public sealed partial class AudioService : Node
{
    // -------------------------------------------------------------------------
    // Spec-sourced constants — ALL magic IDs cite their spec origin.
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Standard UI button click SFX.
    ///     spec: Docs/RE/names.yaml runtime_constants.UI_CLICK_SFX_ID (value=861010101).
    ///     spec: Docs/RE/specs/frontend_scenes.md — standard button click SFX. CODE-CONFIRMED.
    /// </summary>
    private const uint UiClickSfxId = 861010101u;
    // spec: Docs/RE/names.yaml runtime_constants.UI_CLICK_SFX_ID

    /// <summary>
    ///     Character-select enter SFX (state 4 → "enter" action).
    ///     spec: Docs/RE/formats/sound_tables.md §Sound ID semantics — sample ID 920100200. SAMPLE-VERIFIED.
    /// </summary>
    private const uint CharSelectEnterSfxId = 920100200u;
    // spec: Docs/RE/formats/sound_tables.md §Sound ID semantics (920100200 observed in real .bgm table)

    // NOTE: WorldSpawnSfxId (862010105) from names.yaml is a 3D directional spawn sound
    // (kind 5, 200-unit radius) per client_runtime.md §spawn. It is NOT a 2D front-end cue
    // and does not belong in AudioService (which owns only 2D cues). Spawn SFX routing is an
    // actor-event concern handled by the 3D world layer.
    // spec: Docs/RE/specs/client_runtime.md §10 — "A 3D directional spawn sound (id 862010105, kind 5)".
    // spec: Docs/RE/specs/sound.md §8.1 — "triggerSfxByKind … kind-routed 3D, data/sound/3d/".

    // NOTE: 910066000 was previously wired here as "WorldEntryBgmId" but
    // sound.md §15.6c lists it under "UNVERIFIED scene role — do not wire them".
    // The per-area .bgm sound table (sound.md §6.6) is the authoritative in-world BGM source;
    // TryStartAreaBgmAsync below handles that path. Constant intentionally removed.
    // spec: Docs/RE/specs/sound.md §15.6c.

    /// <summary>
    ///     BGM override ID used when the local player indoor flag is set.
    ///     spec: Docs/RE/names.yaml runtime_constants.INDOOR_BGM_OVERRIDE_ID (value=863500002).
    ///     spec: Docs/RE/specs/sound.md §6.6 (indoor override). CODE-CONFIRMED.
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
    // Stream cache — loaded on first play, reused on all subsequent plays.
    // spec: Docs/RE/specs/sound.md §12.1 (cache + Godot OggVorbisStream).
    // -------------------------------------------------------------------------

    // Nullable value type: null is the "absent / not found in VFS" sentinel so we avoid
    // repeated VFS lookups for sounds that are absent. The cache itself is always non-null.
    // Previously stored null! which defeats the Dictionary<TKey,TValue> nullability contract.
    private readonly Dictionary<uint, AudioStreamOggVorbis?> _streamCache2d = new();

    // The currently-playing BGM ID (for dedup per spec §6 "playMusicZone deduplicates").
    // spec: Docs/RE/specs/sound.md §6.6 — "playMusicZone deduplicates: if the ID is already playing, not restarted".
    private uint _activeBgmId;
    // NOTE: _streamCache3d and GetOrLoadStream3d were removed — AudioService owns only 2D cues;
    // 3D positional SFX is a world/actor concern and had no callers here.
    // spec: Docs/RE/specs/sound.md §8.1 — 3D sounds are kind-routed from the actor layer.

    // -------------------------------------------------------------------------
    // VFS access
    // -------------------------------------------------------------------------

    private RealClientAssets? _assets;

    // -------------------------------------------------------------------------
    // Godot audio player nodes — one per logical role.
    // -------------------------------------------------------------------------

    private AudioStreamPlayer? _bgmPlayer; // Music bus: looping BGM

    // -------------------------------------------------------------------------
    // Thread-safe cached area ID
    // -------------------------------------------------------------------------

    // Refreshed on the main thread in _Process; read by TryGetActiveAreaId on the thread-pool.
    // volatile guarantees that the thread-pool worker always sees the latest value written
    // by the main thread without needing a lock (single int, always 32-bit aligned).
    // spec: Docs/RE/formats/terrain.md §1.1 — area id used for path construction.
    private volatile int _cachedActiveAreaId;

    // -------------------------------------------------------------------------
    // Event bus subscription
    // -------------------------------------------------------------------------

    // Stored so we can find the bus reader in _Process.
    private IClientEventBus? _eventBus;

    // Tracks the last scene state we processed so world-entry fires exactly once.
    private EngineSceneState _lastState = EngineSceneState.Login;
    private AudioStreamPlayer? _sfxPlayer; // Sfx bus: one-shot 2D SFX (for UI + spawn sounds)

    private bool _vfsAvailable;
    // -------------------------------------------------------------------------
    // Static singleton accessor (main-thread only)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     The live AudioService instance, or null if audio has not been initialised.
    ///     Set in _Ready, cleared in _ExitTree.
    /// </summary>
    public static AudioService? Instance { get; private set; }

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
            var ctx = GetNode<ClientContext>("/root/ClientContext");
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
    ///     Drains the Application event bus for audio-relevant events each frame.
    ///     Runs on the main thread; all audio calls here are main-thread safe.
    ///     Also refreshes <see cref="_cachedActiveAreaId" /> from the scene tree so that
    ///     <see cref="TryGetActiveAreaId" /> (called from the thread-pool) can read it safely.
    ///     spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — all Node mutation on main thread.
    ///     spec: Docs/RE/formats/terrain.md §1.1 — area id used for path construction.
    /// </summary>
    public override void _Process(double delta)
    {
        // Refresh the cached area id on the main thread.
        // FindChild is legal here because _Process runs on the Godot main thread.
        // The write is to a volatile int so the thread-pool TryStartAreaBgmAsync path
        // sees the updated value without a lock.
        // spec: Docs/RE/formats/terrain.md §1.1 — area id used for path construction.
        try
        {
            var renderer = GetTree().Root.FindChild("RealWorldRenderer", true, false)
                as RealWorldRenderer;
            _cachedActiveAreaId = renderer?.TargetAreaId ?? 0;
        }
        catch
        {
            // Tolerate headless / missing tree; cached value stays at last known good.
        }

        if (_eventBus is null) return;

        // GameLoop owns the EventBus reader (TryRead is destructive); AudioService instead polls the
        // scene machine's Current state once per frame so it never steals GameLoop's events.
        PollStateMachineForAudio();
    }
}