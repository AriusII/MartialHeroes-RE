using Godot;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Client.Godot.Autoload;

namespace MartialHeroes.Client.Godot.HUD;

/// <summary>
/// Minimal placeholder HUD. Subscribes to Application events forwarded by
/// <see cref="World.GameLoop"/> and updates label text / progress bars.
///
/// PASSIVE: zero game logic, zero stat math, zero protocol knowledge.
/// The event payload carries everything; this node only projects it onto controls.
///
/// Control hierarchy (created in _Ready — no .tscn required for the skeleton):
///   VBoxContainer (anchor top-left)
///     Label  _stateLabel   — shows current ClientState
///     Label  _actorCount   — shows number of visible actors
///     HBoxContainer
///       Label "HP:"
///       ProgressBar _hpBar — hp/maxhp of the first observed local player
///
/// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — HUD bound to Application state.
/// </summary>
public sealed partial class GameHud : Control
{
    // -------------------------------------------------------------------------
    // Control handles (built in _Ready)
    // -------------------------------------------------------------------------

    private Label _stateLabel = null!;
    private Label _actorCount = null!;
    private ProgressBar _hpBar = null!;

    // -------------------------------------------------------------------------
    // View state (display only — no domain state)
    // -------------------------------------------------------------------------

    private int _visibleActorCount;

    // We display HP for the first PlayerCharacter we observe.
    private ActorKey _trackedPlayerKey;
    private bool _hasTrackedPlayer;
    private uint _trackedHp;
    private uint _trackedMaxHp;

    // -------------------------------------------------------------------------
    // Initialisation
    // -------------------------------------------------------------------------

    /// <summary>Called by GameLoop._Ready; gives the HUD its context handle.</summary>
    public void Initialise(ClientContext context)
    {
        // Nothing to store from context right now; the HUD receives events from GameLoop.
        // If the HUD needs to call a use case (e.g. open inventory), it would store context here.
        _ = context;
    }

    public override void _Ready()
    {
        // Anchor the HUD to the top-left.
        AnchorLeft = 0f;
        AnchorTop = 0f;
        AnchorRight = 0f;
        AnchorBottom = 0f;
        OffsetLeft = 8f;
        OffsetTop = 8f;
        OffsetRight = 280f;
        OffsetBottom = 140f;

        // Background panel.
        var panel = new PanelContainer();
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        panel.Size = new Vector2(260, 130);
        AddChild(panel);

        var vbox = new VBoxContainer();
        panel.AddChild(vbox);

        _stateLabel = new Label { Text = "State: Login" };
        vbox.AddChild(_stateLabel);

        _actorCount = new Label { Text = "Actors: 0" };
        vbox.AddChild(_actorCount);

        // HP row.
        var hpRow = new HBoxContainer();
        vbox.AddChild(hpRow);

        var hpLabel = new Label { Text = "HP: " };
        hpRow.AddChild(hpLabel);

        _hpBar = new ProgressBar();
        _hpBar.MinValue = 0;
        _hpBar.MaxValue = 100;
        _hpBar.Value = 100;
        _hpBar.CustomMinimumSize = new Vector2(140, 20);
        hpRow.AddChild(_hpBar);
    }

    // -------------------------------------------------------------------------
    // Event handlers (called from GameLoop._Process, main thread)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reacts to an actor spawn: track the first PlayerCharacter for the HP bar.
    /// No game logic — we only read the event payload and update controls.
    /// </summary>
    public void OnActorSpawned(ActorSpawnedEvent evt)
    {
        _visibleActorCount++;
        _actorCount.Text = $"Actors: {_visibleActorCount}";

        // Track the first PlayerCharacter we see for the placeholder HP bar.
        if (!_hasTrackedPlayer && evt.Key.Sort == EntitySort.PlayerCharacter)
        {
            _hasTrackedPlayer = true;
            _trackedPlayerKey = evt.Key;
            _trackedHp = evt.CurrentHp;
            _trackedMaxHp = evt.MaxHp;
            RefreshHpBar();
        }
    }

    /// <summary>
    /// Reacts to a client lifecycle state change: update the state label.
    /// </summary>
    public void OnClientStateChanged(ClientStateChangedEvent evt)
    {
        _stateLabel.Text = $"State: {evt.Current}";
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void RefreshHpBar()
    {
        if (_trackedMaxHp == 0)
        {
            _hpBar.Value = 0;
            return;
        }

        // Display HP as percentage. No formula — just a ratio of the event-reported values.
        _hpBar.MaxValue = _trackedMaxHp;
        _hpBar.Value = _trackedHp;
    }
}