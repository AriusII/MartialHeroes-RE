using Godot;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Contracts.Scene;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Scenes;
using MartialHeroes.Client.Godot.Ui.Scenes.Select;
using MartialHeroes.Shared.Kernel.Enums;

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
        _audio.PlayBgm();

        _select = BuildSelectScreen();
        _screenHost.SetScreen(_select);

        if (_ctx is not null)
            StartEventDrain(_select, _ctx.EventBus);

        GD.Print("[SelectScene] State 4 Select built CharacterSelectScreen: roster UI, 3D preview actors, " +
                 "and Select camera dolly. spec: client_runtime.md §7.3/§7.4; frontend_scenes.md §3.");
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
            Text = text
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
                name,
                InternalClassToUiIndex(internalClass),
                checked((ushort)faceIndex),
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0);

            var result =
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