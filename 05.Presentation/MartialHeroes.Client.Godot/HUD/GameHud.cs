using Godot;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Client.Godot.HUD;

/// <summary>
/// Full HUD for the real-client build. Subscribes to Application events forwarded by
/// <see cref="World.GameLoop"/> and updates label text / progress bars / hotbar.
///
/// PASSIVE: zero game logic, zero stat math, zero protocol knowledge.
/// Event payloads carry all data; this node only projects them onto Godot controls.
///
/// Control hierarchy (built procedurally in _Ready — no .tscn required):
///   VBoxContainer (anchor top-left, panel)
///     Label  _stateLabel   — shows current ClientState
///     Label  _actorCount   — shows number of visible actors
///     HBoxContainer (HP row)
///       Label "HP:"
///       ProgressBar _hpBar     — current / max HP (from VitalsChanged + SpawnedEvent)
///       Label _hpText          — HP numeric value
///     HBoxContainer (MP row)
///       Label "MP:"
///       ProgressBar _mpBar     — current / max MP
///       Label _mpText          — MP numeric value
///     HBoxContainer (Level row)
///       Label "Lv:"
///       Label _levelLabel
///       Label "XP:"
///       Label _xpLabel
///     Label _buffLabel       — active buff summary (first 3 non-zero effect codes)
///     Label _combatStatsLabel — derived atk/def snapshot from CombatStatsRecomputedEvent
///   HBoxContainer (hotbar anchor bottom)
///     9 × VBoxContainer (slot panels)
///       Label  _hotbarKey[i]   — "1"–"9"
///       Label  _hotbarName[i]  — skill name (CP949 via SkillCatalogue) or "—"
///   PanelContainer (chat anchor bottom-right)
///     VBoxContainer
///       Label  _chatLabel      — last 6 lines of chat
///
/// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — HUD bound to Application state.
/// spec: Docs/RE/specs/input_ui.md §4 — hotbar displays skills from SkillHotbarSlotSetEvent.
/// </summary>
public sealed partial class GameHud : Control
{
    // -------------------------------------------------------------------------
    // Control handles (built in _Ready)
    // -------------------------------------------------------------------------

    private Label _stateLabel = null!;
    private Label _actorCount = null!;
    private ProgressBar _hpBar = null!;
    private Label _hpText = null!;
    private ProgressBar _mpBar = null!;
    private Label _mpText = null!;
    private Label _levelLabel = null!;
    private Label _xpLabel = null!;
    private Label _buffLabel = null!;
    private Label _combatStatsLabel = null!;

    // Hotbar: 9 skill slots (spec: input_ui.md §4 — hotbar slots 0–239, first 9 shown).
    private const int HotbarVisibleSlots = 9;
    private readonly Label[] _hotbarKey = new Label[HotbarVisibleSlots];
    private readonly Label[] _hotbarName = new Label[HotbarVisibleSlots];

    // Chat: ring buffer of last 6 lines.
    private readonly Queue<string> _chatLines = new(6);
    private const int ChatLineMax = 6;
    private Label _chatLabel = null!;

    // -------------------------------------------------------------------------
    // View state (display only — no domain state)
    // -------------------------------------------------------------------------

    private int _visibleActorCount;

    // We display HP/MP for the first PlayerCharacter we observe as the local player.
    private ActorKey _trackedPlayerKey;
    private bool _hasTrackedPlayer;
    private uint _trackedHp;
    private uint _trackedMaxHp;
    private uint _trackedMp;
    private uint _trackedMaxMp;
    private ushort _trackedLevel;

    // Hotbar: slot → skill ID mapping (updated by SkillHotbarSlotSetEvent).
    // spec: Docs/RE/specs/handlers.md §4 (5/33 SmsgSkillHotbarSlotSet).
    private readonly SkillId[] _hotbarSkills = new SkillId[HotbarVisibleSlots];

    // Buff summary: first 3 active effect codes.
    private readonly int[] _activeBuffCodes = new int[3];

    // ClientContext reference (for catalogue lookups).
    private ClientContext? _context;

    // -------------------------------------------------------------------------
    // Initialisation
    // -------------------------------------------------------------------------

    /// <summary>Called by GameLoop._Ready; gives the HUD its context handle for catalogue lookups.</summary>
    public void Initialise(ClientContext context)
    {
        _context = context;
    }

    public override void _Ready()
    {
        GD.Print("[GameHud] _Ready start");

        // Guard the entire HUD construction: a control creation failure must not crash the scene.
        // If the HUD partially fails, the game still runs — just without some UI elements.
        try
        {
            ReadyInternal();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameHud] _Ready failed: {ex.Message}. HUD may be partially visible.");
            // Ensure null-checked labels are non-null so event handlers don't null-ref.
            EnsureFallbackLabels();
        }
    }

    private void ReadyInternal()
    {
        // Anchor the HUD to the top-left.
        AnchorLeft = 0f;
        AnchorTop = 0f;
        AnchorRight = 0f;
        AnchorBottom = 0f;
        OffsetLeft = 8f;
        OffsetTop = 8f;
        OffsetRight = 300f;
        OffsetBottom = 230f;

        // Background panel — stats panel top-left.
        var panel = new PanelContainer();
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        panel.CustomMinimumSize = new Vector2(280, 220);
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
        hpRow.AddChild(new Label { Text = "HP: " });
        _hpBar = new ProgressBar { MinValue = 0, MaxValue = 100, Value = 100 };
        _hpBar.CustomMinimumSize = new Vector2(120, 18);
        hpRow.AddChild(_hpBar);
        _hpText = new Label { Text = "0/0" };
        hpRow.AddChild(_hpText);

        // MP row.
        var mpRow = new HBoxContainer();
        vbox.AddChild(mpRow);
        mpRow.AddChild(new Label { Text = "MP: " });
        _mpBar = new ProgressBar { MinValue = 0, MaxValue = 100, Value = 100 };
        _mpBar.CustomMinimumSize = new Vector2(120, 18);
        // Blue tint for MP bar using a StyleBoxFlat override on the fill.
        var mpFillStyle = new StyleBoxFlat();
        mpFillStyle.BgColor = new Color(0.1f, 0.3f, 0.9f, 1f);
        _mpBar.AddThemeStyleboxOverride("fill", mpFillStyle);
        mpRow.AddChild(_mpBar);
        _mpText = new Label { Text = "0/0" };
        mpRow.AddChild(_mpText);

        // Level / XP row.
        var lvRow = new HBoxContainer();
        vbox.AddChild(lvRow);
        lvRow.AddChild(new Label { Text = "Lv: " });
        _levelLabel = new Label { Text = "—" };
        lvRow.AddChild(_levelLabel);
        lvRow.AddChild(new Label { Text = "  XP: " });
        _xpLabel = new Label { Text = "0" };
        lvRow.AddChild(_xpLabel);

        // Buff summary.
        _buffLabel = new Label { Text = "Buffs: —" };
        vbox.AddChild(_buffLabel);

        // Combat stats summary.
        _combatStatsLabel = new Label { Text = "" };
        vbox.AddChild(_combatStatsLabel);

        // ---- Hotbar (bottom of screen) ----
        try
        {
            BuildHotbar();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameHud] BuildHotbar failed: {ex.Message}");
        }

        // ---- Chat (bottom-right corner) ----
        try
        {
            BuildChatPanel();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameHud] BuildChatPanel failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Ensures all Label/ProgressBar fields are non-null so event handlers never null-ref,
    /// even if _Ready failed before constructing them. Creates lightweight off-screen stubs.
    /// </summary>
    private void EnsureFallbackLabels()
    {
        _stateLabel ??= new Label();
        _actorCount ??= new Label();
        _hpBar ??= new ProgressBar();
        _hpText ??= new Label();
        _mpBar ??= new ProgressBar();
        _mpText ??= new Label();
        _levelLabel ??= new Label();
        _xpLabel ??= new Label();
        _buffLabel ??= new Label();
        _combatStatsLabel ??= new Label();
        _chatLabel ??= new Label();
        for (int i = 0; i < HotbarVisibleSlots; i++)
        {
            _hotbarKey[i] ??= new Label();
            _hotbarName[i] ??= new Label();
        }
    }

    // -------------------------------------------------------------------------
    // Hotbar construction
    // -------------------------------------------------------------------------

    private void BuildHotbar()
    {
        // Anchor to bottom-centre.
        var hotbarPanel = new PanelContainer();
        hotbarPanel.AnchorLeft = 0.5f;
        hotbarPanel.AnchorTop = 1.0f;
        hotbarPanel.AnchorRight = 0.5f;
        hotbarPanel.AnchorBottom = 1.0f;
        hotbarPanel.OffsetLeft = -225f;
        hotbarPanel.OffsetTop = -80f;
        hotbarPanel.OffsetRight = 225f;
        hotbarPanel.OffsetBottom = -4f;
        AddChild(hotbarPanel);

        var hbox = new HBoxContainer();
        hotbarPanel.AddChild(hbox);

        for (int i = 0; i < HotbarVisibleSlots; i++)
        {
            var slotVBox = new VBoxContainer();
            slotVBox.CustomMinimumSize = new Vector2(48, 64);
            hbox.AddChild(slotVBox);

            // Slot key label ("1"–"9").
            _hotbarKey[i] = new Label { Text = $"{i + 1}", HorizontalAlignment = HorizontalAlignment.Center };
            slotVBox.AddChild(_hotbarKey[i]);

            // Skill name (from SkillCatalogue via CP949 name).
            _hotbarName[i] = new Label { Text = "—", HorizontalAlignment = HorizontalAlignment.Center };
            _hotbarName[i].CustomMinimumSize = new Vector2(48, 40);
            _hotbarName[i].AutowrapMode = TextServer.AutowrapMode.Word;
            slotVBox.AddChild(_hotbarName[i]);
        }
    }

    // -------------------------------------------------------------------------
    // Chat panel construction
    // -------------------------------------------------------------------------

    private void BuildChatPanel()
    {
        var chatPanel = new PanelContainer();
        chatPanel.AnchorLeft = 1.0f;
        chatPanel.AnchorTop = 1.0f;
        chatPanel.AnchorRight = 1.0f;
        chatPanel.AnchorBottom = 1.0f;
        chatPanel.OffsetLeft = -360f;
        chatPanel.OffsetTop = -180f;
        chatPanel.OffsetRight = -4f;
        chatPanel.OffsetBottom = -4f;
        AddChild(chatPanel);

        var chatVBox = new VBoxContainer();
        chatPanel.AddChild(chatVBox);

        _chatLabel = new Label();
        _chatLabel.Text = "";
        _chatLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _chatLabel.CustomMinimumSize = new Vector2(350, 160);
        chatVBox.AddChild(_chatLabel);
    }

    // -------------------------------------------------------------------------
    // Event handlers (called from GameLoop._Process, main thread)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reacts to an actor spawn: track the first PlayerCharacter for the vitals display.
    /// No game logic — reads event payload only and updates controls.
    /// </summary>
    public void OnActorSpawned(ActorSpawnedEvent evt)
    {
        if (_actorCount is null) return; // HUD not yet ready or failed to build.
        _visibleActorCount++;
        _actorCount.Text = $"Actors: {_visibleActorCount}";

        // Track the first PlayerCharacter we see as the local player.
        if (!_hasTrackedPlayer && evt.Key.Sort == EntitySort.PlayerCharacter)
        {
            _hasTrackedPlayer = true;
            _trackedPlayerKey = evt.Key;
            _trackedHp = evt.CurrentHp;
            _trackedMaxHp = evt.MaxHp;
            _trackedLevel = evt.Level;
            // Initial MP not in spawn event; show 0/0 until VitalsChanged arrives.
            _trackedMp = 0;
            _trackedMaxMp = 0;
            RefreshVitals();
            _levelLabel.Text = $"{_trackedLevel}";
        }
    }

    /// <summary>
    /// Reacts to a vitals change event: refreshes HP / MP bars.
    /// No stat computation — values come directly from the event (server-authoritative).
    /// spec: Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml.
    /// </summary>
    public void OnActorVitalsChanged(ActorVitalsChangedEvent evt)
    {
        if (!_hasTrackedPlayer || evt.Key != _trackedPlayerKey) return;

        _trackedHp = evt.CurrentHp;
        _trackedMp = evt.CurrentMp;
        // MaxHP/MaxMP come from the spawn event or from CombatStatsRecomputed.
        // If max not yet set, show current as max so the bar shows full.
        if (_trackedMaxHp == 0) _trackedMaxHp = evt.CurrentHp;
        if (_trackedMaxMp == 0) _trackedMaxMp = evt.CurrentMp;
        RefreshVitals();
    }

    /// <summary>
    /// Reacts to a level-up event: refreshes level label and vitals.
    /// spec: Docs/RE/packets/5-32_level_up.yaml.
    /// </summary>
    public void OnActorLeveledUp(ActorLeveledUpEvent evt)
    {
        if (!_hasTrackedPlayer || evt.Key != _trackedPlayerKey) return;

        _trackedLevel = evt.NewLevel;
        _trackedHp = evt.CurrentHp;
        _trackedMp = evt.CurrentMp;
        _levelLabel.Text = $"{_trackedLevel}";
        RefreshVitals();
    }

    /// <summary>
    /// Reacts to the server's stat sync update: refreshes XP display.
    /// spec: Docs/RE/specs/handlers.md §4 (5/67 SmsgStatsUpdate).
    /// </summary>
    public void OnActorStatSync(ActorStatSyncEvent evt)
    {
        if (!_hasTrackedPlayer || evt.Key != _trackedPlayerKey) return;
        // spec: Docs/RE/specs/handlers.md §4 — CurrentXp i64 at +16: CONFIRMED.
        _xpLabel.Text = evt.CurrentXp.ToString("N0");
    }

    /// <summary>
    /// Reacts to a derived combat-stat recompute: refreshes the combat-stats summary label.
    /// The event is published on equip/buff/level changes by the Application layer.
    /// PASSIVE: we only read the pre-computed stats from the event — no formula here.
    /// spec: Docs/RE/specs/combat.md §1 / §2 — CombatStats aggregate.
    /// </summary>
    public void OnCombatStatsRecomputed(CombatStatsRecomputedEvent evt)
    {
        if (!_hasTrackedPlayer || evt.Key != _trackedPlayerKey) return;

        var s = evt.Stats;
        // Refresh MaxHP/MaxMP from the recomputed aggregate so the bars stay accurate.
        // spec: Docs/RE/specs/combat.md §1 — MaxLife (flat HP) + MaxEnergy (flat MP).
        // These are flat contributions; actual max HP/MP would require the full formula (UNVERIFIED).
        // We use them as a best-effort upper bound for the display bars.
        if (s.MaxLife > 0)
        {
            _trackedMaxHp = (uint)s.MaxLife;
            // Clamp current to new max.
            if (_trackedHp > _trackedMaxHp) _trackedHp = _trackedMaxHp;
        }

        if (s.MaxEnergy > 0)
        {
            _trackedMaxMp = (uint)s.MaxEnergy;
            if (_trackedMp > _trackedMaxMp) _trackedMp = _trackedMaxMp;
        }

        RefreshVitals();

        // Show atk/def summary using confirmed CombatStats fields.
        // spec: Docs/RE/specs/combat.md §1 — MinDamage, MaxDamage, Defence.
        _combatStatsLabel.Text = $"Atk:{s.MinDamage}–{s.MaxDamage}  Def:{s.Defence}";
    }

    /// <summary>
    /// Reacts to a buff slot change: refreshes the buff summary label (first 3 active slots).
    /// PASSIVE: reads DurationTicks from the event — no formula, no timer here.
    /// spec: Docs/RE/specs/handlers.md §4 (5/31 SmsgBuffSlotUpdate).
    /// </summary>
    public void OnBuffSlotChanged(BuffSlotChangedEvent evt)
    {
        if (!_hasTrackedPlayer || evt.Key != _trackedPlayerKey) return;
        // Track the first 3 active buff codes as a quick summary.
        // SlotIndex 0..30 — only store first 3.
        if (evt.SlotIndex < _activeBuffCodes.Length)
        {
            _activeBuffCodes[evt.SlotIndex] = evt.DurationTicks > 0 ? evt.EffectCode : 0;
        }

        RefreshBuffLabel();
    }

    /// <summary>
    /// Reacts to a skill-hotbar slot set event: updates the hotbar label for that slot.
    /// The CP949 skill name is retrieved from SkillCatalogue (empty name → "?").
    /// spec: Docs/RE/specs/handlers.md §4 (5/33 SmsgSkillHotbarSlotSet).
    /// </summary>
    public void OnSkillHotbarSlotSet(SkillHotbarSlotSetEvent evt)
    {
        // Only display the first HotbarVisibleSlots slots (0–8).
        if (evt.HotbarSlot >= HotbarVisibleSlots) return;

        int slot = evt.HotbarSlot;
        _hotbarSkills[slot] = evt.Skill;

        // Look up the skill name from the catalogue — CP949 decoded at parse time.
        // spec: Docs/RE/formats/config_tables.md §2.8 skills.scr — skill_id → name lookup UNVERIFIED
        //       (name field not confirmed in the 1504-byte record; we use the id as fallback).
        string skillName = ResolveSkillName(evt.Skill.Value);
        _hotbarName[slot].Text = skillName;
    }

    /// <summary>
    /// Reacts to a chat broadcast: appends to the rolling chat window.
    /// Text is already CP949-decoded at the Application boundary.
    /// spec: Docs/RE/packets/5-7_chat_broadcast.yaml.
    /// </summary>
    public void OnChatBroadcast(ChatBroadcastEvent evt)
    {
        string line = $"[{evt.SenderName}]: {evt.Text}";
        if (_chatLines.Count >= ChatLineMax)
            _chatLines.Dequeue();
        _chatLines.Enqueue(line);
        _chatLabel.Text = string.Join("\n", _chatLines);
    }

    /// <summary>
    /// Reacts to a client lifecycle state change: update the state label.
    /// </summary>
    public void OnClientStateChanged(ClientStateChangedEvent evt)
    {
        if (_stateLabel is null) return;
        _stateLabel.Text = $"State: {evt.Current}";
    }

    // -------------------------------------------------------------------------
    // HUD hit-test (used by HudInputHandler)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true when the screen point (x,y) lands inside any HUD control rect.
    /// Called by the <see cref="MartialHeroes.Client.Godot.Input.HudInputHandler"/> to
    /// implement the "UI is the gate" chain-of-responsibility pattern.
    /// spec: Docs/RE/specs/input_ui.md §3 — "UI hit-test first".
    /// </summary>
    public bool HitTest(int x, int y)
    {
        Vector2 pt = new Vector2(x, y);
        // Walk all direct children (panel containers) for a rect hit.
        for (int i = 0; i < GetChildCount(); i++)
        {
            if (GetChild(i) is Control c && c.GetRect().HasPoint(pt))
                return true;
        }

        return false;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void RefreshVitals()
    {
        // HP bar — display ratio from confirmed server values, no formula.
        if (_trackedMaxHp > 0)
        {
            _hpBar.MaxValue = _trackedMaxHp;
            _hpBar.Value = _trackedHp;
        }
        else
        {
            _hpBar.Value = 0;
        }

        _hpText.Text = $"{_trackedHp}/{_trackedMaxHp}";

        // MP bar.
        if (_trackedMaxMp > 0)
        {
            _mpBar.MaxValue = _trackedMaxMp;
            _mpBar.Value = _trackedMp;
        }
        else
        {
            _mpBar.Value = 0;
        }

        _mpText.Text = $"{_trackedMp}/{_trackedMaxMp}";
    }

    private void RefreshBuffLabel()
    {
        var active = System.Array.FindAll(_activeBuffCodes, c => c != 0);
        if (active.Length == 0)
        {
            _buffLabel.Text = "Buffs: —";
            return;
        }

        _buffLabel.Text = "Buffs: " + string.Join(", ", active);
    }

    /// <summary>
    /// Resolves a skill ID to a display name via SkillCatalogue.
    /// Returns "Skill#id" when the catalogue is unavailable or the ID is missing.
    /// spec: Docs/RE/formats/config_tables.md §2.8 skills.scr — skill_id lookup.
    /// NOTE: The 1504-byte skill record does not have a confirmed name field (spec §2.8
    ///       known unknowns). We display the numeric id as a dev label until the name
    ///       offset is confirmed.
    /// </summary>
    private string ResolveSkillName(uint skillId)
    {
        // skill.scr records do not have a confirmed name column (spec §2.8 known unknowns).
        // TODO: when the name field offset is confirmed in spec, update SkillCatalogue.
        // For now: show "Sk#ID" as a clearly dev label.
        return $"Sk#{skillId}";
    }
}