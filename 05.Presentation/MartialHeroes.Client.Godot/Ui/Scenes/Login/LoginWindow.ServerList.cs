using Godot;
using MartialHeroes.Client.Presentation.Screens.Layout;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

public sealed partial class LoginWindow
{
    private void DoEnsureServerSelect()
    {
        if (_serverListRoot is null) return;
        if (_serverSelect is not null && IsInstanceValid(_serverSelect)) return;
        if (ServerSelectFactory is null) return;

        _serverSelect = ServerSelectFactory();
        _serverSelect.Name = "ServerSelectSubView";
        _serverSelect.ServerSelected += OnServerSelected;
        _serverSelect.Visible = true;
        _serverListRoot.AddChild(_serverSelect);
    }

    public void RaiseServerListError(bool fetchFailed)
    {
        var msgId = fetchFailed
            ? (int)LoginLayout.MsgErrConnectFail
            : (int)LoginLayout.MsgErrNoServers;
        RunState(6);
        ShowErrorPanel(msgId);
    }

    private void OnServerSelected(int serverId)
    {
        _collectedServerId = serverId;
        GD.Print($"[LoginWindow] Server selected (id={serverId}). spec: login_flow.md §2.1.");
        RunState(38);
        RunState(39);
    }


    private void DoOpenPin()
    {
        if (_pinView is null || !IsInstanceValid(_pinView))
        {
            if (PinFactory is null || _pinKeypadRoot is null) return;
            _pinView = PinFactory();
            _pinView.Name = "PinSubView";
            _pinView.PinSubmitted += OnPinSubmitted;
            _pinView.Cancelled += OnPinCancelled;
            _pinView.Visible = true;
            _pinKeypadRoot.AddChild(_pinView);
        }
    }

    private void OnPinSubmitted(string pin)
    {
        _collectedPin = pin;
        GD.Print($"[LoginWindow] PIN collected (len={pin.Length}). spec: login_flow.md §4.2.");
        RunState(32);
        RunState(33);
    }

    private void OnPinCancelled()
    {
        RunState(6);
        GD.Print("[LoginWindow] PIN cancelled; returning to validate-armed idle.");
    }


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