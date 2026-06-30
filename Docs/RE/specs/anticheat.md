# Anti-Cheat Subsystem — `doida.exe`

> verification: IDA Pro 9.3  
> ida_anchor: f61f66a9  
> confidence: CONSUMER-CONFIRMED (triad structure, slot map, singleton orchestration, page-guard cadence — C15-S18 2026-06-30); static-hypothesis where explicitly marked  
> date: 2026-06-30  
> Build: `f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963`  
> Promoted: C15-S18 (2026-06-30) — singleton wiring, base-class fidelity note, page-guard cadence. Folded: `anticheat_core.md` + `xtrap_integration.md` (CYCLE 15/17 precursors; XTrap loader, XProc3, and telemetry sections remain static-hypothesis).

---

## Overview

The client integrates a **three-tier anti-cheat architecture**:

| Layer | Class | Role |
|-------|-------|------|
| 1 | `GGGProtect` | Core protection engine — time gating, stack cookies |
| 2 | `GProtect` | Orchestration base — six vtable check slots (1–6) are no-op return-1 stubs |
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

### GProtect — Base Class Slot Behaviour

The six check slots (slots 1–6) of the `GProtect` vtable are all no-op stubs that return the integer 1
unconditionally. Slot 0 is the virtual deleting destructor. The base class carries no data members of its
own — the associated protection state (time-gate fields and IAT snapshots) lives in BSS globals, not in
the 4-byte object body.

---

## VFunc_00 Slot — Scalar Deleting Destructors

> **static-hypothesis** — derived from _dirty/ precursors; unverified at f61f66a9 via live session.

Under MSVC, virtual-slot 0 of any polymorphic class is the scalar deleting destructor (`operator delete`
wrapper). Symbols previously labelled as "UI panel factory overrides" (for `GProtect` / `GXProtect`) and
as "10 heavy protection-engine slots" (for `GGGProtect`) are not factory or engine functions — they are the
virtual destructors of UI panel and subsystem classes that inherit from these base classes.

MSVC's destructor sequence restores the vtable pointer to the parent class before calling the parent's
destructor body. The `GProtect__ctor_1` write observed inside these functions is the mandatory
vtable-restore step performed on every derived-class destructor, not a factory call.

### GGGProtect vtable — VFunc_00_0 through VFunc_00_9: true owning classes

| Slot label | True owning class | Actual role |
|---|---|---|
| `GGGProtect__VFunc_00_0` | `SkillConfirmPanel` | Scalar deleting destructor |
| `GGGProtect__VFunc_00_1` | `SkillPanel` | Scalar deleting destructor |
| `GGGProtect__VFunc_00_2` | `WarInfoPanel` | Scalar deleting destructor |
| `GGGProtect__VFunc_00_3` | `DXTextureList` | Scalar deleting destructor |
| `GGGProtect__VFunc_00_4` | `Diamond::GUComponent` | Scalar deleting destructor |
| `GGGProtect__VFunc_00_5` | `Pointer` | Scalar deleting destructor |
| `GGGProtect__VFunc_00_6` | `AnimationPointer` | Scalar deleting destructor |
| `GGGProtect__VFunc_00_7` | `Diamond::GTextureManager` | Scalar deleting destructor |
| `GGGProtect__VFunc_00_8` | `CmdHandler` | Scalar deleting destructor |
| `GGGProtect__VFunc_00_9` | `BroodWarMapInfoPanel_MapStatePanel` | Scalar deleting destructor (name suspicious — possible label-propagation artefact; flag for IDB review) |

### GProtect / GXProtect vtable — VFunc_00 overrides

The overrides previously labelled as panel factory constructors (`GreetPanel`, `LoginPanel`,
`ServerSelectionPanel`, `OptionPanel_Character`, etc.) are likewise the scalar deleting destructors of
those same panel classes, which inherit from `GProtect` or `GXProtect`.

---

## Singleton Wiring and Boot Orchestration

> Consumer-confirmed at f61f66a9 (C15-S18).

### Static-Init Construction

A C-runtime static initializer calls a factory that allocates **4 bytes** — exactly one vtable pointer —
and constructs the **base `GProtect`** object, storing the result into a global protection-singleton slot.
The base class carries no data members; its associated protection state lives in BSS globals (time-gate
fields, IAT snapshots). An atexit-style cleanup path invokes the singleton's virtual deleting destructor
(slot 0) and zeroes the global slot at process exit.

### WinMain Scene-State Pump

The WinMain scene state machine drives the singleton through a family of thin vtable-forwarder thunks at
scene-state transition boundaries. Each forwarder loads the singleton pointer, loads its vtable, and
dispatches to the corresponding slot (slots 1–6 are all exposed this way).

At the window/device-init state, **slot 1 is called as a hard boot gate**: if it returns 0 the state
machine branches to a failure/quit scene and boot does not proceed. Later states call slots 3 and 5.

### Fidelity Note — Base Class Instantiated, Gates Pass Trivially

**The object resident in the protection singleton is the base `GProtect` class, whose slots 1–6 are all
no-op return-1 stubs.** Every WinMain pump call — including the slot-1 hard boot gate — therefore returns
1 and passes without any check being performed. The in-band protection orchestration is present in the
binary but effectively neutralised in the shipped client as statically observed.

The `GGGProtect` (core-engine) and `GXProtect` (XTrap-bridge) override classes are fully compiled with
distinct vtables, RTTI descriptors, and real slot bodies (including the XTrap registration token). No
static construction site was found that instantiates either override into the pumped singleton slot.
Whether either class is constructed via a runtime code path invisible to static analysis is a live-only
question — see Debugger-Pending Residuals below.

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

### XTrapVa.dll Dynamic Loader

> **static-hypothesis** — unverified at f61f66a9.

`XTrap_LoadLibraryVa` resolves the DLL through a three-step search path:

| Priority | Path tried |
|----------|-----------|
| 1 | `[caller-supplied init path]\XTrap\XTrapVa.dll` |
| 2 | `[doida.exe parent directory]\XTrap\XTrapVa.dll` |
| 3 | `.\XTrap\XTrapVa.dll` (fallback on `ERROR_MOD_NOT_FOUND` / Win32 error 126) |

Behavior:
- If the module handle is already populated, the function writes loader status 8195 (already loaded) and returns 0 — double-init is blocked.
- On load success, `XTrap_VerifyXProc3Module` is called to validate the loaded module before the handle is stored.
- On complete load failure, `GetLastError` is captured and loader status 8196 (DLL absent or corrupt) is written.

### XProc3 Export Coupling

> **static-hypothesis** — unverified at f61f66a9.

The sole public coupling export from `XTrapVa.dll` is **`XProc3`**, resolved at runtime via
`GetProcAddress` by `XTrap_ResolveXProc3`.

Call sequence:
1. Client calls `XProc3` with the addresses of five internal state-tracking fields plus a local callback pointer.
2. `XProc3` populates the callback pointer with an internal DLL function address.
3. Client invokes the callback with two encrypted data blocks:

| Block | Size | Decryption routine | Role |
|-------|------|--------------------|------|
| Token Block | 1596 bytes | `Crypt_DecryptBuffer` (128-bit key; key value omitted) | Session and hardware-integrity tokens |
| Config Block | 300 bytes | `Crypt_DecryptBuffer` (128-bit key; key value omitted) | Anti-cheat behaviour parameters (scan frequencies, etc.) |

If `XProc3` resolution or the callback invocation fails, loader status 675854 is written and client
initialisation aborts immediately.

### Token Integrity Checksum

> **static-hypothesis** — unverified at f61f66a9.

Before the Token Block is used, the client validates it via an XOR checksum across six consecutive
DWORDs of the decrypted token array. If the computed XOR does not match the expected value stored in
the seventh position, the token is considered corrupt and the integer error sentinel **4095** is
substituted as a hardware-error code in all telemetry fields that would normally carry token data.

---

## XTrap Network Telemetry

> **static-hypothesis** — unverified at f61f66a9.

XTrap maintains a dedicated TCP channel to its relay server, separate from the game protocol socket.

- **Winsock initialisation:** `XTrap_Socket_ctor` calls `WSAStartup` for version 2.2.
- **Target:** IP `211.115.86.66`, TCP port `2424` (configured in `XTrap_SocketSetup`).
- **Connect-with-retry** (`XTrap_ConnectWithRetry`):
  - Immediate abort if the first `connect` call fails with `WSAETIMEDOUT` (error 10060).
  - For any other network error: up to **10 reconnection attempts**, each separated by a **200 ms** wait via `WaitForSingleObject`.
  - On success, `getsockname` is called to record the local port assigned to the established socket; this local port is later embedded in the telemetry payload.

### XL_XF_V1 Telemetry Packet Format

Packets are assembled by `XTrap_BuildXlXfV1Packet` and carry a fixed header followed by an encrypted
payload.

**Header (12 bytes):**

| Field | Size | Value |
|-------|------|-------|
| Magic signature | 8 bytes | `"XL_XF_V1"` (ASCII, null-padded to 8) |
| Total size | 4 bytes | Length of serialised telemetry payload + 32 |

**Payload (532 bytes — 133 DWORD slots):**

| DWORD index | Type | Role |
|-------------|------|------|
| 0 | DWORD | Decrypted token value, or 4095 on token validation failure |
| 1–16 | CHAR[62] | System identification string A |
| 17–32 | CHAR[62] | System identification string B |
| 33–48 | CHAR[62] | System identification string C |
| 49–64 | CHAR[62] | System identification string D |
| 65 | DWORD | Global system identifier |
| 66 | DWORD | System state query result (call 1) |
| 67 | DWORD | System state query result (call 2) |
| 68 | DWORD | Local TCP port recorded by `getsockname` at connect time |
| 69 | DWORD | System state query result (call 3) |
| 70 | DWORD | Packet type constant: **3** |
| 71 | DWORD | Telemetry event code (per-call parameter) |
| 72 | DWORD | Reserved, always 0 |
| 73 | DWORD | System or API error code (per-call parameter) |
| 74 | DWORD | Network source index (per-call parameter) |
| 75 | DWORD | Memory integrity token field (index 8 of token array) |
| 76 | DWORD | Reserved, always 0 |
| 77 | DWORD | Magic constant A (value from _dirty/ analysis; omitted) |
| 78 | DWORD | Magic constant B (value from _dirty/ analysis; omitted) |
| 79 | DWORD | Reserved, always 0 |
| 80 | DWORD | Memory integrity token field (index 16 of token array) |
| 81 | DWORD | Memory integrity token field (index 20 of token array) |
| 82 | DWORD | Memory integrity token field (index 12 of token array) |
| 83–132 | CHAR[198] | Error context / call-source string (max 198 bytes) |

The assembled payload is XOR-stream-encrypted before transmission. The 12-byte header is prepended
and the complete frame is sent to `211.115.86.66:2424`.

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

## PE Selfcheck (XTrap)

> **static-hypothesis** — unverified at f61f66a9.

### Disk-Mapping Mechanism

`Selfcheck_MapModuleAndVerify` performs an integrity check by mapping the physical executable image
directly from disk, bypassing any in-memory modifications:

1. Opens the executable via `CreateFileA` with read-only access (`GENERIC_READ`, `FILE_SHARE_READ`, `OPEN_EXISTING`).
2. Queries file size via `GetFileSize`.
3. Creates a read-only file mapping via `CreateFileMappingA` (`PAGE_READONLY`) and maps it into the process address space via `MapViewOfFile` (`FILE_MAP_READ`).
4. Reads the first fields of the mapped image and verifies both the `"MZ"` (DOS) and `"PE"` (NT headers) signatures.

File/mapping failure status codes:

| Status | Cause |
|--------|-------|
| 2 | `CreateFileA` returned `INVALID_HANDLE_VALUE` |
| 3 | `GetFileSize` failed |
| 4 | `CreateFileMappingA` failed |
| 5 | `MapViewOfFile` failed |

### LDE — Instruction Length Decoder and Hook Detection

`LDE_DecodeInstruction` is an x86 Length Disassembler Engine embedded in `XTrapVa.dll`. It decodes
instruction prefixes, opcodes, ModRM, and SIB bytes to determine exact instruction length (1–15 bytes).

`Selfcheck_DecodeInstructionsAndHash` uses it to walk the code sections and actively test for hook
patterns:

| Opcode byte | Instruction | Interpreted as |
|-------------|-------------|----------------|
| `0xCC` | `INT 3` | Software breakpoint |
| `0xE9` | `JMP rel` | Function redirection / detour hook |
| `0xE8` | `CALL rel` | Call hook / detour |

To avoid false positives, the routine masks bytes that correspond to entries in the PE
base-relocation table (loader-adjusted absolute addresses) and IAT slots (import pointer fields).
These masked bytes are excluded from the section hash comparison.

### PE Integrity Signature Blobs at File Tail

Two cryptographic blobs are embedded at fixed positions relative to the end of the physical
`doida.exe` file:

| Blob | Size | Position |
|------|------|---------|
| Primary | 608 bytes | `FileSize - 992` |
| Secondary | 32 bytes | `FileSize - 384` |

Primary blob verification (`Selfcheck_HashSectionsAndExports`):
1. Computes a hash of the masked code and export sections.
2. Descrambles the computed hash block using a keyed routine (key value omitted) to produce the reference block.
3. Compares the reference block byte-for-byte with the 608-byte primary blob in the physical file.
4. Mismatch → selfcheck status **6** (code or export tampering detected).

Secondary blob verification:
1. Takes the 608-byte reference block and hashes it with `Crypt_HashBuffer`, producing a 32-byte digest.
2. Compares the 32-byte digest with the secondary blob at `FileSize - 384`.
3. Mismatch → selfcheck status **7** (signature chain integrity failure).
4. Both checks pass → file handles and mapping are released; selfcheck returns success.

---

## Secure Auth Context — Page-Guard Cadence

> Consumer-confirmed at f61f66a9 (C15-S18). This cadence belongs to the **secure auth/crypto
> subsystem**, not to the GXProtect triad.

### Guarded Region

A single **~11 808-byte (0x2E20)** secure auth/crypto context object is held **PAGE_NOACCESS** at rest,
making it unreadable and unwritable to passive memory scanners and casual debugger reads without kernel
privilege. The context holds the session's RSA/credential state: imported public-key material (modulus and
exponent as FLINT bignums), two server scalars, a millisecond timestamp, and a heap pointer to a staged
credential buffer. Teardown zeroes the full 0x2E20-byte extent and destroys two embedded mutexes near its
tail.

### Cadence Pattern

Every secure operation wraps its work in an identical bracket:

```
unlock to read-write  →  perform crypto/auth work  →  relock to no-access
```

The relock fires on **every** return path, including each early-error exit. The cadence is
**event-driven and per-operation**: the guard window spans exactly one auth or key-exchange call. There is
no fixed time interval.

### Operations Protected by the Bracket

| Operation | Triggered by |
|-----------|-------------|
| Parse incoming key-exchange message (server-to-client phase 0) | Network receive of handshake packet |
| Build outgoing login packet | Login submission |
| Build secure auth reply (client-to-server phase 1) | Auth continuation |
| Destroy and zero the context | Session teardown (logout or disconnect) |

A separate, unrelated 4-byte code-guard pair toggles a single 4-byte location between read-write and
execute-read-write protection — a self-modifying-code or function-pointer hotpatch facility; its call
sites are not further characterised here.

---

## Debugger-Pending Residuals (R-CAP)

The following facts are runtime-only and cannot be settled by static analysis alone. They are
non-blocking and scoped to the GXProtect triad subsystem.

| Tag | Question | Breakpoint plan |
|-----|----------|----------------|
| R-CAP-AC-1 | Is the `GGGProtect` core-engine class ever constructed at runtime (e.g. swapped into the singleton by XTrap)? | Break on `GGGProtect__ctor`; inspect the global singleton slot at each hit. |
| R-CAP-AC-2 | Is the `GXProtect` XTrap-bridge class ever constructed at runtime? | Break on `GXProtect__ctor`; inspect the global singleton slot at each hit. |
| R-CAP-AC-3 | Does the XTrap-init slot (slot 1 of the XTrap-bridge vtable) ever execute? | Break on `GXProtect__XTrapInit` during boot; observe whether it fires before or after `XTrapVa.dll` load. |

---

## XTrap Status Codes

> **static-hypothesis** — unverified at f61f66a9; source: _dirty/ CYCLE 15/17 analysis.

| Scope | Value | Cause |
|-------|-------|-------|
| Loader (XTrapVa) | 8195 | `XTrapVa.dll` already loaded — double-init blocked |
| Loader (XTrapVa) | 8196 | `XTrapVa.dll` load failure (absent or corrupt) |
| Loader (ancillary) | 675851 | Ancillary XTrap file validation failure (`xtrap.xt`, `XTrapVa.dll` hash) |
| Loader (XProc3) | 675854 | `XProc3` export resolution failure via `GetProcAddress` |
| Selfcheck (file) | 2 | `CreateFileA` returned `INVALID_HANDLE_VALUE` |
| Selfcheck (file) | 3 | `GetFileSize` failed |
| Selfcheck (file) | 4 | `CreateFileMappingA` failed |
| Selfcheck (file) | 5 | `MapViewOfFile` failed |
| Selfcheck (integrity) | 6 | Primary blob mismatch — code/export tampering detected |
| Selfcheck (integrity) | 7 | Secondary blob mismatch — signature chain corrupted |
| Telemetry (token) | 4095 | Token integrity XOR checksum mismatch; used as hardware-error sentinel in payload |
