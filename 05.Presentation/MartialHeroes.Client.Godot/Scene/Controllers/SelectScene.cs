using System.Collections.Immutable;
using Godot;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Scene;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Screens;
using MartialHeroes.Client.Godot.Ui.Scenes.Select;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.Scene.Controllers;

/// <summary>
/// State 4 — SelectWindow. Builds the character-list UI, 3D preview actor row, and the dedicated
/// Select preview camera; all flow intents are forwarded to Application use-cases.
/// Now uses the new Ui/Scenes substrate (CharSelectWindow + CharSelectEventDrainer).
/// The 3D scene layer (CharSelectScene3D/CharCreatePreview3D) is REUSED unchanged.
/// spec: Docs/RE/specs/client_runtime.md §7.3 / §7.4; Docs/RE/specs/frontend_scenes.md §3–§8.
/// </summary>
public sealed partial class SelectScene : StubSceneController
{
    private ClientContext? _ctx;
    private SceneHost? _host;
    private ScreenHost? _screenHost;
    private FrontEndAudio? _audio;
    private CharSelectWindow? _select; // NEW: Ui/Scenes substrate
    private CharSelectEventDrainer? _drainer; // NEW: typed for CharSelectWindow
    private bool _confirmInFlight;

    /// <inheritdoc/>
    public override EngineSceneState State => EngineSceneState.Select;

    /// <inheritdoc/>
    public override void OnEnter(SceneHost host)
    {
        Name = $"Scene{(int)State}_{State}";
        _host = host;
        _ctx = GetNodeOrNull<ClientContext>("/root/ClientContext");

        _screenHost = new ScreenHost { Name = "SelectScreenHost" };
        AddChild(_screenHost);

        _audio = new FrontEndAudio { Name = "SelectFrontEndAudio" };
        AddChild(_audio);
        _audio.PlayBgm();

        _select = BuildSelectScreen();
        _screenHost.SetScreen(_select);

        if (_ctx is not null)
            StartEventDrain(_select, _ctx.EventBus);

        if (IsDevOfflineMode() || DisplayServer.GetName() == "headless")
            SeedDevRoster();

        GD.Print("[SelectScene] State 4 Select built CharacterSelectScreen: roster UI, 3D preview actors, " +
                 "and Select camera dolly. spec: client_runtime.md §7.3/§7.4; frontend_scenes.md §3.");

        if (DisplayServer.GetName() == "headless" || OS.GetEnvironment("MH_SELECT_AUTOCONFIRM") == "1")
        {
            SceneTreeTimer timer = GetTree().CreateTimer(0.35);
            timer.Timeout += AutoConfirmForHeadless;
        }
    }

    public override void _ExitTree()
    {
        if (_select is not null)
        {
            _select.EnterGameRequested -= OnEnterGameRequested;
            _select.BackRequested -= OnBackRequested;
            _select.CreateCharacterRequested -= OnCreateCharacterRequested;
            _select.DeleteCharacterRequested -= OnDeleteCharacterRequested;
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
        };

        select.EnterGameRequested += OnEnterGameRequested;
        select.BackRequested += OnBackRequested;
        select.CreateCharacterRequested += OnCreateCharacterRequested;
        select.DeleteCharacterRequested += OnDeleteCharacterRequested;
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
            case CharCreateResultEvent create:
                GD.Print($"[SelectScene] CharCreateResult success={create.Success} slot={create.AssignedSlotId} " +
                         $"error={create.ErrorCode}; waiting for core roster refresh. spec: frontend_scenes.md §6.");
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
        _ = ConfirmSlotAsync(slotIndex, allowDevFallback: IsDevOfflineMode());
    }

    private async Task ConfirmSlotAsync(int slotIndex, bool allowDevFallback)
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

        if (allowDevFallback && _host?.CurrentState == EngineSceneState.Select)
        {
            GD.Print("[SelectScene] Dev/headless fallback advancing Select→InGame through SceneHost.Advance. " +
                     "spec: client_runtime.md §7.5.1; real connected flow uses UseCases.SelectCharacterAsync.");
            _host.CallDeferred(SceneHost.MethodName.Advance);
        }
    }

    private void OnCreateCharacterRequested(string name, int internalClass, int faceIndex)
    {
        GD.Print($"[SelectScene] CreateCharacterRequested(name='{name}', class={internalClass}, face={faceIndex}); " +
                 "forwarding to UseCases.CreateCharacterAsync. spec: frontend_scenes.md §4/§8.");
        _ = CreateCharacterAsync(name, internalClass, faceIndex);
    }

    private void OnDeleteCharacterRequested(int slotIndex, string characterName)
    {
        GD.Print($"[SelectScene] DeleteCharacterRequested(slot={slotIndex}, name='{characterName}'); " +
                 "forwarding to UseCases.DeleteCharacterAsync. spec: frontend_scenes.md §5/§8.");
        _ = DeleteCharacterAsync(slotIndex);
    }

    private async Task CreateCharacterAsync(string name, int internalClass, int faceIndex)
    {
        if (_ctx?.UseCases is not { } useCases)
        {
            GD.PushWarning("[SelectScene] Create skipped: ClientContext.UseCases unavailable.");
            return;
        }

        try
        {
            var request = new CharacterCreateRequest(
                Name: name,
                UiClassIndex: InternalClassToUiIndex(internalClass),
                Face: checked((ushort)faceIndex),
                Sex: 0,
                HairOrReserved: 0,
                Stat0: 0,
                Stat1: 0,
                Stat2: 0,
                Stat3: 0,
                Stat4: 0,
                PointsRemaining: 0);

            CharacterNameValidationResult result =
                await useCases.CreateCharacterAsync(request, CancellationToken.None);
            if (!result.IsValid)
                GD.Print($"[SelectScene] Create rejected by Application validation; msgId={result.MessageId}. " +
                         "spec: frontend_scenes.md §4.4.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SelectScene] CreateCharacterAsync failed/skipped: {ex.Message}");
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
            bool sent = await useCases.DeleteCharacterAsync(slotIndex, CancellationToken.None);
            GD.Print($"[SelectScene] DeleteCharacterAsync({slotIndex}) sent={sent}. spec: frontend_scenes.md §5.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SelectScene] DeleteCharacterAsync({slotIndex}) failed/skipped: {ex.Message}");
        }
    }

    private static byte InternalClassToUiIndex(int internalClass) => internalClass switch
    {
        4 => 0,
        1 => 1,
        3 => 2,
        2 => 3,
        _ => 0,
    };

    private void OnBackRequested()
    {
        GD.Print("[SelectScene] Back requested from SelectWindow; routing to scene-aware quit for now. " +
                 "spec: client_runtime.md §7.5.3.");
        _ctx?.SceneMachine.RequestQuit();
    }

    private void SeedDevRoster()
    {
        ImmutableArray<CharacterListSlot> slots = DevCharacterList();
        if (_ctx is not null)
        {
            _ctx.EventBus.Publish(new CharacterListEvent(ServerId: 0, ChannelId: 0, Characters: slots));
            GD.Print($"[SelectScene] DEV/headless roster published through EventBus ({slots.Length} slots).");
        }
        else
        {
            _select?.ApplyCharacterList(slots);
            GD.Print($"[SelectScene] DEV/headless roster applied directly ({slots.Length} slots; no ClientContext).");
        }
    }

    private void AutoConfirmForHeadless()
    {
        if (_host?.CurrentState != EngineSceneState.Select) return;

        GD.Print("[SelectScene] Headless/dev auto-confirm slot 0 so SceneHost can verify state 4→5.");
        _ = ConfirmSlotAsync(slotIndex: 0, allowDevFallback: true);
    }

    private static ImmutableArray<CharacterListSlot> DevCharacterList() =>
        ImmutableArray.Create(
            // Dev/offline seed for headless + screenshot verification. PosX/PosZ are representative saved
            // map coords (descriptor +644/+648) so the info-row "%d , %d" position line is exercised.
            // spec: Docs/RE/specs/frontend_scenes.md §3.2 (descriptor position floats).
            new CharacterListSlot(SlotIndex: 0, Name: "무사", Level: 25, ServerClass: 1, CurrentHp: 650, PosX: 2048f, PosZ: -6144f),
            new CharacterListSlot(SlotIndex: 1, Name: "격사", Level: 32, ServerClass: 3, CurrentHp: 520, PosX: 1536f, PosZ: -3590f),
            new CharacterListSlot(SlotIndex: 2, Name: "도사", Level: 18, ServerClass: 2, CurrentHp: 480, PosX: 1024f, PosZ: -512f),
            new CharacterListSlot(SlotIndex: 3, Name: "@BLANK@", Level: 0, ServerClass: 0, CurrentHp: 0, PosX: 0f, PosZ: 0f),
            new CharacterListSlot(SlotIndex: 4, Name: "@BLANK@", Level: 0, ServerClass: 0, CurrentHp: 0, PosX: 0f, PosZ: 0f));

    private static bool IsDevOfflineMode()
    {
        string? envVal = System.Environment.GetEnvironmentVariable("DEV_OFFLINE_FLOW");
        if (envVal is "1" or "true" or "yes") return true;

        string cfgVal = ReadCfgKey("dev_offline_flow", "0");
        return cfgVal is "1" or "true" or "yes";
    }

    private static string ReadCfgKey(string key, string defaultValue)
    {
        string path;
        try
        {
            path = ProjectSettings.GlobalizePath("res://client_dir.cfg");
        }
        catch
        {
            path = "client_dir.cfg";
        }

        if (!File.Exists(path)) return defaultValue;

        try
        {
            foreach (string rawLine in File.ReadLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                int eq = line.IndexOf('=');
                if (eq < 0) continue;

                if (line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    return line[(eq + 1)..].Trim();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SelectScene] ReadCfgKey('{key}') failed: {ex.Message}");
        }

        return defaultValue;
    }
}