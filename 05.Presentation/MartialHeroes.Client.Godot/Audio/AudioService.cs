using Godot;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Godot.World;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.Audio;

public sealed partial class AudioService : Node
{
    private const uint UiClickSfxId = 861010101u;

    private const uint CharSelectEnterSfxId = 920100200u;


    private const uint IndoorBgmOverrideId = 863500002u;

    private const uint MusicExemptIdA = 861010109u;
    private const uint MusicExemptIdB = 861010110u;

    private const string MusicBusName = "Music";
    private const string SfxBusName = "Sfx";

    private const float DefaultMusicVolume = 1.0f;
    private const float DefaultSfxVolume = 1.0f;


    private readonly Dictionary<uint, AudioStreamOggVorbis?> _streamCache2d = new();

    private uint _activeBgmId;


    private RealClientAssets? _assets;


    private AudioStreamPlayer? _bgmPlayer;


    private volatile int _cachedActiveAreaId;


    private IClientEventBus? _eventBus;

    private EngineSceneState _lastState = EngineSceneState.Login;
    private AudioStreamPlayer? _sfxPlayer;

    private bool _vfsAvailable;

    public static AudioService? Instance { get; private set; }


    public override void _Ready()
    {
        Instance = this;
        GD.Print("[AudioService] _Ready: initialising audio subsystem.");

        EnsureAudioBusLayout();

        BuildPlayers();

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

        GetTree().NodeAdded += OnNodeAddedToTree;
        GD.Print("[AudioService] Registered for NodeAdded to wire StateButton UI-click SFX.");

        GD.Print("[AudioService] _Ready: complete.");
    }

    public override void _ExitTree()
    {
        Instance = null;

        if (GetTree() is SceneTree tree)
            tree.NodeAdded -= OnNodeAddedToTree;

        _assets?.Dispose();
        _assets = null;
    }

    public override void _Process(double delta)
    {
        try
        {
            var renderer = GetTree().Root.FindChild("RealWorldRenderer", true, false)
                as RealWorldRenderer;
            _cachedActiveAreaId = renderer?.TargetAreaId ?? 0;
        }
        catch
        {
        }

        if (_eventBus is null) return;

        PollStateMachineForAudio();
    }
}