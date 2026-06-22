using System.Collections.Immutable;
using Godot;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Contracts.Scene;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Scenes;
using MartialHeroes.Client.Godot.Ui.Scenes.Select;
using MartialHeroes.Shared.Kernel.Enums;

// spec: Docs/RE/specs/character_creation.md §1.2 / §2.1 / §3 (face/AppearanceA defaults; point-buy forwarded from create form)

namespace MartialHeroes.Client.Godot.Scene.Controllers;

/// <summary>
///     State 4 — SelectWindow. Builds the character-list UI, 3D preview actor row, and the dedicated
///     Select preview camera; all flow intents are forwarded to Application use-cases.
///     Now uses the new Ui/Scenes substrate (CharSelectWindow + CharSelectEventDrainer).
///     The 3D scene layer (CharSelectScene3D/CharCreatePreview3D) is REUSED unchanged.
///     spec: Docs/RE/specs/client_runtime.md §7.3 / §7.4; Docs/RE/specs/frontend_scenes.md §3–§8.
/// </summary>
public sealed partial class SelectScene : StubSceneController
{
    private FrontEndAudio? _audio;
    private bool _confirmInFlight;
    private ClientContext? _ctx;
    private CharSelectEventDrainer? _drainer; // NEW: typed for CharSelectWindow
    private SceneHost? _host;
    private ScreenHost? _screenHost;
    private CharSelectWindow? _select; // NEW: Ui/Scenes substrate

    /// <inheritdoc />
    public override EngineSceneState State => EngineSceneState.Select;

    /// <inheritdoc />
    public override void OnEnter(SceneHost host)
    {
        Name = $"Scene{(int)State}_{State}";
        _host = host;
        _ctx = GetNodeOrNull<ClientContext>("/root/ClientContext");

        _screenHost = new ScreenHost { Name = "SelectScreenHost" };
        AddChild(_screenHost);

        _audio = new FrontEndAudio { Name = "SelectFrontEndAudio" };
        AddChild(_audio);
        // Single-BGM contract (§3.8.1): char-select BGM 920100200 is ONE looping category-0
        // voice on ONE shared slot. PlayBgm is idempotent (FrontEndAudio guards with
        // `if (_bgmPlayer.Playing) return;` = the binary's per-id dedup + single-slot reuse),
        // so it can never double-play. Stopped on scene-exit in _ExitTree.
        // spec: Docs/RE/specs/frontend_scenes.md §3.8.1 (single-BGM contract). CODE-CONFIRMED.
        _audio.PlayBgm();

        _select = BuildSelectScreen();
        _screenHost.SetScreen(_select);

        if (_ctx is not null)
            StartEventDrain(_select, _ctx.EventBus);

        GD.Print("[SelectScene] State 4 Select built CharacterSelectScreen: roster UI, 3D preview actors, " +
                 "and Select camera dolly. spec: client_runtime.md §7.3/§7.4; frontend_scenes.md §3.");

        // Roster catch-up replay: the live 3/4 CharacterListEvent fires during LOAD state (state 3),
        // before this drainer is armed. If the Application store already retained the roster, replay
        // it directly into ApplyCharacterList so the 5 preview actors build with the correct class.
        // The drainer is still fully armed above — if a fresh 3/4 arrives later, ApplyCharacterList
        // runs again (full rebuild). This replay is additive, not a drainer suppression.
        // spec: Docs/RE/specs/frontend_scenes.md §3.1.
        var retained = _ctx?.CharacterSelection?.ProjectRetainedRoster() ?? ImmutableArray<CharacterListSlot>.Empty;
        if (retained.Length > 0)
        {
            _select.ApplyCharacterList(retained);
            GD.Print($"[SelectScene] Replayed {retained.Length} retained roster slots into ApplyCharacterList " +
                     "(live 3/4 CharacterListEvent fired pre-Select-drainer). spec: frontend_scenes.md §3.1.");
        }
    }

    public override void _ExitTree()
    {
        // Single-BGM contract (§3.8.1): stop the owned looping voice on char-select scene-exit
        // so no stale category-0 BGM survives into the world/login scene.
        _audio?.StopBgm();

        if (_select is not null)
        {
            _select.EnterGameRequested -= OnEnterGameRequested;
            _select.BackRequested -= OnBackRequested;
            _select.CreateCharacterRequested -= OnCreateCharacterRequested;
            _select.DeleteCharacterRequested -= OnDeleteCharacterRequested;
            _select.RenameCharacterRequested -= OnRenameCharacterRequested;
            _select = null;
        }

        if (_drainer is not null)
        {
            _drainer.EventDrained -= OnApplicationEvent;
            _drainer = null;
        }
    }

    private CharSelectWindow BuildSelectScreen()
    {
        // Resolve HudAtlasLibrary + HudTextLibrary from ClientContext (null if not yet ready).
        var atlas = _ctx?.HudAtlas;
        var text = _ctx?.HudText;

        var select = new CharSelectWindow
        {
            Name = "CharSelectWindow",
            Atlas = atlas,
            Text = text,
            // Inject the per-scene audio node so the window can play the generic UI click cue at the
            // head of each action branch and the per-class preview BGM on a class change. The buttons
            // themselves are silent — the owner window plays the cue. spec: sound.md §15.1 / §15.2 / §15.6b.
            Audio = _audio
        };

        select.EnterGameRequested += OnEnterGameRequested;
        select.BackRequested += OnBackRequested;
        select.CreateCharacterRequested += OnCreateCharacterRequested;
        select.DeleteCharacterRequested += OnDeleteCharacterRequested;
        select.RenameCharacterRequested += OnRenameCharacterRequested;
        return select;
    }

    private void StartEventDrain(CharSelectWindow select, IClientEventBus bus)
    {
        _drainer = new CharSelectEventDrainer { Name = "CharSelectEventDrainer" };
        _drainer.Bind(select, bus);
        _drainer.EventDrained += OnApplicationEvent;
        AddChild(_drainer);
        GD.Print("[SelectScene] CharSelectEventDrainer armed for state 4. spec: frontend_scenes.md §3.1.");
    }

    private void OnApplicationEvent(IClientEvent evt)
    {
        switch (evt)
        {
            case CharacterListEvent list:
                GD.Print($"[SelectScene] CharacterListEvent applied ({list.Characters.Length} slots). " +
                         "spec: frontend_scenes.md §3.1; login_flow.md §3.2.");
                break;
            case CharManageResultEvent manage:
                GD.Print($"[SelectScene] CharManageResult success={manage.Success} subtype={manage.Subtype} " +
                         $"count={manage.AccountCharacterCount}. spec: frontend_scenes.md §5/§6.");
                break;
            case CharRenameResultEvent rename:
                GD.Print($"[SelectScene] CharRenameResult success={rename.Success} newName='{rename.NewName}' " +
                         $"error={rename.ErrorCode}. spec: frontend_scenes.md §6.");
                break;
            case SceneStateChangedEvent stateChange when stateChange.Next.State != State:
                // Out-of-band committed transition (e.g. 3/5 Select→InGame, or 3/100 Select→Quit/Error).
                // The Application scene machine already pre-committed the new state; converge the visible
                // controller without re-advancing the machine (Advance() would jump past the target).
                // spec: Docs/RE/specs/client_runtime.md §7.5.3.
                GD.Print(
                    $"[SelectScene] SceneStateChangedEvent {stateChange.Previous.State}→{stateChange.Next.State}; " +
                    "out-of-band committed transition — calling SyncToCurrentState. spec: client_runtime.md §7.5.3.");
                _host?.CallDeferred(SceneHost.MethodName.SyncToCurrentState);
                break;
        }
    }

    private void OnEnterGameRequested(string characterName, int slotIndex)
    {
        GD.Print(
            $"[SelectScene] Enter requested for '{characterName}' slot={slotIndex}; forwarding to UseCases.SelectCharacterAsync. " +
            "spec: frontend_scenes.md §7; cmsg_char_enter.yaml.");
        _ = ConfirmSlotAsync(slotIndex);
    }

    private async Task ConfirmSlotAsync(int slotIndex)
    {
        if (_confirmInFlight) return;
        _confirmInFlight = true;

        try
        {
            if (_ctx?.UseCases is { } useCases)
                await useCases.SelectCharacterAsync(slotIndex, CancellationToken.None);
            else
                GD.PushWarning("[SelectScene] ClientContext.UseCases unavailable; cannot send 1/9 enter-game request.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SelectScene] SelectCharacterAsync({slotIndex}) failed/skipped: {ex.Message}");
        }
        finally
        {
            _confirmInFlight = false;
        }
    }

    private void OnCreateCharacterRequested(string name, int internalClass, int faceIndex, int[] stats)
    {
        GD.Print($"[SelectScene] CreateCharacterRequested(name='{name}', class={internalClass}, face={faceIndex}, " +
                 $"stats=({stats[0]},{stats[1]},{stats[2]},{stats[3]},{stats[4]})); " +
                 "forwarding to UseCases.CreateCharacterAsync. spec: frontend_scenes.md §4/§8; character_creation.md §2.1.");
        _ = CreateCharacterAsync(name, internalClass, faceIndex, stats);
    }

    private void OnDeleteCharacterRequested(int slotIndex, string characterName)
    {
        GD.Print($"[SelectScene] DeleteCharacterRequested(slot={slotIndex}, name='{characterName}'); " +
                 "forwarding to UseCases.DeleteCharacterAsync. spec: frontend_scenes.md §5/§8.");
        _ = DeleteCharacterAsync(slotIndex);
    }

    private async Task CreateCharacterAsync(string name, int internalClass, int faceIndex, int[] stats)
    {
        if (_ctx?.UseCases is not { } useCases)
        {
            GD.PushWarning("[SelectScene] Create skipped: ClientContext.UseCases unavailable.");
            return;
        }

        try
        {
            // The 5 point-buy stat display values come from the create form's spinner state
            // (CharSelectWindow._statValues[0..4], stepped via acts 25–34, seeded at 10).
            // NON-SEQUENTIAL binary-confirmed pairing: Stat0=25+/30−, Stat1=26+/31−, Stat2=27+/32−,
            // Stat3=29+/34−, Stat4=28+/33−.  Passed through verbatim; Application validates
            // (BuildPointBuy / PointBuySeed) before writing the 52-byte 1/6 body.
            // Fall back to the seed floor (10) if the stats array is malformed.
            // spec: Docs/RE/scenes/charselect.md §4.3 (spinner pairing — binary-confirmed SHA 263bd994).
            // spec: Docs/RE/specs/character_creation.md §2.1 (sum+budget=55; floor 10; seed budget 5).
            const uint statFloor = 10u; // spec: character_creation.md §2.1
            var s0 = stats is { Length: >= 1 } ? (uint)Math.Max(0, stats[0]) : statFloor;
            var s1 = stats is { Length: >= 2 } ? (uint)Math.Max(0, stats[1]) : statFloor;
            var s2 = stats is { Length: >= 3 } ? (uint)Math.Max(0, stats[2]) : statFloor;
            var s3 = stats is { Length: >= 4 } ? (uint)Math.Max(0, stats[3]) : statFloor;
            var s4 = stats is { Length: >= 5 } ? (uint)Math.Max(0, stats[4]) : statFloor;

            // Derive the remaining budget from the sum invariant: PointsRemaining = 55 − sum(stats).
            // Application's BuildPointBuy validates this; we forward the raw display value so that
            // Application can detect and report any constraint violation to the player.
            // spec: Docs/RE/specs/character_creation.md §2.1 (invariant: sum+budget = 55).
            var sum = s0 + s1 + s2 + s3 + s4;
            var pts = sum <= 55u ? 55u - sum : 0u; // spec: character_creation.md §2.1

            // Face must be in [1..7]. The create form initialises _createFaceIndex to FaceMin (1) and clamps
            // it on every stepper press, so faceIndex arriving here is already ≥ 1. We clamp defensively
            // to guarantee Face ≠ 0 (Application throws ArgumentOutOfRangeException on Face 0).
            // spec: Docs/RE/specs/character_creation.md §1.2 (Face initialised to 1, range 1..7).
            var safeFace = (ushort)Math.Clamp(faceIndex, 1, 7); // spec: character_creation.md §1.2

            var request = new CharacterCreateRequest(
                name,
                // UiClassIndex: the UI button index 0..3. InternalClassToUiIndex inverts the internal class
                // id the signal carries (emitted as UiToInternal[_createUiClass] in CharSelectWindow).
                // Application's CreateCharacterAsync re-maps it via RemapCreateClass {0→4,1→1,2→3,3→2}.
                // spec: Docs/RE/specs/character_creation.md §3 (UI-index → internal remap stays in Application).
                InternalClassToUiIndex(internalClass),
                safeFace, // spec: character_creation.md §1.2
                // AppearanceA: class-implied, not stepped on the create path; defaults to 1.
                // spec: Docs/RE/specs/character_creation.md §1.2 (AppearanceA @0x14 default 1).
                1,
                // AppearanceB: reserved; defaults to 0.
                // spec: Docs/RE/specs/character_creation.md §1.2 (AppearanceB @0x16 default 0).
                0,
                s0, // Stat0 — spinner 25+/30−. spec: charselect.md §4.3; character_creation.md §2.1.
                s1, // Stat1 — spinner 26+/31−. spec: charselect.md §4.3; character_creation.md §2.1.
                s2, // Stat2 — spinner 27+/32−. spec: charselect.md §4.3; character_creation.md §2.1.
                s3, // Stat3 — spinner 29+/34−. spec: charselect.md §4.3; character_creation.md §2.1.
                s4, // Stat4 — spinner 28+/33−. spec: charselect.md §4.3; character_creation.md §2.1.
                pts // PointsRemaining = 55 − sum(stats). spec: character_creation.md §2.1.
            );

            GD.Print($"[SelectScene] CreateCharacterAsync: name='{name}' uiClass={request.UiClassIndex} " +
                     $"face={request.Face} appA={request.AppearanceA} appB={request.AppearanceB} " +
                     $"stats=({request.Stat0},{request.Stat1},{request.Stat2},{request.Stat3},{request.Stat4}) " +
                     $"pts={request.PointsRemaining}. spec: charselect.md §4.3; character_creation.md §1.2/§2.1/§3.");

            var result = await useCases.CreateCharacterAsync(request, CancellationToken.None);
            if (!result.IsValid)
                GD.Print($"[SelectScene] Create rejected by Application validation; msgId={result.MessageId}. " +
                         "spec: frontend_scenes.md §4.4.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SelectScene] CreateCharacterAsync failed/skipped: {ex.Message}");
        }
    }

    private void OnRenameCharacterRequested(int slotIndex, string newName)
    {
        GD.Print($"[SelectScene] RenameCharacterRequested(slot={slotIndex}, newName='{newName}'); " +
                 "forwarding to UseCases.RenameCharacterAsync. spec: frontend_scenes.md §6; cmsg_char_rename.yaml.");
        _ = RenameCharacterAsync(slotIndex, newName);
    }

    private async Task RenameCharacterAsync(int slotIndex, string newName)
    {
        if (_ctx?.UseCases is not { } useCases)
        {
            GD.PushWarning("[SelectScene] Rename skipped: ClientContext.UseCases unavailable.");
            return;
        }

        try
        {
            // Application validates the name and (when valid) sends 1/13 CmsgRenameCharacter (18-byte
            // body). The 3/6 rename result / 3/7 char-manage subtype-1 drives the displayed-name refresh.
            // spec: Docs/RE/specs/frontend_scenes.md §6; Docs/RE/packets/cmsg_char_rename.yaml.
            var result = await useCases.RenameCharacterAsync(slotIndex, newName, CancellationToken.None);
            if (!result.IsValid)
                GD.Print($"[SelectScene] Rename rejected by Application validation; msgId={result.MessageId}. " +
                         "spec: frontend_scenes.md §6/§4.4.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SelectScene] RenameCharacterAsync({slotIndex}) failed/skipped: {ex.Message}");
        }
    }

    private async Task DeleteCharacterAsync(int slotIndex)
    {
        if (_ctx?.UseCases is not { } useCases)
        {
            GD.PushWarning("[SelectScene] Delete skipped: ClientContext.UseCases unavailable.");
            return;
        }

        try
        {
            var sent = await useCases.DeleteCharacterAsync(slotIndex, CancellationToken.None);
            GD.Print($"[SelectScene] DeleteCharacterAsync({slotIndex}) sent={sent}. spec: frontend_scenes.md §5.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SelectScene] DeleteCharacterAsync({slotIndex}) failed/skipped: {ex.Message}");
        }
    }

    private static byte InternalClassToUiIndex(int internalClass)
    {
        return internalClass switch
        {
            4 => 0,
            1 => 1,
            3 => 2,
            2 => 3,
            _ => 0
        };
    }

    private void OnBackRequested()
    {
        GD.Print("[SelectScene] Back requested from SelectWindow; routing to scene-aware quit for now. " +
                 "spec: client_runtime.md §7.5.3.");
        _ctx?.SceneMachine.RequestQuit();
    }
}