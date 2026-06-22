// Ui/Scenes/Login/LoginWindow.StateMachine.cs
//
// Partial: flowSubState machine — RunState / ApplyVisibility / DispatchState + curtain animation.
// spec: Docs/RE/specs/frontend_layout_tables.md §2.2 / §2.3

using Godot;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

public sealed partial class LoginWindow
{
    // Threshold for the form decorative plate snap (offset > 200).
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.3 "at offset>200 snap the form decorative plate
    //   (member +0x27C, atlas A1 src 0,469 size 494×113) to canvas dst (265,548)
    //   (G2 debugger-confirmed 2026 / IDB 263bd994 — supersedes prior '(494,469)' value)"
    private const float FormPlateSnapThresh = 200f; // spec: frontend_layout_tables.md §2.3

    // Absolute canvas snap position for the form decorative plate.
    // G2 DEBUGGER-CONFIRMED: canvas dst (265,548) — the 494 is the WIDTH and 469 is the src-top,
    // NOT the destination. spec: Docs/RE/specs/frontend_layout_tables.md §2.3.
    private const float FormPlateSnapCanvasX = 265f; // spec: frontend_layout_tables.md §2.3 G2-confirmed
    private const float FormPlateSnapCanvasY = 548f; // spec: frontend_layout_tables.md §2.3 G2-confirmed

    // Track whether the form decorative plate has already been snapped (one-time event). spec §2.3.
    private bool _formDecoSnapped;
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

        // The (270,85,483,490) loginwindow.dds src(0,490) frame chrome is the SERVER-LIST COLUMN chrome,
        // NOT a login banner. DECOMPILE-CONFIRMED (IDB 263bd994, sub-state machine): the loginwindow.dds
        // (270,85,483,490) art is shared by idx 175 (Notice/EULA panel — ALWAYS hidden) and idx 202
        // (server-list COLUMN — SetVisible(1) ONLY at sub-state 35). The actual login illustration is the
        // FULLSCREEN _backgroundLayer (idx 156, loginwindow.dds 0,110,1024,490). Showing this chrome before
        // sub-state 35 paints the server-list backdrop over the login/credential/PIN screens (the bug the
        // maintainer reported). So gate it to the server-list band only.
        // spec: Docs/RE/specs/frontend_layout_tables.md §2.1 (G2 decompile-confirmed sub-state schedule, IDB 263bd994)
        if (_bannerFrame is not null)
            _bannerFrame.Visible = state >= 35;

        // Curtain panels are ALWAYS-PRESENT Y-animated panels — they carry ONLY the login_slice1.dds
        // stone chrome (upper/lower stone panels). They do NOT carry the central banner frame or logo
        // (those are loginwindow.dds, in the static _bannerFrame). The curtains are never hidden;
        // they slide off-canvas: top rests at Y=-222, bottom at Y=+548 after the open animation.
        // spec: frontend_layout_tables.md §2.2 "Curtains | not a hideable widget" / §2.3 (re-confirmed
        // from the binary 2026-06-19). Debugger-confirmed IDB 263bd994: curtains are login_slice1.dds only.
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

        // Server-list COLUMN container (idx 202 equivalent): structural host for ServerSelectSubView rows.
        // Hidden during intro (1), curtain (2-3), credential rest (6-28), PIN (31-32), and fetch (33-34).
        // Shown only when the server list arrives at sub-state 35, and remains shown through 36-41.
        // The visual chrome (loginwindow.dds 270,85,483,490) is provided by the static _bannerFrame which
        // is always visible from state 2 — this container is transparent and gates only the row children.
        // NOTE: a prior version of BuildServerListRoot added a login_slice1.dds backdrop at (0,0,1024,398)
        // inside this container, making it paint curtain-top art (idx 157) over the screen at state >= 35.
        // That backdrop has been REMOVED. The gate (>= 35) is correct for idx 202.
        // spec: Docs/RE/_dirty/login_layout/validation/loginwindow_visibility_schedule.md §DELIVERABLE 2
        //   "Sub-state 35: show idx 202 (server-list COLUMN panel) SetVisible(1)"
        //   (static-decompile-confirmed, IDB 263bd994)
        // spec: Docs/RE/_dirty/login_layout/validation/loginwindow_visibility_schedule.md §DELIVERABLE 3
        //   "idx 202 gate: state >= 35" (static-decompile-confirmed, IDB 263bd994)
        if (_serverListRoot is not null)
            _serverListRoot.Visible = state >= 35;

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
                _formDecoSnapped = false; // reset the one-time form-plate snap flag. spec §2.3.
                if (_formDecoPlate is not null) _formDecoPlate.Visible = false; // hidden until offset>200. spec §2.3.
                Audio?.PlayLoginCurtainSfx();
                GD.Print("[LoginWindow] State 1: SFX 861010105. spec: §2.2/§7.");
                RunState(2);
                break;

            case 2:
                // Curtain opening — TickCurtain() advances per _Process frame.
                break;

            case 3:
                // Curtain done: clear accumulator to 0 (spec §2.3 "Accumulator reset at sub-state 3 = yes,
                // cleared to 0") then immediately advance to form idle.
                // spec: Docs/RE/specs/frontend_layout_tables.md §2.3 "At sub-state 3 the accumulator is
                //   cleared to 0 and end positions are hard-set: top = −222, bottom = 548"
                _curtainAcc = 0f; // spec: frontend_layout_tables.md §2.3 "Accumulator reset at sub-state 3"
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

        // Form decorative plate snap: at offset>200, snap to absolute canvas (494,469) and reveal.
        // One-time snap — once triggered, the plate stays at its snapped position.
        // The plate is a child of formPanel; to achieve absolute canvas position (494,469), the
        // panel-local position must be (494 − formPanel.CanvasX, 469 − formPanel.CanvasY).
        // Since formPanel.Position.X == 0, local X == 494. Local Y == 469 − formRideY.
        // spec: Docs/RE/specs/frontend_layout_tables.md §2.3
        //   "at offset>200 snap form decorative plate (member +0x27C) to screen position (494,469)"
        if (!_formDecoSnapped && _curtainAcc > FormPlateSnapThresh && _formDecoPlate is not null)
        {
            var localY = FormPlateSnapCanvasY - formRideY; // panel-local Y to achieve canvas Y=548. spec §2.3.
            _formDecoPlate.Position = new Vector2(FormPlateSnapCanvasX, localY);
            _formDecoPlate.Visible = true; // reveal at snap. spec §2.3.
            _formDecoSnapped = true;
            GD.Print(
                $"[LoginWindow] FormDecoPlate snapped to canvas (265,548) at offset={_curtainAcc:F0}. spec: §2.3 G2-confirmed.");
        }

        if (_curtainAcc > CurtainCompleteThresh) // spec §2.2 "at offset>222 → 3". CODE-CONFIRMED.
        {
            // Clamp to the exact spec end-position so the curtain is pixel-perfect before the
            // credential form becomes visible (state 6). Without the clamp, the curtain stops
            // at e.g. 225 instead of 222, leaving a 3-px overshoot gap. spec: §2.3.
            _curtainAcc = CurtainCompleteThresh; // spec: frontend_layout_tables.md §2.3 "end = 222"
            if (_curtainTop is not null)
                _curtainTop.Position = new Vector2(0f, -CurtainCompleteThresh);
            if (_curtainBot is not null)
                _curtainBot.Position = new Vector2(0f, CurtainBotBaseY + CurtainCompleteThresh);
            var formFinalY = CurtainBotBaseY + CurtainCompleteThresh;
            if (_formPanel is not null) _formPanel.Position = new Vector2(0f, formFinalY);
            if (_credPanel is not null) _credPanel.Position = new Vector2(0f, formFinalY);

            // Also pin the form deco plate panel-local Y at its snapped canvas position.
            // G2-confirmed dst (265,548): at formFinalY=548, panel-local Y = 548−548 = 0. spec §2.3.
            if (_formDecoPlate is not null && _formDecoSnapped)
                _formDecoPlate.Position = new Vector2(FormPlateSnapCanvasX, FormPlateSnapCanvasY - formFinalY);

            // spec: §2.2 "at offset>222 → 3". CODE-CONFIRMED.
            _curtainDone = true;
            GD.Print("[LoginWindow] Curtain complete (offset>222) → state 3. spec: §2.2.");
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

        // Also snap and reveal the form decorative plate to absolute canvas (265,548). spec §2.3.
        // G2 debugger-confirmed 2026 / IDB 263bd994. At formOpenY=548, panel-local Y = 548−548 = 0. spec §2.3.
        if (_formDecoPlate is not null && !_formDecoSnapped)
        {
            _formDecoPlate.Position = new Vector2(FormPlateSnapCanvasX, FormPlateSnapCanvasY - formOpenY);
            _formDecoPlate.Visible = true;
            _formDecoSnapped = true;
        }
        else if (_formDecoPlate is not null)
        {
            // Already snapped; ensure position is correct at the final canvas position. spec §2.3.
            _formDecoPlate.Position = new Vector2(FormPlateSnapCanvasX, FormPlateSnapCanvasY - formOpenY);
        }

        _curtainDone = true;
        if (_flowSubState < 3) RunState(3);
    }
}