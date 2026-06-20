<!--
verification: confirmed (the socket-subsystem count, which opcode majors ride which socket,
  the single-persistent-opcode-connection model, the connect lifecycle vs the scene FSM, the
  lobby->game server-address handoff CHAIN, the lobby static port 10000, the static fallback/config
  constants, and the XTrap static defaults are all control-flow-confirmed on build 263bd994 — the
  endpoint constants re-verified against doida.exe IDB SHA 263bd994, CYCLE 7, 2026-06-20);
  static-hypothesis (the scene-substate numbers attached to each connect/query step);
  capture/debugger-pending (the runtime host/port VALUES beyond the binary-literal fallbacks, and
  the connection-state code meanings 201/202/203/232).
ida_anchor: 263bd994
ida_reverified: 2026-06-20   # endpoints + 1/2 keepalive re-verified against doida.exe IDB SHA 263bd994, CYCLE 7
evidence: [static-ida]
sample_verified: false
-->

# Network Connection Topology — Clean-Room Specification

> Neutral, rewritten architectural specification, promoted from CYCLE 5 dirty-room analyst notes
> under **EU Software Directive 2009/24/EC Art. 6** (decompilation permitted solely to achieve
> interoperability). It contains **no decompiler output, no pseudo-code, no legacy symbol names, and
> no binary virtual addresses**. The network-endpoint constants quoted below (the lobby fallback host
> and the anti-cheat host:port) are **data constants embedded in the binary** — interoperability facts,
> not code addresses.
>
> **Spec path (cite this):** `// spec: Docs/RE/specs/connection_topology.md`
>
> **Scope.** How many sockets the client opens, which server/endpoint each serves, **which opcode
> majors ride which socket**, the connection lifecycle relative to the 8-state scene machine, and the
> **login → game/world server-address handoff**. This is the structural frame *around* the dispatch
> plumbing; the per-socket detail is owned by neighbours and cited, not duplicated:
> - `network_dispatch.md` — the dispatcher, lifecycle, keepalives, framing/Nagle reassembly **on the
>   game socket (A below)**.
> - `crypto.md` — the byte cipher, LZ4 codec, and the secure `(0,0)`→`1/4` handshake.
> - `opcodes.md` — the 8-byte wire frame header + the opcode catalogue.
> - `lobby.yaml` — the **header-less** lobby blocking-query wire shapes (socket family B below).

---

## 1. Three socket subsystems — but exactly ONE persistent opcode connection

The client opens **three distinct socket subsystems**, but only **one** of them is the persistent,
opcode-framed game connection. *([confirmed]; build 263bd994.)*

| # | Subsystem | Sockets | I/O style | Host resolution | Target | Carries |
|---|---|---|---|---|---|---|
| **A** | **Game/world opcode connection** (the embedded `NetConn`) | **1, persistent** | overlapped `WSARecv`/`WSASend`, 4 auto-reset events, dedicated I/O thread, non-blocking connect + ~2 s `select` + `SO_ERROR` check | **DNS (`gethostbyname`)** — host **RUNTIME-ONLY** | the game host:port from the lobby channel-endpoint token (§7); **NO static IP or port baked in** (both staged into the client object at connect) | **ALL opcode majors 0–5** — `0` key-exchange, `1` login/char-mgmt (+ the `1/2` keepalive), `2` game actions, `3` char-mgmt/chat, `4` Response, `5` Push — plus all keepalives/idle fillers and the `5/146`→`2/146` ack |
| **B** | **Login / lobby queries** | **N, throwaway** | **blocking**, one-shot (open → request → recv → LZ4-decompress → close); **NO 8-byte opcode header** | `inet_addr` (dotted-quad, **no DNS**) | lobby host, **port 10000** (server list) and **port 10000 + server_id** (channel endpoint) | the server-list records and the 30-byte game-endpoint token (§7) |
| **C** | **XTrap anti-cheat relay** | **1, blocking** | blocking + 10 × 200 ms retry + `getsockname` | `inet_addr` literal | **`211.115.86.66:2424`** (binary-literal constant) | `XL_XF_V1` anti-cheat frames — independent of the opcode protocol |

**The single most load-bearing fact:** **every opcode major (0–5) rides the one persistent socket A.**
The login endpoint and the **World Server** (the major-4 Response / major-5 Push traffic) are the
**same socket A** — there is **no separate "world server" socket**, and **no login→game socket
reconnect**. The lobby (B) only *hands off an address*; the game socket then connects there once and
carries the whole post-login session. *([confirmed].)*

---

## 2. Subsystem A — the game/world opcode connection

This is the connection every other netcode spec describes. It is the `NetClient`'s embedded `NetConn`:
its **connect routine** opens the socket, creates the auto-reset events (receive-completion,
send-signal, graceful-close, shutdown), spawns the I/O thread (one of the two user `_beginthreadex`
sites), does a **non-blocking connect with a ~2 s `select` and an `SO_ERROR` check**, and resolves its
host by **DNS (`gethostbyname`)**. Winsock bring-up is `WSAStartup(0x0202)`. The single send
convergence, the master dispatcher, all keepalives/idle fillers, and the link-health ack all live on
this socket — see `network_dispatch.md`. Nagle is **ON** (the sole `setsockopt` sets `SO_RCVBUF`, never
`TCP_NODELAY`), so frames coalesce — see `network_dispatch.md §4.4a`. *([confirmed].)*

> **Endpoint is RUNTIME-ONLY (binary wins).** There is **no static game / World-Server IP or port
> immediate anywhere in the binary** for socket A. The connect command reads the **host string** and
> the **port** from `NetClient` object fields (staged from the lobby channel-endpoint handoff, §7),
> resolves the host through `gethostbyname`, and connects. The **World-Server transition is an address
> handoff on the SAME opcode socket**, so its host/port likewise arrive as runtime field values — there
> is no reconnect and no second baked-in endpoint. The error strings on this path confirm the
> field-driven model (a `"not connected to host ip[%s] / port[%d]"` diagnostic). *([confirmed]* the
> resolution chain is static; the operative host **and** port VALUES are `[capture/debugger-pending]`.)*

The client is a **pure outbound TCP client**: there is **no `bind`/`listen`/`accept`** anywhere.
*([confirmed].)*

---

## 3. Subsystem B — the login / lobby blocking queries

A family of **short-lived blocking sockets**, each one-shot: open → send a request → receive →
LZ4-decompress → close. These carry **no 8-byte opcode frame header** — they are a distinct wire shape
from socket A, owned by `lobby.yaml`. Hosts are resolved with `inet_addr` (dotted-quad, **no DNS**).
Two query kinds: the **server-list (roster)** query at **port `10000` (RESOLVED static)**, and the
**channel-endpoint** query at **port `10000 + server_id`** (the `10000` base is a static immediate; the
per-server offset is the runtime `server_id`). The channel-endpoint query is what **returns the game
socket's target address** (§7). *([confirmed]* structure / static ports; the query payload field
semantics are owned by `lobby.yaml` and stay `[capture/debugger-pending]` where noted there.)*

**Lobby host is RUNTIME-ONLY** (resolved in the priority chain of §7); the only static fallback is the
binary-literal **`211.196.150.4`**. The config filenames **`ip.txt`** and **`list.dat`** are static.

> **Registry leg supplies a DISPLAY NAME only (binary wins — note to avoid mis-attribution).** A
> registry read of **`HKLM\SOFTWARE\crspace\do`** value **`servername`** (with a CP949 Korean default
> on a key/value miss) runs at the **end of the `list.dat` load** and only fills the **server display
> name** field of a list record — it does **NOT** supply the connection host/IP or port. The registry is
> part of the lobby *server-list record* assembly, not endpoint (host:port) resolution. *([confirmed].)*

---

## 4. Subsystem C — the XTrap anti-cheat relay

A separate **blocking** socket (with a 10 × 200 ms connect retry and a `getsockname` call) connects to
the host **`211.115.86.66`** and port **`2424`** and exchanges `XL_XF_V1` anti-cheat frames. Both are
**RESOLVED static defaults** — applied (via `inet_addr` + `htons`) when no host argument is supplied. It
is **independent of both the opcode protocol and the lobby queries**. *([confirmed].)*

> **Correction to earlier notes (binary wins).** Some CYCLE-4 dirty notes attributed a
> "lobby socket helper" cluster (a `socket`+`inet_addr`+`htons` constructor, a blocking
> connect-with-retry, a `getsockname`) to the **server-list** path. That cluster is actually the
> **XTrap relay (C)**. The real server-list path is the `ip.txt` probe driven by the login-window
> state machine (§7), not that helper cluster.

> **NOT an endpoint — the crash-report (BugTrap) server (binary wins — note to avoid mis-attribution).**
> A dotted-quad **`183.99.71.33`** with port **`9999`** is baked in, but it is the **BugTrap** crash /
> minidump reporter (a diagnostic crash-dump upload), **NOT** a game / lobby / anti-cheat socket — it is
> none of subsystems A/B/C and carries no opcode or anti-cheat traffic. It is recorded here only so a
> reader does not mistake it for a fourth network endpoint. **No other dotted-quad IP constants exist in
> the binary** — the only four are the lobby fallback `211.196.150.4`, the XTrap default `211.115.86.66`,
> and this BugTrap `183.99.71.33`. *([confirmed].)*

---

## 5. C1 RESOLVED — `1/2 CmsgLobbyPing` is game-connection traffic

**`1/2 CmsgLobbyPing` is sent on the same persistent game socket A as every other opcode major — there
is NO separate lobby socket for the `1/2` ping.** *([confirmed].)* Control-flow chain: the builder
stamps `major=1 / minor=2`, fetches the `NetClient` singleton, hands the frame to the single send
convergence (`Net_SendPacket`: timestamp → cipher → LZ4 → enqueue onto the embedded `NetConn`), and it
goes out as an overlapped `WSASend` on socket A.

The apparent CYCLE-4 tension is resolved: the send-census ("`1/2` → the convergence") and `lobby.yaml`
("the lobby is a separate blocking socket") were **both correct but describing disjoint surfaces** —
the lobby blocking threads (B) never call the `1/2` builder. On this build, the "lobby ping" is
functionally a **game-connection keepalive**, not lobby-server traffic. (Naming note: the `1/2`
builder's canonical name should reflect "game keepalive", not "lobby", to prevent future confusion.)

> **`1/2` is a header-only idle filler with NO cadence (CYCLE 7).** *([confirmed]; build 263bd994.)*
> The `1/2` keepalive is **header-only** — an **8-byte frame, no body** — and is driven by the
> network-client's **send-proxy / idle-filler thread** (a ~10 ms poll loop): while its enable gate is
> set and no send is in flight, it fires on a poll tick whenever the link is idle. **There is no
> periodic interval immediate** — the prior "400 ms" figure is a **WARN-log latency threshold** for an
> over-pending queued send, not a send cadence. Its full mechanics, alongside the other keepalive/idle
> mechanisms (the `2/10000` 20-second timer frame, the `2/13` move filler, and the `2/112` toggle), are
> owned by `network_dispatch.md §4.5` — cited here, not duplicated. The on-wire spacing of this idle
> filler is `[capture/debugger-pending]`.

---

## 6. Connection lifecycle vs the 8-state scene machine

*([confirmed]* on the connect/disconnect control flow; the exact scene-substate numbers are
`[static-hypothesis]`; the `gs[0]` watchdog timing is read from the routine.)*

- **Socket A connects ONCE**, at the **login-window join handoff** (around login substate `40→41`),
  **after** the blocking lobby queries (B) have staged the endpoint token: the **server-list** query
  runs around substate `35→36` (port 10000) and the **channel-endpoint** query around `39→40`
  (port 10000 + server_id). The master scene code `gs[0]` then advances to **7 (LOADING)** under a
  ~30 s watchdog.
- **Char-select → enter-world rides the already-open socket A** — there is **no second connect**.
- **Socket A disconnects only on**: an explicit programmatic disconnect, a `WSARecv`/socket error, or
  application exit. **Leave-world / logout-to-menu does NOT close A** (it sends a keepalive-toggle plus
  a leave-world packet over A and stays connected).
- The **type-15 / sub-code-102 connection-state machine** (`network_dispatch.md §5`) is a **scene/link
  reconciler** (it reads `gs[0]`, arms ~5 s/10 s retry watchdogs, and can force the scene to 7) — it is
  **not** the socket open/close itself. The code meanings `201/202/203/232` stay
  `[capture/debugger-pending]`.

---

## 7. Server-address acquisition — the login→game/world handoff

*([confirmed]* on the resolution CHAIN and the single-source store; the runtime host/port VALUES beyond
the binary-literal fallbacks are `[capture/debugger-pending]`.)*

- **Lobby host** is resolved in strict priority: `ip.txt` (a 19-char token) → `list.dat` / the CIPList
  selected by the registry value `HKLM\SOFTWARE\crspace\do : servername` (host at record `+256`) →
  fallback to the binary-literal **`211.196.150.4`**. Reached via `inet_addr` (no DNS).
- **The game/world address is NOT known locally.** It comes from the **channel-endpoint query**
  (subsystem B, port 10000 + server_id), which returns a **30-byte `"<host> <port>"` ASCII token**
  (SPACE-delimited; the port parsed with `atol`). The client splits it (host → a `NetClient` field,
  port → another), then socket A connects there via **DNS (`gethostbyname`)**.
- The channel-endpoint token is the **SOLE source** of the game address (single writer / single reader
  of the host/port store) — there is **no opcode-wire redirect packet** that re-points the connection.
- **The login/lobby host and the game/world host are distinct endpoints**, but the game/world endpoint
  is a **single socket** carrying all opcode majors.

---

## 8. Porter implications (what a faithful re-implementation must do)

- Model **ONE persistent TCP opcode connection** for the whole post-login session (login + char-mgmt +
  game actions + the World Server Response/Push all ride it); apply the framing/Nagle reassembly of
  `network_dispatch.md §4.4a` to it.
- Model the **lobby/login queries as separate, short-lived BLOCKING request/response sockets** with
  their own **header-less** wire shape (`lobby.yaml`) — they are not part of the opcode stream.
- **Learn the game endpoint at runtime** from the channel-endpoint token; do **not** hard-code it. The
  only binary fallback is the **lobby** host (`211.196.150.4`), never the game host.
- Treat **XTrap** as an independent anti-cheat channel (`211.115.86.66:2424`); a faithful client may
  stub it, but must not conflate it with the opcode or lobby sockets.

---

## 9. Cross-reference map

| This spec covers | Owned elsewhere — cite, don't duplicate |
|---|---|
| Socket subsystem inventory + which majors ride which socket (§1) | per-opcode routing/handlers → `opcodes.md`, `handlers.md` |
| The game socket's dispatcher / lifecycle / keepalives / framing (§2) | `network_dispatch.md` (esp. §4 lifecycle, §4.4a Nagle/reassembly, §5 conn-state machine) |
| The secure handshake that runs over socket A right after connect | `crypto.md` + `network_dispatch.md §1.4` (`(0,0)`→`1/4`) |
| The lobby blocking-query wire shapes (§3) | `lobby.yaml` |
| The scene state machine the lifecycle keys off (§6) | `scenes/scene_state_machine.md` |

## Open items (capture/debugger-pending)

- The **runtime host/port VALUES** (lobby and game) beyond the binary-literal fallbacks — a capture or
  a debugger trace of a real login→channel→connect cycle would pin them.
- The **connection-state code meanings** `201/202/203/232` (`network_dispatch.md §5`).
- The XTrap frame contents (`XL_XF_V1`) — out of opcode scope; anti-cheat, not gameplay.
