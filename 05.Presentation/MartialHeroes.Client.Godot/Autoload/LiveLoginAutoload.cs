// Autoload/LiveLoginAutoload.cs
//
// DEV/DIAGNOSTIC autoload — headless live-login instrument.
//
// PURPOSE:
//   Drives the full login → server-select → game-connect → enter-game flow programmatically
//   when three env vars are set:
//       MH_LOGIN_USER   — account name
//       MH_LOGIN_PASS   — account password
//       MH_LOGIN_PIN    — (optional) second-password / PIN; may be empty
//
//   When ANY of USER/PASS is absent this autoload is a strict no-op so the normal
//   headless boot is completely unaffected (no env-var = nothing happens).
//
// FLOW (when enabled):
//   1. DriveLoginAsync: LoginAsync → FetchServerListAsync → SelectServerAsync →
//      OpenGameConnectionAsync → roster arrives via 3/1 (GamePacketHandler fills the store).
//   2. _Process poll: once a non-blank slot appears in CharacterSelection.Snapshot(), trigger
//      SelectCharacterAsync(firstOccupiedSlotIndex) ONCE (_enterTriggered latch). This sends
//      the 1/9 CmsgEnterGameRequest (slot + 33-byte SessionToken at payload +0x01 + u32 version
//      token at payload +0x24). The server replies with 3/5 EnterGameAck, then 4/1 world snapshot.
//      spec: Docs/RE/specs/login_flow.md §1 step 9 / §3.3 / §3.5.
//      spec: Docs/RE/packets/cmsg_char_enter.yaml (SessionToken @0x01; VersionToken @0x24).
//
// ROSTER LOGGING APPROACH (WHY WE DO NOT DRAIN IClientEventBus):
//   IClientEventBus.Reader is a single-consumer System.Threading.Channels.ChannelReader<T>.
//   Calling TryRead() here would STEAL events from the real consumers (CharSelectEventDrainer,
//   GameLoop). Instead we POLL ClientContext.CharacterSelection.Snapshot() every ~500 ms
//   for up to 30 s and log once the snapshot becomes non-empty (name != "@BLANK@"). This
//   reads from the shared CharacterSelectionStore — the same instance that GamePacketHandler
//   fills on 3/1 and that ApplicationUseCases reads on SelectCharacterAsync — without
//   consuming any bus events.
//
// 4/1 WORLD SNAPSHOT DETECTION:
//   ClientContext does not expose a public "local player spawned" boolean that can be polled
//   without draining the IClientEventBus. The arrival of 4/1 is therefore observed via the
//   existing per-frame traces emitted by DispatcherFrameSink ("[Net←] 4/1 payload=NNNN B")
//   and by GameLoop ("[GameLoop] LocalPlayerSpawnedEvent") in the headless log — NOT via a
//   second poll in this autoload. We log the enter TRIGGER below and rely on those existing
//   traces for the world-snapshot confirmation.
//
// SECURITY:
//   Credentials come from OS env vars ONLY. NEVER hardcode them. NEVER commit them.
//   This autoload ships into the project but is a pure NO-OP when the env vars are absent.
//
// THREADING:
//   _Ready and _Process run on the Godot main thread.
//   The async login task and SelectCharacterAsync are fire-and-forget; all Godot node
//   mutations use CallDeferred.
//   spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — all Godot node mutation on main thread.
//
// spec: Docs/RE/specs/login_flow.md §1 step 9 (enter-game: slot + 1/9)
// spec: Docs/RE/specs/login_flow.md §3.3 / §3.5 (SessionToken in 1/9; CharacterSelectionStore)
// spec: Docs/RE/specs/login_flow.md §4.2 (LoginAsync stages the credential for the 0/0→1/4 exchange)
// spec: Docs/RE/specs/login_flow.md §2.0 (FetchServerListAsync / SelectServerAsync lobby flow)
// spec: Docs/RE/packets/cmsg_char_enter.yaml (SessionToken @0x01, 33 bytes; VersionToken @0x24)

using Godot;
using MartialHeroes.Client.Application.UseCases;

namespace MartialHeroes.Client.Godot.Autoload;

/// <summary>
/// Dev/diagnostic autoload that drives the full login → lobby → game-connection flow
/// programmatically when <c>MH_LOGIN_USER</c> / <c>MH_LOGIN_PASS</c> env vars are set.
/// A strict no-op when those vars are absent — the normal headless boot is unaffected.
///
/// spec: Docs/RE/specs/login_flow.md §4.2 / §2.0 / §3.5.
/// </summary>
public sealed partial class LiveLoginAutoload : Node
{
    // -------------------------------------------------------------------------
    // Poll timing constants
    // -------------------------------------------------------------------------

    /// <summary>Poll interval for the roster snapshot check (seconds).</summary>
    private const double PollIntervalSec = 0.5;

    /// <summary>Maximum time to wait for a populated roster before giving up.</summary>
    private const double PollTimeoutSec = 30.0;

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    private bool _enabled; // true when USER+PASS are present
    private bool _rosterLogged; // prevent double roster-log
    private bool _enterTriggered; // latch: SelectCharacterAsync fired ONCE after roster populates
    private double _pollAccumSec; // seconds since last roster poll
    private double _pollTotalSec; // total seconds waited for roster

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        try
        {
            // Credentials are read from OS env vars (System.Environment, NOT Godot.OS.GetEnvironment —
            // project idiom per the namespace-collision pitfall: global::Godot.* for Godot statics).
            // spec: CLAUDE.md "Namespace-collision pitfall".
            string? user = System.Environment.GetEnvironmentVariable("MH_LOGIN_USER");
            string? pass = System.Environment.GetEnvironmentVariable("MH_LOGIN_PASS");
            string? pin = System.Environment.GetEnvironmentVariable("MH_LOGIN_PIN");

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            {
                GD.Print("[LiveLogin] disabled (set MH_LOGIN_USER/MH_LOGIN_PASS/MH_LOGIN_PIN to enable)");
                _enabled = false;
                return;
            }

            _enabled = true;
            GD.Print($"[LiveLogin] enabled for user '{user}' (pin={(!string.IsNullOrEmpty(pin) ? "set" : "empty")}).");

            // Defer the actual async drive by one frame so ClientContext.UseCases is fully initialised.
            // Using CallDeferred ensures we start the async chain on the Godot main thread.
            CallDeferred(MethodName.DriveLogin, user, pass, pin ?? string.Empty);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LiveLogin] _Ready failed: {ex.Message}");
            _enabled = false;
        }
    }

    public override void _Process(double delta)
    {
        // Stop polling once both the roster log AND the enter trigger are done.
        if (!_enabled || (_rosterLogged && _enterTriggered)) return;

        // Poll the CharacterSelectionStore snapshot every PollIntervalSec for up to PollTimeoutSec.
        // We do NOT drain IClientEventBus.Reader (single-consumer channel — would steal events from
        // CharSelectEventDrainer / GameLoop). Polling the shared store snapshot is safe and non-destructive.
        _pollAccumSec += delta;
        _pollTotalSec += delta;

        if (_pollTotalSec >= PollTimeoutSec)
        {
            GD.PrintErr("[LiveLogin] timed out waiting for 3/1 character roster (30 s elapsed).");
            _enabled = false; // stop polling
            _rosterLogged = true;
            _enterTriggered = true;
            return;
        }

        if (_pollAccumSec < PollIntervalSec) return;
        _pollAccumSec = 0;

        var ctx = GetNodeOrNull<ClientContext>("/root/ClientContext");
        if (ctx?.CharacterSelection is not { } store) return;

        IReadOnlyList<CharacterSlotRecord?> snapshot = store.Snapshot();

        // Find the first occupied, non-blank slot index.
        int firstOccupiedIdx = -1;
        string firstOccupiedName = string.Empty;
        var names = new System.Collections.Generic.List<string>();
        for (int i = 0; i < snapshot.Count; i++)
        {
            CharacterSlotRecord? slot = snapshot[i];
            if (slot is not null &&
                !string.Equals(slot.Name, CharacterSelectionStore.BlankSlotSentinel,
                    System.StringComparison.Ordinal))
            {
                names.Add(slot.Name);
                if (firstOccupiedIdx < 0)
                {
                    firstOccupiedIdx = i;
                    firstOccupiedName = slot.Name;
                }
            }
        }

        if (names.Count == 0) return; // roster not yet populated

        // Log the roster once.
        if (!_rosterLogged)
        {
            GD.Print($"[LiveLogin] char-select populated: {names.Count} [{string.Join(", ", names)}]");
            _rosterLogged = true;
        }

        // Trigger enter-game ONCE for the first occupied slot.
        // spec: Docs/RE/specs/login_flow.md §1 step 9 / §3.3 / §3.5.
        // spec: Docs/RE/packets/cmsg_char_enter.yaml (SessionToken @0x01, 33 bytes).
        if (!_enterTriggered)
        {
            _enterTriggered = true;

            // DEV no-enter gate (MH_LOGIN_NOENTER): validate login→server-list→char-list WITHOUT sending
            // 1/9 enter-game. Used to exercise the roster/server-list flow live without ghosting the
            // account in-world (a successful enter holds an account-level re-entry lock for a long time).
            // Offline-safe: a strict no-op unless the env var is set.
            string? noEnter = System.Environment.GetEnvironmentVariable("MH_LOGIN_NOENTER");
            if (!string.IsNullOrEmpty(noEnter))
            {
                GD.Print("[LiveLogin] MH_LOGIN_NOENTER set — stopping after char-list (no 1/9 enter-game).");
                _enabled = false;
                return;
            }

            // DEV slot override (MH_LOGIN_SLOT): enter a specific occupied slot instead of the first
            // occupied — used to sidestep a server re-entry lock on a recently-ghosted character.
            int enterIdx = firstOccupiedIdx;
            string enterName = firstOccupiedName;
            string? slotEnv = System.Environment.GetEnvironmentVariable("MH_LOGIN_SLOT");
            if (!string.IsNullOrEmpty(slotEnv) && int.TryParse(slotEnv, out int wantSlot) &&
                wantSlot >= 0 && wantSlot < snapshot.Count && snapshot[wantSlot] is { } wantRec &&
                !string.Equals(wantRec.Name, CharacterSelectionStore.BlankSlotSentinel,
                    System.StringComparison.Ordinal))
            {
                enterIdx = wantSlot;
                enterName = wantRec.Name;
            }

            GD.Print($"[LiveLogin] entering slot {enterIdx} ({enterName}) … " +
                     "spec: login_flow.md §3.3 / §3.5; cmsg_char_enter.yaml.");

            // SelectCharacterAsync is pure Application (no Godot node mutation) — safe to call from
            // _Process. Fire-and-forget: observe faults via ContinueWith to avoid unobserved exceptions.
            // 4/1 world-snapshot arrival is observed via the existing trace:
            //   "[Net←] 4/1 payload=NNNN B"  (DispatcherFrameSink in ClientContext)
            // and GameLoop's "[GameLoop] LocalPlayerSpawnedEvent" — ClientContext exposes no public
            // "local player spawned" boolean that can be polled without draining IClientEventBus.
            ValueTask enterTask =
                ctx.UseCases.SelectCharacterAsync(enterIdx, System.Threading.CancellationToken.None);
            _ = enterTask.AsTask().ContinueWith(
                t => GD.PrintErr($"[LiveLogin] SelectCharacterAsync faulted: {t.Exception}"),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted |
                System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously);
        }

        // Both log and trigger are done; disable further polling.
        if (_rosterLogged && _enterTriggered)
            _enabled = false;
    }

    // -------------------------------------------------------------------------
    // Deferred login driver (called from _Ready via CallDeferred)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Drives the full login → lobby → game-connection flow asynchronously (fire-and-forget).
    /// Errors are caught and logged; nothing is rethrown so the Godot main loop is never interrupted.
    /// </summary>
    private void DriveLogin(string user, string pass, string pin)
    {
        // Fire-and-forget: observe faults via ContinueWith so the task is never unobserved.
        Task driveTask = DriveLoginAsync(user, pass, pin);
        _ = driveTask.ContinueWith(
            t => GD.PrintErr($"[LiveLogin] drive task faulted: {t.Exception}"),
            System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted |
            System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously);
    }

    private async Task DriveLoginAsync(string user, string pass, string pin)
    {
        try
        {
            var ctx = GetNodeOrNull<ClientContext>("/root/ClientContext");
            if (ctx is null)
            {
                GD.PrintErr("[LiveLogin] ClientContext not found at /root/ClientContext — aborting.");
                _enabled = false;
                return;
            }

            // Step 1: Stage the login credential so the LoginHandshakeDriver answers the inbound
            // 0/0 KeyExchange with the secure 1/4 Auth reply.
            // spec: Docs/RE/specs/login_flow.md §4.2 (credential pre-staged; 1/4 built on 0/0 arrival).
            await ctx.UseCases.LoginAsync(user, pass, string.IsNullOrEmpty(pin) ? null : pin,
                CancellationToken.None).ConfigureAwait(false);
            GD.Print($"[LiveLogin] credentials staged for user '{user}'. spec: login_flow.md §4.2.");

            // Step 2: Fetch the server list from the lobby.
            // spec: Docs/RE/specs/login_flow.md §2.0 / §2.1 (ip.txt → list.dat → fallback host resolution).
            IReadOnlyList<MartialHeroes.Network.Abstractions.Lobby.LobbyServerRecord> servers =
                await ctx.UseCases.FetchServerListAsync(CancellationToken.None).ConfigureAwait(false);

            if (servers.Count == 0)
            {
                // An empty server list means the lobby returned nothing — there is no server to
                // select. A fabricated id would produce a bogus endpoint and mislead Tier-1.
                // Abort cleanly so the real situation is visible in the log.
                // spec: Docs/RE/specs/login_flow.md §2.1
                GD.PrintErr(
                    "[LiveLogin] lobby returned no servers — aborting live login (no server to select). spec: login_flow.md §2.1");
                _enabled = false;
                return;
            }

            ushort serverId = servers[0].ServerId;
            GD.Print($"[LiveLogin] server list received ({servers.Count} entries); selecting serverId={serverId}.");

            // Step 3: Resolve the game-server channel endpoint for the chosen server.
            // spec: Docs/RE/specs/login_flow.md §2.2 (channel-endpoint query: port 10000 + serverId).
            MartialHeroes.Network.Abstractions.Lobby.LobbyChannelEndpoint endpoint =
                await ctx.UseCases.SelectServerAsync(serverId, CancellationToken.None).ConfigureAwait(false);
            GD.Print(
                $"[LiveLogin] channel endpoint resolved: {endpoint.Host}:{endpoint.Port}. spec: login_flow.md §2.2.");

            // Step 4: Open the TCP game connection. The staged credential + the inbound 0/0→1/4
            // handshake then drives the server to push the character roster (3/1).
            // OpenGameConnectionAsync is idempotent; the crypto relay is activated once the session opens.
            await ctx.OpenGameConnectionAsync(endpoint.Host, endpoint.Port).ConfigureAwait(false);
            GD.Print("[LiveLogin] game connection opened; awaiting roster (3/1 → CharacterSelectionStore). " +
                     "Roster logged via _Process poll. spec: login_flow.md §3.2.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LiveLogin] DriveLoginAsync failed: {ex.Message}");
            _enabled = false;
        }
    }
}