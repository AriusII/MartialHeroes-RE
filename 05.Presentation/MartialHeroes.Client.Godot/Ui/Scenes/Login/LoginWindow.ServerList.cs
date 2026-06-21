// Ui/Scenes/Login/LoginWindow.ServerList.cs
//
// Partial: server-list sub-view management, PIN management, save-ID persistence.
// spec: Docs/RE/specs/frontend_layout_tables.md §2.2 / §2.5 / §3

using Godot;
using MartialHeroes.Client.Presentation.Screens.Layout;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

public sealed partial class LoginWindow
{
    // -------------------------------------------------------------------------
    // Server-select management
    // -------------------------------------------------------------------------

    private void DoOpenServerSelect()
    {
        DoEnsureServerSelect();
        RunState(33); // ApplyVisibility(35..37) shows _serverListRoot — the sole gate. spec: frontend_layout_tables.md §2.2.
    }

    private void DoEnsureServerSelect()
    {
        if (_serverListRoot is null) return;
        if (_serverSelect is not null && IsInstanceValid(_serverSelect)) return;
        if (ServerSelectFactory is null) return;

        _serverSelect = ServerSelectFactory();
        _serverSelect.Name = "ServerSelectSubView";
        _serverSelect.ServerSelected += OnServerSelected;
        _serverSelect.Visible = true; // one-time enable; _serverListRoot gates show/hide. spec §2.1.
        _serverListRoot.AddChild(_serverSelect);
    }

    /// <summary>
    ///     Raises the validation-error message box with the server-list fetch error message.
    ///     Called by the fetch-result path at sub-state 36 when the server list is empty (msg 4027)
    ///     or the fetch failed (msg 4028). First resets to sub-state 6 per spec §2.2 (state 36 → 37,
    ///     then the error panel rides over the idle credential form).
    ///     spec: Docs/RE/specs/frontend_layout_tables.md §2.1a / §2.2 state 36
    /// </summary>
    public void RaiseServerListError(bool fetchFailed)
    {
        // spec §2.1a: "no servers returned → msg 4027; fetch result −1 → msg 4028".
        var msgId = fetchFailed
            ? (int)LoginLayout.MsgErrConnectFail // 4028 — fetch error (−1)
            : (int)LoginLayout.MsgErrNoServers; // 4027 — zero records returned
        RunState(37); // advance to the "list shown" idle (credential form shows). spec §2.2.
        ShowErrorPanel(msgId);
    }

    private void OnServerSelected(int serverId)
    {
        _collectedServerId = serverId;
        GD.Print($"[LoginWindow] Server selected (id={serverId}). spec: login_flow.md §2.1.");
        // Channel-endpoint fetch (38), then show the connecting popup (39).
        // State 39 raises Confirm-A + emits ConnectRequested so LoginScene starts the join worker.
        // Do NOT auto-advance to 41 — success arrives from LoginScene via NotifyConnectSuccess.
        // spec: frontend_layout_tables.md §2.2 "37→38 commit guard → 38 channel-endpoint fetch → 39 connecting popup".
        RunState(38);
        RunState(39); // raises popup + fires ConnectRequested. spec: §2.2/§4.
    }

    // -------------------------------------------------------------------------
    // PIN management
    // -------------------------------------------------------------------------

    private void DoOpenPin()
    {
        // Lazily create the PinSubView once, parented into the structural _pinKeypadRoot. The root's
        // visibility (set by ApplyVisibility for states 31/32) is the SOLE show/hide authority; the
        // child is enabled once and never toggled directly. spec: §2.2/§3.
        if (_pinView is null || !IsInstanceValid(_pinView))
        {
            if (PinFactory is null || _pinKeypadRoot is null) return;
            _pinView = PinFactory();
            _pinView.Name = "PinSubView";
            _pinView.PinSubmitted += OnPinSubmitted;
            _pinView.Cancelled += OnPinCancelled;
            _pinView.Visible = true; // one-time enable; _pinKeypadRoot gates show/hide.
            _pinKeypadRoot.AddChild(_pinView);
        }
    }

    private void OnPinSubmitted(string pin)
    {
        _collectedPin = pin;
        GD.Print($"[LoginWindow] PIN collected (len={pin.Length}). spec: login_flow.md §4.2.");
        RunState(32); // poll → 33 immediately; leaving 31/32 hides _pinKeypadRoot via ApplyVisibility.
        RunState(33);
    }

    private void OnPinCancelled()
    {
        RunState(6); // leaving 31/32 hides _pinKeypadRoot via ApplyVisibility.
        GD.Print("[LoginWindow] PIN cancelled; returning to validate-armed idle.");
    }

    // -------------------------------------------------------------------------
    // Save-ID persistence
    // Original target (CONFIRMED, static IDA, CYCLE 18 Phase A):
    //   file = DoOption.ini (EXE-relative), section = [DO_OPTION], key = OPTION_ID.
    //   Value = stored login-id string; "(null)" / empty = no saved id.
    //   spec: frontend_layout_tables.md §2.5 (CONFIRMED).
    // Port equivalent: user://mh_options.cfg ConfigFile, section/key from LoginLayout.SaveId*.
    //   This is the layer-05 translation of the Windows private-profile API to Godot's ConfigFile.
    //   Behavior is unchanged — do not modify working ConfigFile paths.
    // -------------------------------------------------------------------------

    private void OnSaveIdToggled(bool pressed)
    {
        _saveIdChecked = pressed;
        if (!pressed) PersistSaveId("");
    }

    private void LoadSaveId()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(LoginLayout.SaveIdConfigPath) != Error.Ok) return;
        var savedId = cfg.GetValue(LoginLayout.SaveIdSection, LoginLayout.SaveIdKey,
            Variant.From(LoginLayout.SaveIdNullSentinel));
        var saved = savedId.AsString();
        if (saved.Length > 0 && saved != LoginLayout.SaveIdNullSentinel)
        {
            _savedId = saved;
            _saveIdChecked = true;
        }
    }

    private void PersistSaveId(string id)
    {
        var cfg = new ConfigFile();
        cfg.SetValue(LoginLayout.SaveIdSection, LoginLayout.SaveIdKey,
            id.Length > 0 ? id : LoginLayout.SaveIdNullSentinel);
        var err = cfg.Save(LoginLayout.SaveIdConfigPath);
        if (err != Error.Ok)
            GD.PrintErr($"[LoginWindow] PersistSaveId failed (err={err}).");
    }
}