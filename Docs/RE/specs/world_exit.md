<!--
verification: routing/teardown-control-flow [confirmed] (the existence of TWO distinct world-exit
  opcodes built by TWO distinct teardown routines, the guard on the heavier path, the keepalive-disarm
  ordering, the shared exit tail, and the convergence on scene state 6 sub-state 8 are
  control-flow-confirmed on build 263bd994);
  the concrete in-game UI control that fires each opcode, whether the SERVER closes the socket on the
  lighter path, and which agent issues the real socket close are static-hypothesis / live-pending (6-D);
  the "4/137" server message is flagged NEEDS-REVIEW (its label is suspect).
ida_anchor: 263bd994
evidence: [static-ida]
sample_verified: false
-->

# World Exit — Logout & Leave-World Teardown — Clean-Room Specification

> Neutral, rewritten specification promoted from dirty-room analyst notes under **EU Software
> Directive 2009/24/EC Art. 6** (decompilation permitted solely to achieve interoperability). It
> contains **no decompiler output, no pseudo-code, no legacy symbol names, and no binary virtual
> addresses**.
>
> **Spec path (cite this):** `// spec: Docs/RE/specs/world_exit.md`
>
> **Scope.** How the client leaves the in-world session and returns to the login screen: the **two
> distinct world-exit opcodes**, the **two distinct teardown routines** that send them, why they are
> never sent together, the shared exit tail, and where the actual session drop happens. The opcode
> framing/dispatch and the connection lifecycle are owned by neighbours and cited, not duplicated:
> - `opcodes.md` — the 8-byte wire frame header + the opcode catalogue.
> - `connection_topology.md` — the single persistent opcode socket A and why leave-world does **not**
>   reconnect it (§6: "leave-world / logout-to-menu does NOT close A").
> - `network_dispatch.md` — the dispatcher, the keepalive toggles, and the scene/link reconciler.
> - `scenes/scene_state_machine.md` — the 8-state scene machine the teardown converges on.

---

## 1. Two world-exit opcodes — one or the other, never both

The client has **two distinct ways to leave the in-world session**, each emitting a **different
header-only opcode** from a **different teardown routine**. Exactly one path runs per exit; the choice
is made by the scene/lifecycle state at the moment of exit. *([confirmed]* that two separate routines
and two separate opcodes exist and are mutually exclusive.)*

| Opcode | Name | Sent by | Wire shape | Guarded? | Keepalive | Character |
|--------|------|---------|------------|----------|-----------|-----------|
| **1/0** | **CmsgLogout** | the **quit / logout** teardown routine | **header-only, 8 bytes**, no payload | no | does **not** disarm the keepalive | **fire-and-forget** — arms no in-flight latch; the server is expected to drop the session |
| **2/0** | **CmsgLeaveWorld** | the **leave-world transition** teardown routine | **header-only, 8 bytes**, no payload | **yes** | **disarms the keepalive toggle first** | heavier guarded transition with more elaborate UI/object teardown |

Both opcodes carry **no body** — the 8-byte frame header is the entire message (`opcodes.md §Wire
frame header`).

### 1.1 `1/0 CmsgLogout` — the in-world quit

Emitted when the in-world player **quits** — the ESC / quit-confirm gesture taken while already in the
world. The logout routine is the **lighter** of the two:

- It does **not** disarm the keepalive toggle.
- It arms **no in-flight latch** — it does not register a pending reply slot and does not wait for an
  acknowledgement. It is **fire-and-forget**; the session drop is left to the server (and/or to the
  scene reaching its return state — §3).

### 1.2 `2/0 CmsgLeaveWorld` — the guarded leave-world transition

Emitted by a **guarded** transition routine that runs **only when the entered-world flag is set and a
player-lifecycle field is in the right state**. When those conditions hold, this routine is the
**heavier** path and runs in a deliberate order:

1. **First, disarm the `2/112` keepalive toggle** (stop the in-world heartbeat before tearing down).
2. Perform the more elaborate **UI / object teardown** (more than the logout path does).
3. **Then** send `2/0 CmsgLeaveWorld`.

The keepalive disarm being **ordered before** the send is the defining structural difference from the
logout path. *([confirmed]* on the guard, the disarm-then-send ordering, and that this path is heavier.)*

---

## 2. Shared exit tail

Despite the two opcodes and two routines, both paths **converge on the same exit tail** after the
opcode is sent: *([confirmed]* on each step of the tail.)*

1. **Play an exit sound effect.**
2. **Tear down the in-world UI / panels.**
3. **Schedule a deferred completion event** — a timed event roughly **~1.5 s ahead** (a delayed
   finish, not an immediate transition).
4. **Converge on scene state 6, sub-state 8** — the **return-to-login** scene
   (`scenes/scene_state_machine.md`).

So the *destination* is identical (back to login); only the *opcode sent* and the *amount of
teardown* differ between the two routines.

---

## 3. Who actually closes the socket — NOT these routines

**Neither teardown routine performs an explicit client socket close.** *([confirmed]* that no socket
close is issued from either routine.)* This is consistent with `connection_topology.md §6`, which
records that leave-world / logout-to-menu does **not** close the persistent opcode socket A from the
exit path itself.

The real session drop is therefore either:
- **server-driven** (the server drops the connection in response to the exit opcode), and/or
- **driven by the scene reaching state 6 / sub-state 8** (a return-state handler or the deferred timed
  event of §2 issues the actual teardown of the connection).

Exactly which of these performs the close — and whether the server closes on the lighter `1/0` path —
is **live-pending (6-D)** (see §5).

---

## 4. NEEDS-REVIEW — the suspect "4/137" server message

A **server-driven** message currently labelled **"4/137"** reads, on the routing side, like a
**scene-control message**: it routes **one subtype to the leave-world teardown** path and **another
subtype to the logout teardown** path. In other words, a single server message can drive **either**
exit path.

> **Do not over-assert what "4/137" is.** The label is **suspect** and is recorded here only as a
> NEEDS-REVIEW item. What is structurally observed is narrow: *a server message exists that can dispatch
> into either teardown routine by subtype.* Its true opcode identity, its canonical name, and its full
> field shape are **not** asserted by this spec and must be re-confirmed before being relied on
> (`opcodes.md` is the catalogue of record for any eventual identity).

---

## 5. Open items (live-pending (6-D))

- **Which concrete in-game UI control fires `1/0` vs `2/0`** — the exact button / menu entry / ESC
  sub-state that selects the logout path versus the leave-world transition path.
- **Whether the server actually closes the socket on `1/0`** (the fire-and-forget assumption that the
  server drops the session on logout).
- **Whether a state-6/8 return handler or the deferred timed event (§2) issues the real socket close**
  on either path.
- **The "4/137" label review** (§4) — confirm or refute the opcode identity and the by-subtype dispatch
  into the two teardown routines.

---

## 6. Cross-reference map

| This spec covers | Owned elsewhere — cite, don't duplicate |
|---|---|
| The two world-exit opcodes `1/0` and `2/0` and their teardown routines (§1) | `opcodes.md` (catalogue of record), `packets/` (field specs) |
| Why leave-world does not reconnect / the persistent socket lifecycle (§3) | `connection_topology.md §6` |
| The `2/112` keepalive toggle disarmed before `2/0` (§1.2) | `network_dispatch.md` (keepalives) |
| The return scene state 6 / sub-state 8 (§2) | `scenes/scene_state_machine.md` |
