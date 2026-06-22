using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Shared.Kernel.Enums;
using MartialHeroes.Shared.Kernel.State;

namespace MartialHeroes.Client.Application.Contracts.Scene;

// spec: Docs/RE/specs/client_runtime.md §7 — the faithful 8-case (0..7) master scene machine.
public sealed class SceneStateMachine
{
    private readonly IClientEventBus _eventBus;

    /// <summary>Creates the machine at the constructor-default boot state (0 Init, sub 8).</summary>
    public SceneStateMachine(IClientEventBus eventBus, GameState? initial = null)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        Current = initial ?? GameState.Initial;
    }

    /// <summary>The live engine-state struct — the sole source of truth for the active scene.</summary>
    public GameState Current { get; private set; }

    // spec: Docs/RE/specs/client_runtime.md §7.3 — OPENNING/SKIP gates Load: true → 2→4, false → 2→3.
    public bool SkipOpening { get; set; }

    // spec: resource_pipeline.md §2.5 — reload marker; only tells LoadOrchestrator to skip the msg.xdb pre-load.
    public bool LoadIsReload { get; private set; }

    /// <summary>
    ///     The "enter-world confirmed" latch — the engine-free analogue of the original's
    ///     "already entering" guard that distinguishes a BOOT load (terminates at Opening/Select per
    ///     OPENNING/SKIP) from an ENTER load (terminates at InGame).
    ///     <para>
    ///         CORRECTED 2026-06-21 (live server 211.196.150.4): on the live server the enter is
    ///         confirmed by the latch-armed <c>4/1 SmsgGameStateTick</c> (see
    ///         <see cref="OnWorldEntryConfirmed" />), NOT by an enter-ladder <c>3/5</c> (which never
    ///         arrives between the 1/9 and the 4/1 — the only 3/5 is the unsolicited post-login
    ///         account-ack that precedes the 1/9). This flag remains the engine-free "this Load is the
    ///         world load" marker for the defensive 3/5 path and for the 3/100 enter-phase rejection,
    ///         but the authoritative enter trigger is the in-flight latch read at 4/1.
    ///     </para>
    ///     spec: Docs/RE/specs/client_runtime.md §7.5.3 (enter path terminates at 5);
    ///     Docs/RE/specs/login_flow.md §1 step 9 / §3.4 (CORRECTED enter ladder 1/9 → 4/1);
    ///     Docs/RE/specs/net_contracts.md §1.3 (CORRECTED — 4/1 confirms + clears the latch).
    /// </summary>
    public bool EnteringWorld { get; private set; }

    // spec: Docs/RE/specs/client_runtime.md §7.3 — true after Quit/Error fell through the field-0 == 8 exit tail.
    public bool HasExited { get; private set; }

    // spec: Docs/RE/specs/client_runtime.md §7.3 — Quit/Error converge on the shared exit tail (field-0 → 8).
    public bool IsTerminal => Current.State is EngineSceneState.Quit or EngineSceneState.Error;

    // spec: Docs/RE/specs/client_runtime.md §7.5.1 — case-body next-state write: 0→1→(2→3/4)→4→5→4, 6/7→teardown.
    public bool AdvanceScene()
    {
        return Current.State switch
        {
            EngineSceneState.Init => Commit(Current.To(EngineSceneState.Login)), // 0 → 1
            EngineSceneState.Login => Commit(Current.To(EngineSceneState.Load)), // 1 → 2
            EngineSceneState.Load => AdvanceLoadScene(),
            EngineSceneState.Opening => Commit(Current.To(EngineSceneState.Select)), // 3 → 4
            EngineSceneState.Select => Commit(Current.To(EngineSceneState.InGame)), // 4 → 5
            EngineSceneState.InGame => Commit(Current.To(EngineSceneState.Select)), // 5 → 4 (default return)
            EngineSceneState.Quit or EngineSceneState.Error => ExitTail(),
            _ => false
        };
    }

    // spec: Docs/RE/specs/client_runtime.md §7.5.2 — EnterGameAck (3/5) forces Load (state 2), state-agnostic.
    //
    // CORRECTED 2026-06-21 (live server 211.196.150.4): on the live server the enter ladder is
    // 1/9 → 4/1 — the enter is confirmed by 4/1 (see OnWorldEntryConfirmed), and there is NO
    // enter-ladder 3/5. The only 3/5 is the UNSOLICITED post-login account-ack the replica pushes right
    // after the 3/4/3/1 roster (login_flow.md §1 step 7 / §3.4), and it arrives BEFORE any 1/9 — so the
    // in-flight latch is NOT armed when it is received. This method therefore forces Load (state 2) but
    // leaves the enter-world latch DISARMED in the normal case, so the boot load terminates at
    // Opening/Select (the roster), never the world. The world is entered ONLY by a latch-armed 4/1.
    //
    // The <paramref name="enterRequestPending"/> guard is retained for robustness: should some server
    // ever interleave a 3/5 while a 1/9 is genuinely in flight, it pre-arms EnteringWorld; but on the
    // live server this branch never taken (the 3/5 precedes the 1/9) and 4/1 remains the authority.
    //
    // spec: Docs/RE/specs/client_runtime.md §7.5.2 / §7.5.3; Docs/RE/specs/login_flow.md §1 step 7
    // (unsolicited post-login 3/5, CORRECTED) / §3.4 (CORRECTED enter ladder 1/9 → 4/1);
    // Docs/RE/specs/net_contracts.md §1.3 (CORRECTED enter ladder; 4/1 confirms + clears the latch).
    public bool OnEnterGameAck(bool enterRequestPending)
    {
        // Defensive only: on the live server a 3/5 never arrives with the latch armed (it precedes the
        // 1/9). The authoritative enter confirmation is the latch-armed 4/1 (OnWorldEntryConfirmed).
        if (enterRequestPending) EnteringWorld = true;
        return Commit(Current.To(EngineSceneState.Load));
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // SCENE-STATE NUMBERING RECONCILIATION (CharSelect entry) — the case-index vs game-state-value caveat
    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //
    // The CYCLE 11 frontend_scenes.md §A.4 records that, in the binary, the engine scene machine's
    // switch CASE INDEX and the running GAME-STATE VALUE it writes are NOT the same number for the two
    // front-of-world scenes — they are SWAPPED:
    //
    //     switch case index 4  builds CharacterSelect  and writes running game-state VALUE 5
    //     switch case index 5  builds the in-world scene and writes running game-state VALUE 4
    //
    // So in the binary, character-select runs while the running game-state VALUE == 5 (via case index 4),
    // and the in-world scene runs while the VALUE == 4 (via case index 5).
    //
    // This C# enum (EngineSceneState, layer 01 — out of this layer's scope, intentionally NOT renumbered)
    // tracks the binary's CASE INDEX, not its running game-state value:
    //     EngineSceneState.Select  = 4  == case index 4  == binary CharSelect (binary game-state VALUE 5)
    //     EngineSceneState.InGame  = 5  == case index 5  == binary in-world    (binary game-state VALUE 4)
    //
    // Because the C# enum follows the CASE-INDEX convention CONSISTENTLY for BOTH scenes, there is NO
    // off-by-one between CharSelect and World here: Select→InGame→Select are correct state-machine edges
    // (4→5→4 as enum/case indices). The binary's running-game-state-VALUE numbering (5 for CharSelect,
    // 4 for World) is the SWAPPED view and is deliberately NOT mirrored by the enum value — a future
    // reader must NOT "fix" the enum to 5/4 (that would invert the consistent case-index mapping and
    // break World). The only place the value-5/value-4 numbering matters is when correlating a live
    // binary game-state read against this enum: read it as case index, not as the running value.
    // spec: Docs/RE/specs/frontend_scenes.md §A.4 (case index vs game-state value, SWAPPED for these two
    // scenes); Docs/RE/specs/client_runtime.md §7.5.1 (the C# enum tracks the case index: Select=4, InGame=5).
    //
    // spec: Docs/RE/specs/client_runtime.md §7.5.2 — CharacterList (3/1) on Load/Select forces a Select re-entry (sub 8).
    public bool OnCharacterListReceived()
    {
        if (Current.State is EngineSceneState.Load or EngineSceneState.Select)
        {
            // A fresh roster re-enters char-select — any in-flight enter is abandoned, so disarm the
            // latch (the cached descriptor is no longer the load destination).
            // spec: Docs/RE/specs/client_runtime.md §7.5.2 (3/1 → Select), §7.9.5.
            EnteringWorld = false;
            return Commit(Current.To(EngineSceneState.Select, GameState.SubStateNone), true);
        }

        return false;
    }

    // spec: Docs/RE/specs/client_runtime.md §7.5.2 — 4/1 with no local player materializable = spawn-failure fallback 5→4.
    public bool OnGameStateTickNoLocalPlayer()
    {
        return Current.State == EngineSceneState.InGame && Commit(Current.To(EngineSceneState.Select));
    }

    // spec: Docs/RE/specs/login_flow.md §1 step 9 / §3.4 (CORRECTED 2026-06-21 against the live server);
    //       Docs/RE/specs/net_contracts.md §1.3 (CORRECTED enter ladder).
    //
    // LIVE ENTER CONFIRMATION (4/1). The live server's enter ladder is 1/9 → 4/1: the 4/1
    // SmsgGameStateTick world-entry snapshot IS the enter confirmation, with NO enter-ladder 3/5 in
    // between (the only 3/5 on this server is the UNSOLICITED post-login account-ack that arrives
    // BEFORE the 1/9). So a 4/1 observed while the in-flight latch is ARMED (a 1/9 was sent) is the
    // server's confirmation that the world was entered: drive the scene to InGame (state 5) directly
    // and consume the enter-world latch. This subsumes the older 3/5-driven Load → InGame promotion
    // (OnEnterGameAck + AdvanceLoadScene) which never fired on the live server because the enter-ladder
    // 3/5 never arrives. <paramref name="enterRequestPending"/> carries the latch-armed state captured
    // by the handler BEFORE it clears the latch.
    //
    // A 4/1 with no enter pending (no 1/9 sent) is an ordinary in-world tick and must NOT transition
    // the scene (it is already in InGame, or the unsolicited-3/5 path already drove Load); this method
    // returns false (an explicit no-op) so total-machine coverage is preserved.
    //
    // ENTER-LATCH LIFECYCLE (CORRECTED CYCLE 12 Phase 2 — the 3/14-emits-1/9-which-arms reconciliation).
    // Under the corrected ladder 1/7(mode 1) → 3/14(flag≠0) → 1/9 → 3/5 → 4/1, the enter-in-flight signal
    // is the 1/9 EMIT, not the 1/7 play-confirm: the 3/14 handler emits 1/9 (ApplicationUseCases.
    // EmitEnterWorldRequest), and THAT emit Arm()s the in-flight latch. So when this 4/1 read fires, the
    // latch is ARMED iff a 1/9 was emitted by a positive-flag 3/14 — exactly the enter-confirmation
    // condition. (net_contracts.md §1.3's census lists 3/14 among the latch-CLEARERS; that clear now
    // applies ONLY to the 3/14 ZERO-flag path — the positive-flag path re-arms via the 1/9 emit, which is
    // deliberate, not a regression of the census.) The mode-1 1/7 itself only arms the char-mgmt latch for
    // keepalive suppression; it is NOT the enter-in-flight signal. spec: Docs/RE/specs/net_contracts.md
    // §1.3 (1/9 arms the enter latch; 4/1 clears it); Docs/RE/specs/frontend_scenes.md §7 (the enter ladder).
    public bool OnWorldEntryConfirmed(bool enterRequestPending)
    {
        if (!enterRequestPending) return false; // no 1/9 in flight — not an enter confirmation.

        // The live ladder collapses the spec's 4 → 2 → … → 5 enter path onto the single 4/1: from
        // Select (the scene the 1/9 was sent from) or Load (if an unsolicited 3/5 nudged Load first),
        // the latch-armed 4/1 commits straight to InGame (5). spec: client_runtime.md §7.5.3 (enter
        // path terminates at 5); login_flow.md §1 step 9 (CORRECTED 1/9 → 4/1).
        EnteringWorld = false;
        if (Current.State is EngineSceneState.Select or EngineSceneState.Load)
            return Commit(Current.To(EngineSceneState.InGame));

        // Already in InGame (e.g. a re-enter tick) — no transition, but the enter is confirmed.
        return false;
    }

    // spec: Docs/RE/specs/handlers.md §23.1 — CANONICAL 3/100 SmsgCharActionResult dual-mode code table
    //       (NOT 3/7); Docs/RE/specs/client_runtime.md §7.5.2 — the engine-state-transition VIEW.
    public bool OnCharActionResult(int result, bool hasLocalPlayer)
    {
        // ENTER-PHASE REJECTION (Load + EnteringWorld). A 3/100 that arrives while an enter pre-armed
        // EnteringWorld and pushed the scene to Load (the defensive 3/5 path), but the server answered
        // 3/100 instead of the 4/1 world tick, is a REJECTION of the enter — e.g. a duplicate-session /
        // ghost-lock. The world must NOT be entered: abandon the in-flight enter, disarm the latch, and
        // return to char-select (Select, sub 8) so the user stays at the roster and the reason code is
        // surfaced (the handler publishes the decoded code). Only Select / InGame have explicit 3/100
        // rows in §7.5.2; this Load-phase row is the faithful realisation of "enter the world ONLY on the
        // server's enter confirmation (the latch-armed 4/1); a 3/100 during the enter phase keeps the
        // client at char-select". spec: Docs/RE/specs/login_flow.md §1 step 9 / §3.4 (CORRECTED — enter
        // only on a latch-armed 4/1); Docs/RE/specs/net_contracts.md §1.3 (CORRECTED enter ladder
        // 1/9 → 4/1; 4/1 confirms + clears the latch); Docs/RE/specs/client_runtime.md §7.5.2.
        if (Current.State == EngineSceneState.Load && EnteringWorld)
        {
            EnteringWorld = false;
            return Commit(Current.To(EngineSceneState.Select, GameState.SubStateNone), true);
        }

        // SELECT-MODE (lobby / character-select, no local player) — the full §23.1 select-mode code
        // table. spec: Docs/RE/specs/handlers.md §23.1 (CANONICAL 3/100 dual-mode code table,
        // select-mode rows); Docs/RE/specs/client_runtime.md §7.5.2 (engine-state-transition view).
        if (Current.State == EngineSceneState.Select && !hasLocalPlayer)
            return result switch
            {
                // code 0 → "return to prior scene" quit-return path. spec: handlers.md §23.1 (select 0 → 6/8).
                0 => Commit(Current.To(EngineSceneState.Quit, GameState.SubStateNone)), // → 6/8
                // RECOVERABLE char-operation error family → 7/5 (recoverable Error sub-state), error-detail = code.
                // 22 and 23 are RECOVERABLE (the binary reaches them via the code>=22 guard), NOT the fatal 7/8
                // out-of-range bucket — a classifier that drops 22/23 into 7/8 diverges from the binary. Code 23
                // additionally surfaces notice string-id 1604 (presentation maps it from the raw published code)
                // and is excluded from the deferred retry timer (the C# scene machine does not model that timer).
                // spec: handlers.md §23.1 (select recoverable family {1,2,3,4,5,7,22,23} → 7/5; 22/23 correction).
                1 or 2 or 3 or 4 or 5 or 7 or 22 or 23 => Commit(Current.ToError(5, result)), // → 7/5
                // create/rename/billing-accepted reload path → Load (state 2). spec: handlers.md §23.1 (reload family).
                202 or 203 or 232 => CommitReloadLoad(), // → 2 reload
                // PUBLISH-ONLY, NON-transitioning band: the handler already published the raw code to presentation
                // (no engine-state write here); the scene does NOT change. Return false (no-op) so these codes do
                // not fall into a transition. spec: handlers.md §23.1 (select publish-only rows
                // {10,11,16,200,201,204-211,220-227}).
                10 or 11 or 16 or 200 or 201 => false, // publish-only, no transition
                >= 204 and <= 211 => false, // publish-only band 204..211, no transition
                >= 220 and <= 227 => false, // publish-only band 220..227, no transition
                // INERT TAIL: the 200-block jumptable default {212..219, 228..231} has NO state write and NO
                // publish-code call — it is a true no-op in the binary. Return false. spec: handlers.md §23.1
                // (select-mode row "212-219, 228-231 → (no state write) — jumptable default, inert tail").
                >= 212 and <= 219 => false, // jumptable-default inert tail, no transition
                >= 228 and <= 231 => false, // jumptable-default inert tail, no transition
                // §23.1 enumerates the COMPLETE select-mode code set as {0, 1-5, 7, 10, 11, 16, 22, 23} ∪ {200..232}
                // and enumerates NO fatal select-mode out-of-range arm: every code IN that closed set is handled
                // above, so any remaining code reaching here is a value OUTSIDE the fully-enumerated set. Codes
                // 0..232 within the set never reach this default (they are all matched above); only a provably
                // out-of-set code (e.g. negative or > 232) lands here, surfaced as a generic error.
                // spec: handlers.md §23.1 (the select-mode code set is FULLY ENUMERATED; there is NO fatal
                // select-mode out-of-range arm for in-set codes — the default catches only out-of-set values).
                _ => Commit(Current.ToError(GameState.SubStateNone, result)) // → 7/8 (out-of-set only)
            };

        // IN-WORLD MODE (local player present) — keyed PURELY on local-player presence, per §23.1's two
        // top-level modes ("keyed on whether the local player exists yet"). The earlier extra
        // Current.State == InGame co-gate is NOT in the spec — §23.1 selects the in-world block on
        // local-player presence alone, and its state writes (0 → 6/8, any other non-zero → 7/8, detail =
        // code) are independent of the current scene. The enter-phase (Load + EnteringWorld) is already
        // handled by the rejection guard at the top of this method, so dropping the InGame co-gate does not
        // expose that case. spec: handlers.md §23.1 (in-world mode keyed on local-player presence;
        // 0 → 6/8, other non-zero → 7/8 detail = code).
        if (hasLocalPlayer)
            return result == 0
                ? Commit(Current.To(EngineSceneState.Quit, GameState.SubStateNone)) // → 6/8
                : Commit(Current.ToError(GameState.SubStateNone, result)); // → 7/8

        return false;
    }

    // spec: Docs/RE/specs/client_runtime.md §7.5.2 — disconnect: load-time (state 2) → 7/2, otherwise → 7/8.
    public bool OnDisconnected()
    {
        if (IsTerminal) return false;

        var sub = Current.State == EngineSceneState.Load ? 2 : GameState.SubStateNone;
        return Commit(Current.ToError(sub, 0));
    }

    // spec: Docs/RE/specs/client_runtime.md §7.5.3 — scene-aware quit router → Quit (6).
    public bool RequestQuit()
    {
        if (IsTerminal || HasExited) return false;

        return Current.State switch
        {
            EngineSceneState.Login or EngineSceneState.Load => Commit(Current.To(EngineSceneState.Quit, 2)),
            EngineSceneState.Select or EngineSceneState.InGame => Commit(Current.To(EngineSceneState.Quit,
                GameState.SubStateNone)),
            _ => false
        };
    }

    // spec: resource_pipeline.md §2.5 — Load re-reads OPENNING/SKIP unconditionally; reload marker only feeds LoadOrchestrator.
    private bool AdvanceLoadScene()
    {
        LoadIsReload = false;

        // ENTER load: the defensive 3/5 path (OnEnterGameAck) pre-armed the latch. On the live
        // server this branch is NEVER taken — the only 3/5 is the unsolicited post-login
        // account-ack that arrives BEFORE any 1/9 (latch unarmed), so EnteringWorld is false
        // when AdvanceLoadScene runs on the normal path. The authoritative enter confirmation is
        // the latch-armed 4/1 SmsgGameStateTick (OnWorldEntryConfirmed), which bypasses this
        // method entirely (Select/Load → InGame directly).
        // spec: Docs/RE/specs/login_flow.md §3.4 (CORRECTED — live enter ladder 1/9 → 4/1;
        // the only 3/5 is the pre-1/9 unsolicited post-login ack);
        // Docs/RE/specs/net_contracts.md §1.3 (CORRECTED enter ladder; 4/1 confirms the latch).
        if (EnteringWorld)
        {
            EnteringWorld = false;
            return Commit(Current.To(EngineSceneState.InGame));
        }

        // BOOT load: follow the OPENNING/SKIP gate unchanged. spec: client_runtime.md §7.5.1.
        return Commit(Current.To(SkipOpening ? EngineSceneState.Select : EngineSceneState.Opening));
    }

    private bool CommitReloadLoad()
    {
        LoadIsReload = true;
        return Commit(Current.To(EngineSceneState.Load));
    }

    private bool ExitTail()
    {
        if (HasExited) return false;

        HasExited = true;
        return true;
    }

    private bool Commit(GameState next, bool forceReentry = false)
    {
        var previous = Current;
        if (previous == next && !forceReentry) return false;

        Current = next;
        if (next.State is EngineSceneState.Quit or EngineSceneState.Error)
            // A terminal transition (quit / disconnect / error) abandons any in-flight enter, so the
            // enter-world latch must not survive to misroute a later recovery load.
            // spec: Docs/RE/specs/client_runtime.md §7.5.2 (state-2 disconnect → 7/2), §7.5.3.
            EnteringWorld = false;
        else
            HasExited = false;

        _eventBus.Publish(new SceneStateChangedEvent(previous, next));
        return true;
    }
}