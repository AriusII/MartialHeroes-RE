
using Godot;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Network.Abstractions.Lobby;
using MartialHeroes.Shared.Kernel.Enums;
using Environment = System.Environment;

namespace MartialHeroes.Client.Godot.Autoload;

public sealed partial class ClientContext
{

    private Task? _envLoginTask;


    internal void MaybeStartEnvLogin()
    {
        var loginId = Environment.GetEnvironmentVariable("MH_LOGIN_ID");
        var loginPw = Environment.GetEnvironmentVariable("MH_LOGIN_PW");
        var loginPin = Environment.GetEnvironmentVariable("MH_LOGIN_PIN");
        var sessionToken = Environment.GetEnvironmentVariable("MH_SESSION_TOKEN");
        var enterSlotStr = Environment.GetEnvironmentVariable("MH_LOGIN_ENTER_SLOT");

        var autoLoginFlag = Environment.GetEnvironmentVariable("MH_AUTOLOGIN");

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
                        if (eq <= 0) continue;
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

        if (IsAutoLoginOff(autoLoginFlag))
        {
            GD.Print("[ClientContext/EnvLogin] Auto-login DISABLED (MH_AUTOLOGIN/AUTOLOGIN=0) — harness inert; " +
                     "drive the login flow MANUALLY through the UI. spec: login_flow.md §1.");
            return;
        }

        if (string.IsNullOrWhiteSpace(loginId) || string.IsNullOrWhiteSpace(loginPw))
        {
            GD.Print("[ClientContext/EnvLogin] MH_LOGIN_ID / MH_LOGIN_PW absent from env AND " +
                     "%LOCALAPPDATA%\\MartialHeroes\\login.creds — harness inactive; " +
                     "interactive UI login path unchanged. spec: login_flow.md §1.");
            return;
        }

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

        var id = loginId.Trim();
        var pw = loginPw.Trim();
        var pin = string.IsNullOrWhiteSpace(loginPin) ? null : loginPin.Trim();
        var slot = enterSlot;

        var envLoginTask = RunEnvLoginAsync(id, pw, pin, slot, _loopCts!.Token);
        _envLoginTask = envLoginTask;

        _ = envLoginTask.ContinueWith(
            t => GD.PrintErr($"[ClientContext/EnvLogin] Harness faulted: {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }

    private static bool IsAutoLoginOff(string? flag)
    {
        if (string.IsNullOrWhiteSpace(flag)) return false;
        var t = flag.Trim();
        return t == "0"
               || t.Equals("false", StringComparison.OrdinalIgnoreCase)
               || t.Equals("off", StringComparison.OrdinalIgnoreCase)
               || t.Equals("no", StringComparison.OrdinalIgnoreCase);
    }


    internal void DrainEnvLogin()
    {
        if (_envLoginTask is null) return;
        try
        {
            _envLoginTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
        finally
        {
            _envLoginTask = null;
        }
    }


    private async Task RunEnvLoginAsync(
        string loginId,
        string loginPw,
        string? loginPin,
        int? enterSlot,
        CancellationToken ct)
    {
        try
        {
            GD.Print("[ClientContext/EnvLogin] Phase 0: waiting for GameState=Login.");
            await WaitForLoginStateAsync(ct).ConfigureAwait(false);
            GD.Print("[ClientContext/EnvLogin] Phase 0 DONE: GameState=Login.");

            ct.ThrowIfCancellationRequested();

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

            LobbyServerRecord selectedRecord = default;
            var foundServer = false;
            foreach (var r in servers)
                if (r.IsSelectable)
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

            GD.Print($"[ClientContext/EnvLogin] Phase 3: staging credentials (account present len={loginId.Length}, " +
                     $"password=**REDACTED**, pin={(loginPin is null ? "none" : "**REDACTED** (a7-gated)")}). " +
                     "spec: login_flow.md §4.2.");
            await UseCases.LoginAsync(loginId, loginPw, loginPin, ct).ConfigureAwait(false);
            GD.Print("[ClientContext/EnvLogin] Phase 3 DONE: credentials staged (1/4 will carry them). " +
                     "spec: login_flow.md §4.2; crypto.md §6.1.");

            ct.ThrowIfCancellationRequested();

            GD.Print($"[ClientContext/EnvLogin] Phase 4: OpenGameConnectionAsync({endpoint.Host}:{endpoint.Port}). " +
                     "spec: login_flow.md §3.0.");
            await OpenGameConnectionAsync(endpoint.Host, endpoint.Port).ConfigureAwait(false);
            GD.Print("[ClientContext/EnvLogin] Phase 4 DONE: TCP game connection open. " +
                     "spec: login_flow.md §3.0.");

            ct.ThrowIfCancellationRequested();

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