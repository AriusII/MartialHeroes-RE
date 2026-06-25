using Godot;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

public sealed partial class LoginWindow
{
    private const float FormPlateSnapThresh = 200f;

    private const float FormPlateSnapCanvasX = 265f;
    private const float FormPlateSnapCanvasY = 548f;

    private bool _formDecoSnapped;

    private void RunState(int state)
    {
        _flowSubState = state;
        GD.Print($"[LoginWindow] flowSubState={state}. spec: frontend_layout_tables.md §2.2.");
        ApplyVisibility(state);
        DispatchState(state);
    }

    private void ApplyVisibility(int state)
    {
        if (_backgroundLayer is not null)
            _backgroundLayer.Visible = state >= 2;

        if (_bannerFrame is not null)
            _bannerFrame.Visible = state >= 35;

        if (_curtainTop is not null) _curtainTop.Visible = true;
        if (_curtainBot is not null) _curtainBot.Visible = true;

        if (_formGroup is not null)
            _formGroup.Visible = state is >= 2 and < 35;

        if (_credentialGroup is not null)
            _credentialGroup.Visible = state is >= 6 and <= 28;

        if (_noticePanel is not null)
            _noticePanel.Visible = false;

        if (_serverListRoot is not null)
        {
            _serverListRoot.Visible = state >= 35;
            if (state >= 35)
                MoveChild(_serverListRoot, GetChildCount() - 1);
        }

        if (_serverListStrip is not null) _serverListStrip.Visible = false;
        if (_serverListStripDeco is not null) _serverListStripDeco.Visible = false;

        if (_pinYesNoPanel is not null)
            _pinYesNoPanel.Visible = false;

        var pinOn = state == 31 || state == 32;
        if (_pinKeypadRoot is not null)
            _pinKeypadRoot.Visible = pinOn;
    }

    private void DispatchState(int state)
    {
        switch (state)
        {
            case 1:
                _curtainAcc = 0f;
                _curtainDone = false;
                _formDecoSnapped = false;
                if (_formDecoPlate is not null) _formDecoPlate.Visible = false;
                Audio?.PlayLoginCurtainSfx();
                GD.Print("[LoginWindow] State 1: SFX 861010105. spec: §2.2/§7.");
                RunState(2);
                break;

            case 2:
                break;

            case 3:
                _curtainAcc = 0f;
                RunState(4);
                break;

            case 4:
                RunState(5);
                break;

            case 5:
                RunState(6);
                break;

            case 6:
                CallDeferred(MethodName.FocusCredentialField);
                break;

            case 29:
                RunValidation();
                break;

            case 31:
                DoOpenPin();
                break;

            case 32:
                break;

            case 33:
                DoEnsureServerSelect();
                RunState(34);
                break;

            case 34:
                RunState(35);
                break;

            case 35:
                GD.Print("[LoginWindow] State 35: fetching server list. spec: §2.2.");
                break;

            case 36:
                break;

            case 37:
                break;

            case 38:
                GD.Print("[LoginWindow] State 38: channel-endpoint fetch. spec: §2.2.");
                break;

            case 39:
                GD.Print("[LoginWindow] State 39: raise connecting popup (Confirm-A, msg 4023). spec: §2.1/§2.2/§4.");
                ShowConnectingPopup();
                EmitSignal(SignalName.ConnectRequested, _collectedServerId, _collectedPin);
                break;

            case 40:
                GD.Print("[LoginWindow] State 40: hand-off (post-connect). spec: §2.2/§2.6.");
                break;

            case 41:
                GD.Print("[LoginWindow] State 41: hand-off → LoginFlowCompleted. spec: §2.6.");
                EmitSignal(SignalName.LoginFlowCompleted, _collectedServerId, _collectedPin);
                break;
        }
    }

    private void FocusCredentialField()
    {
        if (_savedId.Length > 0 && _pwBox is not null)
            _pwBox.GrabFocus();
        else
            _idBox?.GrabFocus();
    }


    private void TickCurtain()
    {
        _curtainAcc += CurtainSpeed;

        if (_curtainTop is not null)
            _curtainTop.Position = new Vector2(0f, -_curtainAcc);
        if (_curtainBot is not null)
            _curtainBot.Position = new Vector2(0f, CurtainBotBaseY + _curtainAcc);

        var formRideY = CurtainBotBaseY + _curtainAcc;
        if (_formPanel is not null) _formPanel.Position = new Vector2(0f, formRideY);
        if (_credPanel is not null) _credPanel.Position = new Vector2(0f, formRideY);

        if (!_formDecoSnapped && _curtainAcc > FormPlateSnapThresh && _formDecoPlate is not null)
        {
            var localY = FormPlateSnapCanvasY - formRideY;
            _formDecoPlate.Position = new Vector2(FormPlateSnapCanvasX, localY);
            _formDecoPlate.Visible = true;
            _formDecoSnapped = true;
            GD.Print(
                $"[LoginWindow] FormDecoPlate snapped to canvas (265,548) at offset={_curtainAcc:F0}. spec: §2.3 G2-confirmed.");
        }

        if (_curtainAcc > CurtainCompleteThresh)
        {
            _curtainAcc = CurtainCompleteThresh;
            if (_curtainTop is not null)
                _curtainTop.Position = new Vector2(0f, -CurtainCompleteThresh);
            if (_curtainBot is not null)
                _curtainBot.Position = new Vector2(0f, CurtainBotBaseY + CurtainCompleteThresh);
            var formFinalY = CurtainBotBaseY + CurtainCompleteThresh;
            if (_formPanel is not null) _formPanel.Position = new Vector2(0f, formFinalY);
            if (_credPanel is not null) _credPanel.Position = new Vector2(0f, formFinalY);

            if (_formDecoPlate is not null && _formDecoSnapped)
                _formDecoPlate.Position = new Vector2(FormPlateSnapCanvasX, FormPlateSnapCanvasY - formFinalY);

            _curtainDone = true;
            GD.Print("[LoginWindow] Curtain complete (offset>222) → state 3. spec: §2.2.");
            RunState(3);
        }
    }

    private void SnapCurtainOpen()
    {
        _curtainAcc = CurtainCompleteThresh;
        if (_curtainTop is not null) _curtainTop.Position = new Vector2(0f, -CurtainCompleteThresh);
        if (_curtainBot is not null)
            _curtainBot.Position = new Vector2(0f, CurtainBotBaseY + CurtainCompleteThresh);
        var formOpenY = CurtainBotBaseY + CurtainCompleteThresh;
        if (_formPanel is not null) _formPanel.Position = new Vector2(0f, formOpenY);
        if (_credPanel is not null) _credPanel.Position = new Vector2(0f, formOpenY);

        if (_formDecoPlate is not null && !_formDecoSnapped)
        {
            _formDecoPlate.Position = new Vector2(FormPlateSnapCanvasX, FormPlateSnapCanvasY - formOpenY);
            _formDecoPlate.Visible = true;
            _formDecoSnapped = true;
        }
        else if (_formDecoPlate is not null)
        {
            _formDecoPlate.Position = new Vector2(FormPlateSnapCanvasX, FormPlateSnapCanvasY - formOpenY);
        }

        _curtainDone = true;
        if (_flowSubState < 3) RunState(3);
    }
}