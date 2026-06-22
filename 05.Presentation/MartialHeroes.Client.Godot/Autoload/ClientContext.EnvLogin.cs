// ClientContext.EnvLogin.cs — env-gated "copie conforme" verification harness.
//
// Drives the REAL login flow (same use-case intents as LoginScene's OK button) when the
// maintainer sets the appropriate environment variables OR the creds file:
//
//   STEP 1 — login → char-select (roster printed, no enter-world):
//     MH_LOGIN_ID=<account>
//     MH_LOGIN_PW=<password>
//     MH_SESSION_TOKEN=<token>    (optional; zero-filled when absent)
//
//   STEP 2 — full enter-world (adds slot selection after the roster arrives):
//     + MH_LOGIN_ENTER_SLOT=<0..4>
//
// Credentials can also be supplied via a gitignored creds file:
//   %LOCALAPPDATA%\MartialHeroes\login.creds
//   Format: KEY=VALUE lines; lines starting with '#' are comments; blank lines skipped.
//   Keys: MH_LOGIN_ID, MH_LOGIN_PW, MH_LOGIN_PIN, MH_SESSION_TOKEN, MH_LOGIN_ENTER_SLOT
//   Precedence: env var WINS over creds file; file value is the fallback only when env is absent.
//
// When NONE of the env vars or creds-file keys supply MH_LOGIN_ID + MH_LOGIN_PW the harness
// is FULLY INERT — the interactive UI login path is completely unchanged.
//
// spec: Docs/RE/specs/login_flow.md §1 / §2 / §3 / §4 — the ordered lifecycle this harness
//        exercises via the exact same IApplicationUseCases calls the UI makes.
//
// SECURITY NOTE: credentials come from env vars or the gitignored creds file; they are logged
// as "present/absent + length + source" but their VALUES are never printed. MH_LOGIN_PW and
// MH_SESSION_TOKEN values are always redacted in logs.
// spec: login_flow.md §4.2 — password never in a plaintext log.

using System.Collections.Immutable;
using Godot;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Network.Abstractions.Lobby;
using MartialHeroes.Shared.Kernel.Enums;
using Environment = System.Environment;

namespace MartialHeroes.Client.Godot.Autoload;

public sealed partial class ClientContext
{
    // -------------------------------------------------------------------------
    // Env-harness task state
    // -------------------------------------------------------------------------

    // The background task for the env-login harness. Started in _Ready alongside the
    // engine loop when MH_LOGIN_ID is present. Drained in _ExitTree alongside _loopTask.
    private Task? _envLoginTask;

    // -------------------------------------------------------------------------
    // Public entry point — called from _Ready after BuildApplicationGraph
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Reads the env-gated credentials and, if present, spawns a background task that drives
    ///     the real login flow through IApplicationUseCases (same intents as LoginScene). When the
    ///     env vars are absent this method is a no-op and the interactive UI path is untouched.
    ///     <para>
    ///         spec: Docs/RE/specs/login_flow.md §1 — ordered lifecycle;
    ///         §4.2 — credential staging (LoginAsync); §2 — lobby (FetchServerListAsync /
    ///         SelectServerAsync); §3.3 — enter-game (SelectCharacterAsync).
    ///     </para>
    /// </summary>
    internal void MaybeStartEnvLogin()
    {
        // ---- MH_OFFLINE_ROSTER early-exit branch --------------------------------
        // When MH_OFFLINE_ROSTER=1 is set, inject a synthetic CharacterListEvent
        // directly into the event bus WITHOUT a TCP connection to the dead original
        // server (211.196.150.4:11407). This lets the headless verify loop reach
        // CharSelectWindow._Ready → BuildInventory and print its structural
        // breadcrumb WITHOUT needing a live replica server.
        //
        // Flow (fully async, no TCP):
        //   1. Wait for SceneMachine → Login (state 1, same as real harness).
        //   2. Set SkipOpening = true; AdvanceScene twice (Login→Load→Select or
        //      Login→Load then OnCharacterListReceived() → Select).
        //   3. Wait 1 s for the presentation SceneHost to swap in SelectScene and
        //      arm its CharSelectEventDrainer on the main thread.
        //   4. Publish a synthetic CharacterListEvent (2 dummy slots with class 1
        //      and class 2 so the slot-actor row has something to render) to
        //      EventBus. The CharSelectEventDrainer drains it on the next _Process
        //      tick and calls CharSelectWindow.ApplyCharacterList — which triggers
        //      the '[CharSelectWindow] inventory built: ...' breadcrumb.
        //
        // STRICTLY PASSIVE: does NOT call any TCP/login/server-select use cases.
        // Does NOT modify domain state (CharacterSelectionStore not populated —
        // the slot actors build from the event; the store is unused offline).
        // Does NOT advance the engine state beyond what the SceneMachine permits
        // (AdvanceScene + OnCharacterListReceived are the spec state transitions).
        //
        // OPEN@DEBUGGER: the per-slot +0x1548 flag semantic and the exact slot
        // descriptor byte layout are debugger-pending; the synthetic slots use
        // InternalClass 1/2 (Musa/Salsu) as sensible defaults for visual verification.
        if (string.Equals(
                Environment.GetEnvironmentVariable("MH_OFFLINE_ROSTER"), "1",
                StringComparison.Ordinal))
        {
            GD.Print("[ClientContext/EnvLogin] MH_OFFLINE_ROSTER=1 — offline-roster harness ACTIVE. " +
                     "Bypassing TCP; injecting synthetic CharacterListEvent into EventBus. " +
                     "spec: frontend_scenes.md §3.1 / §11.5h (CharSelectWindow breadcrumb target).");

            // Capture locals for the task closure.
            var ct = _loopCts?.Token ?? CancellationToken.None;
            var offlineTask = RunOfflineRosterAsync(ct);
            // Store alongside the real env-login task (same drain in _ExitTree).
            _envLoginTask = offlineTask;

            _ = offlineTask.ContinueWith(
                t => GD.PrintErr($"[ClientContext/EnvLogin] Offline-roster harness faulted: {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            return; // do NOT proceed to the TCP harness
        }
        // -------------------------------------------------------------------------

        // Read once at startup; never re-read mid-session.
        // Env var is the primary source; the creds file is the fallback for any key not set in env.
        var loginId = Environment.GetEnvironmentVariable("MH_LOGIN_ID");
        var loginPw = Environment.GetEnvironmentVariable("MH_LOGIN_PW");
        var loginPin = Environment.GetEnvironmentVariable("MH_LOGIN_PIN");
        var sessionToken = Environment.GetEnvironmentVariable("MH_SESSION_TOKEN");
        var enterSlotStr = Environment.GetEnvironmentVariable("MH_LOGIN_ENTER_SLOT");

        // Auto-login DISABLE toggle (env wins; creds-file "AUTOLOGIN" key is the fallback below).
        // When off, the harness stays inert so the maintainer can drive the UI login MANUALLY,
        // step by step — the creds file is kept untouched. Re-enable by removing the flag / setting 1.
        var autoLoginFlag = Environment.GetEnvironmentVariable("MH_AUTOLOGIN");

        // ---- creds-file fallback -----------------------------------------------
        // File: %LOCALAPPDATA%\MartialHeroes\login.creds
        // Format: KEY=VALUE lines; '#'-prefixed lines are comments; blank lines skipped.
        // Only falls back when the corresponding env var is null/whitespace.
        // Fail-open: a missing, unreadable, or malformed file behaves like "env-only".
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var credsPath = Path.Combine(localAppData, "MartialHeroes", "login.creds");
            if (File.Exists(credsPath))
            {
                Dictionary<string, string>? fileDict = null;
                try
                {
                    fileDict = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var rawLine in File.ReadAllLines(credsPath))
                    {
                        var line = rawLine.Trim();
                        if (line.Length == 0 || line.StartsWith('#'))
                            continue;
                        var eq = line.IndexOf('=');
                        if (eq <= 0) continue; // malformed — no key
                        var key = line[..eq].Trim();
                        var val = line[(eq + 1)..].Trim();
                        if (key.Length > 0)
                            fileDict[key] = val;
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[ClientContext/EnvLogin] Could not read creds file '{credsPath}': {ex.Message} — " +
                                "continuing with env vars only.");
                    fileDict = null;
                }

                if (fileDict is not null)
                {
                    // Env var WINS; file is the fallback only when env is absent/whitespace.
                    if (string.IsNullOrWhiteSpace(loginId) && fileDict.TryGetValue("MH_LOGIN_ID", out var fId))
                        loginId = fId;
                    if (string.IsNullOrWhiteSpace(loginPw) && fileDict.TryGetValue("MH_LOGIN_PW", out var fPw))
                        loginPw = fPw;
                    if (string.IsNullOrWhiteSpace(loginPin) && fileDict.TryGetValue("MH_LOGIN_PIN", out var fPin))
                        loginPin = fPin;
                    if (string.IsNullOrWhiteSpace(sessionToken) &&
                        fileDict.TryGetValue("MH_SESSION_TOKEN", out var fTok)) sessionToken = fTok;
                    if (string.IsNullOrWhiteSpace(enterSlotStr) &&
                        fileDict.TryGetValue("MH_LOGIN_ENTER_SLOT", out var fSlot)) enterSlotStr = fSlot;
                    if (string.IsNullOrWhiteSpace(autoLoginFlag) &&
                        fileDict.TryGetValue("AUTOLOGIN", out var fAuto)) autoLoginFlag = fAuto;
                }
            }
        }
        // -------------------------------------------------------------------------

        // Auto-login DISABLE gate: drive the UI login MANUALLY, step by step.
        // Set env MH_AUTOLOGIN=0  OR  add a line "AUTOLOGIN=0" to login.creds → harness stays
        // inert (creds file kept; interactive UI login path used). Re-enable: set 1 / remove the line.
        if (IsAutoLoginOff(autoLoginFlag))
        {
            GD.Print("[ClientContext/EnvLogin] Auto-login DISABLED (MH_AUTOLOGIN/AUTOLOGIN=0) — harness inert; " +
                     "drive the login flow MANUALLY through the UI. spec: login_flow.md §1.");
            return;
        }

        if (string.IsNullOrWhiteSpace(loginId) || string.IsNullOrWhiteSpace(loginPw))
        {
            // No credentials from env OR creds file — interactive UI path unchanged.
            GD.Print("[ClientContext/EnvLogin] MH_LOGIN_ID / MH_LOGIN_PW absent from env AND " +
                     "%LOCALAPPDATA%\\MartialHeroes\\login.creds — harness inactive; " +
                     "interactive UI login path unchanged. spec: login_flow.md §1.");
            return;
        }

        // Log presence + length + source only; never log the raw credential values.
        // spec: login_flow.md §4.2 — password is never in a plaintext log.
        int? enterSlot = null;
        if (!string.IsNullOrWhiteSpace(enterSlotStr)
            && int.TryParse(enterSlotStr.Trim(), out var parsedSlot)
            && parsedSlot is >= 0 and <= 4)
            enterSlot = parsedSlot;

        GD.Print($"[ClientContext/EnvLogin] Harness ACTIVE. " +
                 $"MH_LOGIN_ID present (len={loginId.Trim().Length}), " +
                 $"MH_LOGIN_PW present (len={loginPw.Trim().Length}), " +
                 $"MH_LOGIN_PIN={(string.IsNullOrWhiteSpace(loginPin) ? "absent" : $"present (len={loginPin.Trim().Length})")}, " +
                 $"MH_SESSION_TOKEN={(string.IsNullOrWhiteSpace(sessionToken) ? "absent" : $"present (len={sessionToken.Trim().Length})")}, " +
                 $"MH_LOGIN_ENTER_SLOT={(enterSlot.HasValue ? enterSlot.Value.ToString() : "absent (stop at char-select)")}. " +
                 "spec: login_flow.md §1.");

        // Capture local copies so the task closure never touches the mutable member.
        // _loopCts is already created by BuildApplicationGraph before this is called.
        var id = loginId.Trim();
        var pw = loginPw.Trim();
        var pin = string.IsNullOrWhiteSpace(loginPin) ? null : loginPin.Trim();
        var slot = enterSlot;

        var envLoginTask = RunEnvLoginAsync(id, pw, pin, slot, _loopCts!.Token);
        _envLoginTask = envLoginTask;

        // Observe faults (fire-and-forget; not the drain handle).
        _ = envLoginTask.ContinueWith(
            t => GD.PrintErr($"[ClientContext/EnvLogin] Harness faulted: {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }

    // Returns true when the auto-login toggle string disables the harness ("0"/"false"/"off"/"no").
    // A null/absent flag means "not disabled" → the harness runs as before when creds are present.
    private static bool IsAutoLoginOff(string? flag)
    {
        if (string.IsNullOrWhiteSpace(flag)) return false;
        var t = flag.Trim();
        return t == "0"
               || t.Equals("false", StringComparison.OrdinalIgnoreCase)
               || t.Equals("off", StringComparison.OrdinalIgnoreCase)
               || t.Equals("no", StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Harness drain (called from _ExitTree)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Drains the env-login background task if it was started, as part of the _ExitTree
    ///     cleanup. The task shares _loopCts so it is already cancelled before this is called.
    /// </summary>
    internal void DrainEnvLogin()
    {
        if (_envLoginTask is null) return;
        try
        {
            _envLoginTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Expected: OperationCanceledException wraps the cancellation.
        }
        finally
        {
            _envLoginTask = null;
        }
    }

    // -------------------------------------------------------------------------
    // Core harness logic
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Background task that drives the real login flow via IApplicationUseCases.
    ///     This is the exact same sequence LoginScene + LoginWindow perform via user gestures —
    ///     no bypass, no fake path.
    ///     Flow:
    ///     <list type="number">
    ///         <item>
    ///             Wait for the SceneMachine to reach Login (GameState 1). The lobby is already
    ///             resolved by the composition root; the server list is fetched and a server is
    ///             selected (first selectable server in the list). spec: login_flow.md §2.
    ///         </item>
    ///         <item>
    ///             Stage credentials: LoginAsync(id, pw) — same call as LoginScene.OnLoginAccepted.
    ///             spec: login_flow.md §4.2.
    ///         </item>
    ///         <item>
    ///             Open the TCP game connection (same as LoginScene.SelectServerAsync).
    ///             spec: login_flow.md §3.0.
    ///         </item>
    ///         <item>
    ///             Wait for the char-select roster (3/1 or 3/4 → CharacterListReceivedEvent).
    ///             Print slot count and first-slot name. spec: login_flow.md §7.
    ///         </item>
    ///         <item>
    ///             If MH_LOGIN_ENTER_SLOT is set: SelectCharacterAsync(slot) — same call as the
    ///             char-select OK button. spec: login_flow.md §3.3.
    ///         </item>
    ///     </list>
    /// </summary>
    /// <param name="loginId">Account name from MH_LOGIN_ID env var.</param>
    /// <param name="loginPw">Password from MH_LOGIN_PW env var (never logged as a value).</param>
    /// <param name="enterSlot">Slot index from MH_LOGIN_ENTER_SLOT, or null to stop at char-select.</param>
    /// <param name="ct">Linked to _loopCts; cancelled on _ExitTree.</param>
    private async Task RunEnvLoginAsync(
        string loginId,
        string loginPw,
        string? loginPin,
        int? enterSlot,
        CancellationToken ct)
    {
        try
        {
            // ------------------------------------------------------------------
            // Phase 0: wait for GameState → Login (state 1).
            // spec: login_flow.md §1 (the Login form is state 1 of the 8-state spine).
            // ------------------------------------------------------------------
            GD.Print("[ClientContext/EnvLogin] Phase 0: waiting for GameState=Login.");
            await WaitForLoginStateAsync(ct).ConfigureAwait(false);
            GD.Print("[ClientContext/EnvLogin] Phase 0 DONE: GameState=Login.");

            ct.ThrowIfCancellationRequested();

            // ------------------------------------------------------------------
            // Phase 1: fetch server list and select the first available server.
            // spec: login_flow.md §2.1 / §2.2 / §3.0.
            // ------------------------------------------------------------------
            GD.Print("[ClientContext/EnvLogin] Phase 1: FetchServerListAsync.");
            IReadOnlyList<LobbyServerRecord> servers;
            try
            {
                servers = await UseCases.FetchServerListAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ClientContext/EnvLogin] FetchServerListAsync failed: {ex.Message}. " +
                            "Cannot proceed. spec: login_flow.md §2.1.");
                return;
            }

            ct.ThrowIfCancellationRequested();

            // Select the first server that passes the canonical selectability gate.
            // spec: login_flow.md §2.1 — IsSelectable: status==0 && load<2400.
            LobbyServerRecord selectedRecord = default;
            var foundServer = false;
            foreach (var r in servers)
                if (r.IsSelectable) // spec: login_flow.md §2.1
                {
                    selectedRecord = r;
                    foundServer = true;
                    break;
                }

            if (!foundServer)
            {
                GD.PrintErr($"[ClientContext/EnvLogin] No selectable server (total={servers.Count}). " +
                            "spec: login_flow.md §2.1 — IsSelectable: status==0 && load<2400.");
                return;
            }

            GD.Print($"[ClientContext/EnvLogin] Phase 1: selected server_id={selectedRecord.ServerId}, " +
                     $"status={selectedRecord.StatusCode}, load={selectedRecord.Load}. spec: login_flow.md §2.1.");

            // ------------------------------------------------------------------
            // Phase 2: resolve channel endpoint for the selected server.
            // spec: login_flow.md §2.2 — "port = 10000 + selected server_id".
            // ------------------------------------------------------------------
            GD.Print($"[ClientContext/EnvLogin] Phase 2: SelectServerAsync(server_id={selectedRecord.ServerId}).");
            LobbyChannelEndpoint endpoint;
            try
            {
                endpoint = await UseCases.SelectServerAsync((ushort)selectedRecord.ServerId, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ClientContext/EnvLogin] SelectServerAsync failed: {ex.Message}. " +
                            "spec: login_flow.md §2.2.");
                return;
            }

            ct.ThrowIfCancellationRequested();
            GD.Print($"[ClientContext/EnvLogin] Phase 2 DONE: game endpoint={endpoint.Host}:{endpoint.Port}. " +
                     "spec: login_flow.md §2.2 / §3.0.");

            // ------------------------------------------------------------------
            // Phase 3: stage credentials.
            // spec: login_flow.md §4.2 — credential staging via LoginAsync. The password goes
            //        into LoginCredentialStore and later into the RSA 1/4 ciphertext; it is NEVER
            //        printed and NEVER placed in a plaintext log.
            // ------------------------------------------------------------------
            GD.Print($"[ClientContext/EnvLogin] Phase 3: staging credentials (account present len={loginId.Length}, " +
                     $"password=**REDACTED**, pin={(loginPin is null ? "none" : "**REDACTED** (a7-gated)")}). " +
                     "spec: login_flow.md §4.2.");
            await UseCases.LoginAsync(loginId, loginPw, loginPin, ct).ConfigureAwait(false);
            GD.Print("[ClientContext/EnvLogin] Phase 3 DONE: credentials staged (1/4 will carry them). " +
                     "spec: login_flow.md §4.2; crypto.md §6.1.");

            ct.ThrowIfCancellationRequested();

            // ------------------------------------------------------------------
            // Phase 4: open the TCP game connection.
            // spec: login_flow.md §3.0 — game server connect via gethostbyname (DNS allowed).
            // ------------------------------------------------------------------
            GD.Print($"[ClientContext/EnvLogin] Phase 4: OpenGameConnectionAsync({endpoint.Host}:{endpoint.Port}). " +
                     "spec: login_flow.md §3.0.");
            await OpenGameConnectionAsync(endpoint.Host, endpoint.Port).ConfigureAwait(false);
            GD.Print("[ClientContext/EnvLogin] Phase 4 DONE: TCP game connection open. " +
                     "spec: login_flow.md §3.0.");

            ct.ThrowIfCancellationRequested();

            // ------------------------------------------------------------------
            // Phase 5: wait for the character roster (3/1 or 3/4).
            // spec: login_flow.md §6 — "SUCCESS => unsolicited 3/1 SmsgCharacterList push".
            //       Also 3/4 SmsgSceneEntityUpdate carries the roster (CYCLE 4 live observation).
            // ------------------------------------------------------------------
            GD.Print("[ClientContext/EnvLogin] Phase 5: waiting for CharacterListReceivedEvent. " +
                     "Expected inbound sequence: 0/0 KeyExchange → 1/4 AuthReply (auto) → 3/4 or 3/1 roster. " +
                     "spec: login_flow.md §6 / §7.");

            var rosterTimeout = TimeSpan.FromSeconds(60);
            var roster = await WaitForCharacterListAsync(rosterTimeout, ct).ConfigureAwait(false);

            if (roster is null)
            {
                GD.PrintErr("[ClientContext/EnvLogin] Phase 5: timed out waiting for character roster. " +
                            "Check credentials, server availability, and 3/100 SmsgCharActionResult logs. " +
                            "spec: login_flow.md §6.");
                return;
            }

            GD.Print($"[ClientContext/EnvLogin] Phase 5 DONE: roster received (slot_count={roster.Count}). " +
                     "STEP 1 (login → char-select) COMPLETE. spec: login_flow.md §7.");
            for (var i = 0; i < roster.Count; i++)
            {
                var rec = roster[i];
                if (rec is not null)
                    GD.Print($"[ClientContext/EnvLogin]   slot[{i}] name='{rec.Name}' " +
                             $"level={rec.Level}. spec: login_flow.md §3.2.1.");
            }

            ct.ThrowIfCancellationRequested();

            // ------------------------------------------------------------------
            // Phase 6 (optional): enter-world.
            // spec: login_flow.md §9 — "player confirms slot; client sends 1/9; server answers 3/5".
            // Gated by MH_LOGIN_ENTER_SLOT. When absent, harness stops here (STEP 1 only).
            // ------------------------------------------------------------------
            if (!enterSlot.HasValue)
            {
                GD.Print("[ClientContext/EnvLogin] MH_LOGIN_ENTER_SLOT absent — stopping at char-select. " +
                         "STEP 2 (enter-world) skipped. spec: login_flow.md §9.");
                return;
            }

            GD.Print($"[ClientContext/EnvLogin] Phase 6: SelectCharacterAsync(slot={enterSlot.Value}). " +
                     "Sends 1/9 CmsgEnterGameRequest. spec: login_flow.md §9; cmsg_char_enter.yaml.");
            try
            {
                await UseCases.SelectCharacterAsync(enterSlot.Value, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ClientContext/EnvLogin] SelectCharacterAsync(slot={enterSlot.Value}) failed: " +
                            $"{ex.Message}. spec: login_flow.md §9.");
                return;
            }

            GD.Print($"[ClientContext/EnvLogin] Phase 6 DONE: 1/9 sent (slot={enterSlot.Value}). " +
                     "Waiting for 3/5 SmsgEnterGameAck then 4/1 SmsgGameStateTick world spawn. " +
                     "STEP 2 (enter-world) initiated. spec: login_flow.md §9; login_flow.md §3.4.");
        }
        catch (OperationCanceledException)
        {
            GD.Print("[ClientContext/EnvLogin] Harness cancelled (shutdown or _ExitTree).");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ClientContext/EnvLogin] Harness unhandled exception: {ex}");
        }
    }

    // -------------------------------------------------------------------------
    // Offline-roster harness (MH_OFFLINE_ROSTER=1)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Background task for the offline-roster harness. Drives the SceneMachine from
    ///     Login → Select WITHOUT a TCP connection, then injects a synthetic
    ///     <see cref="CharacterListEvent" /> into <see cref="EventBus" /> so the
    ///     <c>CharSelectEventDrainer</c> can deliver it to <c>CharSelectWindow.ApplyCharacterList</c>,
    ///     printing the structural breadcrumb that proves the 124-widget inventory was built.
    ///     <para>
    ///         No TCP socket is opened; no credential staging; no server-list fetch. STRICTLY PASSIVE:
    ///         the synthetic event is purely a view-state injection (two placeholder
    ///         <see cref="CharacterListSlot" /> records with InternalClass 1/2) — no real domain
    ///         mutation occurs. <c>CharacterSelectionStore</c> is NOT populated (offline harness
    ///         only targets the structural count breadcrumb, not the enter-game flow).
    ///     </para>
    ///     OPEN@DEBUGGER: synthetic slot InternalClass values (1=Musa, 2=Salsu) are sensible
    ///     defaults; actual class IDs are debugger-pending for offline slots.
    /// </summary>
    private async Task RunOfflineRosterAsync(CancellationToken ct)
    {
        try
        {
            // ------------------------------------------------------------------
            // Phase 0: wait for SceneMachine → Login (state 1).
            // spec: client_runtime.md §7.1 — Init(0) → Login(1) on first SceneHost._Ready.
            // ------------------------------------------------------------------
            GD.Print("[ClientContext/EnvLogin/Offline] Phase 0: waiting for GameState=Login.");
            await WaitForLoginStateAsync(ct).ConfigureAwait(false);
            GD.Print("[ClientContext/EnvLogin/Offline] Phase 0 DONE: GameState=Login.");

            ct.ThrowIfCancellationRequested();

            // ------------------------------------------------------------------
            // Phase 1: advance to Load, skip Opening, then force to Select.
            // spec: client_runtime.md §7.1 Login(1)→Load(2); §7.5.2 CharacterList → Select.
            // ------------------------------------------------------------------
            // Set SkipOpening so Load→Select directly (not Load→Opening→Select).
            // spec: client_runtime.md §7.3 — OPENNING/SKIP true → 2→4 (Load→Select direct).
            SceneMachine.SkipOpening = true;
            GD.Print("[ClientContext/EnvLogin/Offline] Phase 1: SkipOpening=true; advancing Login→Load.");

            // Advance Login(1)→Load(2). This triggers SceneStateChangedEvent on the bus,
            // which SceneHost drains and calls SyncToCurrentState → LoadScene.OnEnter.
            SceneMachine.AdvanceScene(); // Login → Load
            GD.Print("[ClientContext/EnvLogin/Offline] Phase 1: SceneMachine at Load. " +
                     "Waiting 500 ms for LoadScene to initialise.");

            // Brief wait for LoadScene._Ready to fire on the main thread.
            await Task.Delay(500, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            // Force Load → Select via OnCharacterListReceived (spec §7.5.2: the 3/1 receipt
            // during Load forces Load→Select transition). This commits SceneStateChangedEvent.
            SceneMachine.OnCharacterListReceived();
            GD.Print(
                "[ClientContext/EnvLogin/Offline] Phase 1: OnCharacterListReceived() → Select transition committed.");

            // ------------------------------------------------------------------
            // Phase 2: wait for the SelectScene to be active and its drainer armed.
            // The SceneStateChangedEvent is drained by SceneHost._Process on the next
            // main-thread frame; SyncToCurrentState → SelectScene.OnEnter runs there.
            // 1 s is generous for a headless run at 60 fps (= ~60 frames). spec: §7.3.
            // ------------------------------------------------------------------
            GD.Print("[ClientContext/EnvLogin/Offline] Phase 2: waiting 1 s for SelectScene drainer to arm.");
            await Task.Delay(1000, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            // ------------------------------------------------------------------
            // Phase 3: publish a synthetic CharacterListEvent into EventBus.
            // The CharSelectEventDrainer (armed by SelectScene.OnEnter) drains this on
            // the next _Process tick and calls CharSelectWindow.ApplyCharacterList,
            // which prints the structural breadcrumb.
            // Synthetic slots: slot 0 = InternalClass 1 (Musa), slot 1 = InternalClass 2 (Salsu), slot 2 = InternalClass 3 (Dosa).
            // OPEN@DEBUGGER: class IDs for offline slots are sensible defaults (1/2 = Musa/Salsu).
            // spec: frontend_scenes.md §3.1 (ApplyCharacterList contract).
            // spec: ClientEvents.cs (CharacterListEvent / CharacterListSlot).
            // ------------------------------------------------------------------
            // Synthetic slots use the §3.7.5 starter AppearanceVariant per class so
            // model_class_id = 5*(class + 4*variant) - 24 is POSITIVE (not sentinel):
            //   Musa  class=1, variant=1 → 5*(1+4)  - 24 =  1  ✓
            //   Salsu class=2, variant=2 → 5*(2+8)  - 24 = 26  ✓
            //   Dosa  class=3, variant=1 → 5*(3+4)  - 24 = 11  ✓
            // Variant=0 for any class yields model_class_id ≤ -19 → invisible sentinel (no mesh).
            // spec: skinning.md §3.5.2 (appearance_key = 5*(class+4*variant)-24, {1,11,16,26}).
            // spec: frontend_scenes.md §3.7.5 (starter variants {1,2,1,1} for classes {1,2,3,4}).
            var syntheticSlots = ImmutableArray.Create(
                new CharacterListSlot(
                    0,
                    "TestMusa",
                    10,
                    1,
                    100,
                    512f,
                    512f,
                    1, // Musa skeleton (g1.bnd).
                    1, // §3.7.5 starter variant → model_class_id=1. CODE-CONFIRMED.
                    1),
                new CharacterListSlot(
                    1,
                    "TestSalsu",
                    5,
                    2,
                    80,
                    524f,
                    512f,
                    2, // Salsu skeleton (g2.bnd).
                    2, // §3.7.5 starter variant → model_class_id=26. CODE-CONFIRMED.
                    1),
                new CharacterListSlot(
                    2,
                    "TestDosa",
                    8,
                    3,
                    90,
                    512f,
                    512f,
                    3, // Dosa skeleton (g3.bnd).
                    1, // §3.7.5 starter variant → model_class_id=11. CODE-CONFIRMED.
                    1));

            var syntheticEvent = new CharacterListEvent(
                0,
                0,
                syntheticSlots);

            var published = EventBus?.Publish(syntheticEvent) ?? false;
            GD.Print($"[ClientContext/EnvLogin/Offline] Phase 3: synthetic CharacterListEvent published " +
                     $"({syntheticSlots.Length} slots, published={published}). " +
                     "CharSelectWindow.ApplyCharacterList will print the structural breadcrumb on next _Process. " +
                     "spec: frontend_scenes.md §3.1 / §11.5h.");

            GD.Print("[ClientContext/EnvLogin/Offline] Offline-roster harness COMPLETE. " +
                     "Expected next log line: '[CharSelectWindow] inventory built: panels=10 images=37 " +
                     "buttons=46 labels=29 textboxes=2 (total=124); actionBindings=42'. spec: frontend_scenes.md §11.5h.");
        }
        catch (OperationCanceledException)
        {
            GD.Print("[ClientContext/EnvLogin/Offline] Offline-roster harness cancelled (shutdown or _ExitTree).");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ClientContext/EnvLogin/Offline] Harness unhandled exception: {ex}");
        }
    }

    // -------------------------------------------------------------------------
    // Wait helpers (pure async, no Godot main-thread mutation)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Polls SceneMachine until GameState == Login (state 1) or cancellation.
    ///     The SceneMachine boots at Init (0) and advances to Login on the first
    ///     <c>Advance()</c> call from SceneHost._Ready.
    ///     spec: Docs/RE/specs/client_runtime.md §7.1 — Init(0) → Login(1) on first advance.
    /// </summary>
    private async Task WaitForLoginStateAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (SceneMachine?.Current.State == EngineSceneState.Login)
                return;
            await Task.Delay(50, ct).ConfigureAwait(false);
        }

        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    ///     Polls the CharacterSelectionStore until at least one slot is populated (roster arrived),
    ///     with a bounded timeout. Returns a snapshot of the roster or null on timeout.
    ///     <para>
    ///         The 3/1 or 3/4 handler populates CharacterSelectionStore (via GamePacketHandler) and
    ///         publishes a CharacterListReceivedEvent. We poll the store because
    ///         EventBus is a SingleReader channel (LoginScene drains it); reading it here would
    ///         starve the UI. Polling the store is race-free: once populated it stays populated
    ///         until a new roster replaces it.
    ///         spec: login_flow.md §7 — roster drives char-select; store spec: login_flow.md §3.5.
    ///     </para>
    /// </summary>
    /// <param name="timeout">Maximum time to wait before returning null.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task<IReadOnlyList<CharacterSlotRecord?>?> WaitForCharacterListAsync(
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            var snapshot = UseCases?.GetCharacterRoster();
            if (snapshot is { Count: > 0 })
            {
                // At least one slot is populated (non-null entry or roster count > 0).
                // The store returns CharacterSlotRecord?[] where non-null = occupied slot.
                // A fully blank roster (all nulls) still has Count > 0 but no occupied slots;
                // check for at least one non-null record before declaring success.
                // spec: login_flow.md §3.5 — roster populated by 3/1 or 3/4 handler.
                var hasAny = false;
                foreach (var r in snapshot)
                    if (r is not null)
                    {
                        hasAny = true;
                        break;
                    }

                if (hasAny) return snapshot;
            }

            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        return null;
    }
}