# Anti-Cheat Subsystem — `doida.exe`

> Build: `f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963`  
> Analysis: Static, IDA Pro — CYCLE 15

---

## Overview

The client integrates a **three-tier anti-cheat architecture**:

| Layer | Class | Role |
|-------|-------|------|
| 1 | `GGGProtect` | Core protection engine — time gating, stack cookies |
| 2 | `GProtect` | UI panel factory + base class |
| 3 | `GXProtect` | XTrap SDK integration + logging bridge |

A background monitor thread (`AntiCheat_MonitorThread`) polls every **3 555 ms** for API hook
injection, network hook injection, and debugger presence. Any detected violation triggers an
obfuscated log entry and calls `AppEvent_ProcessCodeOrFatalExit` with a specific exit code.

---

## Class Hierarchy

`GGGProtect` derives from `GProtect`. `GXProtect` also derives from `GProtect` via its
constructor. Both `GGGProtect` and `GXProtect` install separate vtables; `GProtect` installs its
own vtable via a thin-variant constructor (`GProtect__ctor_1`). Each class has a distinct RTTI
type descriptor.

### Constructors

| Symbol | Notes |
|--------|-------|
| `GGGProtect__ctor` | Installs the `GGGProtect` vtable; calls `GProtect__ctor_1` |
| `GProtect__ctor` | Installs the `GProtect` vtable |
| `GProtect__ctor_1` | Thin variant — vtable-write only |
| `GXProtect__ctor` | Installs the `GXProtect` vtable; calls `GProtect__ctor_1` |

---

## GProtect / GXProtect — UI Panel Factory

The `VFunc_00` slot across both `GProtect` and `GXProtect` overrides implements a **polymorphic
UI panel factory**. Each override constructs a specific top-level panel type:

| Override | Panel Constructed |
|----------|------------------|
| GProtect__VFunc_00 | GreetPanel |
| GProtect__VFunc_00_3 | OptionPanel_Character |
| GProtect__VFunc_00_4 | OptionPanel_Graphic |
| GProtect__VFunc_00_5 | (screen variant) |
| GXProtect__VFunc_00 | LoginPanel |
| GXProtect__VFunc_00_2 | ServerSelectionPanel |
| GXProtect__VFunc_00_4 | LoginSecondPasswordPanel |
| GXProtect__VFunc_00_5 | LottoPanel |
| GXProtect__VFunc_00_6 | ErrorPanel |
| GXProtect__VFunc_00_8 | ExitPanel |
| GXProtect__VFunc_00_9 | GameAddictionWarningPanel |

The VFunc_00 pattern is a classic virtual constructor idiom: the base class holds a pointer to
the factory, and subclasses specialize the panel type instantiated at runtime.

---

## GXProtect — XTrap SDK Integration

### `GXProtect__XTrapInit` (vtable slot 1)

Passes an obfuscated 232-character hex-encoded module-licence token to `XTrap_InitLoader`. The
token is an XTrap SDK registration blob; it validates the client registration with the XTrap SDK.
The raw token bytes are a verbatim binary artifact and are not reproduced in this spec.

### `GXProtect__LogBridge` (vtable slot 6)

Routes log messages to two sinks:

1. `XTrap_LogInternal` — internal XTrap logging channel.
2. `DebugLog_Stub` — a trivial stub registered as a debug printf-style channel.

---

## GGGProtect — Core Protection Engine

### `GGGProtect__TimeDeltaQuery` (vtable slot 2)

Maintains a process-lifetime elapsed-time counter with a 10 000 ms reset gate. On first call,
the function sets `TimeDeltaQuery_InitFlag` and stamps `TimeDeltaQuery_LastTimestamp` from
`Time_GetMs`. Subsequent calls return the elapsed delta from that stamp. If the delta exceeds
10 000 ms, the timestamp resets (a rate limiter for protection checks).

### `GGGProtect__StackCookieCheck` (vtable slot 1)

XORs a stack variable's own address with the compile-time security cookie, then passes the result
to `SecurityCookieVerify` — an SEH frame-unwind verifier matching the `__security_check_cookie`
pattern.

---

## The 3555 ms Integrity Monitor Loop

### `AntiCheat_MonitorThread`

**Thread entry:** spawned as a background thread. The shutdown-event handle (member offset +0x14
in the thread-parameter block) is passed to `WaitForSingleObject` with a **3555 ms** timeout. The
inner loop runs on every non-signalled wake-up.

**Check sequence (per tick):**

1. `AntiCheat_CheckApiHooks` — verifies timer IAT snapshots are intact.  
   Failure → `AppEvent_ProcessCodeOrFatalExit` with exit code `1581`.
2. `AntiCheat_CheckNetworkHooks` — verifies network IAT snapshots are intact.  
   Failure → `AntiCheat_DisconnectAndQuit` (silent disconnect + quit, error codes `2225` / `1584`).
3. `AntiCheat_CheckDebuggerPresence` — tests `PEB.BeingDebugged` or a supplemental watchdog flag.  
   Failure → `AppEvent_ProcessCodeOrFatalExit` with exit code `500`.

**Thread-parameter block (partial):**

| Offset | Type | Purpose |
|--------|------|---------|
| +0x14 | HANDLE | Shutdown event (WaitForSingleObject target) |
| +28 | BYTE | Run-loop continue flag |
| +29 | BYTE | Supplemental tamper flag (set by external watchdog) |
| +4188 | void* | Fatal-exit callback context base |

**Fatal exit codes:**

| Code | Trigger |
|------|---------|
| 1581 | Timer API hooks detected (any of: QPC, GetTickCount, timeGetTime) |
| 2225 | Network API hooks detected (WSASend or send redirected) |
| 500 | Debugger presence (PEB.BeingDebugged) or watchdog flag set |

---

## API Hook Detector — `AntiCheat_CheckApiHooks`

At startup, the client snapshots three timer function pointers into BSS globals:

| Canonical name | IAT function snapshotted |
|----------------|--------------------------|
| `AntiCheat_IatSnapshot_QPC` | `QueryPerformanceCounter` |
| `AntiCheat_IatSnapshot_GetTickCount` | `GetTickCount` |
| `AntiCheat_IatSnapshot_TimeGetTime` | `timeGetTime` |

On each 3555 ms tick, the live IAT pointers are compared against the snapshots.
Mismatch → obfuscated log + fatal exit code `1581`.

Bypass flags: `AntiCheat_ApiCheckBypassFlag` (skip all checks) or `AntiCheat_ApiCheckInitFlag`
(detector not yet initialized — skip).

---

## Network Hook Detector — `AntiCheat_CheckNetworkHooks`

Snapshots two WinSock send functions at startup:

| Canonical name | IAT function snapshotted |
|----------------|--------------------------|
| `AntiCheat_IatSnapshot_WSASend` | `WSASend` |
| `AntiCheat_IatSnapshot_Send` | `send` |

Mismatch → log + fatal exit via `AntiCheat_DisconnectAndQuit` (error codes `2225` / `1584`).

Bypass flag: `AntiCheat_NetCheckBypassFlag`.

---

## Debugger Presence Check — `AntiCheat_CheckDebuggerPresence`

Two modes depending on the flag at member offset +4713 in the anti-cheat object:

| Mode | Condition | Mechanism |
|------|-----------|-----------|
| Standard | flag at +4713 is non-zero | Reads `PEB.BeingDebugged` from the Thread Environment Block |
| PID-leak | flag at +4713 is zero | Returns the low byte of `ClientId.UniqueProcess` — always non-zero on a live process |

Bypass flag: `AntiCheat_DebugCheckBypassFlag`.

---

## Log Obfuscation

Anti-cheat log messages are passed through `AntiCheat_ObfuscateThreadMessage` before writing.
This function XOR-deobfuscates strings from an in-binary encrypted string pool
(`AntiCheat_EncStringPool_A`, `AntiCheat_EncStringPool_B`, `AntiCheat_EncStringPool_C`, and
further pool entries). The deobfuscated strings are never stored in clear-text in the executable
— only the runtime copy is legible.

---

## GGGProtect "Core" vtable — 10 Heavy Slots

The true protection engine functions occupy ten virtual slots in the `GGGProtect` vtable:

| Slot | Estimated Role |
|------|----------------|
| 00_0 | Packet integrity validator (large function) |
| 00_1 | Checksum verifier (medium) |
| 00_2 | Memory integrity scanner (large) |
| 00_3 | Handler |
| 00_4 | Handler |
| 00_5 | Handler |
| 00_6 | Handler |
| 00_7 | Handler |
| 00_8 | Handler |
| 00_9 | Handler |

Full analysis of slots 00_0 and 00_2 is deferred to a future analysis cycle.
