using System.Collections.Immutable;
using Godot;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Contracts.Scene;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Scenes;
using MartialHeroes.Client.Godot.Ui.Scenes.Select;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.Scene.Controllers;

public sealed partial class SelectScene : StubSceneController
{
    private FrontEndAudio? _audio;
    private bool _confirmInFlight;
    private ClientContext? _ctx;
    private CharSelectEventDrainer? _drainer;
    private SceneHost? _host;
    private ScreenHost? _screenHost;
    private CharSelectWindow? _select;

    public override EngineSceneState State => EngineSceneState.Select;

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
        var atlas = _ctx?.HudAtlas;
        var text = _ctx?.HudText;

        var select = new CharSelectWindow
        {
            Name = "CharSelectWindow",
            Atlas = atlas,
            Text = text,
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
            const uint statFloor = 10u;
            var s0 = stats is { Length: >= 1 } ? (uint)Math.Max(0, stats[0]) : statFloor;
            var s1 = stats is { Length: >= 2 } ? (uint)Math.Max(0, stats[1]) : statFloor;
            var s2 = stats is { Length: >= 3 } ? (uint)Math.Max(0, stats[2]) : statFloor;
            var s3 = stats is { Length: >= 4 } ? (uint)Math.Max(0, stats[3]) : statFloor;
            var s4 = stats is { Length: >= 5 } ? (uint)Math.Max(0, stats[4]) : statFloor;

            var sum = s0 + s1 + s2 + s3 + s4;
            var pts = sum <= 55u ? 55u - sum : 0u;

            var safeFace = (ushort)Math.Clamp(faceIndex, 1, 7);

            var request = new CharacterCreateRequest(
                name,
                InternalClassToUiIndex(internalClass),
                safeFace,
                1,
                0,
                s0,
                s1,
                s2,
                s3,
                s4,
                pts
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