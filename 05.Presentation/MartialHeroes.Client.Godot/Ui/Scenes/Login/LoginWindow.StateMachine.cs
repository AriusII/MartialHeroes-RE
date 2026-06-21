// Ui/Scenes/Login/LoginWindow.StateMachine.cs
//
// Partial: flowSubState machine — RunState / ApplyVisibility / DispatchState + curtain animation.
// spec: Docs/RE/specs/frontend_layout_tables.md §2.2 / §2.3

using Godot;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

public sealed partial class LoginWindow
{
    // -------------------------------------------------------------------------
    // State machine
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.2
    // -------------------------------------------------------------------------

    private void RunState(int state)
    {
        _flowSubState = state;
        GD.Print($"[LoginWindow] flowSubState={state}. spec: frontend_layout_tables.md §2.2.");
        ApplyVisibility(state);
        DispatchState(state);
    }

    // Per-sub-state visibility gating (SOLE authority — no .Visible writes elsewhere for these groups).
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.1 / §2.2 bands (CORRECTED, CYCLE 18 C5b).
    private void ApplyVisibility(int state)
    {
        // Background (loginwindow.dds): shown on the 1→2 edge (revealed behind parting curtains),
        // never hidden afterward. spec: §2.2 bands "Background visible from state 2".
        if (_backgroundLayer is not null)
            _backgroundLayer.Visible = state >= 2;

        // Curtain panels are ALWAYS-PRESENT Y-animated panels — they carry the frame + banner baked art
        // (top panel: 디오 logo / URL / dragon / rings + upper & side stone, rests Y=-222; bottom panel:
        // lower & side stone + the credential form host, rests Y=+548) and are NEVER hidden. Hiding them
        // after the raise (the old `state <= 2`) erased the whole frame/banner — the end-of-curtain bug.
        // spec: frontend_layout_tables.md §2.2 "Curtains | not a hideable widget" / §2.3 (re-confirmed
        // from the binary 2026-06-19).
        if (_curtainTop is not null) _curtainTop.Visible = true;
        if (_curtainBot is not null) _curtainBot.Visible = true;

        // Form group (host strip: server-submit[102], help/quit[105], confirm face-plate, help deco):
        // ALWAYS PRESENT from state 2. Distinct from the credential group.
        // spec: §2.2 bands "Login-form host strip | always present" (CORRECTED from 3..32).
        if (_formGroup is not null)
            _formGroup.Visible = state >= 2;

        // Credential group (ID/PW textboxes, Save-ID, OK[103], label plates, frame art): SOLE
        // visibility authority. Per the §2.2 EDGE ladder (the precise IDA truth, more authoritative
        // than the reconstructed "5..33" band summary): SHOWN entering state 6 (the validate-armed idle
        // where the user types), HIDDEN entering 29 (validate hides it + raises PIN), and re-hidden at
        // 31/33. So the faithful visible interval is [6, 28] (in practice only state 6; PIN/server-list
        // and states 3/4 keep it hidden). spec: §2.2 ladder (29/31/33 "hide the credential group").
        if (_credentialGroup is not null)
            _credentialGroup.Visible = state is >= 6 and <= 28;

        // Notice panel: always hidden (init=hidden, never re-shown). spec §2.1.
        if (_noticePanel is not null)
            _noticePanel.Visible = false;

        // Server-list CONTENT panel: shown on the 34→35 edge, records painted at 37, hidden leaving 37.
        // State 33 only STARTS the fetch worker; the content panel appears at 35.
        // spec: §2.2 bands "Server-list CONTENT panel | state 35..37" (NOT 33..37; CYCLE 18 Phase A).
        if (_serverListRoot is not null)
            _serverListRoot.Visible = state is >= 35 and <= 37;

        // Quit/help strip + help plate: shown on the 34→35 edge and remain visible from state 35 onward.
        // spec: Docs/RE/specs/frontend_layout_tables.md §2.2 "Quit/help strip + help plate | state 35 onward"
        // (Not limited to 35..37 — states 38+ are transient connection states and the spec keeps the strip shown.)
        var serverListOpen = state >= 35;
        if (_serverListStrip is not null) _serverListStrip.Visible = serverListOpen;
        if (_serverListStripDeco is not null) _serverListStripDeco.Visible = serverListOpen;

        // PIN yes/no: hidden (init hidden, separate prompt not in active flow). spec §2.1.
        if (_pinYesNoPanel is not null)
            _pinYesNoPanel.Visible = false;

        // PIN keypad root: shown in 31/32 only. The PinSubView child lives inside this root and is
        // never toggled directly — this root is the SOLE visibility authority. spec §2.2/§3.
        var pinOn = state == 31 || state == 32;
        if (_pinKeypadRoot is not null)
            _pinKeypadRoot.Visible = pinOn;
    }

    private void DispatchState(int state)
    {
        switch (state)
        {
            case 1:
                // Intro one-shot: SFX, reset curtain, immediately → 2.
                // spec: §2.2 "1 intro one-shot: play curtain SFX 861010105 (cat 2); reset curtain offset 0".
                _curtainAcc = 0f;
                _curtainDone = false;
                Audio?.PlayLoginCurtainSfx();
                GD.Print("[LoginWindow] State 1: SFX 861010105. spec: §2.2/§7.");
                RunState(2);
                break;

            case 2:
                // Curtain opening — TickCurtain() advances per _Process frame.
                break;

            case 3:
                // Curtain done → immediately advance to form idle.
                RunState(4);
                break;

            case 4:
                // Settle step (auto): the curtain-settle auto-advances 3→4→5→6 to the resting login
                // (rest = 6, credential form visible). No user-input wait here. spec: frontend_layout_tables.md
                // §2.2 CORRECTION 2026-06-19 (the real client shows the credential form at end-of-curtain).
                RunState(5);
                break;

            case 5:
                // Commit form → validate-armed idle.
                RunState(6);
                break;

            case 6:
                // Validate-armed idle. spec §2.2 "6 validate-armed idle: OK button (103) or Enter → 29".
                // Focus the ID textbox at show time, or PW if a saved ID is present.
                // spec: Docs/RE/specs/frontend_layout_tables.md §2.7 "construct routine focuses the ID textbox at show time"
                // spec: Docs/RE/specs/frontend_layout_tables.md §2.5 "move focus to the PW box" when saved ID pre-filled
                CallDeferred(MethodName.FocusCredentialField);
                break;

            case 29:
                // Validate — runs synchronously. spec §2.2.
                RunValidation();
                break;

            case 31:
                // PIN entry — ensure keypad open. spec §2.2 "31 PIN entry: keypad modal shown".
                DoOpenPin();
                break;

            case 32:
                // PIN poll — wait for PinSubmitted signal.
                break;

            case 33:
                // Start server-list fetch. spec §2.2 "33 start server-list fetch worker".
                DoEnsureServerSelect();
                RunState(34);
                break;

            case 34:
                // (Re)start fetch → 35.
                RunState(35);
                break;

            case 35:
                // Fetching — show loading progress (stub). spec §2.2 "35 fetching".
                GD.Print("[LoginWindow] State 35: fetching server list. spec: §2.2.");
                break;

            case 36:
                // Fetch result (driven externally by ApplyServerList). spec §2.2 "36 fetch result".
                break;

            case 37:
                // Server list shown. spec §2.2 "37 server list shown: user picks a plate".
                break;

            case 38:
                // Channel-endpoint fetch. spec: frontend_layout_tables.md §2.2 "38 channel-endpoint fetch → 39".
                GD.Print("[LoginWindow] State 38: channel-endpoint fetch. spec: §2.2.");
                break;

            case 39:
                // Show the connecting popup (Confirm-A, msg 4023) + start join worker.
                // spec: frontend_layout_tables.md §2.2 state 39 / §4
                //   "39 show connecting popup (Confirm-A, msg 4023, single button action 113 = Cancel→34) + start join worker"
                //   CORRECTION 2026-06-19: popup is raised at sub-state 39, not 40.
                GD.Print("[LoginWindow] State 39: raise connecting popup (Confirm-A, msg 4023). spec: §2.1/§2.2/§4.");
                ShowConnectingPopup();
                // Ask LoginScene to begin the real connect. spec: §4 "connecting popup shown → join worker started".
                EmitSignal(SignalName.ConnectRequested, _collectedServerId, _collectedPin);
                break;

            case 40:
                // Hand-off: post-connect idle (TAB credential string, secure-context, login packet 0x2B).
                // LoginScene drives this via the connect result path, not the FSM auto-advance.
                // spec: frontend_layout_tables.md §2.2 state 40 / §2.6.
                GD.Print("[LoginWindow] State 40: hand-off (post-connect). spec: §2.2/§2.6.");
                break;

            case 41:
                // Hand-off: emit LoginFlowCompleted. spec §2.6.
                GD.Print("[LoginWindow] State 41: hand-off → LoginFlowCompleted. spec: §2.6.");
                EmitSignal(SignalName.LoginFlowCompleted, _collectedServerId, _collectedPin);
                break;
        }
    }

    // Focus the appropriate credential field when entering state 6.
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.5 "pre-fill → focus PW box; else focus ID box"
    private void FocusCredentialField()
    {
        if (_savedId.Length > 0 && _pwBox is not null)
            _pwBox.GrabFocus(); // PW box when ID is pre-filled. spec: §2.5.
        else
            _idBox?.GrabFocus(); // ID box by default. spec: §2.7.
    }

    // -------------------------------------------------------------------------
    // Curtain animation
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.3
    // -------------------------------------------------------------------------

    private void TickCurtain()
    {
        _curtainAcc += CurtainSpeed; // spec §2.2 "offset+=5". CODE-CONFIRMED.

        // Top Y = −offset; bottom Y = offset+326. spec §2.3. CODE-CONFIRMED.
        if (_curtainTop is not null)
            _curtainTop.Position = new Vector2(0f, -_curtainAcc);
        if (_curtainBot is not null)
            _curtainBot.Position = new Vector2(0f, CurtainBotBaseY + _curtainAcc);

        var formRideY = CurtainBotBaseY + _curtainAcc;
        if (_formPanel is not null) _formPanel.Position = new Vector2(0f, formRideY);
        if (_credPanel is not null) _credPanel.Position = new Vector2(0f, formRideY);

        if (_curtainAcc >= CurtainCompleteThresh)
        {
            // spec: §2.2 "at offset>222 → 3". CODE-CONFIRMED.
            _curtainDone = true;
            GD.Print("[LoginWindow] Curtain complete (offset≥222) → state 3. spec: §2.2.");
            RunState(3);
        }
    }

    private void SnapCurtainOpen()
    {
        // Snap to end positions: top Y = −222, bottom Y = 548 (= 222 + 326), extent 222.
        // spec: Docs/RE/specs/frontend_layout_tables.md §2.3
        //   "end positions: top Y = −222, bottom Y = 548 (= 222 + 326); extent = 222 px each way"
        _curtainAcc = CurtainCompleteThresh; // 222 spec: frontend_layout_tables.md §2.3
        if (_curtainTop is not null) _curtainTop.Position = new Vector2(0f, -CurtainCompleteThresh); // top Y = −222
        if (_curtainBot is not null)
            _curtainBot.Position = new Vector2(0f, CurtainBotBaseY + CurtainCompleteThresh); // bottom Y = 548
        var formOpenY = CurtainBotBaseY + CurtainCompleteThresh; // 548 spec: frontend_layout_tables.md §2.3
        if (_formPanel is not null) _formPanel.Position = new Vector2(0f, formOpenY);
        if (_credPanel is not null) _credPanel.Position = new Vector2(0f, formOpenY);

        _curtainDone = true;
        if (_flowSubState < 3) RunState(3);
    }
}