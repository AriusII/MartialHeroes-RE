// ClientContext.EnvLogin.cs — env-gated "copie conforme" verification harness.
//
// Drives the REAL login flow (same use-case intents as LoginScene's OK button) when the
// maintainer sets the appropriate environment variables:
//
//   STEP 1 — login → char-select (roster printed, no enter-world):
//     MH_LOGIN_ID=<account>
//     MH_LOGIN_PW=<password>
//     MH_SESSION_TOKEN=<token>    (optional; zero-filled when absent)
//
//   STEP 2 — full enter-world (adds slot selection after the roster arrives):
//     + MH_LOGIN_ENTER_SLOT=<0..4>
//
// When NONE of the env vars are present the harness is FULLY INERT — the interactive UI
// login path is completely unchanged; not a single path is auto-driven.
//
// spec: Docs/RE/specs/login_flow.md §1 / §2 / §3 / §4 — the ordered lifecycle this harness
//        exercises via the exact same IApplicationUseCases calls the UI makes.
//
// SECURITY NOTE: credentials come ONLY from env vars; they are logged as "present/absent +
// length" but their VALUES are never printed. MH_LOGIN_PW and MH_SESSION_TOKEN values are
// always redacted in logs. spec: login_flow.md §4.2 — password never in a plaintext log.

using Godot;
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
        // Read once at startup; never re-read mid-session.
        var loginId = Environment.GetEnvironmentVariable("MH_LOGIN_ID");
        var loginPw = Environment.GetEnvironmentVariable("MH_LOGIN_PW");
        var loginPin = Environment.GetEnvironmentVariable("MH_LOGIN_PIN");

        if (string.IsNullOrWhiteSpace(loginId) || string.IsNullOrWhiteSpace(loginPw))
        {
            // No env credentials — interactive UI path unchanged.
            GD.Print("[ClientContext/EnvLogin] MH_LOGIN_ID / MH_LOGIN_PW absent — harness inactive; " +
                     "interactive UI login path unchanged. spec: login_flow.md §1.");
            return;
        }

        // Log presence + length only; never log the raw password value.
        // spec: login_flow.md §4.2 — password is never in a plaintext log.
        var enterSlotStr = Environment.GetEnvironmentVariable("MH_LOGIN_ENTER_SLOT");
        int? enterSlot = null;
        if (!string.IsNullOrWhiteSpace(enterSlotStr)
            && int.TryParse(enterSlotStr.Trim(), out var parsedSlot)
            && parsedSlot is >= 0 and <= 4)
            enterSlot = parsedSlot;

        GD.Print($"[ClientContext/EnvLogin] Harness ACTIVE. " +
                 $"MH_LOGIN_ID present (len={loginId.Trim().Length}), " +
                 $"MH_LOGIN_PW present (len={loginPw.Trim().Length}), " +
                 $"MH_LOGIN_PIN={(string.IsNullOrWhiteSpace(loginPin) ? "absent" : $"present (len={loginPin.Trim().Length})")}, " +
                 $"MH_SESSION_TOKEN={(Environment.GetEnvironmentVariable("MH_SESSION_TOKEN") is { Length: > 0 } t ? $"present (len={t.Length})" : "absent")}, " +
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

            // Select the first server with status_code == 0 and load < 2400.
            // spec: login_flow.md §2.1 — "selectability gate: status==0 && load<2400".
            LobbyServerRecord selectedRecord = default;
            var foundServer = false;
            foreach (var r in servers)
                if (r.StatusCode == 0 && r.Load < 2400)
                {
                    selectedRecord = r;
                    foundServer = true;
                    break;
                }

            if (!foundServer)
            {
                GD.PrintErr($"[ClientContext/EnvLogin] No selectable server (total={servers.Count}). " +
                            "spec: login_flow.md §2.1 — gate status==0 && load<2400.");
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
                endpoint = await UseCases.SelectServerAsync(selectedRecord.ServerId, ct).ConfigureAwait(false);
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
            GD.Print($"[ClientContext/EnvLogin] Phase 3: staging credentials (account='{loginId}', " +
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